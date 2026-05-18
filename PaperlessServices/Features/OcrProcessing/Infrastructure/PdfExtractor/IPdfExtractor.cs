namespace PaperlessServices.Features.OcrProcessing.Infrastructure.PdfExtractor;

/// <summary>
///     Technical interface for extracting text from PDF documents using OCR.
/// </summary>
public interface IPdfExtractor
{
	/// <summary>
	///     Extracts text from a PDF stream using OCR technology.
	/// </summary>
	/// <param name="pdfStream">The PDF file stream to process.</param>
	/// <param name="cancellationToken">
	///     Cooperative cancellation. Checked before and after the underlying OCR call
	///     because the library does not natively accept a token; honours graceful
	///     shutdown so the slowest worker step does not block container teardown.
	/// </param>
	/// <returns>Extracted text or error.</returns>
	Task<ErrorOr<string>> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default);
}
