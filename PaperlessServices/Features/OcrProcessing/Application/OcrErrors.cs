namespace PaperlessServices.Features.OcrProcessing.Application;

public static class OcrErrors
{
	public static Error EmptyDocument() => Error.Validation(
		"Ocr.EmptyDocument",
		"OCR extraction resulted in empty content; document may be corrupt or unsupported");

	public static Error DownloadFailed(string filePath) => Error.Failure(
		"Ocr.DownloadFailed",
		$"Failed to download file from storage: {filePath}");

	public static Error ExtractionFailed(string details) => Error.Failure(
		"Ocr.ExtractionFailed",
		$"OCR extraction failed: {details}");
}
