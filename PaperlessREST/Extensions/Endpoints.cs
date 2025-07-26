using Microsoft.AspNetCore.Http.HttpResults;
using PaperlessREST.Services;
using Scalar.Kiota.Extension;
using SWEN3.Paperless.RabbitMq.Sse;

namespace PaperlessREST.Extensions;

public static class Endpoints
{
    public static void MapEndpoints(this WebApplication app)
    {
        app.MapScalarWithKiota();
        app.MapOcrEventStream();
        app.MapDocumentEndpoints();
    }
}

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.NewVersionedApi("Documents");
        var v1documents = api.MapGroup("/api/v{version:apiVersion}/documents")
            .HasApiVersion(1, 0)
            .WithTags("Documents")
            .WithOpenApi()
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        v1documents.MapGet("/", GetDocuments)
            .WithName(nameof(GetDocuments))
            .WithSummary("Get recent documents");

        v1documents.MapGet("/search", SearchDocuments)
            .WithName(nameof(SearchDocuments))
            .WithSummary("Search documents by content");

        v1documents.MapGet("/{id:guid}", GetDocumentById)
            .WithName(nameof(GetDocumentById))
            .WithSummary("Get document by ID");

        v1documents.MapPost("/", UploadDocument)
            .WithName(nameof(UploadDocument))
            .WithSummary("Upload a PDF document")
            .Accepts<IFormFile>("multipart/form-data")
            .ProducesValidationProblem()
            .DisableAntiforgery();

        v1documents.MapDelete("/{id:guid}", DeleteDocument)
            .WithName(nameof(DeleteDocument))
            .WithSummary("Delete a document")
            .ProducesProblem(StatusCodes.Status404NotFound);

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

    private static async Task<NoContent> DeleteDocument(
        Guid id,
        IDocumentService documentService,
        CancellationToken cancellationToken)
    {
        await documentService.DeleteDocumentAsync(id, cancellationToken);
        return TypedResults.NoContent();
    }
}