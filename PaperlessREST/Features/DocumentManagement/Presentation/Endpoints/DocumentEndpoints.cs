using PaperlessREST.Features.DocumentManagement.Presentation.Filters;
using PaperlessREST.Host.Extensions;

namespace PaperlessREST.Features.DocumentManagement.Presentation.Endpoints;

/// <summary>
///     API endpoints for document management operations.
/// </summary>
/// <remarks>
///     <para>
///         Documents progress through states: <see cref="DocumentStatus.Pending" /> →
///         <see cref="DocumentStatus.Completed" /> or <see cref="DocumentStatus.Failed" />.
///     </para>
///     <para>
///         OCR processing happens asynchronously via RabbitMQ after upload.
///         AI summarization occurs after OCR completes.
///     </para>
///     <para>
///         Rate limiting policies:
///         <list type="bullet">
///             <item>Read operations: 100 req/min</item>
///             <item>Write operations: 20 req/min</item>
///             <item>Search operations: 60 req/min</item>
///         </list>
///     </para>
/// </remarks>
public static class DocumentEndpoints
{
	/// <summary>
	///     Maps all document management endpoints to the application.
	/// </summary>
	/// <param name="app">The endpoint route builder.</param>
	/// <returns>The endpoint route builder for chaining.</returns>
	public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
	{
		IVersionedEndpointRouteBuilder api = app.NewVersionedApi("Documents");
		RouteGroupBuilder v1docs = api.MapGroup("/api/v{version:apiVersion}/documents")
			.HasApiVersion(1, 0)
			.WithTags("Documents");

		v1docs.MapGet("/", GetDocuments)
			.WithName(nameof(GetDocuments))
			.RequireRateLimiting(RateLimitPolicies.ReadOperations)
			.CacheOutput(CachePolicies.DocumentList);

		v1docs.MapGet("/search", SearchDocuments)
			.WithName(nameof(SearchDocuments))
			.RequireRateLimiting(RateLimitPolicies.SearchOperations);

		v1docs.MapGet("/{id:guid}", GetDocumentById)
			.WithName(nameof(GetDocumentById))
			.RequireRateLimiting(RateLimitPolicies.ReadOperations)
			.CacheOutput(CachePolicies.DocumentById)
			.ProducesGetByIdErrors();

		v1docs.MapGet("/{id:guid}/summary", GetSummary)
			.WithName(nameof(GetSummary))
			.RequireRateLimiting(RateLimitPolicies.ReadOperations)
			.CacheOutput(CachePolicies.DocumentById)
			.ProducesNotFound();

		v1docs.MapPost("/", UploadDocument)
			.WithName(nameof(UploadDocument))
			.Accepts<IFormFile>("multipart/form-data")
			.DisableAntiforgery()
			.RequireRateLimiting(RateLimitPolicies.WriteOperations)
			.ValidatePdfUpload()
			.ProducesDocumentUploadErrors();

		v1docs.MapDelete("/{id:guid}", DeleteDocument)
			.WithName(nameof(DeleteDocument))
			.RequireRateLimiting(RateLimitPolicies.WriteOperations)
			.ProducesDeleteErrors();

		return app;
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// Simple queries - no ErrorOr (can't fail meaningfully)
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	///     Retrieves documents with cursor-based pagination.
	/// </summary>
	/// <param name="pagination">Pagination parameters (pageSize, cursor).</param>
	/// <param name="documentService">The document service for data access.</param>
	/// <param name="cancellationToken">Cancellation token for the operation.</param>
	/// <returns>A paginated response with documents and next page cursor.</returns>
	/// <remarks>
	///     <para>
	///         Uses GUIDv7-based cursor pagination for efficient traversal.
	///         Pass the last document's ID as cursor to get the next page.
	///     </para>
	///     <para>
	///         Retrieves documents directly from PostgreSQL, bypassing Elasticsearch.
	///         Returns metadata only (no content) for performance.
	///     </para>
	/// </remarks>
	/// <response code="200">Returns paginated documents with cursor for next page.</response>
	public static async Task<Ok<PaginatedDocumentsResponse>> GetDocuments(
		[AsParameters] PaginationQuery pagination,
		IDocumentService documentService,
		CancellationToken cancellationToken)
	{
		(List<Document> items, bool hasMore) = await documentService
			.GetDocumentsPagedAsync(pagination.PageSize, pagination.Cursor, cancellationToken);

		PaginatedDocumentsResponse response = new()
		{
			Items = items.ConvertAll(d => d.ToDocumentDto()),
			HasMore = hasMore,
			NextCursor = hasMore && items.Count > 0 ? items[^1].Id : null
		};

		return TypedResults.Ok(response);
	}

	/// <summary>
	///     Searches documents by content using full-text search.
	/// </summary>
	/// <param name="search">Search parameters including query text and result limit.</param>
	/// <param name="documentService">The document service for search operations.</param>
	/// <param name="cancellationToken">Cancellation token for the operation.</param>
	/// <returns>A list of documents matching the search query.</returns>
	/// <remarks>
	///     Performs full-text search on OCR-extracted content using Elasticsearch.
	///     Supports fuzzy matching across all indexed fields.
	///     Only documents that have completed OCR processing are searchable.
	/// </remarks>
	/// <response code="200">Returns matching documents.</response>
	public static async Task<Ok<List<DocumentSearchResultDto>>> SearchDocuments(
		[AsParameters] SearchQuery search,
		IDocumentService documentService,
		CancellationToken cancellationToken)
	{
		List<DocumentSearchResultDto> results = await documentService
			.SearchDocumentsAsync(search.Query, search.Limit, cancellationToken)
			.Select(r => r.ToDocumentSearchResultDto())
			.ToListAsync(cancellationToken);

		return TypedResults.Ok(results);
	}

	// ═══════════════════════════════════════════════════════════════════════════
	// Queries with NotFound - use ToOkOr404
	// Domain error: DocumentErrors.NotFound → ErrorType.NotFound → 404
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	///     Retrieves a specific document by its unique identifier.
	/// </summary>
	/// <param name="id" example="550e8400-e29b-41d4-a716-446655440000">The unique document identifier.</param>
	/// <param name="documentService">The document service for data access.</param>
	/// <param name="cancellationToken">Cancellation token for the operation.</param>
	/// <returns>The document metadata including OCR content if available.</returns>
	/// <remarks>
	///     Queries PostgreSQL directly; does not depend on Elasticsearch availability.
	///     Returns full document metadata including OCR-extracted content.
	/// </remarks>
	/// <response code="200">Returns the requested document.</response>
	/// <response code="404">
	///     Document with the specified ID does not exist.
	///     Domain error: <c>DocumentErrors.NotFound</c>
	/// </response>
	public static Task<Results<Ok<DocumentDto>, NotFound>> GetDocumentById(
		Guid id,
		IDocumentService documentService,
		CancellationToken cancellationToken) =>
		documentService.GetDocumentByIdAsync(id, cancellationToken)
			.ToOkOr404(doc => doc.ToDocumentDto());

	/// <summary>
	///     Retrieves the AI-generated summary for a document.
	/// </summary>
	/// <param name="id" example="550e8400-e29b-41d4-a716-446655440000">The unique document identifier.</param>
	/// <param name="documentService">The document service for data access.</param>
	/// <param name="cancellationToken">Cancellation token for the operation.</param>
	/// <returns>The AI-generated summary, or null if not yet generated.</returns>
	/// <remarks>
	///     Summaries are generated asynchronously by the GenAI microservice after OCR completes.
	///     A null summary indicates the document exists but hasn't been summarized yet.
	/// </remarks>
	/// <response code="200">Returns the document summary (may be null if not yet generated).</response>
	/// <response code="404">
	///     Document with the specified ID does not exist.
	///     Domain error: <c>DocumentErrors.NotFound</c>
	/// </response>
	public static Task<Results<Ok<SummaryDto>, NotFound>> GetSummary(
		Guid id,
		IDocumentService documentService,
		CancellationToken cancellationToken) =>
		documentService.GetDocumentByIdAsync(id, cancellationToken)
			.ToOkOr404(doc => new SummaryDto { Summary = doc.Summary });

	// ═══════════════════════════════════════════════════════════════════════════
	// Commands - use ToAcceptedAtRouteOrProblem / ToNoContentOr404
	// Domain errors:
	//   - DocumentErrors.StorageFailed → ErrorType.Failure → 500
	//   - DocumentErrors.NotFound → ErrorType.NotFound → 404
	// ═══════════════════════════════════════════════════════════════════════════

	/// <summary>
	///     Uploads a PDF document for OCR processing.
	/// </summary>
	/// <param name="request">The upload request containing the PDF file.</param>
	/// <param name="documentService">The document service for upload operations.</param>
	/// <param name="cancellationToken">Cancellation token for the operation.</param>
	/// <returns>The created document metadata with a Location header pointing to the new resource.</returns>
	/// <remarks>
	///     <para>
	///         Workflow: Validates PDF → Stores in MinIO → Creates DB record → Publishes OCR message.
	///     </para>
	///     <para>
	///         Returns 202 Accepted immediately. OCR processing happens asynchronously via RabbitMQ.
	///         The OCR microservice extracts text and indexes it in Elasticsearch.
	///     </para>
	/// </remarks>
	/// <response code="202">
	///     Document accepted for processing. Location header contains the URL to check status.
	/// </response>
	/// <response code="422">
	///     Validation failed. File must be a PDF and not exceed 10MB.
	///     Domain error: <c>ErrorType.Validation</c>
	/// </response>
	/// <response code="500">
	///     Permanent server error during storage (disk full, permissions).
	///     Domain error: <c>DocumentErrors.StorageFailed</c>
	/// </response>
	/// <response code="503">
	///     Infrastructure temporarily unavailable (MinIO, RabbitMQ). Retry after delay.
	///     Domain error: <c>DocumentErrors.StorageUnavailable</c>
	/// </response>
	public static Task<Results<AcceptedAtRoute<CreateDocumentResponse>, ValidationProblem, ProblemHttpResult>>
		UploadDocument(
			[AsParameters] UploadDocumentRequest request,
			IDocumentService documentService,
			CancellationToken cancellationToken) =>
		documentService.UploadDocumentAsync(request, cancellationToken)
			.ToAcceptedAtRouteOrProblem(
				doc => doc.ToCreateDocumentResponse(),
				nameof(GetDocumentById),
				d => new { id = d.Id });

	/// <summary>
	///     Deletes a document from all storage systems.
	/// </summary>
	/// <param name="id" example="550e8400-e29b-41d4-a716-446655440000">The unique document identifier.</param>
	/// <param name="documentService">The document service for delete operations.</param>
	/// <param name="cancellationToken">Cancellation token for the operation.</param>
	/// <returns>No content on successful deletion.</returns>
	/// <remarks>
	///     Removes document from: PostgreSQL database, MinIO object storage, and Elasticsearch index.
	///     Elasticsearch deletion is best-effort; operation succeeds even if search index removal fails.
	/// </remarks>
	/// <response code="204">Document successfully deleted from all storage systems.</response>
	/// <response code="404">
	///     Document with the specified ID does not exist.
	///     Domain error: <c>DocumentErrors.NotFound</c>
	/// </response>
	public static Task<Results<NoContent, NotFound>> DeleteDocument(
		Guid id,
		IDocumentService documentService,
		CancellationToken cancellationToken) =>
		documentService.DeleteDocumentAsync(id, cancellationToken)
			.ToNoContentOr404();
}
