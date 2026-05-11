namespace PaperlessREST.Tests.Unit;

public sealed class OcrResultListenerTests : IDisposable
{
	// ═══════════════════════════════════════════════════════════════
	// CONSTANTS
	// ═══════════════════════════════════════════════════════════════

	private const string CompletedStatus = "Completed";
	private const string FailedStatus = "Failed";
	private const string ExtractedContent = "This is extracted OCR text from the document.";
	private readonly Mock<IRabbitMqConsumer<OcrEvent>> _consumer;
	private readonly Mock<IRabbitMqConsumerFactory> _consumerFactory;
	private readonly Mock<IDocumentService> _documentService;
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<OcrResultListener> _logger;

	// ═══════════════════════════════════════════════════════════════
	// CONSTRUCTION
	// ═══════════════════════════════════════════════════════════════

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IServiceScope> _scope;

	private readonly Mock<IServiceScopeFactory> _scopeFactory;
	private readonly Mock<IServiceProvider> _serviceProvider;
	private readonly Mock<ISseStream<OcrEvent>> _sseStream;

	public OcrResultListenerTests()
	{
		_scopeFactory = _mocks.Create<IServiceScopeFactory>();
		_scope = _mocks.Create<IServiceScope>();
		_serviceProvider = _mocks.Create<IServiceProvider>();
		_documentService = _mocks.Create<IDocumentService>();
		_consumerFactory = _mocks.Create<IRabbitMqConsumerFactory>();
		_consumer = _mocks.Create<IRabbitMqConsumer<OcrEvent>>();
		_sseStream = _mocks.Create<ISseStream<OcrEvent>>();
		_logger = new FakeLogger<OcrResultListener>(_logCollector);

		SetupScopeFactory();
	}

