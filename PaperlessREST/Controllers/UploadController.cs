using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using PaperlessREST.Models;
using PaperlessREST.Services;

namespace PaperlessREST.Controllers;

[ApiController]
[Route("api/documents/upload")]
public class UploadController : ControllerBase
{
    private readonly IUploadService _uploadService;
    private readonly ILogger<UploadController> _logger;
    private readonly IValidator<DocumentUploadDto> _uploadValidator;

    public UploadController(
        IUploadService uploadService,
        IValidator<DocumentUploadDto> uploadValidator,
        ILogger<UploadController> logger)
    {
        _uploadService = uploadService;
        _uploadValidator = uploadValidator;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult<DocumentDto>> Upload(
        [FromForm] DocumentUploadDto uploadDto,
        CancellationToken cancellationToken)
    {
        try
        {
            var validationResult = await _uploadValidator.ValidateAsync(uploadDto, cancellationToken);
            if (!validationResult.IsValid)
            {
                return BadRequest(validationResult.Errors);
            }

            var document = await _uploadService.Upload(uploadDto, cancellationToken);
            return CreatedAtAction(nameof(GetDocumentController.GetById), "GetDocument", new { id = document.Id }, document);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, "Request canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading document");
            return StatusCode(500, "An error occurred while uploading the document");
        }
    }
}
