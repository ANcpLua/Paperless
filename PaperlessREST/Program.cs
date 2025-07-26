using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using FluentValidation;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using PaperlessREST;
using PaperlessREST.Extensions;
using PaperlessREST.Services;
using SWEN3.Paperless.RabbitMq.Consuming;
using SWEN3.Paperless.RabbitMq.Models;
using SWEN3.Paperless.RabbitMq.Sse;

var builder = WebApplication.CreateBuilder(args);

builder.AddDependencies();

var app = builder.Build();

await app.InitializeApplicationAsync();

app.ConfigureMiddleware();
app.MapEndpoints();

await app.RunAsync();

namespace PaperlessREST
{
    public interface IDocumentRepository
    {
        IAsyncEnumerable<Document> GetRecentDocumentsAsync(int limit,
            CancellationToken cancellationToken = default);

        ValueTask<Document?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<Document> AddAsync(Document document, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
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

        public async IAsyncEnumerable<Document> GetRecentDocumentsAsync(int limit,
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

        public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
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

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IProblemDetailsService _problemDetailsService;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment environment,
        IProblemDetailsService problemDetailsService)
    {
        _logger = logger;
        _environment = environment;
        _problemDetailsService = problemDetailsService;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Exception occurred: {Message}", exception.Message);

        var problemDetails = CreateProblemDetails(httpContext, exception);

        httpContext.Response.StatusCode = problemDetails.Status!.Value;

        await _problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception
        });

        return true;
    }

    private ProblemDetails CreateProblemDetails(HttpContext httpContext, Exception exception)
    {
        // Map known exceptions to appropriate HTTP status codes. With minimal API validation
        // enabled, System.ComponentModel.DataAnnotations.ValidationException is no longer
        // thrown by the pipeline. However, business‑level validation failures thrown by
        // FluentValidation should still result in a 400 Bad Request response.
        var statusCode = exception switch
        {
            KeyNotFoundException => StatusCodes.Status404NotFound,
            FluentValidation.ValidationException => StatusCodes.Status400BadRequest,
            OperationCanceledException => StatusCodes.Status499ClientClosedRequest,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            TimeoutException => StatusCodes.Status408RequestTimeout,
            NotImplementedException => StatusCodes.Status501NotImplemented,
            _ => StatusCodes.Status500InternalServerError
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Type = $"https://httpstatuses.io/{statusCode}",
            Title = ReasonPhrases.GetReasonPhrase(statusCode),
            Detail = _environment.IsDevelopment() ? exception.Message : GetProductionErrorMessage(exception),
            Instance = httpContext.Request.Path
        };

        // Populate error details for FluentValidation exceptions. DataAnnotations
        // validation errors are handled by the built‑in validation pipeline and won't reach
        // this handler.
        if (exception is FluentValidation.ValidationException validationException && validationException.Errors.Any())
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray()
                );
        }

        // In development, include additional debugging information to aid troubleshooting.
        if (_environment.IsDevelopment())
        {
            problemDetails.Extensions["debugInfo"] = new
            {
                exception = exception.GetType().Name,
                message = exception.Message,
                stackTrace = exception.StackTrace,
                innerException = exception.InnerException?.Message
            };
        }

        return problemDetails;
    }

    private static string GetProductionErrorMessage(Exception exception) => exception switch
    {
        KeyNotFoundException => "The requested resource was not found.",
        // When FluentValidation throws, expose a generic message to avoid leaking implementation details.
        FluentValidation.ValidationException => "The request contains invalid data.",
        OperationCanceledException => "The request was cancelled.",
        UnauthorizedAccessException => "You are not authorized to access this resource.",
        TimeoutException => "The request timed out.",
        NotImplementedException => "This feature is not implemented.",
        _ => "An error occurred while processing your request."
    };
}

public record SearchQuery
{
    [Required(ErrorMessage = "Search query is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Search query must be between 1 and 100 characters")]
    public required string Query { get; init; }

    [Range(1, 100, ErrorMessage = "Limit must be between 1 and 100")]
    public int Limit { get; init; } = 10;
}

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

        if (File.Length > 50 * 1024 * 1024)
        {
            yield return new ValidationResult(
                "File size must not exceed 50MB",
                [nameof(File)]);
        }
    }
}

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