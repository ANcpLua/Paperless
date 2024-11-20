namespace PaperlessServices.Tesseract;

public interface IOcrClient
{
    string OcrPdf(Stream pdfStream);
    string OcrImage(Stream imageStream);
}
