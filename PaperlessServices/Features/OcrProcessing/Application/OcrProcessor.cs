namespace PaperlessServices.Features.OcrProcessing.Application;

/// <summary>
///     Application service that orchestrates OCR document processing workflow.
///     Coordinates: Download from storage → Extract text via OCR → Index in search
/// </summary>
public class OcrProcessor(
	IStorageService storageService,
	IPdfExtractor pdfExtractor,
	ISearchIndexService searchService,
	TimeProvider timeProvider,
	ILogger<OcrProcessor> logger
) : IOcrProcessor
{
	public async Task<ErrorOr<OcrEvent>> ProcessDocumentAsync(
		OcrCommand command,
		CancellationToken cancellationToken = default)
	{
		// Step 1: Download PDF from storage
		ErrorOr<Stream> streamResult = await DownloadAsync(command.FilePath, cancellationToken);
		if (streamResult.IsError)
		{
			return streamResult.Errors;
		}

		await using Stream stream = streamResult.Value;

		// Step 2: Extract text using OCR
		ErrorOr<string> textResult = await pdfExtractor.ExtractTextAsync(stream, cancellationToken);
		if (textResult.IsError)
		{
			return textResult.Errors;
		}

		string text = textResult.Value;

		// Step 3: Index document in search engine
		await searchService.IndexDocumentAsync(
			command.JobId,
			command.FileName,
			text,
			command.CreatedAt,
			cancellationToken
		);

		logger.LogInformation("Successfully processed OCR job {JobId}", command.JobId);

		return new OcrEvent(command.JobId, "Completed", text, timeProvider.GetUtcNow());
	}

	private async Task<ErrorOr<Stream>> DownloadAsync(string filePath, CancellationToken cancellationToken)
	{
		try
		{
			Stream stream = await storageService.DownloadAsync(filePath, cancellationToken);
			return stream;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to download file: {FilePath}", filePath);
			return OcrErrors.DownloadFailed(filePath);
		}
	}
}
