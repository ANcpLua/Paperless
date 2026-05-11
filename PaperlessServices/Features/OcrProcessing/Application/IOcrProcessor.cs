namespace PaperlessServices.Features.OcrProcessing.Application;

/// <summary>
///     Application service interface for processing OCR requests.
///     Orchestrates the workflow: Download → Extract → Index
/// </summary>
public interface IOcrProcessor
{
	/// <summary>
	///     Processes a document OCR request through the complete workflow.
	/// </summary>
	/// <param name="command">OCR command containing job details</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>OCR event with processing result or error</returns>
	Task<ErrorOr<OcrEvent>> ProcessDocumentAsync(OcrCommand command, CancellationToken cancellationToken = default);
}
