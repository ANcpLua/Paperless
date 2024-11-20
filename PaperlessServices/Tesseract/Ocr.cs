using System.Text;
using ImageMagick;
using Microsoft.Extensions.Options;
using Tesseract;

namespace PaperlessServices.Tesseract;

public class Ocr : IOcrClient
{
    private readonly string? _language;
    private readonly string? _tessDataPath;
    private readonly ILogger<Ocr> _logger;

    public Ocr(IOptions<OcrOptions> options, ILogger<Ocr> logger)
    {
        var ocrOptions = options.Value;
        _tessDataPath = ocrOptions.TessDataPath;
        _language = ocrOptions.Language;
        _logger = logger;

        MagickNET.Initialize();
    }

    public string OcrPdf(Stream inputStream)
    {
        var stringBuilder = new StringBuilder();
        try
        {
            var settings = new MagickReadSettings
            {
                Density = new Density(300)
            };

            using var images = new MagickImageCollection();
            images.Read(inputStream, settings);
            _logger.LogInformation($"Processing {images.Count} pages...");

            foreach (var image in images)
            {
                ProcessImage(image, stringBuilder);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing PDF file");
            throw;
        }

        return stringBuilder.ToString().Trim();
    }

    public string OcrImage(Stream imageStream)
    {
        var stringBuilder = new StringBuilder();
        try
        {
            using var magickImage = new MagickImage(imageStream);
            ProcessImage(magickImage, stringBuilder);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image file");
            throw;
        }

        return stringBuilder.ToString().Trim();
    }

    private void ProcessImage(IMagickImage image, StringBuilder stringBuilder)
    {
        try
        {
            image.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
            image.Format = MagickFormat.Png;
            image.ColorSpace = ColorSpace.Gray;
            image.Alpha(AlphaOption.Remove);
            image.Enhance();
            image.Deskew(new Percentage(40));
            image.Contrast();

            var imageData = image.ToByteArray();

            using var tesseract = new TesseractEngine(_tessDataPath, _language, EngineMode.Default);
            using var pix = Pix.LoadFromMemory(imageData);

            using var page = tesseract.Process(pix);
            var text = page.GetText();
            stringBuilder.AppendLine(text);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing image");
        }
    }
}
