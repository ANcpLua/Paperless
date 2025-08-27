using System.ComponentModel.DataAnnotations;
using CreatePdf.NET;
using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using PaperlessServices.Program;
using SWEN3.Paperless.RabbitMq;
using SWEN3.Paperless.RabbitMq.Consuming;
using SWEN3.Paperless.RabbitMq.Models;
using SWEN3.Paperless.RabbitMq.Publishing;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOcrServices(builder.Configuration);

var host = builder.Build();

await host.RunAsync();

namespace PaperlessServices.Program
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddOcrServices(this IServiceCollection services,
            ConfigurationManager configuration)
        {
            services
                .AddPaperlessRabbitMq(configuration)
                .AddHostedService<OcrWorker>()
                .AddOptionsWithValidateOnStart<MinioOptions>()
                .BindConfiguration("Storage:Minio")
                .ValidateDataAnnotations()
                .Services
                .AddSingleton<IMinioClient>(sp =>
                {
                    var options = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
                    return new MinioClient()
                        .WithEndpoint(options.Endpoint)
                        .WithCredentials(options.AccessKey, options.SecretKey)
                        .WithSSL(options.UseSsl)
                        .Build();
                })
                .AddSingleton(new ElasticsearchClient(
                    new ElasticsearchClientSettings(new Uri(configuration["Elasticsearch:Uri"]!))
                        .DefaultIndex(configuration["Elasticsearch:IndexName"]!)
                        .ThrowExceptions()))
                .AddSingleton<IStorageService, StorageService>()
                .AddSingleton<ISearchIndexService, SearchIndexService>()
                .AddSingleton<IOcrService, OcrService>()
                .AddSingleton<IOcrProcessor, OcrProcessor>();

            return services;
        }
    }

    public interface IStorageService
    {
        Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default);
    }

    public class StorageService : IStorageService
    {
        private readonly IMinioClient _minio;
        private readonly IOptions<MinioOptions> _options;
        private readonly ILogger<StorageService> _logger;

        public StorageService(IMinioClient minio, IOptions<MinioOptions> options, ILogger<StorageService> logger)
        {
            _minio = minio;
            _options = options;
            _logger = logger;
        }

        public async Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default)
        {
            var stream = new MemoryStream();
            await _minio.GetObjectAsync(new GetObjectArgs()
                    .WithBucket(_options.Value.BucketName)
                    .WithObject(filePath)
                    .WithCallbackStream(async (s, ct) => await s.CopyToAsync(stream, ct)),
                cancellationToken);

            stream.Position = 0;
            _logger.LogInformation("Downloaded file from storage: {FilePath}", filePath);
            return stream;
        }
    }

    public interface ISearchIndexService
    {
        Task IndexDocumentAsync(Guid id, string fileName, string content, string storagePath,
            CancellationToken cancellationToken = default);
    }

    public class SearchIndexService : ISearchIndexService
    {
        private readonly ElasticsearchClient _elastic;
        private readonly ILogger<SearchIndexService> _logger;

        public SearchIndexService(ElasticsearchClient elastic, ILogger<SearchIndexService> logger)
        {
            _elastic = elastic;
            _logger = logger;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            var index = await _elastic.Indices.ExistsAsync(_elastic.ElasticsearchClientSettings.DefaultIndex,
                cancellationToken);
            if (index.Exists) return;

            await _elastic.Indices.CreateAsync(_elastic.ElasticsearchClientSettings.DefaultIndex, c => c
                    .Mappings(m => m
                        .Properties<object>(p => p
                            .Keyword("id")
                            .Text("fileName")
                            .Text("content")
                            .Keyword("status")
                            .Date("processedAt")
                            .Keyword("storagePath")
                        )
                    ), cancellationToken
            );

            _logger.LogInformation("Created Elasticsearch index: {IndexName}",
                _elastic.ElasticsearchClientSettings.DefaultIndex);
        }

        public async Task IndexDocumentAsync(Guid id, string fileName, string content, string storagePath,
            CancellationToken cancellationToken = default)
        {
            await InitializeAsync(cancellationToken);

            await _elastic.IndexAsync(new
            {
                id, fileName, content, status = "Completed", processedAt = DateTimeOffset.UtcNow, storagePath,
                createdAt = DateTimeOffset.UtcNow
            }, i => i.Id(id.ToString()).Refresh(Refresh.True), cancellationToken);

            _logger.LogInformation("Indexed document {DocumentId} in search index", id);
        }
    }

    public interface IOcrService
    {
        Task<string> ExtractTextAsync(Stream pdfStream);
    }

    public class OcrService : IOcrService
    {
        private readonly ILogger<OcrService> _logger;

        public OcrService(ILogger<OcrService> logger)
        {
            _logger = logger;
        }

        public async Task<string> ExtractTextAsync(Stream pdfStream)
        {
            var text = await Pdf.Load(pdfStream).OcrAsync();
            _logger.LogInformation("Extracted {CharCount} characters from PDF", text.Length);
            return text;
        }
    }

    public interface IOcrProcessor
    {
        Task<OcrEvent> ProcessDocumentAsync(OcrCommand command, CancellationToken cancellationToken = default);
    }

    public class OcrProcessor : IOcrProcessor
    {
        private readonly IStorageService _storageService;
        private readonly IOcrService _ocrService;
        private readonly ISearchIndexService _searchService;
        private readonly ILogger<OcrProcessor> _logger;

        public OcrProcessor(
            IStorageService storageService,
            IOcrService ocrService,
            ISearchIndexService searchService,
            ILogger<OcrProcessor> logger)
        {
            _storageService = storageService;
            _ocrService = ocrService;
            _searchService = searchService;
            _logger = logger;
        }

        public async Task<OcrEvent> ProcessDocumentAsync(OcrCommand command,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Download PDF
                await using var stream = await _storageService.DownloadAsync(command.FilePath, cancellationToken);

                // Extract text
                var text = await _ocrService.ExtractTextAsync(stream);

                // Index document - errors are logged inside the service
                await _searchService.IndexDocumentAsync(command.JobId, command.FileName, text, command.FilePath,
                    cancellationToken);

                _logger.LogInformation("Successfully processed OCR job {JobId}", command.JobId);
                return new OcrEvent(command.JobId, "Completed", text, DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process OCR job {JobId}", command.JobId);
                return new OcrEvent(command.JobId, "Failed", null, DateTimeOffset.UtcNow);
            }
        }
    }

    public class OcrWorker : BackgroundService
    {
        private readonly IRabbitMqConsumerFactory _consumerFactory;
        private readonly IOcrProcessor _processor;
        private readonly IRabbitMqPublisher _publisher;
        private readonly ILogger<OcrWorker> _logger;

        public OcrWorker(IRabbitMqConsumerFactory consumerFactory, IOcrProcessor processorFactory,
            IRabbitMqPublisher publisher, ILogger<OcrWorker> logger)
        {
            _consumerFactory = consumerFactory;
            _processor = processorFactory;
            _publisher = publisher;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await using var consumer = await _consumerFactory.CreateConsumerAsync<OcrCommand>();

            await foreach (var request in consumer.ConsumeAsync(stoppingToken))
            {
                await ProcessMessage(request, consumer, stoppingToken);
            }
        }

        private async Task ProcessMessage(OcrCommand request, IRabbitMqConsumer<OcrCommand> consumer,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Processing OCR job {JobId} for file {FileName}",
                    request.JobId, request.FileName);

                var result = await _processor.ProcessDocumentAsync(request, cancellationToken);
                await _publisher.PublishOcrEventAsync(result);
                await consumer.AckAsync();

                _logger.LogInformation("Published OCR result for job {JobId} with status {Status}",
                    request.JobId, result.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish OCR result for job {JobId}", request.JobId);
                await consumer.NackAsync(requeue: true);
            }
        }
    }

    public class MinioOptions
    {
        [Required(ErrorMessage = "MinIO endpoint is required")]
        public string Endpoint { get; set; } = null!;

        [Required(ErrorMessage = "MinIO access key is required")]
        public string AccessKey { get; set; } = null!;

        [Required(ErrorMessage = "MinIO secret key is required")]
        public string SecretKey { get; set; } = null!;

        [Required(ErrorMessage = "MinIO bucket name is required")]
        public string BucketName { get; set; } = null!;

        public bool UseSsl { get; set; } = false;
    }
}

