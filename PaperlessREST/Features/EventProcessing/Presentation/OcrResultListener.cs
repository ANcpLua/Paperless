namespace PaperlessREST.Features.EventProcessing.Presentation;

public class OcrResultListener(
	IRabbitMqConsumerFactory consumerFactory,
	IServiceScopeFactory scopeFactory,
	ISseStream<OcrEvent> sseStream,
	ILogger<OcrResultListener> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("OCR Result Listener started");

		await using IRabbitMqConsumer<OcrEvent> consumer = await consumerFactory.CreateConsumerAsync<OcrEvent>();

		await foreach (OcrEvent result in consumer.ConsumeAsync(stoppingToken))
		{
			await ProcessMessage(result, consumer, stoppingToken);
		}

		logger.LogInformation("OCR Result Listener stopped");
	}

	internal async Task ProcessMessage(OcrEvent result, IRabbitMqConsumer<OcrEvent> consumer,
		CancellationToken cancellationToken)
	{
		// Create a new scope for each message to ensure proper isolation
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		IDocumentService documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();

		try
		{
			logger.LogInformation("Received OCR result for job {JobId} with status {Status}", result.JobId,
				result.Status);

			string? content = result.Status is "Completed" ? result.Text : null;
			ErrorOr<Updated> processResult =
				await documentService.ProcessOcrResultAsync(result.JobId, result.Status, content, cancellationToken);

			if (processResult.IsError)
			{
				await consumer.NackAsync(false);
				return;
			}

			sseStream.Publish(result);

			await consumer.AckAsync();
			logger.LogInformation("Successfully processed OCR result for job {JobId}", result.JobId);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error processing OCR result for job {JobId}", result.JobId);
			await consumer.NackAsync();
		}
	}
}
