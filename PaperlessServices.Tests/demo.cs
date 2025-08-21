// using System.ComponentModel.DataAnnotations;
// using System.Text;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Configuration;
// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Options;
// using Minio;
// using Minio.DataModel.Args;
// using Testcontainers.Minio;
// using Testcontainers.PostgreSql;
// using Xunit;
//
// namespace PaperlessServices.Tests;
//
// #region Domain and Infrastructure
//
// public class Document
// {
//     public Guid Id { get; private set; }
//     public string FileName { get; private set; }
//     public string StoragePath { get; private set; }
//     public DateTime CreatedAt { get; private set; }
//
//     private Document()
//     {
//     }
//
//     public static Document Create(string? fileName)
//     {
//         var id = Guid.NewGuid();
//         return new Document
//         {
//             Id = id,
//             FileName = fileName,
//             StoragePath = $"{id}/{fileName}",
//             CreatedAt = DateTime.UtcNow
//         };
//     }
// }
//
// public class DocumentEntity
// {
//     public Guid Id { get; set; }
//     public string? FileName { get; set; }
//     public string? StoragePath { get; set; }
//     public DateTime CreatedAt { get; set; }
// }
//
// public class DocumentDbContext : DbContext
// {
//     public DocumentDbContext(DbContextOptions<DocumentDbContext> options) : base(options)
//     {
//     }
//
//     public DbSet<DocumentEntity> Documents { get; set; } = null!;
// }
//
// public interface IDocumentRepository
// {
//     Task<Document> AddAsync(Document document, CancellationToken ct = default);
// }
//
// public interface IDocumentStorageService
// {
//     Task UploadAsync(Stream stream, string storagePath, long fileSize, CancellationToken ct = default);
// }
//
// public interface IDocumentService
// {
//     Task<Document> UploadDocumentAsync(string fileName, Stream content, CancellationToken ct = default);
// }
//
// public class DocumentRepository : IDocumentRepository
// {
//     private readonly IDbContextFactory<DocumentDbContext> _contextFactory;
//     public DocumentRepository(IDbContextFactory<DocumentDbContext> contextFactory) => _contextFactory = contextFactory;
//
//     public async Task<Document> AddAsync(Document document, CancellationToken ct = default)
//     {
//         await using var db = await _contextFactory.CreateDbContextAsync(ct);
//         var entity = new DocumentEntity
//         {
//             Id = document.Id, FileName = document.FileName, StoragePath = document.StoragePath,
//             CreatedAt = document.CreatedAt
//         };
//         db.Documents.Add(entity);
//         await db.SaveChangesAsync(ct);
//         return document;
//     }
//
//     public async Task<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
//     {
//         await using var db = await _contextFactory.CreateDbContextAsync(ct);
//         var entity = await db.Documents.FindAsync([id], ct);
//         return entity == null ? null : Document.Create(entity.FileName);
//     }
// }
//
// public class DocumentStorageService : IDocumentStorageService
// {
//     private readonly IMinioClient _minio;
//     private readonly IOptions<MinioOptions> _options;
//
//     public DocumentStorageService(IMinioClient minio, IOptions<MinioOptions> options)
//     {
//         _minio = minio;
//         _options = options;
//     }
//
//     public async Task UploadAsync(Stream stream, string storagePath, long fileSize, CancellationToken ct = default)
//     {
//         await _minio.PutObjectAsync(new PutObjectArgs()
//             .WithBucket(_options.Value.BucketName)
//             .WithObject(storagePath)
//             .WithStreamData(stream)
//             .WithObjectSize(fileSize), ct);
//     }
// }
//
// public class DocumentService : IDocumentService
// {
//     private readonly IDocumentRepository _repository;
//     private readonly IDocumentStorageService _storage;
//
//     public DocumentService(IDocumentRepository repository, IDocumentStorageService storage)
//     {
//         _repository = repository;
//         _storage = storage;
//     }
//
//     public async Task<Document> UploadDocumentAsync(string fileName, Stream content, CancellationToken ct = default)
//     {
//         var document = Document.Create(fileName);
//         await _storage.UploadAsync(content, document.StoragePath, content.Length, ct);
//         await _repository.AddAsync(document, ct);
//         return document;
//     }
// }
//
// public class MinioOptions
// {
//     [Required] public string Endpoint { get; set; } = null!;
//     [Required] public string AccessKey { get; set; } = null!;
//     [Required] public string SecretKey { get; set; } = null!;
//     [Required] public string BucketName { get; set; } = "paperless";
//     public bool UseSsl { get; set; }
// }
//
// public static class TestServiceCollectionExtensions
// {
//     public static IServiceCollection AddPaperlessTestServices(this IServiceCollection services, IConfiguration config)
//     {
//         services.AddOptions<MinioOptions>().Bind(config.GetSection("Storage:Minio"));
//
//         services.AddSingleton<IMinioClient>(sp =>
//         {
//             var opt = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
//             return new MinioClient()
//                 .WithEndpoint(opt.Endpoint)
//                 .WithCredentials(opt.AccessKey, opt.SecretKey)
//                 .WithSSL(opt.UseSsl)
//                 .Build();
//         });
//
//         services.AddDbContextFactory<DocumentDbContext>(opts =>
//             opts.UseNpgsql(config.GetConnectionString("PaperlessDb")));
//
//         services.AddSingleton<IDocumentRepository, DocumentRepository>();
//         services.AddSingleton<IDocumentStorageService, DocumentStorageService>();
//         services.AddSingleton<IDocumentService, DocumentService>();
//
//         return services;
//     }
// }
//
// #endregion
//
// public class PaperlessFixture : IAsyncLifetime
// {
//     private PostgreSqlContainer _dbContainer = null!;
//     private MinioContainer _minioContainer = null!;
//
//     public IServiceProvider Services { get; private set; } = null!;
//
//     public async ValueTask InitializeAsync()
//     {
//         var projectRoot = FindProjectDirectory();
//
//         var initialConfig = new ConfigurationBuilder()
//             .SetBasePath(projectRoot)
//             .AddJsonFile("appsettings.json", optional: false)
//             .Build();
//
//         var minioOptionsFromConfig = initialConfig.GetSection("Storage:Minio").Get<MinioOptions>()
//                                      ?? throw new InvalidOperationException(
//                                          "MinIO configuration section is missing in appsettings.json");
//
//         _dbContainer = new PostgreSqlBuilder().Build();
//
//         _minioContainer = new MinioBuilder()
//             .WithUsername(minioOptionsFromConfig.AccessKey)
//             .WithPassword(minioOptionsFromConfig.SecretKey)
//             .Build();
//
//         await Task.WhenAll(_dbContainer.StartAsync(), _minioContainer.StartAsync());
//
//         var dynamicConfigOverrides = new Dictionary<string, string?>
//         {
//             { "ConnectionStrings:PaperlessDb", _dbContainer.GetConnectionString() },
//             { "Storage:Minio:Endpoint", _minioContainer.GetConnectionString().Replace("http://", "") }
//         };
//
//         var finalConfig = new ConfigurationBuilder()
//             .AddConfiguration(initialConfig)
//             .AddInMemoryCollection(dynamicConfigOverrides)
//             .Build();
//
//         var services = new ServiceCollection();
//         services.AddSingleton<IConfiguration>(finalConfig);
//         services.AddPaperlessTestServices(finalConfig);
//         Services = services.BuildServiceProvider();
//
//         await using var scope = Services.CreateAsyncScope();
//         var dbContext = scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
//         await dbContext.Database.EnsureCreatedAsync();
//
//         var minioClient = scope.ServiceProvider.GetRequiredService<IMinioClient>();
//         var minioOptions = scope.ServiceProvider.GetRequiredService<IOptions<MinioOptions>>().Value;
//         await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(minioOptions.BucketName));
//     }
//
//     private static string FindProjectDirectory()
//     {
//         var directory = new DirectoryInfo(AppContext.BaseDirectory);
//         while (directory != null && !directory.GetFiles("*.csproj").Any())
//         {
//             directory = directory.Parent;
//         }
//
//         return directory?.FullName
//                ?? throw new DirectoryNotFoundException("Could not find the project root directory.");
//     }
//
//     public async ValueTask DisposeAsync()
//     {
//         await Task.WhenAll(
//             _dbContainer?.DisposeAsync().AsTask() ?? Task.CompletedTask,
//             _minioContainer?.DisposeAsync().AsTask() ?? Task.CompletedTask);
//     }
// }
//
// public abstract class BaseIntegrationTest : IClassFixture<PaperlessFixture>, IAsyncLifetime
// {
//     private readonly PaperlessFixture _fixture;
//     protected AsyncServiceScope Scope { get; private set; }
//
//     protected BaseIntegrationTest(PaperlessFixture fixture)
//     {
//         _fixture = fixture;
//     }
//
//     public async ValueTask InitializeAsync()
//     {
//         Scope = _fixture.Services.CreateAsyncScope();
//         await OnInitializeAsync();
//     }
//
//     public async ValueTask DisposeAsync()
//     {
//         await Scope.DisposeAsync();
//     }
//
//     protected virtual ValueTask OnInitializeAsync() => ValueTask.CompletedTask;
// }
//
// public class DocumentServiceTests : BaseIntegrationTest
// {
//     private IDocumentService _documentService = null!;
//     private DocumentDbContext _dbContext = null!;
//     private IMinioClient _minioClient = null!;
//     private IOptions<MinioOptions> _minioOptions = null!;
//
//     public DocumentServiceTests(PaperlessFixture fixture) : base(fixture)
//     {
//     }
//
//     protected override ValueTask OnInitializeAsync()
//     {
//         _documentService = Scope.ServiceProvider.GetRequiredService<IDocumentService>();
//         _dbContext = Scope.ServiceProvider.GetRequiredService<DocumentDbContext>();
//         _minioClient = Scope.ServiceProvider.GetRequiredService<IMinioClient>();
//         _minioOptions = Scope.ServiceProvider.GetRequiredService<IOptions<MinioOptions>>();
//         return ValueTask.CompletedTask;
//     }
//
//     [Fact]
//     public async Task UploadDocumentAsync_WhenInvoked_PersistsDocumentToDatabaseAndStorage()
//     {
//         // Arrange
//         const string fileName = "professional-test.pdf";
//         const string fileContent = "This is a test.";
//         await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fileContent));
//
//         // Act
//         var createdDocument = await _documentService.UploadDocumentAsync(fileName, stream, CancellationToken.None);
//
//         // Assert
//         var dbDocument =
//             await _dbContext.Documents.FindAsync([createdDocument.Id], TestContext.Current.CancellationToken);
//         Assert.NotNull(dbDocument);
//         Assert.Equal(fileName, dbDocument.FileName);
//
//         var objectStat = await _minioClient.StatObjectAsync(new StatObjectArgs()
//             .WithBucket(_minioOptions.Value.BucketName)
//             .WithObject(dbDocument.StoragePath), TestContext.Current.CancellationToken);
//         Assert.NotNull(objectStat);
//         Assert.Equal(stream.Length, objectStat.Size);
//     }
// }

