namespace PaperlessREST.Features.DocumentManagement.Presentation.Filters;

/// <summary>
///     Endpoint filter that validates PDF uploads before processing.
/// </summary>
/// <remarks>
///     Validates:
///     <list type="bullet">
///         <item>File presence</item>
///         <item>File size (max 10MB)</item>
///         <item>Content type (application/pdf only)</item>
///     </list>
/// </remarks>
public sealed class PdfUploadFilter : IEndpointFilter
{
	public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
	{
		IFormFile? file = ctx.Arguments.OfType<UploadDocumentRequest>().FirstOrDefault()?.File
		                  ?? ctx.Arguments.OfType<IFormFile>().FirstOrDefault();

		if (file is null)
			return ValidationError("File is required");

		if (file.Length > FileUploadConstraints.MaxFileSizeBytes)
			return ValidationError($"File size cannot exceed {FileUploadConstraints.MaxFileSizeBytes / FileUploadConstraints.BytesPerMegabyte:F0} MB");

		string contentType = file.ContentType?.Split(';')[0].Trim() ?? "";
		if (!contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase))
			return ValidationError("Only PDF files are allowed");

		return await next(ctx);
	}

	private static ValidationProblem ValidationError(string message) =>
		TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["File"] = [message] });
}

public static class PdfUploadFilterExtensions
{
	public static RouteHandlerBuilder ValidatePdfUpload(this RouteHandlerBuilder builder) =>
		builder.AddEndpointFilter<PdfUploadFilter>();
}
