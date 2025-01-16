using PaperlessServices.TesseractOCR;

namespace PaperlessServices.Extensions;

public static class TesseractOcrServiceCollectionExtensions
{
    public static void AddTesseractOcr(this IServiceCollection services)
    {
        services.AddScoped<IOcrClient>(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();

            var language = configuration["OCR:Language"] ?? "eng";
            var tessDataPath = configuration["OCR:TessDataPath"] ?? "./tessdata";

            return new Ocr(language, tessDataPath);
        });

        services.AddHostedService<OcrWorkerService>();
    }
}
