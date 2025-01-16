using System.Text;
using ImageMagick;
using Tesseract;

namespace PaperlessServices.TesseractOCR;

public class Ocr : IOcrClient
{
    private readonly string? _language;
    private readonly string? _tessDataPath;

    public Ocr(string? language, string? tessDataPath)
    {
        _language = language ?? "eng";
        _tessDataPath = tessDataPath ?? "./tessdata";

        MagickNET.Initialize(); // ImageMagick
    }

    public string OcrPdf(Stream inputStream)
    {
        var stringBuilder = new StringBuilder();

        {
            // Image reading settings with desired density
            var settings = new MagickReadSettings
            {
                Density = new Density(300)
            };

            using var images = new MagickImageCollection();
            images.Read(inputStream, settings); // Read PDF as images

            // Process each page/image
            foreach (var image in images)
            {
                ProcessImage(image, stringBuilder);
            }

            return stringBuilder.ToString().Trim();
        }
    }

    private void ProcessImage(IMagickImage image, StringBuilder stringBuilder)
    {
        // Preprocessing steps to enhance image quality for OCR
        image.Density = new Density(300, 300, DensityUnit.PixelsPerInch);
        image.Format = MagickFormat.Png;
        image.ColorSpace = ColorSpace.Gray;
        image.Alpha(AlphaOption.Remove);
        image.Enhance();
        image.Deskew(new Percentage(40));
        image.Contrast();


        // Convert processed image to byte array for TesseractOCR
        var imageData = image.ToByteArray();

        // Initialize TesseractOCR engine and perform OCR
        using var tesseract = new TesseractEngine(_tessDataPath, _language, EngineMode.Default);
        using var pix = Pix.LoadFromMemory(imageData);
        using var page = tesseract.Process(pix);

        var text = page.GetText();
        stringBuilder.AppendLine(text); // Append extracted text
    }
}
        
