using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using FluentValidation;
using JetBrains.Annotations;
using Mapster;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Minio;
using Minio.DataModel.Args;
using PaperlessREST;
using SWEN3.Paperless.RabbitMq;
using SWEN3.Paperless.RabbitMq.Consuming;
using SWEN3.Paperless.RabbitMq.Models;
using SWEN3.Paperless.RabbitMq.Publishing;
using SWEN3.Paperless.RabbitMq.Sse;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestHeaders |
                      HttpLoggingFields.RequestBody |
                      HttpLoggingFields.ResponseStatusCode;
});

builder.Services.AddMapster();
builder.Services.AddProblemDetails();
builder.Services.AddPaperlessServices(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
{
    var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
    await using var db = await contextFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Database migration completed successfully");
}
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpLogging();

app.UseStaticFiles();
app.MapOpenApi();

app.MapOcrEventStream();
app.MapDocumentEndpoints();


await app.RunAsync();

namespace PaperlessREST
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddPaperlessServices(this IServiceCollection services,
            IConfiguration configuration)
        {
            services
                .AddPaperlessRabbitMq(configuration, includeOcrResultStream: true)
                .AddHostedService<OcrResultListener>()
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
                .AddDbContextFactory<DocumentPersistence>((sp, options) =>
                {
                    var connectionString = configuration.GetConnectionString("PaperlessDb");
                    options.UseNpgsql(connectionString, o => o.MapEnum<DocumentStatus>());
                })
                .AddSingleton<IDocumentRepository, DocumentRepository>()
                .AddSingleton<IDocumentStorageService, DocumentStorageService>()
                .AddSingleton<IDocumentSearchService, DocumentSearchService>()
                .AddSingleton<IDocumentService, DocumentService>()
                .AddOpenApi(options =>
                {
                    options.CreateSchemaReferenceId = type =>
                        type.Type.IsEnum ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(type);
                    options.AddDocumentTransformer((document, _, _) =>
                    {
                        document.Info = new OpenApiInfo
                        {
                            Title = "Paperless OCR API",
                            Version = "v1",
                            Description = "API for uploading and processing PDF documents with OCR",
                        };
                        return Task.CompletedTask;
                    });
                })
                .AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton);

            services.AddApiVersioning(opts =>
            {
                opts.DefaultApiVersion = new ApiVersion(1, 0);
                opts.AssumeDefaultVersionWhenUnspecified = true;
            });

            return services;
        }
    }

    public interface IDocumentRepository
    {
        IAsyncEnumerable<Document> GetRecentDocumentsAsync(int limit = 50, CancellationToken cancellationToken = default);
        ValueTask<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default);
        ValueTask<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Document?> UpdateAsync(Document document, CancellationToken cancellationToken = default);
    }

    public class DocumentRepository : IDocumentRepository
    {
        private readonly IDbContextFactory<DocumentPersistence> _contextFactory;
        private readonly ILogger<DocumentRepository> _logger;

        public DocumentRepository(IDbContextFactory<DocumentPersistence> contextFactory, ILogger<DocumentRepository> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async IAsyncEnumerable<Document> GetRecentDocumentsAsync(int limit = 50,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entities = db.Documents
                .OrderByDescending(d => d.CreatedAt)
                .Take(limit)
                .AsAsyncEnumerable();

            await foreach (var entity in entities.WithCancellation(cancellationToken))
            {
                yield return entity.ToDocument();
            }
        }

        public async ValueTask<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await db.Documents.FindAsync([id], cancellationToken);

            return entity?.ToDocument();
        }

        public async Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default)
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var entity = document.ToDocumentEntity();

            db.Documents.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Document {DocumentId} persisted to database", entity.Id);

            return entity.ToDocument();
        }

        public async Task<Document?> UpdateAsync(Document document, CancellationToken cancellationToken = default)
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

            var entity = document.ToDocumentEntity();

            db.Documents.Update(entity);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Document {DocumentId} updated in database", entity.Id);

            return entity.ToDocument();
        }

        public async ValueTask<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await db.Documents.FindAsync([id], cancellationToken);
            if (entity is null) return false;

            db.Documents.Remove(entity);
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Document {DocumentId} removed from database", id);
            return true;
        }
    }

    public interface IDocumentStorageService
    {
        Task<string> UploadAsync(Stream stream, string storagePath, long fileSize,
            CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
    }

    public class DocumentStorageService : IDocumentStorageService
    {
        private readonly IMinioClient _minio;
        private readonly IOptions<MinioOptions> _options;
        private readonly ILogger<DocumentStorageService> _logger;

        public DocumentStorageService(IMinioClient minio, IOptions<MinioOptions> options,
            ILogger<DocumentStorageService> logger)
        {
            _minio = minio;
            _options = options;
            _logger = logger;
        }

        public async Task<string> UploadAsync(Stream stream, string storagePath, long fileSize,
            CancellationToken cancellationToken = default)
        {
            var bucketName = _options.Value.BucketName;

            var bucketExists = await _minio.BucketExistsAsync(
                new BucketExistsArgs().WithBucket(bucketName), cancellationToken);

            if (!bucketExists)
            {
                await _minio.MakeBucketAsync(
                    new MakeBucketArgs().WithBucket(bucketName), cancellationToken);
                _logger.LogInformation("Created storage bucket '{BucketName}'", bucketName);
            }

            await _minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(storagePath)
                .WithStreamData(stream)
                .WithObjectSize(fileSize)
                .WithContentType("application/pdf"), cancellationToken);

            _logger.LogInformation("Document uploaded to storage at {StoragePath}", storagePath);
            return storagePath;
        }

        public async Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
        {
            try
            {
                await _minio.RemoveObjectAsync(new RemoveObjectArgs()
                    .WithBucket(_options.Value.BucketName)
                    .WithObject(storagePath), cancellationToken);

                _logger.LogInformation("Document removed from storage at {StoragePath}", storagePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove document from storage at {StoragePath}", storagePath);
                return false;
            }
        }
    }

    public interface IDocumentSearchService
    {
        IAsyncEnumerable<T> SearchAsync<T>(string query, int limit = 10, CancellationToken cancellationToken = default)
            where T : class;

        Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }

    public class DocumentSearchService : IDocumentSearchService
    {
        private readonly ElasticsearchClient _elastic;
        private readonly ILogger<DocumentSearchService> _logger;

        public DocumentSearchService(ElasticsearchClient elastic, ILogger<DocumentSearchService> logger)
        {
            _elastic = elastic;
            _logger = logger;
        }

        public async IAsyncEnumerable<T> SearchAsync<T>(
            string query,
            int limit = 10,
            [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
        {
            _logger.LogInformation("Searching for query: {Query} (limit: {Limit})", query, limit);

            var response = await _elastic.SearchAsync<T>(s => s
                    .Indices(_elastic.ElasticsearchClientSettings.DefaultIndex)
                    .Query(q => q
                        .QueryString(qs => qs
                            .Query(query)
                            .DefaultField("*")
                            .Type(TextQueryType.BestFields)
                            .Fuzziness(
                                new Fuzziness("AUTO"))
                            .Lenient()
                        )
                    )
                    .Size(limit)
                    .TrackScores()
                , cancellationToken);

     

            if (!response.IsValidResponse)
            {
                _logger.LogError("Search failed: {Error}", response.ElasticsearchServerError?.Error.Reason);
                yield break;
            }

            _logger.LogInformation("Found {Count} results", response.Documents.Count);

            foreach (var doc in response.Documents)
            {
                yield return doc;
            }
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var deleteRequest = new DeleteRequest(_elastic.ElasticsearchClientSettings.DefaultIndex, id.ToString());
            var response = await _elastic.DeleteAsync(deleteRequest, cancellationToken);

            if (!response.IsValidResponse)
            {
                _logger.LogError("Delete failed: {Error}", response.ElasticsearchServerError?.Error.Reason);
                return false;
            }

            _logger.LogInformation("Document {DocumentId} removed from search index", id);
            return true;
        }
    }

    public interface IDocumentService
    {
        IAsyncEnumerable<Document> GetRecentDocumentsAsync(CancellationToken cancellationToken = default);

        IAsyncEnumerable<object> SearchDocumentsAsync(string query, int limit,
            CancellationToken cancellationToken = default);

        ValueTask<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Document> UploadDocumentAsync(IFormFile file, CancellationToken cancellationToken = default);
        Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default);

        Task<bool> ProcessOcrResultAsync(Guid id, string status, string? content, DateTimeOffset processedAt,
            CancellationToken cancellationToken = default);
    }

    public class DocumentService : IDocumentService
    {
        private readonly IDocumentRepository _repository;
        private readonly IDocumentStorageService _storage;
        private readonly IDocumentSearchService _search;
        private readonly IRabbitMqPublisher _publisher;
        private readonly ILogger<DocumentService> _logger;
        private readonly IValidator<IFormFile> _fileValidator;

        public DocumentService(
            IDocumentRepository repository,
            IDocumentStorageService storage,
            IDocumentSearchService search,
            IRabbitMqPublisher publisher,
            ILogger<DocumentService> logger,
            IValidator<IFormFile> fileValidator)
        {
            _repository = repository;
            _storage = storage;
            _search = search;
            _publisher = publisher;
            _logger = logger;
            _fileValidator = fileValidator;
        }

        public async Task<Document> UploadDocumentAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            await _fileValidator.ValidateAndThrowAsync(file, cancellationToken);

            // Use domain factory method
            var document = Document.CreateFromUpload(file.FileName);

            await using var stream = file.OpenReadStream();
            await _storage.UploadAsync(stream, document.StoragePath, file.Length, cancellationToken);

            var savedDocument = await _repository.AddAsync(document, cancellationToken);

            var ocrRequest = new OcrCommand(savedDocument.Id, savedDocument.FileName, savedDocument.StoragePath);
            await _publisher.PublishOcrCommandAsync(ocrRequest);

            _logger.LogInformation("Document {DocumentId} uploaded successfully", savedDocument.Id);
            return savedDocument;
        }

        public IAsyncEnumerable<Document> GetRecentDocumentsAsync(CancellationToken cancellationToken = default)
        {
            return _repository.GetRecentDocumentsAsync(50, cancellationToken);
        }

        public IAsyncEnumerable<object> SearchDocumentsAsync(
            string query, int limit = 10, CancellationToken cancellationToken = default)
        {
            return _search.SearchAsync<object>(query, limit, cancellationToken);
        }

        public ValueTask<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return _repository.GetByIdAsync(id, cancellationToken);
        }

        public async Task<bool> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var document = await _repository.GetByIdAsync(id, cancellationToken);

            if (document is null || !document.CanBeDeleted())
            {
                if (document is not null)
                    _logger.LogWarning("Document {DocumentId} cannot be deleted in {Status} status", id, document.Status);
                return false;
            }

            var tasks = new[]
            {
                _repository.DeleteAsync(id, cancellationToken).AsTask(),
                _storage.DeleteAsync(document.StoragePath, cancellationToken),
                _search.DeleteAsync(id, cancellationToken)
            };

            await Task.WhenAll(tasks);

            _logger.LogInformation("Document {DocumentId} deleted successfully", id);
            return true;
        }

        public async Task<bool> ProcessOcrResultAsync(Guid id, string status, string? content,
            DateTimeOffset processedAt, CancellationToken cancellationToken = default)
        {
            var document = await _repository.GetByIdAsync(id, cancellationToken);
            if (document is null)
            {
                _logger.LogWarning("Document {DocumentId} not found for OCR result", id);
                return false;
            }

            if (status is "Completed" && content is not null)
            {
                document.MarkAsCompleted(content);
            }

            if (status is not "Completed" || content is null)
            {
                document.MarkAsFailed();
            }

            await _repository.UpdateAsync(document, cancellationToken);
            _logger.LogInformation("Document {DocumentId} processed with status {Status}", id, document.Status);
            return true;
        }
    }

