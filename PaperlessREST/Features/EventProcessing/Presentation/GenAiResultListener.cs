namespace PaperlessREST.Features.EventProcessing.Presentation;

public class GenAiResultListener(
	IRabbitMqConsumerFactory consumerFactory,
	IServiceScopeFactory scopeFactory,
	ISseStream<GenAIEvent> sseStream,
	ILogger<GenAiResultListener> logger) : BackgroundService
{
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		logger.LogInformation("GenAI Result Listener started");

		try
		{
			await using IRabbitMqConsumer<GenAIEvent>
				consumer = await consumerFactory.CreateConsumerAsync<GenAIEvent>();

			await foreach (GenAIEvent genAiEvent in consumer.ConsumeAsync(stoppingToken))
			{
				if (stoppingToken.IsCancellationRequested)
				{
					break;
				}

				await ProcessGenAiEventAsync(genAiEvent, consumer, stoppingToken);
			}
		}
		catch (OperationInterruptedException ex) when (ex.Message.Contains("no queue"))
		{
			logger.LogWarning("GenAI Result Listener disabled - GenAIEvent queue not configured in RabbitMQ. " +
			                  "Update SWEN3.Paperless.RabbitMq library to include GenAIEvent queue support.");

			await Task.Delay(Timeout.Infinite, stoppingToken);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Unexpected error in GenAI Result Listener");
			throw;
		}

		logger.LogInformation("GenAI Result Listener stopped");
	}

	internal async Task ProcessGenAiEventAsync(GenAIEvent genAiEvent, IRabbitMqConsumer<GenAIEvent> consumer,
		CancellationToken cancellationToken)
	{
		// Create a new scope for each message to ensure proper isolation
		await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
		IDocumentService documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();

		try
		{
			// Handle failure case first (empty summary = GenAI failed)
			if (string.IsNullOrWhiteSpace(genAiEvent.Summary))
			{
				logger.LogWarning("GenAI failed for document {DocumentId}: {Error}", genAiEvent.DocumentId,
					genAiEvent.ErrorMessage ?? "Unknown error");
				sseStream.Publish(genAiEvent);
				await consumer.AckAsync();
				return;
			}

			// Success case - has summary
			logger.LogInformation("Received GenAI summary for document {DocumentId}", genAiEvent.DocumentId);

			ErrorOr<Updated> updateResult = await documentService.UpdateDocumentSummaryAsync(genAiEvent.DocumentId,
				genAiEvent.Summary, genAiEvent.GeneratedAt, cancellationToken);

			if (updateResult.IsError)
			{
				if (updateResult.FirstError.Type == ErrorType.NotFound)
				{
					// Document was deleted between OCR and GenAI summary; updating is a no-op.
					// Ack — the message is processed (idempotent outcome).
					logger.LogWarning(
						"Document {DocumentId} no longer exists; dropping GenAI summary - {Error}",
						genAiEvent.DocumentId, updateResult.FirstError.Description);
					await consumer.AckAsync();
					return;
				}

				// Real failure (infrastructure, validation, etc.) — nack without requeue so
				// the message goes to the DLQ instead of being silently dropped. Matches
				// OcrResultListener.ProcessMessage.
				logger.LogWarning("Failed to update document {DocumentId} with GenAI summary - {Error}",
					genAiEvent.DocumentId, updateResult.FirstError.Description);
				await consumer.NackAsync(false);
				return;
			}

			logger.LogInformation("Successfully updated document {DocumentId} with GenAI summary",
				genAiEvent.DocumentId);

			sseStream.Publish(genAiEvent);
			await consumer.AckAsync();
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error processing GenAI result for document {DocumentId}", genAiEvent.DocumentId);
			await consumer.NackAsync();
		}
	}
}
