using PaperlessServices.TesseractOCR;

namespace Tests;

[TestFixture]
public class OcrTests
{
    private IOcrClient _ocr = null!;
    private readonly string _testPdfPath = Path.Combine("IntegrationTests", "HelloWorld.pdf");
    private readonly string _tessDataPath = "./tessdata";

    [SetUp]
    public void SetUp()
    {
        _ocr = new Ocr("eng", _tessDataPath);
    }

    [Test]
    public void OcrPdf_WithNullStream_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.That(() => _ocr.OcrPdf(null!), Throws.ArgumentNullException,
            "Expected ArgumentNullException when attempting OCR on a null stream.");
    }

    [Test]
    public void OcrPdf_WithEmptyStream_ShouldThrowException()
    {
        // Arrange
        using var emptyStream = new MemoryStream();

        // Act & Assert
        Assert.That(() => _ocr.OcrPdf(emptyStream), Throws.Exception,
            "OCR on an empty stream should result in an exception due to invalid PDF content.");
    }
}