// ═══════════════════════════════════════════════════════════════
// API ENDPOINTS
// ═══════════════════════════════════════════════════════════════
    public static class DocumentEndpoints
    {
        public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
        {
            var api = app.NewVersionedApi("Documents");
            var documents = api.MapGroup("/api/v{version:apiVersion}/documents")
                .HasApiVersion(1, 0)
                .WithTags("Documents")
                .WithOpenApi()
                .ProducesProblem(StatusCodes.Status500InternalServerError);

            documents.MapGet("/", GetDocuments)
                .WithName(nameof(GetDocuments))
                .WithSummary("Get recent documents")
                .WithDescription("Retrieves the 50 most recent documents ordered by creation date");

            documents.MapGet("/search", SearchDocuments)
                .WithName(nameof(SearchDocuments))
                .WithSummary("Search documents by content")
                .WithDescription("Full-text search across document content with fuzzy matching");

            documents.MapGet("/{id:guid}", GetDocumentById)
                .WithName(nameof(GetDocumentById))
                .WithSummary("Get document by ID")
                .WithDescription("Retrieves a single document by its unique identifier");

            documents.MapPost("/", UploadDocument)
                .WithName(nameof(UploadDocument))
                .WithSummary("Upload a PDF document")
                .WithDescription("Uploads a PDF document for OCR processing")
                .Accepts<IFormFile>("multipart/form-data")
                .ProducesValidationProblem()
                .DisableAntiforgery();

            documents.MapDelete("/{id:guid}", DeleteDocument)
                .WithName(nameof(DeleteDocument))
                .WithSummary("Delete a document")
                .WithDescription("Removes a document from all storage systems");

            return app;
        }

        private static async Task<Ok<List<DocumentDto>>> GetDocuments(
            IDocumentService documentService,
            CancellationToken cancellationToken)
        {
            var documents = await documentService
                .GetRecentDocumentsAsync(cancellationToken)
                .Select(d => d.ToDocumentDto())
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(documents);
        }

        private static async Task<Ok<List<object>>> SearchDocuments(
            [AsParameters] SearchQuery search,
            IDocumentService documentService,
            CancellationToken cancellationToken)
        {
            var results = await documentService
                .SearchDocumentsAsync(search.Query, search.Limit, cancellationToken)
                .ToListAsync(cancellationToken);

            return TypedResults.Ok(results);
        }

        private static async Task<Results<Ok<DocumentDto>, NotFound>> GetDocumentById(
            [Description("The unique identifier of the document")] [Required]
            Guid id,
            IDocumentService documentService,
            CancellationToken cancellationToken)
        {
            var document = await documentService.GetDocumentByIdAsync(id, cancellationToken);
            if (document is null)
                return TypedResults.NotFound();
            return TypedResults.Ok(document.ToDocumentDto());
        }

        private static async Task<AcceptedAtRoute<CreateDocumentResponse>> UploadDocument(
            [AsParameters] UploadDocumentRequest request,
            IDocumentService documentService,
            CancellationToken cancellationToken)
        {
            var document = await documentService.UploadDocumentAsync(request.File, cancellationToken);
            return TypedResults.AcceptedAtRoute(
                document.ToCreateDocumentResponse(),
                nameof(GetDocumentById),
                new { id = document.Id }
            );
        }

        private static async Task<Results<NoContent, NotFound>> DeleteDocument(
            [Description("The unique identifier of the document to delete")] [Required]
            Guid id,
            IDocumentService documentService,
            CancellationToken cancellationToken)
        {
            var deleted = await documentService.DeleteDocumentAsync(id, cancellationToken);
            if (!deleted)
                return TypedResults.NotFound();
            return TypedResults.NoContent();
        }
    }

