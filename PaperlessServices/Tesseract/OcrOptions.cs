namespace PaperlessServices.Tesseract;

public class OcrOptions
{
    public const string Ocr = "OCR"; 
    public string? Language { get; set; } = "eng";
    public string? TessDataPath { get; set; } = "./tessdata";
}