using Contract.Logger;
using Contract;
using EasyNetQ;
using PaperlessServices.BL;
using PaperlessServices.MinIoStorage;

namespace PaperlessServices.TesseractOCR;

public class OcrWorkerService : BackgroundService
{
    private readonly IBus _messageBus;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _subscriptionId;

    public OcrWorkerService(IBus messageBus, IServiceProvider serviceProvider)
    {
        _messageBus = messageBus;
        _serviceProvider = serviceProvider;
        _subscriptionId = $"ocr_worker_{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}";
    }

    [LogOperation("OcrWorkerService", "ExecuteAsync")]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Subscribe to 'document.uploaded' events
            await _messageBus.PubSub.SubscribeAsync<DocumentUploadedEvent>(
                _subscriptionId,
                HandleDocumentAsync,
                x => x.WithTopic("document.uploaded"),
                stoppingToken);

            // Wait indefinitely unless canceled
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
    }

    [LogOperation("OcrWorkerService", "HandleDocument")]
    private async Task HandleDocumentAsync(DocumentUploadedEvent message, CancellationToken token)
    {
        using var scope = _serviceProvider.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IMinioStorageService>();
        var documentStream = await storageService.GetFileAsync(message.FileName, token);

        await using (documentStream)
        {
            var ocrClient = scope.ServiceProvider.GetRequiredService<IOcrClient>();
            var extractedText = await PerformOcr(documentStream, message.FileName, ocrClient);

            var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var document = await documentService.GetDocument(message.DocumentId, token);

            // Update OCR text
            document.OcrText = extractedText;
            await documentService.UpdateDocument(document, token);

            await PublishResultAsync(message.DocumentId, extractedText, false, DateTime.UtcNow, token);
        }
    }

    [LogOperation("OcrWorkerService", "PerformOcr")]
    private static Task<string> PerformOcr(Stream documentStream, string fileName, IOcrClient ocrClient)
    {
        if (!Path.GetExtension(fileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            // Depending on your approach, you can log or throw
            throw new NotSupportedException("Only PDF files are supported");
        }

        documentStream.Position = 0;
        return Task.FromResult(ocrClient.OcrPdf(documentStream));
    }

    [LogOperation("OcrWorkerService", "PublishResult")]
    private async Task PublishResultAsync(int documentId, string content, bool isError, DateTime processedAt, CancellationToken token)
    {
        var message = new DocumentUploadedEvent
        {
            DocumentId = documentId,
            FileName = content,
            UploadedAt = processedAt,
        };

        var topic = isError ? "document.processing.failed" : "document.processed";
        await _messageBus.PubSub.PublishAsync(message, topic, token);
    }
}
