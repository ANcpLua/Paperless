using Microsoft.AspNetCore.Http.HttpResults;
using PaperlessREST.Services;
using SWEN3.Paperless.RabbitMq.Sse;

namespace PaperlessREST.Extensions;

public static class Endpoints
{
    public static void MapEndpoints(this WebApplication app)
    {
        app.MapOcrEventStream();
        app.MapDocumentEndpoints();
    }
}

public static class DocumentEndpoints
{
    // ReSharper disable once UnusedMethodReturnValue.Global
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Documents");
        var v1documents = api.MapGroup("/api/v{version:apiVersion}/documents")
            .HasApiVersion(1, 0)
            .WithTags("Documents")
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        v1documents.MapGet("/", GetDocuments)
            .WithName(nameof(GetDocuments));

        v1documents.MapGet("/search", SearchDocuments)
            .WithName(nameof(SearchDocuments));
        // .ProducesValidationProblem() // REMOVED: Redundant thanks to https://github.com/captainsafia/aspnet-openapi-xml
        v1documents.MapGet("/{id:guid}", GetDocumentById)
            .WithName(nameof(GetDocumentById));

        v1documents.MapPost("/", UploadDocument)
            .WithName(nameof(UploadDocument))
            .Accepts<IFormFile>("multipart/form-data")
            // .ProducesValidationProblem() // REMOVED: Redundant thanks to https://github.com/captainsafia/aspnet-openapi-xml
            .DisableAntiforgery();

        v1documents.MapDelete("/{id:guid}", DeleteDocument)
            .WithName(nameof(DeleteDocument))
            // Kept because the handler itself doesn't return NotFound,
            // but a global handler might. This makes it explicit.
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>
    /// Get recent documents.
    /// </summary>
    /// <param name="documentService">The service for document operations.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of the 50 most recent document metadata objects.</returns>
    /// <remarks>
    /// Retrieves the 50 most recent documents from PostgreSQL database. 
    /// This endpoint bypasses Elasticsearch as documents are indexed asynchronously by the OCR microservice. 
    /// Returns metadata only (no content) sorted by creation date. 
    /// Errors are handled by GlobalExceptionHandler which returns RFC 7807 problem details.
    /// </remarks>
    /// <response code="200">Returns the list of recent documents.</response>
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

    /// <summary>
    /// Search documents by content.
    /// </summary>
    /// <param name="search">The search parameters, including the query string.</param>
    /// <param name="documentService">The service for document operations.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A list of search results.</returns>
    /// <remarks>
    /// Performs full-text search on document content using Elasticsearch. 
    /// Query supports fuzzy matching and searches across all indexed fields. 
    /// Note: Only documents processed by the OCR microservice are searchable. 
    /// If Elasticsearch is unavailable, returns empty results. 
    /// </remarks>
    /// <response code="200">Returns a list of matching documents.</response>
    /// <response code="400">If the search query is invalid.</response>
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

    /// <summary>
    /// Upload a PDF document.
    /// </summary>
    /// <param name="request">The request containing the file to upload.</param>
    /// <param name="documentService">The service for document operations.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An Accepted response with a location header pointing to the new document.</returns>
    /// <remarks>
    /// Uploads a PDF document and triggers asynchronous OCR processing. 
    /// Workflow: 1) Validates PDF format and uniqueness, 2) Stores file in MinIO object storage, 
    /// 3) Creates database record in PostgreSQL, 4) Publishes message to RabbitMQ for OCR processing. 
    /// Returns 202 Accepted immediately - OCR processing happens asynchronously. 
    /// The OCR microservice will later extract text and index it in Elasticsearch.
    /// </remarks>
    /// <response code="202">Indicates the file has been accepted for processing. The `Location` header contains the URL to check the document's status.</response>
    /// <response code="400">If the uploaded file is not a valid PDF, is a duplicate, or fails other validation checks.</response>
    private static async Task<AcceptedAtRoute<CreateDocumentResponse>> UploadDocument(
        [AsParameters] UploadDocumentRequest request,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        var document = await documentService.UploadDocumentAsync(request, cancellationToken);

        return TypedResults.AcceptedAtRoute(
            document.ToCreateDocumentResponse(),
            nameof(GetDocumentById),
            new { id = document.Id });
    }

    /// <summary>
    /// Get document by ID.
    /// </summary>
    /// <param name="id">The unique identifier of the document.</param>
    /// <param name="documentService">The service for document operations.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The document if found; otherwise, a Not Found result.</returns>
    /// <remarks>
    /// Retrieves a specific document by its unique identifier from PostgreSQL. 
    /// Returns full document metadata including OCR-extracted content if available. 
    /// This endpoint queries PostgreSQL directly and doesn't depend on Elasticsearch availability.
    /// </remarks>
    /// <response code="200">Returns the requested document.</response>
    /// <response code="404">If a document with the specified ID does not exist.</response>
    private static async Task<Results<Ok<DocumentDto>, NotFound>> GetDocumentById(
        Guid id,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        var document = await documentService.GetDocumentByIdAsync(id, cancellationToken);

        return document is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(document.ToDocumentDto());
    }

    /// <summary>
    /// Delete a document.
    /// </summary>
    /// <param name="id">The unique identifier of the document to delete.</param>
    /// <param name="documentService">The service for document operations.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A No Content response on successful deletion.</returns>
    /// <remarks>
    /// Deletes a document from all storage systems. 
    /// Removes from: 1) PostgreSQL database, 2) MinIO object storage, 3) Elasticsearch index (if indexed). 
    /// Elasticsearch deletion is best-effort - if it fails, the operation continues and returns success. 
    /// Note: Cannot verify if document was indexed by OCR service.
    /// </remarks>
    /// <response code="204">The document was successfully deleted.</response>
    /// <response code="404">If a document with the specified ID does not exist.</response>
    private static async Task<NoContent> DeleteDocument(
        Guid id,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        await documentService.DeleteDocumentAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}