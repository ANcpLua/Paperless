using Microsoft.AspNetCore.Mvc;
using PaperlessREST.Models;
using PaperlessREST.Services;

namespace PaperlessREST.Controllers;

[ApiController]
[Route("api/documents")]
public class GetDocumentController : ControllerBase
{
    private readonly IGetDocumentService _service;
    private readonly ILogger<GetDocumentController> _logger;

    public GetDocumentController(
        IGetDocumentService service,
        ILogger<GetDocumentController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DocumentDto>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await _service.GetByIdAsync(id, cancellationToken);
            if (document == null)
            {
                return NotFound();
            }

            return Ok(document);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, "Request canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving document {Id}", id);
            return StatusCode(500, "An error occurred while retrieving the document");
        }
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DocumentDto>>> GetAll(
        CancellationToken cancellationToken)
    {
        try
        {
            var documents = await _service.GetAllAsync(cancellationToken);
            return Ok(documents);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, "Request canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving documents");
            return StatusCode(500, "An error occurred while retrieving documents");
        }
    }
}
