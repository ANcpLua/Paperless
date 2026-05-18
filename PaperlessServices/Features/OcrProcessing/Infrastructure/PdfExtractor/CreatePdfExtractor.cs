namespace PaperlessServices.Features.OcrProcessing.Infrastructure.PdfExtractor;

/// <summary>
///     PDF text extractor implementation using CreatePdf.NET library.
/// </summary>
public class CreatePdfExtractor(ILogger<CreatePdfExtractor> logger) : IPdfExtractor
{
	public async Task<ErrorOr<string>> ExtractTextAsync(Stream pdfStream, CancellationToken cancellationToken = default)
	{
		try
		{
			cancellationToken.ThrowIfCancellationRequested();
			string text = await Pdf.Load(pdfStream).OcrAsync(options: null, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();

			if (string.IsNullOrWhiteSpace(text))
			{
				return OcrErrors.EmptyDocument();
			}

			logger.LogInformation("Extracted {CharCount} characters from PDF", text.Length);
			return text;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "OCR extraction failed");
			return OcrErrors.ExtractionFailed(ex.Message);
		}
	}
}