// // ============== LEAN TEST INFRASTRUCTURE ==============
// #if DEBUG
//
// /// <summary>
// /// Test containers - Resource Reaper handles cleanup automatically.
// /// </summary>
// public sealed class TestContainersManager : IAsyncInitializer
// {
//     private readonly ElasticsearchContainer _elasticsearch = new ElasticsearchBuilder()
//         .WithImage("elasticsearch:9.0.3")
//         .WithEnvironment("xpack.security.enabled", "false")
//         .WithEnvironment("discovery.type", "single-node")
//         .Build();
//
//     private readonly MinioContainer _minio = new MinioBuilder().Build();
//     private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder().Build();
//
//     public async Task InitializeAsync()
//     {
//         await Task.WhenAll(
//             _elasticsearch.StartAsync(),
//             _minio.StartAsync(),
//             _rabbitMq.StartAsync()
//         );
//
//         // Create bucket using the actual configuration
//         var minioClient = new MinioClient()
//             .WithEndpoint(_minio.GetConnectionString().Replace("http://", ""))
//             .WithCredentials(_minio.GetAccessKey(), _minio.GetSecretKey())
//             .WithSSL(false)
//             .Build();
//
//         var bucketName = GetConfiguration()["Storage__Minio__BucketName"]!;
//         if (!await minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName)))
//         {
//             await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
//         }
//     }
//
//     public IReadOnlyDictionary<string, string?> GetConfiguration() => new Dictionary<string, string?>
//     {
//         ["RabbitMQ__Uri"] = $"amqp://guest:guest@{_rabbitMq.Hostname}:{_rabbitMq.GetMappedPublicPort(5672)}",
//         ["Storage__Minio__Endpoint"] = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}",
//         ["Storage__Minio__AccessKey"] = _minio.GetAccessKey(),
//         ["Storage__Minio__SecretKey"] = _minio.GetSecretKey(),
//         ["Storage__Minio__BucketName"] = "test-bucket",
//         ["Storage__Minio__UseSsl"] = "false",
//         ["Elasticsearch__Uri"] = $"http://{_elasticsearch.Hostname}:{_elasticsearch.GetMappedPublicPort(9200)}",
//         ["Elasticsearch__IndexName"] = "test-index"
//     };
// }
//
// /// <summary>
// /// Provides a configured test host for PaperlessServices testing.
// /// </summary>
// public class PaperlessServicesHost : IAsyncInitializer
// {
//     private IHost? _host;
//     
//     [ClassDataSource<TestContainersManager>(Shared = SharedType.PerTestSession)]
//     public required TestContainersManager Containers { get; init; }
//
//     public async Task InitializeAsync()
//     {
//         var builder = Host.CreateApplicationBuilder();
//         builder.Configuration.AddInMemoryCollection(Containers.GetConfiguration());
//         builder.Services.AddOcrServices(builder.Configuration);
//         
//         _host = builder.Build();
//         await _host.StartAsync();
//     }
//
//     // Services accessors
//     public IOcrService OcrService => _host!.Services.GetRequiredService<IOcrService>();
//     public IOcrProcessor OcrProcessor => _host!.Services.GetRequiredService<IOcrProcessor>();
//     public IStorageService StorageService => _host!.Services.GetRequiredService<IStorageService>();
//     public ISearchIndexService SearchIndexService => _host!.Services.GetRequiredService<ISearchIndexService>();
//     public IMinioClient MinioClient => _host!.Services.GetRequiredService<IMinioClient>();
//     public IOptions<MinioOptions> MinioOptions => _host!.Services.GetRequiredService<IOptions<MinioOptions>>();
// }
//
// /// <summary>
// /// PDF test helper - minimal.
// /// </summary>
// public static class PdfTestHelper
// {
//     public static async Task<byte[]> CreatePdfWithTextAsync(string content)
//     {
//         var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pdf");
//         await Pdf.Create(Dye.Black).AddText(content).SaveAsync(tempFile);
//         return await File.ReadAllBytesAsync(tempFile);
//     }
// }
//
// // ============== ACTUAL TESTS ==============
//
// /// <summary>
// /// Storage service tests.
// /// </summary>
// public class StorageServiceTests
// {
//     [ClassDataSource<PaperlessServicesHost>(Shared = SharedType.PerTestSession)]
//     public required PaperlessServicesHost ServicesHost { get; init; }
//
//     [Test]
//     public async Task DownloadAsync_ExistingFile_ReturnsStream()
//     {
//         // Arrange
//         var testData = await PdfTestHelper.CreatePdfWithTextAsync("Test content");
//         var filePath = $"test/{Guid.NewGuid()}.pdf";
//         
//         await ServicesHost.MinioClient.PutObjectAsync(new PutObjectArgs()
//             .WithBucket(ServicesHost.MinioOptions.Value.BucketName)
//             .WithObject(filePath)
//             .WithStreamData(new MemoryStream(testData))
//             .WithObjectSize(testData.Length));
//
//         // Act
//         await using var stream = await ServicesHost.StorageService.DownloadAsync(filePath);
//
//         // Assert
//         await Assert.That(stream.Length).IsGreaterThan(0);
//     }
// }
//
// /// <summary>
// /// OCR service tests.
// /// </summary>
// public class OcrServiceTests
// {
//     [ClassDataSource<PaperlessServicesHost>(Shared = SharedType.PerTestSession)]
//     public required PaperlessServicesHost ServicesHost { get; init; }
//
//     [Test]
//     public async Task ExtractTextAsync_ValidPdf_ExtractsText()
//     {
//         // Arrange
//         var pdfData = await PdfTestHelper.CreatePdfWithTextAsync("Hello OCR World");
//
//         // Act
//         using var stream = new MemoryStream(pdfData);
//         var text = await ServicesHost.OcrService.ExtractTextAsync(stream);
//
//         // Assert
//         await Assert.That(text).Contains("Hello OCR World");
//     }
// }
//
// /// <summary>
// /// Search index service tests.
// /// </summary>
// public class SearchIndexServiceTests
// {
//     [ClassDataSource<PaperlessServicesHost>(Shared = SharedType.PerTestSession)]
//     public required PaperlessServicesHost ServicesHost { get; init; }
//
//     [Test]
//     public async Task IndexDocumentAsync_NewDocument_IndexesSuccessfully()
//     {
//         // Arrange
//         var docId = Guid.NewGuid();
//         const string content = "This is searchable content";
//
//         // Act & Assert (no exception means success)
//         await ServicesHost.SearchIndexService.IndexDocumentAsync(
//             docId, 
//             "test.pdf", 
//             content, 
//             "storage/test.pdf");
//     }
// }
//
// /// <summary>
// /// OCR processor integration tests.
// /// </summary>
// public class OcrProcessorTests
// {
//     [ClassDataSource<PaperlessServicesHost>(Shared = SharedType.PerTestSession)]
//     public required PaperlessServicesHost ServicesHost { get; init; }
//
//     [Test]
//     public async Task ProcessDocumentAsync_ValidCommand_ReturnsCompletedEvent()
//     {
//         // Arrange
//         var pdfData = await PdfTestHelper.CreatePdfWithTextAsync("Process this document");
//         var filePath = $"docs/{Guid.NewGuid()}.pdf";
//         
//         await ServicesHost.MinioClient.PutObjectAsync(new PutObjectArgs()
//             .WithBucket(ServicesHost.MinioOptions.Value.BucketName)
//             .WithObject(filePath)
//             .WithStreamData(new MemoryStream(pdfData))
//             .WithObjectSize(pdfData.Length));
//
//         var command = new OcrCommand(Guid.NewGuid(), "test.pdf", filePath);
//
//         // Act
//         var result = await ServicesHost.OcrProcessor.ProcessDocumentAsync(command);
//
//         // Assert
//         await Assert.That(result.Status).IsEqualTo("Completed");
//         await Assert.That(result.Text).IsNotNull();
//         await Assert.That(result.Text!).Contains("Process this document");
//     }
//
//     [Test]
//     public async Task ProcessDocumentAsync_InvalidPath_ReturnsFailedEvent()
//     {
//         // Arrange
//         var command = new OcrCommand(Guid.NewGuid(), "missing.pdf", "invalid/path.pdf");
//
//         // Act
//         var result = await ServicesHost.OcrProcessor.ProcessDocumentAsync(command);
//
//         // Assert
//         await Assert.That(result.Status).IsEqualTo("Failed");
//         await Assert.That(result.Text).IsNull();
//     }
// }
// #endif