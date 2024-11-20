using Contract;
using EasyNetQ;
using Elastic.Clients.Elasticsearch;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PaperlessServices.BL;
using PaperlessServices.Validation;

namespace PaperlessREST.Controllers;

[ApiController]
[Route("documents")]
public class DocumentController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IStorageService _storageService;
    private readonly IValidator<DocumentDto> _validator;
    private readonly ILogger<DocumentController> _logger;
    private readonly ElasticsearchClient _elasticClient;
    private readonly IBus _bus;

    public DocumentController(
        IDocumentService documentService,
        IStorageService storageService,
        IValidator<DocumentDto> validator,
        ILogger<DocumentController> logger,
        ElasticsearchClient elasticClient,
        IBus bus)
    {
        _documentService = documentService;
        _storageService = storageService;
        _validator = validator;
        _logger = logger;
        _elasticClient = elasticClient;
        _bus = bus;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload([FromForm] DocumentDto documentDto, CancellationToken cancellationToken)
    {
        try
        {
            var validationResult = await _validator.ValidateAsync(documentDto, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Validation failed",
                    errors = validationResult.Errors.Select(e => e.ErrorMessage)
                });
            }

            var result = await _documentService.Upload(documentDto, cancellationToken);

            await _bus.PubSub.PublishAsync(new DocumentUploadedEvent
            {
                DocumentId = result.Id,
                FileName = result.FilePath,
                UploadedAt = DateTime.UtcNow
            }, cancellationToken);

            _logger.LogInformation("Document uploaded successfully: {DocumentId}", result.Id);
            return Ok(new { success = true, message = "Document uploaded successfully", document = result });
        }
        catch (StorageException ex)
        {
            _logger.LogError(ex, "Storage error during document upload");
            return StatusCode(500, new { success = false, message = "Failed to store document" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during document upload");
            return StatusCode(500, new { success = false, message = "An unexpected error occurred" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentService.GetDocument(id, cancellationToken);
            await _storageService.GetFileAsync(document.FilePath, cancellationToken);

            var documentResponse = new
            {
                document.Id,
                document.Name,
                document.DateUploaded,
                document.OcrText,
                FileUrl = $"/documents/{id}/download"
            };

            return Ok(documentResponse);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Document not found" });
        }
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _documentService.GetDocument(id, cancellationToken);
            var fileStream = await _storageService.GetFileAsync(document.FilePath, cancellationToken);
            var contentType = GetContentType(document.FilePath);
            return File(fileStream, contentType, document.Name);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Document not found" });
        }
        catch (StorageException ex)
        {
            _logger.LogError(ex, "Failed to retrieve file for document {DocumentId}", id);
            return StatusCode(500, new { message = "Failed to retrieve document file" });
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchDocuments([FromQuery] string query, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _elasticClient.SearchAsync<DocumentDto>(s => s
                    .Index("paperless-documents")
                    .Size(20)
                    .Query(q => q
                        .MultiMatch(m => m
                            .Query(query)
                            .Fields(new[] { "name^2", "ocrText" })
                            .Fuzziness(new Fuzziness("AUTO"))
                            .MinimumShouldMatch("75%"))),
                cancellationToken);

            if (!response.IsValidResponse)
            {
                _logger.LogError("Search failed: {Error}", response.DebugInformation);
                return StatusCode(500, new { success = false, message = "Search operation failed" });
            }

            return Ok(new
            {
                success = true,
                totalHits = response.Total,
                documents = response.Documents
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during document search");
            return StatusCode(500, new { success = false, message = "An error occurred during search" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching all documents");
        try
        {
            var documents = await _documentService.GetAllDocuments(cancellationToken);
            return Ok(documents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while fetching all documents.");
            return StatusCode(500, "An unexpected error occurred.");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Deleting document with ID: {DocumentId}", id);
            await _storageService.DeleteFileAsync(id.ToString(), cancellationToken);
            await _documentService.DeleteDocument(id, cancellationToken);
            _logger.LogInformation("Document with ID: {DocumentId} deleted successfully", id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while deleting document with ID: {DocumentId}", id);
            return StatusCode(500, "An unexpected error occurred.");
        }
    }

    private string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLower();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}
    
