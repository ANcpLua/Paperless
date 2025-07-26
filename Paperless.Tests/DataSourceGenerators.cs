namespace Paperless.Tests;

public class InvalidFileUploadGenerator : DataSourceGeneratorAttribute<string, string, string>
{
    public override IEnumerable<Func<(string, string, string)>> GenerateDataSources(
        DataGeneratorMetadata dataGeneratorMetadata)
    {
        // filename, content, expectedError
        yield return () => ("document.txt", "text content", "File must have .pdf extension");
        yield return () => ("document.docx", "word content", "File must have .pdf extension");
        yield return () => ("document.jpg", "image data", "File must have .pdf extension");
        yield return () => ("document", "no extension", "File must have .pdf extension");
        yield return () => (".pdf", "hidden file", "File must have .pdf extension");
        yield return () => ("", "empty filename", "File is required");
    }
}

public class SearchQueryValidationGenerator : DataSourceGeneratorAttribute<string, int?, string>
{
    public override IEnumerable<Func<(string, int?, string)>> GenerateDataSources(
        DataGeneratorMetadata dataGeneratorMetadata)
    {
        // query, limit, expectedError
        yield return () => ("", 10, "Search query must be at least 1 character");
        yield return () => (null!, 10, "Search query must be at least 1 character");
        yield return () => ("test", 0, "Limit must be between 1 and 100");
        yield return () => ("test", -1, "Limit must be between 1 and 100");
        yield return () => ("test", 101, "Limit must be between 1 and 100");
        yield return () => ("test", 1000, "Limit must be between 1 and 100");
    }
}

public class DocumentStatusTransitionGenerator : DataSourceGeneratorAttribute<string, bool>
{
    public override IEnumerable<Func<(string, bool)>> GenerateDataSources(
        DataGeneratorMetadata dataGeneratorMetadata)
    {
        // status, canDelete
        yield return () => ("Pending", false);
        yield return () => ("Completed", true);
        yield return () => ("Failed", true);
    }
}