namespace PaperlessREST.Features.DocumentManagement.Presentation.Dto;

/// <summary>
/// Request model for uploading a PDF document.
/// Validation handled by <see cref="Filters.PdfUploadFilter"/>.
///
/// Lives beside the feature (not under <c>Contracts/</c>) because it carries an
/// <see cref="IFormFile"/> — an ASP.NET Core input-model concern, not a transport DTO.
/// The response/query DTOs the OpenAPI contract exposes live in <c>PaperlessREST.Contracts.DocumentManagement</c>.
/// </summary>
public sealed record UploadDocumentRequest
{
	[Description("PDF file to upload (max 10MB, PDF only)")]
	public required IFormFile File { get; init; }
}
