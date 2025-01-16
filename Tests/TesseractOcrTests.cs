using PaperlessServices.TesseractOCR;

namespace Tests;

[TestFixture]
public class OcrTests
{
    [SetUp]
    public void SetUp()
    {
        _ocr = new Ocr("eng", _tessDataPath);
    }

    private Ocr _ocr = null!;

    private readonly string _tessDataPath = null!;

    [Test]
    public void OcrPdf_WithNullStream_ShouldThrowArgumentNullException()
    {
        Assert.That(() => _ocr.OcrPdf(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void OcrPdf_EmptyStream_ShouldThrowException()
    {
        var emptyStream = new MemoryStream();
        Assert.That(() => _ocr.OcrPdf(emptyStream), Throws.Exception);
    }
}
