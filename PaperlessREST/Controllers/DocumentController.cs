using System.ComponentModel.DataAnnotations;
using AutoMapper;
using Contract;
using Contract.Logger;
using EasyNetQ;
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Mvc;
using PaperlessServices.BL;
using PaperlessServices.MinIoStorage;

namespace PaperlessREST.Controllers;

[ApiController]
[Route("documents")]
[Produces("application/json")]
public class DocumentController : ControllerBase
{
    private readonly IBus _bus;
    private readonly IDocumentService _documentService;
    private readonly ElasticsearchClient _elasticClient;
    private readonly IMapper _mapper;
    private readonly IMinioStorageService _minioStorageService;

    public DocumentController(
        IDocumentService documentService,
        IMinioStorageService minioStorageService,
        ElasticsearchClient elasticClient,
        IBus bus,
        IMapper mapper
    )
    {
        _documentService = documentService;
        _minioStorageService = minioStorageService;
        _elasticClient = elasticClient;
        _bus = bus;
        _mapper = mapper;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [LogOperation("Upload", "API")]
    public async Task<ActionResult<DocumentDto>> Upload([FromForm] [Required] string name, [Required] IFormFile file, CancellationToken ct)
    {
        var result = await _documentService.Upload(
            new DocumentDto { Name = name, File = file },
            ct
        );

        // Publish an event once the upload is completed
        if (result != null)
            await _bus.PubSub.PublishAsync(new DocumentUploadedEvent
            {
                DocumentId = result.Id,
                FileName = result.FilePath,
                UploadedAt = result.DateUploaded
            }, ct);

        return Ok(result);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [LogOperation("Get", "API")]
    public async Task<ActionResult<DocumentDto>> Get([FromRoute] [Required] int id, CancellationToken ct)
    {
        var document = await _documentService.GetDocument(id, ct);
        return Ok(document);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [LogOperation("GetAll", "API")]
    public async Task<ActionResult<IEnumerable<DocumentDto>>> GetAll(CancellationToken ct)
    {
        var documents = await _documentService.GetAllDocuments(ct);
        return Ok(documents);
    }

    [HttpGet("{id}/download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [LogOperation("Download", "API")]
    public async Task<IActionResult> Download([FromRoute] [Required] int id, CancellationToken ct)
    {
        var document = await _documentService.GetDocument(id, ct);
        var fileStream = await _minioStorageService.GetFileAsync(document.FilePath, ct);

        return File(fileStream, "application/octet-stream", document.Name);
    }

    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [LogOperation("Search", "API")]
    public async Task<ActionResult<object>> SearchDocuments([FromQuery] [Required] string query, CancellationToken ct)
    {
        var response = await _elasticClient.SearchAsync<DocumentDto>(
            s => s
                 .Index("paperless-documents")
                 .Query(q => q
                     .MultiMatch(mm => mm
                                       .Query(query)
                                       .Fields(new[] { "name", "ocrText" })
                                       .Fuzziness(new Fuzziness("AUTO"))
                                       .MinimumShouldMatch("75%"))),
            ct
        );

        if (!response.IsValidResponse) throw new InvalidOperationException("Search operation failed");

        var documents = _mapper.Map<IEnumerable<DocumentDto>>(response.Documents);
        return Ok(new { totalHits = response.Total, documents });
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [LogOperation("Delete", "API")]
    public async Task<IActionResult> Delete([FromRoute] [Required] int id, CancellationToken ct)
    {
        await _documentService.DeleteDocument(id, ct);
        return NoContent();
    }
}
