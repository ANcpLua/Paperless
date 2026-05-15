namespace PaperlessREST.Features.BatchProcessing.Application;

public static class ReportErrors
{
	public static Error FileNotFound(string path) => Error.NotFound(
		"Report.FileNotFound",
		$"File not found: {path}");

	public static Error InvalidXml(string details) => Error.Validation(
		"Report.InvalidXml",
		$"File is not valid XML: {details}");

	public static Error InvalidSchema(string details) => Error.Validation(
		"Report.InvalidSchema",
		$"XML does not match expected schema: {details}");

	public static Error InvalidDate(string raw) => Error.Validation(
		"Report.InvalidDate",
		$"Invalid 'date' attribute '{raw}'. Expected format 'yyyy-MM-dd'.");

	public static Error InvalidGuid(int index) => Error.Validation(
		"Report.InvalidGuid",
		$"Document at index {index} has invalid or empty GUID");
}

