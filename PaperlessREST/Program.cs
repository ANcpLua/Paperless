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
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
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
using ValidationException = FluentValidation.ValidationException;

// Additional using statements for your project...

var builder = WebApplication.CreateBuilder(args);

// REMOVED: builder.WebHost.UseStaticWebAssets() is called by default for .NET Web SDK projects.
// This is only needed if you are manually creating a WebHostBuilder.

builder.Services.AddHttpLogging(logging => { logging.LoggingFields = HttpLoggingFields.All; });
builder.Services.AddMapster();

// The defaults from JsonSerializerDefaults.Web are already active.
// This call is still necessary for your customizations (Enum converter and Source Generation).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

// The IProblemDetailsService is registered by default, but this AddProblemDetails call
// is required for your specific customizations (e.g., adding traceId).

// This appears to be for FluentValidation, which is not a default service.
builder.Services.AddValidation();

// AddOpenApi() implicitly calls AddEndpointsApiExplorer(), so the separate call is redundant.
builder.Services.AddOpenApi();
builder.Services.AddPaperlessServices(builder.Configuration);

// REMOVED: builder.Services.AddEndpointsApiExplorer() is called automatically by AddOpenApi().

var app = builder.Build();

// This database migration logic is custom application startup logic, not a default.
await using (var scope = app.Services.CreateAsyncScope())
{
    var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
    await using var db = await contextFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
    app.Logger.LogInformation("Database migration completed successfully");
}

app.UseHttpLogging();
app.UseStaticFiles();
app.MapOpenApi();
app.MapOcrEventStream();
app.MapDocumentEndpoints();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    //To visit the documentation, navigate to https://localhost:{port}/scalar/v1.
    // app.MapScalarApiReference();
}

app.UseExceptionHandler(exApp => exApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

    context.Response.StatusCode = exception switch
    {
        KeyNotFoundException => 404,
        ValidationException => 400,
        _ => 500
    };

    var problem = ProblemDetailsExtensions.CreateProblem(exception, context);
    await problem.ExecuteAsync(context);
}));

await app.RunAsync();

app.UseStatusCodePages();
app.UseHttpLogging();
app.UseStaticFiles();

app.MapOcrEventStream();
app.MapDocumentEndpoints();

await app.RunAsync();

namespace PaperlessREST
{
// ═══════════════════════════════════════════════════════════════
// DATA MODELS WITH INPUT VALIDATION (AUTOMATIC)
// ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Search query with automatic validation
    /// </summary>
    public record SearchQuery
    {
        [Required(ErrorMessage = "Search query is required")]
        [StringLength(100, MinimumLength = 1, ErrorMessage = "Search query must be between 1 and 100 characters")]
        public required string Query { get; init; }

        [Range(1, 100, ErrorMessage = "Limit must be between 1 and 100")]
        public int Limit { get; init; } = 10;
    }

    /// <summary>
    /// Upload request with automatic validation
    /// </summary>
    public class UploadDocumentRequest : IValidatableObject
    {
        [Required(ErrorMessage = "File is required")]
        public IFormFile File { get; set; } = null!;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!File.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                yield return new ValidationResult(
                    "Only PDF files are allowed",
                    [nameof(File)]);
            }
        }
    }

// ═══════════════════════════════════════════════════════════════
// BUSINESS LOGIC VALIDATORS (ASYNC WITH FLUENTVALIDATION)
// ═══════════════════════════════════════════════════════════════

    [UsedImplicitly]
    public class UploadDocumentBusinessValidator : AbstractValidator<UploadDocumentRequest>
    {
        public UploadDocumentBusinessValidator(IDocumentRepository repository)
        {
            RuleFor(x => x.File.FileName)
                .MustAsync(async (fileName, cancellation) =>
                {
                    var exists = await repository.FileNameExistsAsync(fileName, cancellation);
                    return !exists;
                })
                .WithMessage("A document with this filename already exists");
        }
    }

    [UsedImplicitly]
    public class DeleteDocumentBusinessValidator : AbstractValidator<Document>
    {
        public DeleteDocumentBusinessValidator()
        {
            RuleFor(x => x)
                .Must(doc => doc.CanBeDeleted())
                .WithMessage(doc => $"Document cannot be deleted in {doc.Status} status");
        }
    }

