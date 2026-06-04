using CreatePdf.NET;

namespace Paperless.TestSupport;

/// <summary>
///     Renders a single-line test PDF from a line of text via CreatePdf.NET's fluent builder and returns
///     its bytes in memory — shared by both integration suites so neither duplicates PDF creation.
///     Uses <see cref="Document.ToBytesAsync" /> (CreatePdf.NET 3.0.5+), which renders straight to a
///     byte array with no disk round-trip, so there is nothing to clean up or leak.
/// </summary>
public static class TestPdf
{
    /// <summary>Renders <paramref name="content" /> (black text on white) to a PDF and returns its bytes.</summary>
    public static Task<byte[]> BytesAsync(string content) =>
        Pdf.Create(Dye.White).AddText(content).ToBytesAsync();
}
