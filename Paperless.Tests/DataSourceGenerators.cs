namespace Paperless.Tests;

public sealed class InvalidFileUploadGenerator
    : DataSourceGeneratorAttribute<string, string, string>
{
    public override IEnumerable<Func<(string, string, string)>> GenerateDataSources(
        DataGeneratorMetadata _)
    {
        yield return () => ("document.txt",  "text content",  "File must have .pdf extension");
        yield return () => ("document.docx", "word content",  "File must have .pdf extension");
        yield return () => ("document.jpg",  "image data",    "File must have .pdf extension");
        yield return () => ("document",      "no extension",  "File must have .pdf extension");
        yield return () => (".pdf",          "hidden file",   "File must have .pdf extension");
        yield return () => ("",              "empty filename","File is required");
    }
}

public sealed class SearchQueryValidationGenerator
    : DataSourceGeneratorAttribute<string, int?, string>
{
    public override IEnumerable<Func<(string, int?, string)>> GenerateDataSources(
        DataGeneratorMetadata _)
    {
        yield return () => ("",    10,  "query must be at least 1 character");
        yield return () => (null!, 10,  "query must be at least 1 character");
        yield return () => ("foo", 0,   "limit must be between 1 and 100");
        yield return () => ("foo", -1,  "limit must be between 1 and 100");
        yield return () => ("foo", 101, "limit must be between 1 and 100");
    }
}

public sealed class DocumentStatusTransitionGenerator
    : DataSourceGeneratorAttribute<string, bool>
{
    public override IEnumerable<Func<(string, bool)>> GenerateDataSources(
        DataGeneratorMetadata _)
    {
        yield return () => ("Pending",   false);
        yield return () => ("Completed", true);
        yield return () => ("Failed",    true);
    }
}