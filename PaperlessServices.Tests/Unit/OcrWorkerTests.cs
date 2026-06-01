using System.Runtime.CompilerServices;

namespace PaperlessServices.Tests.Unit;

/// <summary>
///     Tests for OcrWorker organized by interaction pattern.
///     Each nested class has its own MockRepository with strict behavior,
///     ensuring verification only checks what that test group actually invokes.
/// </summary>
public static class OcrWorkerTests
{
	public sealed class ProcessMessage : IDisposable
	{
		private readonly Mock<IRabbitMqConsumer<OcrCommand>> _consumer;
		private readonly Mock<IRabbitMqConsumerFactory> _consumerFactory;
		private readonly FakeLogCollector _logCollector = new();
		private readonly FakeLogger<OcrWorker> _logger;
		private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
		private readonly Mock<IOcrProcessor> _ocrProcessor;
		private readonly Mock<IRabbitMqPublisher> _publisher;
		private readonly Mock<IServiceScope> _scope;
		private readonly Mock<IServiceScopeFactory> _scopeFactory;
		private readonly Mock<IServiceProvider> _serviceProvider;
		private readonly FakeTimeProvider _timeProvider = new();

		public ProcessMessage()
		{
			_consumerFactory = _mocks.Create<IRabbitMqConsumerFactory>();
			_consumer = _mocks.Create<IRabbitMqConsumer<OcrCommand>>();
			_scopeFactory = _mocks.Create<IServiceScopeFactory>();
			_scope = _mocks.Create<IServiceScope>();
			_serviceProvider = _mocks.Create<IServiceProvider>();
			_ocrProcessor = _mocks.Create<IOcrProcessor>();
			_publisher = _mocks.Create<IRabbitMqPublisher>();
			_logger = new FakeLogger<OcrWorker>(_logCollector);

			SetupScopeFactory();
		}

		public void Dispose()
		{
			TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
			_mocks.VerifyAll();
			_mocks.VerifyNoOtherCalls();
		}

		private void SetupScopeFactory()
		{
			// Must call .As<T>() BEFORE accessing .Object (Moq constraint)
			_scope.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);

			_scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
			_scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
			_serviceProvider.Setup(p => p.GetService(typeof(IOcrProcessor))).Returns(_ocrProcessor.Object);
		}

		private OcrWorker CreateSut() =>
			new(_consumerFactory.Object, _scopeFactory.Object, _publisher.Object, _timeProvider, _logger);

