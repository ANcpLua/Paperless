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



public static class BatchErrors
{
    public static Error PathRequired(string property) => Error.Validation(
        "Batch.PathRequired",
        $"{BatchOptions.SectionName}:{property} is required");

    public static Error InvalidPath(string property, string details) => Error.Validation(
        "Batch.InvalidPath",
        $"{BatchOptions.SectionName}:{property} is not a valid path: {details}");

    public static Error PathsNotDistinct() => Error.Validation(
        "Batch.PathsNotDistinct",
        $"{BatchOptions.SectionName} paths (InputPath, ArchivePath, ErrorPath) must be distinct");

    public static Error InvalidTimeZone(string timeZoneId) => Error.Validation(
        "Batch.InvalidTimeZone",
        $"{BatchOptions.SectionName}:TimeZoneId '{timeZoneId}' is not a valid system timezone");
}
