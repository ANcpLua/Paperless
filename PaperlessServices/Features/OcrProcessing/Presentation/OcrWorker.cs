namespace PaperlessServices.Features.OcrProcessing.Presentation;

public class OcrWorker(
	IRabbitMqConsumerFactory consumerFactory,
	IServiceScopeFactory scopeFactory,
	IRabbitMqPublisher publisher,
	TimeProvider timeProvider,
	ILogger<OcrWorker> logger
) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		await using IRabbitMqConsumer<OcrCommand> consumer = await consumerFactory.CreateConsumerAsync<OcrCommand>();

		await foreach (OcrCommand request in consumer.ConsumeAsync(stoppingToken))
		{
			await ProcessMessage(request, consumer, stoppingToken);
		}
	}

	internal async Task ProcessMessage(
		OcrCommand request,
		IRabbitMqConsumer<OcrCommand> consumer,
		CancellationToken cancellationToken)
	{
		// Create a new scope for each message to ensure proper isolation
		// This prevents state leakage between messages and allows scoped dependencies
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		IOcrProcessor processor = scope.ServiceProvider.GetRequiredService<IOcrProcessor>();

		try
		{
			logger.LogInformation(
				"Processing OCR job {JobId} for file {FileName}",
				request.JobId,
				request.FileName
			);

			ErrorOr<OcrEvent> result = await processor.ProcessDocumentAsync(request, cancellationToken);

			if (result.IsError)
			{
				Error error = result.FirstError;
				logger.LogWarning(
					"OCR job {JobId} failed: [{Code}] {Description}",
					request.JobId,
					error.Code,
					error.Description
				);

				OcrEvent failedEvent = new(
					request.JobId,
					"Failed",
					null,
					timeProvider.GetUtcNow()
				);

				await publisher.PublishOcrEventAsync(failedEvent);
				await consumer.AckAsync();
				return;
			}

			OcrEvent ocrResult = result.Value;
			OcrEvent completedEvent = new(
				request.JobId,
				"Completed",
				ocrResult.Text,
				ocrResult.ProcessedAt
			);

			await publisher.PublishOcrEventAsync(completedEvent);

			GenAICommand genAiCommand = new(request.JobId, ocrResult.Text!);
			await publisher.PublishGenAICommandAsync(genAiCommand);

			await consumer.AckAsync();

			logger.LogInformation(
				"Published OCR result for job {JobId}",
				request.JobId
			);
		}
		catch (Exception ex)
		{
			logger.LogError(
				ex,
				"Infrastructure error processing OCR job {JobId}",
				request.JobId
			);

			await consumer.NackAsync();
		}
	}
}