// ═══════════════════════════════════════════════════════════════
// SERVICE CONFIGURATION
// ═══════════════════════════════════════════════════════════════

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
                .AddDbContextFactory<DocumentPersistence>((_, options) =>
                {
                    var connectionString = configuration.GetConnectionString("PaperlessDb");
                    options.UseNpgsql(connectionString, o => o.MapEnum<DocumentStatus>());
                })
                .AddSingleton<IDocumentRepository, DocumentRepository>()
                .AddSingleton<IDocumentStorageService, DocumentStorageService>()
                .AddSingleton<IDocumentSearchService, DocumentSearchService>()
                .AddSingleton<IDocumentService, DocumentService>()
                .AddValidatorsFromAssemblyContaining<Program>()
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
                });

            services.AddApiVersioning(opts =>
            {
                opts.DefaultApiVersion = new ApiVersion(1, 0);
                opts.AssumeDefaultVersionWhenUnspecified = true;
            });

            return services;
        }
    }

// ═══════════════════════════════════════════════════════════════
// REPOSITORY LAYER
// ═══════════════════════════════════════════════════════════════

    public interface IDocumentRepository
    {
        IAsyncEnumerable<Document> GetRecentDocumentsAsync(int limit = 50,
            CancellationToken cancellationToken = default);

        ValueTask<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default);
        ValueTask<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Document?> UpdateAsync(Document document, CancellationToken cancellationToken = default);
        Task<bool> FileNameExistsAsync(string fileName, CancellationToken cancellationToken = default);
    }

    public class DocumentRepository : IDocumentRepository
    {
        private readonly IDbContextFactory<DocumentPersistence> _contextFactory;
        private readonly ILogger<DocumentRepository> _logger;

        public DocumentRepository(IDbContextFactory<DocumentPersistence> contextFactory,
            ILogger<DocumentRepository> logger)
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

        public async Task<bool> FileNameExistsAsync(string fileName, CancellationToken cancellationToken = default)
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            return await db.Documents.AnyAsync(d => d.FileName == fileName, cancellationToken);
        }
    }

// ═══════════════════════════════════════════════════════════════
// STORAGE SERVICE
// ═══════════════════════════════════════════════════════════════

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
            await _minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_options.Value.BucketName)
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

// ═══════════════════════════════════════════════════════════════
// SEARCH SERVICE
// ═══════════════════════════════════════════════════════════════

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
                            .Fuzziness(new Fuzziness("AUTO"))
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

