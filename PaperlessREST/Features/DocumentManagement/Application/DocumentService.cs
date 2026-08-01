using Minio.Exceptions;
using Result = ErrorOr.Result;

namespace PaperlessREST.Features.DocumentManagement.Application;

/// <summary>
///     Domain service for document business operations.
/// </summary>
/// <remarks>
///     <para>
///         This service works exclusively with domain objects (Document, DocumentStatus).
///         The API layer is responsible for mapping between domain objects and DTOs.
///     </para>
///     <para>
///         Return type semantics using ErrorOr marker types:
///     </para>
///     <list type="bullet">
///         <item><see cref="ErrorOr{T}" /> where T is entity - operation returns data (GET, POST with body)</item>
///         <item><see cref="ErrorOr{Updated}" /> - mutation of existing resource succeeded</item>
///         <item><see cref="ErrorOr{Deleted}" /> - resource removal succeeded</item>
///     </list>
/// </remarks>
public interface IDocumentService
{
	// ═══════════════════════════════════════════════════════════════════════════
	// Queries - infallible (empty result is valid, not an error)
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	///     Gets documents with cursor-based pagination.
	/// </summary>
	/// <param name="pageSize">Number of documents per page.</param>
	/// <param name="cursor">Last document ID from previous page (null for first page).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Paginated result with documents and pagination metadata.</returns>
	Task<(List<Document> Items, bool HasMore)> GetDocumentsPagedAsync(
		int pageSize,
		Guid? cursor = null,
		CancellationToken cancellationToken = default);

	Task<IReadOnlyCollection<DocumentSearchResult>> SearchDocumentsAsync(
		string query,
		int limit,
		CancellationToken cancellationToken = default);

	// ═══════════════════════════════════════════════════════════════════════════
	// Queries - fallible (resource may not exist)
	// ═══════════════════════════════════════════════════════════════════════════

