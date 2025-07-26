using System.Net;
using PaperlessREST;

namespace Paperless.Tests;

public class DocumentDtoFormatter : ArgumentDisplayFormatter
{
    public override bool CanHandle(object? value) => value is DocumentDto;
    public override string FormatValue(object? value)
    {
        var doc = (DocumentDto)value!;
        return $"{doc.FileName} ({doc.Status})";
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
public class FileUploadScenarioFormatter : ArgumentDisplayFormatter
{
    public override bool CanHandle(object? value) => value is string filename;
    public override string FormatValue(object? value) => $"'{value}'";
}
