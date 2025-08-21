using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TUnit.Core;

namespace Paperless.Tests;

// ═══════════════════════════════════════════════════════════════
// CUSTOM ARGUMENT FORMATTERS
// ═══════════════════════════════════════════════════════════════

public class SearchQueryValidationFormatter : ArgumentDisplayFormatter
{
    public override bool CanHandle(object? value) => true; // Handle all parameters

    public override string FormatValue(object? value)
    {
        return value switch
        {
            string { Length: > 0 } query => $"Query: '{query}'",
            int limit and > 0 => $"Limit: {limit}",
            _ => "Invalid search parameters"
        };
    }
}

public class HttpStatusCodeFormatter : ArgumentDisplayFormatter
{
    public override bool CanHandle(object? value) => value is HttpStatusCode;

    public override string FormatValue(object? value)
    {
        var status = (HttpStatusCode)value!;
        return $"{(int)status} {status}";
    }
}


// ═══════════════════════════════════════════════════════════════
// PDF TEST HELPER
// ═══════════════════════════════════════════════════════════════
public static class PdfTestHelper
{
    /// <summary>
    /// Creates a simple PDF for testing.
    ///  All we need no multiple pages or complex layouts, just a single page with text.
    ///  use this as single source of truth for PDF generation in tests.
    ///  This will be used to test PDF upload and processing, nothing more.
    /// </summary>
    public static async Task<byte[]> CreateTestPdf()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.pdf");
        var path = await Pdf.Create()
            .AddText("Hello World!")
            .SaveAsync(tempPath);
        var bytes = await File.ReadAllBytesAsync(path);
        try { File.Delete(path); } catch { }
        return bytes;
    }
}

// ═══════════════════════════════════════════════════════════════
// DATA GENERATORS
// ═══════════════════════════════════════════════════════════════
public sealed class InvalidFileUploadGenerator
    : DataSourceGeneratorAttribute<string, string, string>
{
    protected override IEnumerable<Func<(string, string, string)>> GenerateDataSources(
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
    protected override IEnumerable<Func<(string, int?, string)>> GenerateDataSources(
        DataGeneratorMetadata _)
    {
        yield return () => ("",    10,  "query must be at least 1 character");
        yield return () => (null!, 10,  "query must be at least 1 character");
        yield return () => ("foo", 0,   "limit must be between 1 and 100");
        yield return () => ("foo", -1,  "limit must be between 1 and 100");
        yield return () => ("foo", 101, "limit must be between 1 and 100");
    }
}