namespace PaperlessREST.Features.DocumentManagement.Presentation.Endpoints;

/// <summary>
///     Document management endpoints. ErrorOrX's source generator turns these attributed static
///     handlers into versioned Minimal API routes (via <c>MapErrorOrEndpoints()</c>) and maps each
///     handler's <see cref="ErrorOr{T}" /> result to the correct HTTP response.
/// </summary>
/// <remarks>
///     <see cref="RouteGroupAttribute" /> + <see cref="Asp.Versioning.ApiVersionAttribute" /> reproduce
///     the eShop <c>NewVersionedApi().MapGroup("/api/v{version:apiVersion}/documents")</c> pattern, so
///     the URL-segment contract (<c>/api/v1/documents</c>) is preserved. Rate-limit policies:
///     read 100/min, write 20/min, search 60/min. OCR runs async via RabbitMQ after upload.
/// </remarks>
[ApiVersion("1.0")]
[RouteGroup("/api/v{version:apiVersion}/documents", ApiName = "Documents")]
public static class DocumentEndpoints
{
	/// <summary>Lists documents with GUIDv7 cursor pagination (metadata only, straight from Postgres).</summary>
	[Get("/")]
	[EnableRateLimiting(RateLimitPolicies.ReadOperations)]
	[OutputCache(PolicyName = CachePolicies.DocumentList)]
	public static async Task<ErrorOr<PaginatedDocumentsResponse>> GetDocuments(
		[AsParameters] PaginationQuery pagination,
		IDocumentService documentService,
		CancellationToken cancellationToken)
	{
		var pageSize = pagination.PageSize ?? PaginationConstraints.DefaultPageSize;
		var (items, hasMore) = await documentService
			.GetDocumentsPagedAsync(pageSize, pagination.Cursor, cancellationToken);

		return new PaginatedDocumentsResponse
		{
			Items = items.ConvertAll(static d => d.ToDocumentDto()),
			HasMore = hasMore,
			NextCursor = hasMore ? items[^1].Id : null
		};
	}

	/// <summary>
	///     Full-text search over OCR content via Elasticsearch. Nullable bind targets preserve the
	///     optional HTTP limit; the handler applies the default and owns query/limit validation.
	/// </summary>
	[Get("/search")]
	[EnableRateLimiting(RateLimitPolicies.SearchOperations)]
	public static async Task<ErrorOr<List<DocumentSearchResultDto>>> SearchDocuments(
		string? query,
		IDocumentService documentService,
		CancellationToken cancellationToken,
		int? limit = null)
	{
		if (string.IsNullOrEmpty(query) || query.Length > SearchConstraints.QueryMaxLength)
		{
			return Error.Validation("Query",
				$"Search query must be between {SearchConstraints.QueryMinLength} and {SearchConstraints.QueryMaxLength} characters");
		}

		if (limit is < 1 or > SearchConstraints.MaxResultLimit)
		{
			return Error.Validation("Limit",
				$"Limit must be between 1 and {SearchConstraints.MaxResultLimit}");
		}

		var results = await documentService.SearchDocumentsAsync(
			query,
			limit ?? SearchConstraints.DefaultResultLimit,
			cancellationToken);

		return results.Select(static r => r.ToDocumentSearchResultDto()).ToList();
	}

	/// <summary>Gets a document by id. <c>DocumentErrors.NotFound</c> → 404.</summary>
	[Get("/{id:guid}")]
	[EnableRateLimiting(RateLimitPolicies.ReadOperations)]
	[OutputCache(PolicyName = CachePolicies.DocumentById)]
	public static async Task<ErrorOr<DocumentDto>> GetDocumentById(
		Guid id,
		IDocumentService documentService,
		CancellationToken cancellationToken)
	{
		var result = await documentService.GetDocumentByIdAsync(id, cancellationToken);
		return result.Then(static doc => doc.ToDocumentDto());
	}

	/// <summary>Gets a document's AI summary (null until generated). <c>DocumentErrors.NotFound</c> → 404.</summary>
	[Get("/{id:guid}/summary")]
	[EnableRateLimiting(RateLimitPolicies.ReadOperations)]
	[OutputCache(PolicyName = CachePolicies.DocumentById)]
	public static async Task<ErrorOr<SummaryDto>> GetSummary(
		Guid id,
		IDocumentService documentService,
		CancellationToken cancellationToken)
	{
		var result = await documentService.GetDocumentByIdAsync(id, cancellationToken);
		return result.Then(static doc => new SummaryDto { Summary = doc.Summary });
	}

	/// <summary>
	///     Uploads a PDF for OCR processing → 202 Accepted. PDF size/content-type validation is inline
	///     (ErrorOrX has no endpoint-filter hook); a bad file → <see cref="ErrorType.Validation" /> → 400.
	///     Transient storage failures surface as 503 + Retry-After (declared via <see cref="ProducesErrorAttribute" />);
	///     permanent ones propagate to the global handler as 500.
	/// </summary>
	[Post("/")]
	[AcceptedResponse]
	[ProducesError(503, "ServiceUnavailable")]
	[EnableRateLimiting(RateLimitPolicies.WriteOperations)]
	public static async Task<ErrorOr<CreateDocumentResponse>> UploadDocument(
		IFormFile file,
		IDocumentService documentService,
		CancellationToken cancellationToken)
	{
		if (file.Length > FileUploadConstraints.MaxFileSizeBytes)
		{
			return Error.Validation("File",
				$"File size cannot exceed {FileUploadConstraints.MaxFileSizeBytes / FileUploadConstraints.BytesPerMegabyte:F0} MB");
		}

		var contentType = file.ContentType.Split(';')[0].Trim();
		if (!contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
		{
			return Error.Validation("File", "Only PDF files are allowed");
		}

		var result = await documentService.UploadDocumentAsync(
			new UploadDocumentRequest { File = file }, cancellationToken);

		return result.Then(static doc => doc.ToCreateDocumentResponse());
	}

	/// <summary>Deletes a document from Postgres, MinIO, and Elasticsearch. <c>DocumentErrors.NotFound</c> → 404.</summary>
	[Delete("/{id:guid}")]
	[EnableRateLimiting(RateLimitPolicies.WriteOperations)]
	public static Task<ErrorOr<Deleted>> DeleteDocument(
		Guid id,
		IDocumentService documentService,
		CancellationToken cancellationToken) =>
		documentService.DeleteDocumentAsync(id, cancellationToken);
}