	[ReturnsError(ErrorType.NotFound, "Document.NotFound")]
	ValueTask<ErrorOr<Document>> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default);

	// ═══════════════════════════════════════════════════════════════════════════
	// Commands - returns entity (needed for response body)
	// ═══════════════════════════════════════════════════════════════════════════

	Task<ErrorOr<Document>> UploadDocumentAsync(
		UploadDocumentRequest request,
		CancellationToken cancellationToken = default);

	// ═══════════════════════════════════════════════════════════════════════════
	// Commands - mutations (Updated = resource modified)
	// ═══════════════════════════════════════════════════════════════════════════

	Task<ErrorOr<Updated>> ProcessOcrResultAsync(
		Guid id,
		string status,
		string? content,
		CancellationToken cancellationToken = default);

	Task<ErrorOr<Updated>> UpdateDocumentSummaryAsync(
		Guid id,
		string summary,
		DateTimeOffset generatedAt,
		CancellationToken cancellationToken = default);

	// ═══════════════════════════════════════════════════════════════════════════
	// Commands - deletion (Deleted = resource removed)
	// ═══════════════════════════════════════════════════════════════════════════

	[ReturnsError(ErrorType.NotFound, "Document.NotFound")]
	Task<ErrorOr<Deleted>> DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class DocumentService(
	IDocumentRepository repository,
	IDocumentStorageService storage,
	IDocumentSearchService search,
	IRabbitMqPublisher publisher,
	TimeProvider timeProvider,
	ILogger<DocumentService> logger) : IDocumentService
{
	public async Task<ErrorOr<Document>> UploadDocumentAsync(
		UploadDocumentRequest request,
		CancellationToken cancellationToken = default)
	{
		var document = Document.CreateFromUpload(request.File.FileName, timeProvider);

		// Storage upload - catch infrastructure exceptions and map to domain errors
		try
		{
			await using var stream = request.File.OpenReadStream();
			await storage.UploadAsync(stream, document.StoragePath, request.File.Length, cancellationToken);
		}
		catch (Exception ex)
		{
			if (TryMapStorageException(ex, cancellationToken) is not { } storageError)
			{
				throw;
			}

			logger.LogWarning(
				ex,
				"Storage error {ErrorCode} for {StoragePath}",
				storageError.Code,
				document.StoragePath);
			return storageError;
		}

		var savedDocument = await repository.AddAsync(document, cancellationToken);

		OcrCommand ocrRequest = new(
			savedDocument.Id,
			savedDocument.FileName,
			savedDocument.StoragePath,
			savedDocument.CreatedAt);
		await publisher.PublishOcrCommandAsync(ocrRequest);

		logger.LogInformation("Document {DocumentId} uploaded successfully", savedDocument.Id);
		return savedDocument;
	}

	public async Task<ErrorOr<Updated>> ProcessOcrResultAsync(
		Guid id,
		string status,
		string? content,
		CancellationToken cancellationToken = default)
	{
		var document = await repository.GetByIdAsync(id, cancellationToken);
		if (document is null)
		{
			logger.LogWarning("Document {DocumentId} not found for OCR result", id);
			return DocumentErrors.NotFound(id);
		}

		var transitionResult = status is "Completed" && content is not null
			? document.MarkAsCompleted(content, timeProvider)
			: document.MarkAsFailed(timeProvider);

		if (transitionResult.IsError)
		{
			logger.LogWarning("Document {DocumentId} state transition failed: {Error}",
				id, transitionResult.FirstError.Description);
			return transitionResult.Errors.ToArray();
		}

		if (!await repository.UpdateAsync(document, cancellationToken))
		{
			logger.LogWarning("Document {DocumentId} not found for OCR result update", id);
			return DocumentErrors.NotFound(id);
		}

		logger.LogInformation("Document {DocumentId} processed with status {Status}", id, document.Status);
		return Result.Updated;
	}

	public async Task<ErrorOr<Updated>> UpdateDocumentSummaryAsync(
		Guid id,
		string summary,
		DateTimeOffset generatedAt,
		CancellationToken cancellationToken = default)
	{
		var updated = await repository.UpdateSummaryAsync(id, summary, generatedAt, cancellationToken);
		if (!updated)
		{
			logger.LogWarning("Document {DocumentId} not found for GenAI summary update", id);
			return DocumentErrors.NotFound(id);
		}

		logger.LogInformation("Document {DocumentId} updated with GenAI summary ({SummaryLength} chars)",
			id, summary.Length);
		return Result.Updated;
	}

	public Task<(List<Document> Items, bool HasMore)> GetDocumentsPagedAsync(
		int pageSize,
		Guid? cursor = null,
		CancellationToken cancellationToken = default) =>
		repository.GetDocumentsPagedAsync(pageSize, cursor, cancellationToken);

	public Task<IReadOnlyCollection<DocumentSearchResult>> SearchDocumentsAsync(
		string query,
		int limit,
		CancellationToken cancellationToken = default) =>
		search.SearchAsync(query, limit, cancellationToken);

	public async ValueTask<ErrorOr<Document>> GetDocumentByIdAsync(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		var document = await repository.GetByIdAsync(id, cancellationToken);
		return document is null
			? DocumentErrors.NotFound(id)
			: document;
	}

	public async Task<ErrorOr<Deleted>> DeleteDocumentAsync(
		Guid id,
		CancellationToken cancellationToken = default)
	{
		if (await repository.GetByIdAsync(id, cancellationToken) is not { } document)
		{
			return DocumentErrors.NotFound(id);
		}

		await search.DeleteAsync(id, cancellationToken);
		await storage.DeleteAsync(document.StoragePath, cancellationToken);
		await repository.DeleteAsync(id, cancellationToken);

		logger.LogInformation("Document {DocumentId} deleted successfully", id);
		return Result.Deleted;
	}

	/// <summary>
	///     Maps storage exceptions to domain errors for proper HTTP status codes.
	/// </summary>
	/// <returns>Domain error for known infrastructure failures, null for unknown exceptions.</returns>
	private static Error? TryMapStorageException(
		Exception exception,
		CancellationToken cancellationToken)
	{
		if (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
		{
			return Error.Custom(503,
				"Document.StorageTimeout",
				"The storage operation timed out.",
				new Dictionary<string, object> { ["retryAfter"] = 30 });
		}

		if (exception is UnexpectedMinioException
		    {
			    Response.Code: "InternalError" or "ServiceUnavailable" or "SlowDown" or "RequestTimeout"
		    })
		{
			return Error.Custom(503,
				"Document.StorageServerError",
				"The storage service is temporarily unavailable.",
				new Dictionary<string, object> { ["retryAfter"] = 30 });
		}

		if (exception is HttpRequestException { StatusCode: null } or ConnectionException)
		{
			return Error.Custom(503,
				"Document.StorageConnectionFailed",
				"The storage service could not be reached.",
				new Dictionary<string, object> { ["retryAfter"] = 30 });
		}

		return null;
	}
}