// ═══════════════════════════════════════════════════════════════
// VALIDATORS
// ═══════════════════════════════════════════════════════════════

    [UsedImplicitly]
    public class PdfFileValidator : AbstractValidator<IFormFile>
    {
        public PdfFileValidator()
        {
            RuleFor(f => f)
                .NotNull().WithMessage("File is required")
                .Must(f => f.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                .WithMessage("File must have .pdf extension");
        }
    }

    [UsedImplicitly]
    public class UploadDocumentRequestValidator : AbstractValidator<UploadDocumentRequest>
    {
        public UploadDocumentRequestValidator(IValidator<IFormFile> pdfValidator)
        {
            RuleFor(x => x.File).SetValidator(pdfValidator);
        }
    }

// ═══════════════════════════════════════════════════════════════
// OPTIONS
// ═══════════════════════════════════════════════════════════════

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

// ═══════════════════════════════════════════════════════════════
// PERSISTENCE LAYER
// ═══════════════════════════════════════════════════════════════
    public class DocumentPersistence : DbContext
    {
        public DocumentPersistence(DbContextOptions<DocumentPersistence> options)
            : base(options)
        {
        }

        public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresEnum<DocumentStatus>();

            modelBuilder.Entity<DocumentEntity>(entity =>
            {
                entity.ToTable("documents");
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Id)
                    .HasColumnName("id")
                    .IsRequired();

                entity.Property(e => e.FileName)
                    .HasColumnName("file_name")
                    .HasMaxLength(255);

                entity.Property(e => e.Status).HasColumnType("document_status");

                entity.Property(e => e.CreatedAt)
                    .HasColumnName("created_at")
                    .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.StoragePath)
                    .HasColumnName("storage_path")
                    .HasMaxLength(500);

                entity.Property(e => e.Content)
                    .HasColumnName("content")
                    .HasMaxLength(1000000);

                entity.Property(e => e.ProcessedAt)
                    .HasColumnName("processed_at");
            });
        }}

    public class OcrResultListener : BackgroundService
    {
        private readonly IRabbitMqConsumerFactory _consumerFactory;
        private readonly IDocumentService _documentService;
        private readonly ISseStream<OcrEvent> _stream;
        private readonly ILogger<OcrResultListener> _logger;

        public OcrResultListener(
            IRabbitMqConsumerFactory consumerFactory,
            IDocumentService documentService,
            ISseStream<OcrEvent> stream,
            ILogger<OcrResultListener> logger)
        {
            _consumerFactory = consumerFactory;
            _documentService = documentService;
            _stream = stream;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("OCR Result Listener started");

            await using var consumer = await _consumerFactory.CreateConsumerAsync<OcrEvent>();

            await foreach (var result in consumer.ConsumeAsync(stoppingToken))
            {
                await ProcessMessage(result, consumer, stoppingToken);
            }

            _logger.LogInformation("OCR Result Listener stopped");
        }

        private async Task ProcessMessage(OcrEvent result, IRabbitMqConsumer<OcrEvent> consumer,
            CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation(
                    "Received OCR result for job {JobId} with status {Status}",
                    result.JobId,
                    result.Status);

                var content = result.Status is "Completed" ? result.Text : null;
                var processed = await _documentService.ProcessOcrResultAsync(
                    result.JobId,
                    result.Status,
                    content,
                    result.ProcessedAt,
                    cancellationToken);

                if (!processed)
                {
                    await consumer.NackAsync(requeue: false);
                    return;
                }

                _stream.Publish(result);
                await consumer.AckAsync();
                _logger.LogInformation("Successfully processed OCR result for job {JobId}", result.JobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing OCR result for job {JobId}", result.JobId);
                await consumer.NackAsync(requeue: true);
            }
        }
    }
}