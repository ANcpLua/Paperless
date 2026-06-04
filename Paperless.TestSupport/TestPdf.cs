using CreatePdf.NET;

namespace Paperless.TestSupport;

/// <summary>
///     Builds a single-line test PDF from a line of text using CreatePdf.NET's fluent builder and
///     returns its bytes, shared by both integration suites. CreatePdf.NET's public output is disk-only
///     (<c>SaveAsync</c> writes under the project's <c>output/</c> dir and returns the real path), so this
///     saves with a unique name, reads the returned path, then deletes it — neither suite duplicates the
///     round-trip or leaks PDFs. The root fix is an in-memory output on the library itself
///     (e.g. <c>ToBytesAsync</c>/<c>SaveAsync(Stream)</c>), after which this collapses to a single call.
/// </summary>
public static class TestPdf
{
    /// <summary>Renders <paramref name="content" /> (black text on white) to a PDF and returns its bytes.</summary>
    public static async Task<byte[]> BytesAsync(string content)
    {
        // SaveAsync routes through FileOperations.GetOutputPath (project output dir + a sanitized name),
        // so the file rarely lands where a caller-supplied path would suggest — always read the RETURN.
        var path = await Pdf.Create(Dye.White).AddText(content).SaveAsync($"paperless-test-{Guid.NewGuid():N}.pdf");
        try
        {
            return await File.ReadAllBytesAsync(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
