
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