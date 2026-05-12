namespace PaperlessREST.Features.DocumentManagement.Presentation.Dto;

/// <summary>
/// Request model for uploading a PDF document.
/// Validation handled by <see cref="Filters.PdfUploadFilter"/>.
///
/// Stays in the API project (rather than Paperless.Contracts) because it carries an
/// <see cref="IFormFile"/> — an ASP.NET Core input-model concern, not a transport DTO.
/// All response/query DTOs live in <c>Paperless.Contracts.DocumentManagement</c>.
/// </summary>
public sealed record UploadDocumentRequest
{
	[Description("PDF file to upload (max 10MB, PDF only)")]
	public required IFormFile File { get; init; }
}