	// ═══════════════════════════════════════════════════════════════
	// DISPOSAL
	// ═══════════════════════════════════════════════════════════════

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
		_mocks.VerifyAll();
		_mocks.VerifyNoOtherCalls();
	}

	private void SetupScopeFactory()
	{
		// IMPORTANT: .As<>() must be called BEFORE accessing .Object
		_scope.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);

		// Mock CreateScope (the interface method, not the CreateAsyncScope extension)
		_scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
		_scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
		_serviceProvider.Setup(p => p.GetService(typeof(IDocumentService))).Returns(_documentService.Object);
	}

	private OcrResultListener CreateSut() =>
		new(_consumerFactory.Object, _scopeFactory.Object, _sseStream.Object, _logger);

	private static OcrEvent CreateOcrEvent(
		Guid? jobId = null,
		string status = CompletedStatus,
		string? text = ExtractedContent) =>
		new(jobId ?? Guid.CreateVersion7(), status, text, DateTimeOffset.UtcNow);

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessMessage - Completed Status Success Path
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessMessage_CompletedStatus_ProcessesAndPublishesToSse()
	{
		// Arrange
		OcrEvent ocrEvent = CreateOcrEvent();

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				ocrEvent.JobId,
				CompletedStatus,
				ExtractedContent,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		_sseStream.Setup(s => s.Publish(ocrEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Verification in Dispose via VerifyAll
	}

	[Fact]
	public async Task ProcessMessage_CompletedStatus_LogsSuccessfulProcessing()
	{
		// Arrange
		OcrEvent ocrEvent = CreateOcrEvent();

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				ocrEvent.JobId,
				CompletedStatus,
				ExtractedContent,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		_sseStream.Setup(s => s.Publish(ocrEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains("Successfully processed", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessMessage - Failed Status Path (content = null)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessMessage_FailedStatus_PassesNullContentToService()
	{
		// Arrange - Status is "Failed", so content should be null
		OcrEvent ocrEvent = CreateOcrEvent(status: FailedStatus, text: null);

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				ocrEvent.JobId,
				FailedStatus,
				null,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		_sseStream.Setup(s => s.Publish(ocrEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Verification in Dispose
	}

	public static IEnumerable<ITheoryDataRow> NonCompletedStatuses()
	{
		yield return new TheoryDataRow<string>("Failed")
			.WithTestDisplayName("Status: Failed → null content");
		yield return new TheoryDataRow<string>("Error")
			.WithTestDisplayName("Status: Error → null content");
		yield return new TheoryDataRow<string>("Timeout")
			.WithTestDisplayName("Status: Timeout → null content");
		yield return new TheoryDataRow<string>("Unknown")
			.WithTestDisplayName("Status: Unknown → null content");
	}

	[Theory]
	[MemberData(nameof(NonCompletedStatuses))]
	public async Task ProcessMessage_NonCompletedStatus_PassesNullContent(string status)
	{
		// Arrange - Any status other than "Completed" should pass null content
		OcrEvent ocrEvent = CreateOcrEvent(status: status, text: "SomeText");

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				ocrEvent.JobId,
				status,
				null, // Content is null because status != "Completed"
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		_sseStream.Setup(s => s.Publish(ocrEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Content should be null, not the text from the event
	}

	[Fact]
	public async Task ProcessMessage_CompletedStatusWithNullText_PassesNullContent()
	{
		// Arrange - Status is "Completed" but Text is null
		OcrEvent ocrEvent = CreateOcrEvent(status: CompletedStatus, text: null);

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				ocrEvent.JobId,
				CompletedStatus,
				null, // Text is null even though status is Completed
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		_sseStream.Setup(s => s.Publish(ocrEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Verification in Dispose
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessMessage - Document Not Found (Processing Returns False)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessMessage_ProcessingReturnsError_CallsNackWithoutRequeue()
	{
		// Arrange
		OcrEvent ocrEvent = CreateOcrEvent();

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				ocrEvent.JobId,
				CompletedStatus,
				ExtractedContent,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(DocumentErrors.NotFound(ocrEvent.JobId));

		_consumer.Setup(c => c.NackAsync(false)).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Verification in Dispose; SSE should NOT be published
	}

	[Fact]
	public async Task ProcessMessage_ProcessingReturnsError_DoesNotPublishToSse()
	{
		// Arrange
		OcrEvent ocrEvent = CreateOcrEvent();

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				ocrEvent.JobId,
				CompletedStatus,
				ExtractedContent,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(DocumentErrors.NotFound(ocrEvent.JobId));

		_consumer.Setup(c => c.NackAsync(false)).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - SSE.Publish should never be called
		_sseStream.Verify(s => s.Publish(It.IsAny<OcrEvent>()), Times.Never);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessMessage - Exception Handling
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessMessage_ExceptionThrown_CallsNackWithRequeue()
	{
		// Arrange
		OcrEvent ocrEvent = CreateOcrEvent();
		Exception exception = new InvalidOperationException("Database connection failed");

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				It.IsAny<Guid>(),
				It.IsAny<string>(),
				It.IsAny<string?>(),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(exception);

		_consumer.Setup(c => c.NackAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Nack should be called (with requeue=true by default)
		_consumer.Verify(c => c.NackAsync(It.IsAny<bool>()), Times.Once);
	}

	[Fact]
	public async Task ProcessMessage_ExceptionThrown_LogsError()
	{
		// Arrange
		OcrEvent ocrEvent = CreateOcrEvent();
		Exception exception = new InvalidOperationException("Database connection failed");

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				It.IsAny<Guid>(),
				It.IsAny<string>(),
				It.IsAny<string?>(),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(exception);

		_consumer.Setup(c => c.NackAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Error &&
				l.Message.Contains("Error processing", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task ProcessMessage_ExceptionThrown_DoesNotCallAck()
	{
		// Arrange
		OcrEvent ocrEvent = CreateOcrEvent();

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				It.IsAny<Guid>(),
				It.IsAny<string>(),
				It.IsAny<string?>(),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException());

		_consumer.Setup(c => c.NackAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Ack should never be called on error
		_consumer.Verify(c => c.AckAsync(), Times.Never);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessMessage - Logging
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessMessage_ReceivesEvent_LogsJobIdAndStatus()
	{
		// Arrange
		Guid jobId = Guid.CreateVersion7();
		OcrEvent ocrEvent = CreateOcrEvent(jobId);

		_documentService.Setup(s => s.ProcessOcrResultAsync(
				jobId,
				CompletedStatus,
				ExtractedContent,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		_sseStream.Setup(s => s.Publish(ocrEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		OcrResultListener sut = CreateSut();

		// Act
		await sut.ProcessMessage(ocrEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Log should contain JobId
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains(jobId.ToString(), StringComparison.OrdinalIgnoreCase));
	}
}