// ═══════════════════════════════════════════════════════════════
// DOCUMENT SERVICE WITH ASYNC BUSINESS VALIDATION
// ═══════════════════════════════════════════════════════════════

    public interface IDocumentService
    {
        IAsyncEnumerable<Document> GetRecentDocumentsAsync(CancellationToken cancellationToken = default);

        IAsyncEnumerable<object> SearchDocumentsAsync(string query, int limit,
            CancellationToken cancellationToken = default);

        ValueTask<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default);

        Task<Document> UploadDocumentAsync(UploadDocumentRequest request,
            CancellationToken cancellationToken = default);

        Task DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default);

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
        private readonly IValidator<UploadDocumentRequest> _uploadValidator;
        private readonly IValidator<Document> _deleteValidator;

        public DocumentService(
            IDocumentRepository repository,
            IDocumentStorageService storage,
            IDocumentSearchService search,
            IRabbitMqPublisher publisher,
            ILogger<DocumentService> logger,
            IValidator<UploadDocumentRequest> uploadValidator,
            IValidator<Document> deleteValidator)
        {
            _repository = repository;
            _storage = storage;
            _search = search;
            _publisher = publisher;
            _logger = logger;
            _uploadValidator = uploadValidator;
            _deleteValidator = deleteValidator;
        }

        public async Task<Document> UploadDocumentAsync(UploadDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            // Business validation - throws ValidationException
            await _uploadValidator.ValidateAndThrowAsync(request, cancellationToken);

            var document = Document.CreateFromUpload(request.File.FileName);

            await using var stream = request.File.OpenReadStream();
            await _storage.UploadAsync(stream, document.StoragePath, request.File.Length, cancellationToken);

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

        public async Task DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var document = await _repository.GetByIdAsync(id, cancellationToken);
            if (document is null)
            {
                throw new KeyNotFoundException($"Document {id} not found");
            }

            // Business validation - throws ValidationException
            await _deleteValidator.ValidateAndThrowAsync(document, cancellationToken);

            var tasks = new[]
            {
                _repository.DeleteAsync(id, cancellationToken).AsTask(),
                _storage.DeleteAsync(document.StoragePath, cancellationToken),
                _search.DeleteAsync(id, cancellationToken)
            };

            await Task.WhenAll(tasks);

            _logger.LogInformation("Document {DocumentId} deleted successfully", id);
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
// API ENDPOINTS - SIMPLIFIED
// ═══════════════════════════════════════════════════════════════

    public static class DocumentEndpoints
    {
        public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
        {
            var api = app.NewVersionedApi("Documents");
            var v1documents = api.MapGroup("/api/v{version:apiVersion}/documents")
                .HasApiVersion(1, 0)
                .WithTags("Documents")
                .WithOpenApi();

            v1documents.MapGet("/", GetDocuments)
                .WithName(nameof(GetDocuments))
                .WithSummary("Get recent documents")
                .WithDescription("Retrieves the 50 most recent documents ordered by creation date");

            v1documents.MapGet("/search", SearchDocuments)
                .WithName(nameof(SearchDocuments))
                .WithSummary("Search documents by content")
                .WithDescription("Full-text search across document content with fuzzy matching");

            v1documents.MapGet("/{id:guid}", GetDocumentById)
                .WithName(nameof(GetDocumentById))
                .WithSummary("Get document by ID")
                .WithDescription("Retrieves a single document by its unique identifier");

            v1documents.MapPost("/", UploadDocument)
                .WithName(nameof(UploadDocument))
                .WithSummary("Upload a PDF document")
                .WithDescription("Uploads a PDF document for OCR processing")
                .Accepts<IFormFile>("multipart/form-data")
                .DisableAntiforgery();

            v1documents.MapDelete("/{id:guid}", DeleteDocument)
                .WithName(nameof(DeleteDocument))
                .WithSummary("Delete a document")
                .WithDescription("Removes a document from all storage systems");

            return app;
        }

        private static async Task<Results<Ok<List<DocumentDto>>, InternalServerError<ProblemDetails>>> GetDocuments(
            IDocumentService documentService,
            IHttpContextAccessor httpContextAccessor,
            CancellationToken cancellationToken)
        {
            try
            {
                var documents = await documentService
                    .GetRecentDocumentsAsync(cancellationToken)
                    .Select(d => d.ToDocumentDto())
                    .ToListAsync(cancellationToken);

                return TypedResults.Ok(documents);
            }
            catch (Exception ex)
            {
                return TypedResults.InternalServerError(
                    ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
            }
        }

        private static async Task<Results<Ok<List<object>>, ValidationProblem, InternalServerError<ProblemDetails>>>
            SearchDocuments(
                [AsParameters] SearchQuery search,
                IDocumentService documentService,
                IHttpContextAccessor httpContextAccessor,
                CancellationToken cancellationToken)
        {
            try
            {
                var results = await documentService
                    .SearchDocumentsAsync(search.Query, search.Limit, cancellationToken)
                    .ToListAsync(cancellationToken);

                return TypedResults.Ok(results);
            }
            catch (ValidationException ex)
            {
                return ProblemDetailsExtensions.FluentValidationProblem(ex);
            }
            catch (Exception ex)
            {
                return TypedResults.InternalServerError(
                    ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
            }
        }

        private static async
            Task<Results<Ok<DocumentDto>, NotFound<ProblemDetails>, InternalServerError<ProblemDetails>>>
            GetDocumentById(
                Guid id,
                IDocumentService documentService,
                IHttpContextAccessor httpContextAccessor,
                CancellationToken cancellationToken)
        {
            try
            {
                var document = await documentService.GetDocumentByIdAsync(id, cancellationToken);

                return document is null
                    ? TypedResults.NotFound(ProblemDetailsExtensions
                        .NotFoundProblem("Document", id, new KeyNotFoundException()).Value)
                    : TypedResults.Ok(document.ToDocumentDto());
            }
            catch (Exception ex)
            {
                return TypedResults.InternalServerError(
                    ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
            }
        }

        private static async
            Task<Results<AcceptedAtRoute<CreateDocumentResponse>, ValidationProblem,
                InternalServerError<ProblemDetails>>> UploadDocument(
                [AsParameters] UploadDocumentRequest request,
                IDocumentService documentService,
                IHttpContextAccessor httpContextAccessor,
                CancellationToken cancellationToken)
        {
            try
            {
                var document = await documentService.UploadDocumentAsync(request, cancellationToken);

                return TypedResults.AcceptedAtRoute(
                    document.ToCreateDocumentResponse(),
                    nameof(GetDocumentById),
                    new { id = document.Id });
            }
            catch (ValidationException ex)
            {
                return ProblemDetailsExtensions.FluentValidationProblem(ex);
            }
            catch (Exception ex)
            {
                return TypedResults.InternalServerError(
                    ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
            }
        }

        private static async
            Task<Results<Ok<DeleteResponse>, NotFound<ProblemDetails>, ValidationProblem,
                InternalServerError<ProblemDetails>>> DeleteDocument(
                Guid id,
                IDocumentService documentService,
                IHttpContextAccessor httpContextAccessor,
                CancellationToken cancellationToken)
        {
            try
            {
                await documentService.DeleteDocumentAsync(id, cancellationToken);

                return TypedResults.Ok(new DeleteResponse
                {
                    Message = "Document deleted successfully",
                    DocumentId = id,
                    DeletedAt = DateTimeOffset.UtcNow
                });
            }
            catch (KeyNotFoundException ex)
            {
                return TypedResults.NotFound(ProblemDetailsExtensions.NotFoundProblem("Document", id, ex).Value);
            }
            catch (ValidationException ex)
            {
                return ProblemDetailsExtensions.FluentValidationProblem(ex);
            }
            catch (Exception ex)
            {
                return TypedResults.InternalServerError(
                    ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
            }
        }
    }

    public record DeleteResponse
    {
        public string Message { get; init; } = null!;
        public Guid DocumentId { get; init; }
        public DateTimeOffset DeletedAt { get; init; }
    }
// ═══════════════════════════════════════════════════════════════
// OPTIONS WITH VALIDATION
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
        }
    }

// ═══════════════════════════════════════════════════════════════
// BACKGROUND SERVICE
// ═══════════════════════════════════════════════════════════════

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

public static class ProblemDetailsExtensions
{
    private readonly record struct ExceptionInfo(
        string Type,
        string Message,
        string? StackTrace,
        string? InnerException);

    public static ProblemHttpResult CreateProblem(Exception? exception, HttpContext context)
    {
        exception ??= new Exception("An unknown error occurred");

        var (statusCode, title) = exception switch
        {
            KeyNotFoundException => (404, "Resource not found"),
            ValidationException => (400, "Validation failed"),
            OperationCanceledException => (499, "Request cancelled"),
            _ => (500, "Internal server error")
        };

        var extensions = new Dictionary<string, object?>
        {
            ["exception"] = new ExceptionInfo(
                exception.GetType().Name,
                exception.Message,
                exception.StackTrace,
                exception.InnerException?.Message),
            ["traceId"] = context.TraceIdentifier
        };

        if (exception is ValidationException validationEx && validationEx.Errors.Any())
        {
            extensions["errors"] = validationEx.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());
        }

        return TypedResults.Problem(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = context.Request.Path,
            Extensions = extensions
        });
    }

    public static NotFound<ProblemDetails> NotFoundProblem(
        string resourceName,
        Guid id,
        Exception exception) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = $"{resourceName} not found",
            Detail = $"{resourceName} with ID {id} was not found",
            Status = 404,
            Extensions = new Dictionary<string, object?>
            {
                ["exception"] = new ExceptionInfo(
                    exception.GetType().Name,
                    exception.Message,
                    exception.StackTrace,
                    exception.InnerException?.Message)
            }
        });

    public static ValidationProblem FluentValidationProblem(ValidationException exception) =>
        TypedResults.ValidationProblem(
            exception.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray()),
            detail: exception.Message);
}

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(Document))]
[JsonSerializable(typeof(DocumentDto))]
[JsonSerializable(typeof(CreateDocumentResponse))]
[JsonSerializable(typeof(SearchQuery))]
[JsonSerializable(typeof(UploadDocumentRequest))]
[JsonSerializable(typeof(List<DocumentDto>))]
[JsonSerializable(typeof(List<object>))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
[JsonSerializable(typeof(ProblemDetails))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;

//
//
// /// <summary>
// /// Endpoints for document management operations
// /// </summary>
// public static class DocumentEndpoints
// {
//     /// <summary>
//     /// Maps all document-related API endpoints
//     /// </summary>
//     /// <param name="app">The endpoint route builder</param>
//     /// <returns>The configured endpoint route builder</returns>
//     public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
//     {
//         var api = app.NewVersionedApi("Documents");
//         var v1documents = api.MapGroup("/api/v{version:apiVersion}/documents")
//             .HasApiVersion(1, 0)
//             .WithTags("Documents")
//             .WithOpenApi();
//
//         v1documents.MapGet("/", GetDocuments)
//             .WithName(nameof(GetDocuments));
//
//         v1documents.MapGet("/search", SearchDocuments)
//             .WithName(nameof(SearchDocuments));
//
//         v1documents.MapGet("/{id:guid}", GetDocumentById)
//             .WithName(nameof(GetDocumentById));
//
//         v1documents.MapPost("/", UploadDocument)
//             .WithName(nameof(UploadDocument))
//             .Accepts<IFormFile>("multipart/form-data")
//             .DisableAntiforgery();
//
//         v1documents.MapDelete("/{id:guid}", DeleteDocument)
//             .WithName(nameof(DeleteDocument));
//
//         return app;
//     }
//
//     /// <summary>
//     /// Retrieves the 50 most recent documents ordered by creation date
//     /// </summary>
//     /// <returns>A list of document DTOs</returns>
//     /// <response code="200">Returns the list of documents</response>
//     /// <response code="500">Internal server error</response>
//     private static async Task<Results<Ok<List<DocumentDto>>, InternalServerError<ProblemDetails>>> GetDocuments(
//         IDocumentService documentService,
//         IHttpContextAccessor httpContextAccessor,
//         CancellationToken cancellationToken)
//     {
//         try
//         {
//             var documents = await documentService
//                 .GetRecentDocumentsAsync(cancellationToken)
//                 .Select(d => d.ToDocumentDto())
//                 .ToListAsync(cancellationToken);
//
//             return TypedResults.Ok(documents);
//         }
//         catch (Exception ex)
//         {
//             return TypedResults.InternalServerError(
//                 ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
//         }
//     }
//
//     /// <summary>
//     /// Performs full-text search across document content with fuzzy matching
//     /// </summary>
//     /// <param name="search">Search query parameters</param>
//     /// <param name="documentService">Document service instance</param>
//     /// <param name="httpContextAccessor">HTTP context accessor</param>
//     /// <param name="cancellationToken">Cancellation token</param>
//     /// <returns>List of matching documents</returns>
//     /// <response code="200">Returns search results</response>
//     /// <response code="400">Invalid search parameters</response>
//     /// <response code="500">Internal server error</response>
//     private static async Task<Results<Ok<List<object>>, ValidationProblem, InternalServerError<ProblemDetails>>> SearchDocuments(
//         [AsParameters] SearchQuery search,
//         IDocumentService documentService,
//         IHttpContextAccessor httpContextAccessor,
//         CancellationToken cancellationToken)
//     {
//         try
//         {
//             var results = await documentService
//                 .SearchDocumentsAsync(search.Query, search.Limit, cancellationToken)
//                 .ToListAsync(cancellationToken);
//
//             return TypedResults.Ok(results);
//         }
//         catch (ValidationException ex)
//         {
//             return ProblemDetailsExtensions.FluentValidationProblem(ex);
//         }
//         catch (Exception ex)
//         {
//             return TypedResults.InternalServerError(
//                 ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
//         }
//     }
//
//     /// <summary>
//     /// Retrieves a single document by its unique identifier
//     /// </summary>
//     /// <param name="id">The unique identifier of the document</param>
//     /// <param name="documentService">Document service instance</param>
//     /// <param name="httpContextAccessor">HTTP context accessor</param>
//     /// <param name="cancellationToken">Cancellation token</param>
//     /// <returns>The requested document</returns>
//     /// <response code="200">Returns the requested document</response>
//     /// <response code="404">Document not found</response>
//     /// <response code="500">Internal server error</response>
//     private static async Task<Results<Ok<DocumentDto>, NotFound<ProblemDetails>, InternalServerError<ProblemDetails>>> GetDocumentById(
//         Guid id,
//         IDocumentService documentService,
//         IHttpContextAccessor httpContextAccessor,
//         CancellationToken cancellationToken)
//     {
//         try
//         {
//             var document = await documentService.GetDocumentByIdAsync(id, cancellationToken);
//
//             return document is null
//                 ? TypedResults.NotFound(ProblemDetailsExtensions.NotFoundProblem("Document", id, new KeyNotFoundException()).Value)
//                 : TypedResults.Ok(document.ToDocumentDto());
//         }
//         catch (Exception ex)
//         {
//             return TypedResults.InternalServerError(
//                 ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
//         }
//     }
//
//     /// <summary>
//     /// Uploads a PDF document for OCR processing
//     /// </summary>
//     /// <param name="request">Upload request containing the PDF file</param>
//     /// <param name="documentService">Document service instance</param>
//     /// <param name="httpContextAccessor">HTTP context accessor</param>
//     /// <param name="cancellationToken">Cancellation token</param>
//     /// <returns>Accepted response with document location</returns>
//     /// <response code="202">Document accepted for processing</response>
//     /// <response code="400">Validation failed (invalid file type or duplicate filename)</response>
//     /// <response code="500">Internal server error</response>
//     private static async Task<Results<AcceptedAtRoute<CreateDocumentResponse>, ValidationProblem, InternalServerError<ProblemDetails>>> UploadDocument(
//         [AsParameters] UploadDocumentRequest request,
//         IDocumentService documentService,
//         IHttpContextAccessor httpContextAccessor,
//         CancellationToken cancellationToken)
//     {
//         try
//         {
//             var document = await documentService.UploadDocumentAsync(request, cancellationToken);
//
//             return TypedResults.AcceptedAtRoute(
//                 document.ToCreateDocumentResponse(),
//                 nameof(GetDocumentById),
//                 new { id = document.Id });
//         }
//         catch (ValidationException ex)
//         {
//             return ProblemDetailsExtensions.FluentValidationProblem(ex);
//         }
//         catch (Exception ex)
//         {
//             return TypedResults.InternalServerError(
//                 ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
//         }
//     }
//
//     /// <summary>
//     /// Removes a document from all storage systems
//     /// </summary>
//     /// <param name="id">The unique identifier of the document to delete</param>
//     /// <param name="documentService">Document service instance</param>
//     /// <param name="httpContextAccessor">HTTP context accessor</param>
//     /// <param name="cancellationToken">Cancellation token</param>
//     /// <returns>Confirmation of deletion</returns>
//     /// <response code="200">Document deleted successfully</response>
//     /// <response code="404">Document not found</response>
//     /// <response code="422">Document cannot be deleted in current status</response>
//     /// <response code="500">Internal server error</response>
//     private static async Task<Results<Ok<DeleteResponse>, NotFound<ProblemDetails>, ValidationProblem, InternalServerError<ProblemDetails>>> DeleteDocument(
//         Guid id,
//         IDocumentService documentService,
//         IHttpContextAccessor httpContextAccessor,
//         CancellationToken cancellationToken)
//     {
//         try
//         {
//             await documentService.DeleteDocumentAsync(id, cancellationToken);
//
//             return TypedResults.Ok(new DeleteResponse
//             {
//                 Message = "Document deleted successfully",
//                 DocumentId = id,
//                 DeletedAt = DateTimeOffset.UtcNow
//             });
//         }
//         catch (KeyNotFoundException ex)
//         {
//             return TypedResults.NotFound(ProblemDetailsExtensions.NotFoundProblem("Document", id, ex).Value);
//         }
//         catch (ValidationException ex)
//         {
//             return ProblemDetailsExtensions.FluentValidationProblem(ex);
//         }
//         catch (Exception ex)
//         {
//             return TypedResults.InternalServerError(
//                 ProblemDetailsExtensions.CreateProblem(ex, httpContextAccessor.HttpContext!).ProblemDetails);
//         }
//     }
// }
//
// /// <summary>
// /// Response returned after successful document deletion
// /// </summary>
// public record DeleteResponse
// {
//     /// <summary>
//     /// Confirmation message
//     /// </summary>
//     /// <example>Document deleted successfully</example>
//     public string Message { get; init; } = null!;
//     
//     /// <summary>
//     /// The ID of the deleted document
//     /// </summary>
//     /// <example>550e8400-e29b-41d4-a716-446655440000</example>
//     public Guid DocumentId { get; init; }
//     
//     /// <summary>
//     /// Timestamp when the document was deleted
//     /// </summary>
//     /// <example>2024-01-15T10:30:00Z</example>
//     public DateTimeOffset DeletedAt { get; init; }
// }
//
// /// <summary>
// /// Search query parameters for document search
// /// </summary>
// public record SearchQuery
// {
//     /// <summary>
//     /// The search query string
//     /// </summary>
//     /// <example>invoice 2024</example>
//     [Required(ErrorMessage = "Search query is required")]
//     [StringLength(100, MinimumLength = 1, ErrorMessage = "Search query must be between 1 and 100 characters")]
//     public required string Query { get; init; }
//
//     /// <summary>
//     /// Maximum number of results to return
//     /// </summary>
//     /// <example>10</example>
//     [Range(1, 100, ErrorMessage = "Limit must be between 1 and 100")]
//     public int Limit { get; init; } = 10;
// }
//
// /// <summary>
// /// Request for uploading a PDF document
// /// </summary>
// public class UploadDocumentRequest
// {
//     /// <summary>
//     /// The PDF file to upload
//     /// </summary>
//     [Required(ErrorMessage = "File is required")]
//     public IFormFile File { get; set; } = null!;
// }