		[Fact]
		public async Task SuccessfulOcr_AcknowledgesMessage()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId);
			var successResult = CreateSuccessEvent(jobId);

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ReturnsAsync(successResult);

			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrEvent>()))
				.Returns(Task.CompletedTask);
			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<GenAICommand>()))
				.Returns(Task.CompletedTask);

			_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);
		}

		[Fact]
		public async Task SuccessfulOcr_LogsCompletion()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId);
			var successResult = CreateSuccessEvent(jobId);

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ReturnsAsync(successResult);

			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrEvent>()))
				.Returns(Task.CompletedTask);
			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<GenAICommand>()))
				.Returns(Task.CompletedTask);

			_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);

			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Information &&
					l.Message.Contains("Published OCR result", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public async Task SuccessfulOcr_LogsProcessingStart()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId, "invoice-2024.pdf");
			var successResult = CreateSuccessEvent(jobId);

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ReturnsAsync(successResult);

			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrEvent>()))
				.Returns(Task.CompletedTask);
			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<GenAICommand>()))
				.Returns(Task.CompletedTask);

			_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);

			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Information &&
					l.Message.Contains("Processing OCR job", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public async Task OcrFails_AcknowledgesMessage()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId);
			var ocrError = Error.Failure("Ocr.ExtractionFailed", "Could not extract text from PDF");

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ocrError);

			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrEvent>()))
				.Returns(Task.CompletedTask);

			_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);
		}

		[Fact]
		public async Task OcrFails_LogsWarning()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId);
			var ocrError = Error.Failure("Ocr.ExtractionFailed", "Could not extract text from PDF");

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ocrError);

			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrEvent>()))
				.Returns(Task.CompletedTask);

			_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);

			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Warning &&
					l.Message.Contains("failed", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public async Task OcrFails_LogsErrorCode()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId);
			var ocrError = Error.Failure("Ocr.ExtractionFailed", "Could not extract text from PDF");

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ReturnsAsync(ocrError);

			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrEvent>()))
				.Returns(Task.CompletedTask);

			_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);

			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Warning &&
					l.Message.Contains("Ocr.ExtractionFailed", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public async Task ProcessorThrows_NacksMessage()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId);
			Exception exception = new InvalidOperationException("Infrastructure failure");

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ThrowsAsync(exception);

			_consumer.Setup(c => c.NackAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);
		}

		[Fact]
		public async Task ProcessorThrows_DoesNotAck()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId);
			Exception exception = new InvalidOperationException("Infrastructure failure");

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ThrowsAsync(exception);

			_consumer.Setup(c => c.NackAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);

			_consumer.Verify(c => c.AckAsync(), Times.Never);
		}

		[Fact]
		public async Task ProcessorThrows_LogsError()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId);
			Exception exception = new InvalidOperationException("Infrastructure failure");

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ThrowsAsync(exception);

			_consumer.Setup(c => c.NackAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);

			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Error &&
					l.Message.Contains("Infrastructure error", StringComparison.OrdinalIgnoreCase));
		}

		[Fact]
		public async Task ProcessorThrows_LogsJobId()
		{
			var jobId = Guid.CreateVersion7();
			var command = CreateCommand(jobId);
			Exception exception = new InvalidOperationException("Infrastructure failure");

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command, It.IsAny<CancellationToken>()))
				.ThrowsAsync(exception);

			_consumer.Setup(c => c.NackAsync()).Returns(Task.CompletedTask);

			using var sut = CreateSut();

			await sut.ProcessMessage(command, _consumer.Object, TestContext.Current.CancellationToken);

			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Error &&
					l.Message.Contains(jobId.ToString(), StringComparison.OrdinalIgnoreCase));
		}
	}

	public sealed class ExecuteAsync : IDisposable
	{
		private readonly Mock<IRabbitMqConsumer<OcrCommand>> _consumer;
		private readonly Mock<IRabbitMqConsumerFactory> _consumerFactory;
		private readonly FakeLogCollector _logCollector = new();
		private readonly FakeLogger<OcrWorker> _logger;
		private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
		private readonly Mock<IOcrProcessor> _ocrProcessor;
		private readonly Mock<IRabbitMqPublisher> _publisher;
		private readonly Mock<IServiceScope> _scope;
		private readonly Mock<IServiceScopeFactory> _scopeFactory;
		private readonly Mock<IServiceProvider> _serviceProvider;
		private readonly FakeTimeProvider _timeProvider = new();

		public ExecuteAsync()
		{
			_consumerFactory = _mocks.Create<IRabbitMqConsumerFactory>();
			_consumer = _mocks.Create<IRabbitMqConsumer<OcrCommand>>();
			_scopeFactory = _mocks.Create<IServiceScopeFactory>();
			_scope = _mocks.Create<IServiceScope>();
			_serviceProvider = _mocks.Create<IServiceProvider>();
			_ocrProcessor = _mocks.Create<IOcrProcessor>();
			_publisher = _mocks.Create<IRabbitMqPublisher>();
			_logger = new FakeLogger<OcrWorker>(_logCollector);
		}

		public void Dispose()
		{
			TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
			_mocks.VerifyAll();
			_mocks.VerifyNoOtherCalls();
		}

		private void SetupScopeFactory()
		{
			// Must call .As<T>() BEFORE accessing .Object (Moq constraint)
			_scope.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);

			_scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
			_scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
			_serviceProvider.Setup(p => p.GetService(typeof(IOcrProcessor))).Returns(_ocrProcessor.Object);
		}

		private void SetupConsumerInfrastructure()
		{
			// Must call .As<T>() BEFORE accessing .Object (Moq constraint)
			_consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);

			_consumerFactory.Setup(f => f.CreateConsumerAsync<OcrCommand>())
				.ReturnsAsync(_consumer.Object);
		}

		private void SetupPublisherForSuccess()
		{
			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrEvent>()))
				.Returns(Task.CompletedTask);
			_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<GenAICommand>()))
				.Returns(Task.CompletedTask);
		}

		private OcrWorker CreateSut() =>
			new(_consumerFactory.Object, _scopeFactory.Object, _publisher.Object, _timeProvider, _logger);

		[Fact]
		public async Task EmptyStream_CompletesGracefully()
		{
			using CancellationTokenSource cts = new();
			TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

			// Must call .As<T>() BEFORE accessing .Object (Moq constraint)
			_consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync())
				.Returns(() =>
				{
					disposed.TrySetResult();
					return ValueTask.CompletedTask;
				});

			_consumerFactory.Setup(f => f.CreateConsumerAsync<OcrCommand>())
				.ReturnsAsync(_consumer.Object);

			_consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
				.Returns(CreateAsyncEnumerable());

			using var sut = CreateSut();

			await sut.StartAsync(cts.Token);

			// StartAsync may return before ExecuteAsync runs (BackgroundService hands the
			// continuation off to the thread pool on Release-mode Linux x64). Wait for
			// the production code's `await using` to dispose the consumer, which only
			// happens after the empty stream is fully iterated.
			await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

			await sut.StopAsync(cts.Token);

			_logCollector.GetSnapshot()
				.Should().NotContain(l => l.Level >= LogLevel.Error);
		}

		[Fact]
		public async Task ProcessesMultipleMessages()
		{
			using CancellationTokenSource cts = new();
			var jobId1 = Guid.CreateVersion7();
			var jobId2 = Guid.CreateVersion7();
			var command1 = CreateCommand(jobId1);
			var command2 = CreateCommand(jobId2);

			SetupConsumerInfrastructure();
			SetupScopeFactory();
			SetupPublisherForSuccess();

			_consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
				.Returns(CreateAsyncEnumerable(command1, command2));

			_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command1, It.IsAny<CancellationToken>()))
				.ReturnsAsync(CreateSuccessEvent(jobId1));
			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command2, It.IsAny<CancellationToken>()))
				.ReturnsAsync(CreateSuccessEvent(jobId2));

			using var sut = CreateSut();

			await sut.StartAsync(cts.Token);

			// Wait for both messages to be processed
			await _logCollector.WaitForLogCountAsync(
				l => l.Message.Contains("Published OCR result", StringComparison.OrdinalIgnoreCase),
				expectedCount: 2,
				cancellationToken: TestContext.Current.CancellationToken);

			await sut.StopAsync(cts.Token);

			_logCollector.GetSnapshot()
				.Where(l => l.Message.Contains("Published OCR result", StringComparison.OrdinalIgnoreCase))
				.Should().HaveCount(2);
		}

		[Fact]
		public async Task CancellationStopsProcessing()
		{
			using CancellationTokenSource cts = new();
			var messagesProcessed = 0;

			SetupConsumerInfrastructure();
			SetupScopeFactory();
			SetupPublisherForSuccess();

			_consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
				.Returns((CancellationToken ct) => CreateInfiniteStream(ct));

			_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(It.IsAny<OcrCommand>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((OcrCommand cmd, CancellationToken _) =>
				{
					Interlocked.Increment(ref messagesProcessed);
					return CreateSuccessEvent(cmd.JobId);
				});

			using var sut = CreateSut();

			await sut.StartAsync(cts.Token);

			// Wait until at least one message is processed before cancelling
			await _logCollector.WaitForLogCountAsync(
				l => l.Message.Contains("Published OCR result", StringComparison.OrdinalIgnoreCase),
				expectedCount: 1,
				cancellationToken: TestContext.Current.CancellationToken);

			await cts.CancelAsync();
			await sut.StopAsync(CancellationToken.None);

			messagesProcessed.Should().BeInRange(1, 10);
		}

		[Fact]
		public async Task ProcessorThrows_ContinuesWithNextMessage()
		{
			using CancellationTokenSource cts = new();
			var jobId1 = Guid.CreateVersion7();
			var jobId2 = Guid.CreateVersion7();
			var jobId3 = Guid.CreateVersion7();
			var command1 = CreateCommand(jobId1);
			var command2 = CreateCommand(jobId2);
			var command3 = CreateCommand(jobId3);

			SetupConsumerInfrastructure();
			SetupScopeFactory();
			SetupPublisherForSuccess();

			_consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
				.Returns(CreateAsyncEnumerable(command1, command2, command3));

			_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);
			_consumer.Setup(c => c.NackAsync()).Returns(Task.CompletedTask);

			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command1, It.IsAny<CancellationToken>()))
				.ReturnsAsync(CreateSuccessEvent(jobId1));
			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command2, It.IsAny<CancellationToken>()))
				.ThrowsAsync(new InvalidOperationException("Temporary failure"));
			_ocrProcessor.Setup(p => p.ProcessDocumentAsync(command3, It.IsAny<CancellationToken>()))
				.ReturnsAsync(CreateSuccessEvent(jobId3));

			using var sut = CreateSut();

			await sut.StartAsync(cts.Token);

			// Wait for 2 successful messages and 1 error
			await _logCollector.WaitForLogAsync(
				logs => logs.Count(l => l.Message.Contains("Published OCR result", StringComparison.OrdinalIgnoreCase)) >= 2 &&
				        logs.Any(l => l.Level == LogLevel.Error),
				cancellationToken: TestContext.Current.CancellationToken);

			await sut.StopAsync(cts.Token);

			_logCollector.GetSnapshot()
				.Where(l => l.Message.Contains("Published OCR result", StringComparison.OrdinalIgnoreCase))
				.Should().HaveCount(2);

			_logCollector.GetSnapshot()
				.Should().Contain(l =>
					l.Level == LogLevel.Error &&
					l.Message.Contains("Infrastructure error", StringComparison.OrdinalIgnoreCase));
		}
	}

	#region Constants

	private const string ValidFileName = "document.pdf";
	private const string ValidStoragePath = "documents/2024-01/abc123.pdf";
	private const string ExtractedOcrText = "This is the extracted text from the PDF document.";

	#endregion

	#region Helper Methods

	private static OcrCommand CreateCommand(
		Guid? jobId = null,
		string fileName = ValidFileName,
		string storagePath = ValidStoragePath,
		DateTimeOffset? createdAt = null) =>
		new(jobId ?? Guid.CreateVersion7(), fileName, storagePath, createdAt ?? TimeProvider.System.GetUtcNow().AddMinutes(-5));

	private static OcrEvent CreateSuccessEvent(Guid jobId) =>
		new(jobId, "Completed", ExtractedOcrText, TimeProvider.System.GetUtcNow());

	private static async IAsyncEnumerable<OcrCommand> CreateAsyncEnumerable(params OcrCommand[] commands)
	{
		foreach (var command in commands)
		{
			await Task.Yield();
			yield return command;
		}
	}

	private static async IAsyncEnumerable<OcrCommand> CreateInfiniteStream(
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(30, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			if (!ct.IsCancellationRequested)
			{
				yield return CreateCommand(Guid.CreateVersion7());
			}
		}
	}

	#endregion
}
