using Contract;
using EasyNetQ;
using PaperlessServices.BL;

namespace PaperlessServices.Tesseract;

public class OcrWorkerService : BackgroundService
{
    private readonly ILogger<OcrWorkerService> _logger;
    private readonly IBus _messageBus;
    private readonly IServiceProvider _serviceProvider;

    public OcrWorkerService(
        ILogger<OcrWorkerService> logger,
        IBus messageBus,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _messageBus = messageBus;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting OCR Worker Service");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SetupSubscription(stoppingToken);
                
                // If we successfully subscribed, wait indefinitely (or until cancellation)
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown, don't treat as error
                _logger.LogInformation("OCR Worker Service shutdown requested");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OCR Worker Service. Retrying in 5 seconds...");
                
                try
                {
                    await Task.Delay(5000, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task SetupSubscription(CancellationToken stoppingToken)
    {
        await _messageBus.PubSub.SubscribeAsync<DocumentUploadedEvent>(
            "ocr_worker",
            async (message, token) =>
            {
                using var scope = _serviceProvider.CreateScope();
                await ProcessDocumentAsync(message, scope.ServiceProvider, token);
            },
            config => config.WithTopic("document.uploaded"),
            stoppingToken);

        _logger.LogInformation("Successfully subscribed to document.uploaded events");
    }

    private async Task ProcessDocumentAsync(
        DocumentUploadedEvent message,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Beginning OCR processing for document {DocumentId}", message.DocumentId);

            var storageService = serviceProvider.GetRequiredService<IStorageService>();
            var documentService = serviceProvider.GetRequiredService<IDocumentService>();
            var ocrClient = serviceProvider.GetRequiredService<IOcrClient>();

            // Get document from storage
            using var documentStream = await storageService.GetFileAsync(message.FileName, cancellationToken);

            // Perform OCR
            var extractedText = await PerformOcr(documentStream, message.FileName, ocrClient);

            if (string.IsNullOrWhiteSpace(extractedText))
            {
                throw new InvalidOperationException("OCR processing produced no text output");
            }

            // Get and update document using DocumentService
            var document = await documentService.GetDocument(message.DocumentId, cancellationToken);
            document.OcrText = extractedText;
            // DocumentService will handle both DB update and Elasticsearch indexing
            await documentService.UpdateDocument(document, cancellationToken);

            await NotifyProcessingCompleteAsync(message.DocumentId, extractedText, cancellationToken);
            
            _logger.LogInformation("Completed OCR processing for document {DocumentId}", message.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process document {DocumentId}", message.DocumentId);
            await NotifyProcessingFailedAsync(message.DocumentId, ex.Message, cancellationToken);
        }
    }

    private async Task<string> PerformOcr(Stream documentStream, string fileName, IOcrClient ocrClient)
    {
        var extension = Path.GetExtension(fileName).ToLower();
        documentStream.Position = 0;

        return extension switch
        {
            ".pdf" => await Task.Run(() => ocrClient.OcrPdf(documentStream)),
            ".png" or ".jpg" or ".jpeg" => await Task.Run(() => ocrClient.OcrImage(documentStream)),
            _ => throw new NotSupportedException($"File extension {extension} is not supported for OCR")
        };
    }

    private async Task NotifyProcessingCompleteAsync(int documentId, string content,
        CancellationToken cancellationToken)
    {
        var message = new TextMessage
        {
            DocumentId = documentId,
            Text = content,
            ProcessedAt = DateTime.UtcNow
        };

        await _messageBus.PubSub.PublishAsync(message, "document.processed", cancellationToken);
    }

    private async Task NotifyProcessingFailedAsync(int documentId, string errorMessage, CancellationToken cancellationToken)
    {
        var message = new TextMessage
        {
            DocumentId = documentId,
            Text = $"{errorMessage}\n{Environment.StackTrace}",
            ProcessedAt = DateTime.UtcNow
        };

        await _messageBus.PubSub.PublishAsync(message, "document.processing.failed", cancellationToken);
    }
}