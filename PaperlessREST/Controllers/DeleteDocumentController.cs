using Microsoft.AspNetCore.Mvc;
using PaperlessREST.Services;

namespace PaperlessREST.Controllers;

[ApiController]
[Route("api/documents")]
public class DeleteDocumentController : ControllerBase
{
    private readonly IDeleteDocumentService _service;
    private readonly ILogger<DeleteDocumentController> _logger;

    public DeleteDocumentController(
        IDeleteDocumentService service,
        ILogger<DeleteDocumentController> logger)
    {
        _service = service;
        _logger = logger;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(
        int id,
        CancellationToken cancellationToken)
    {
        try
        {
            await _service.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, "Request canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting document {Id}", id);
            return StatusCode(500, "An error occurred while deleting the document");
        }
    }
}
