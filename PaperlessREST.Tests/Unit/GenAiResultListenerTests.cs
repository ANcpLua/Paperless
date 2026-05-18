namespace PaperlessREST.Tests.Unit;

public sealed class GenAiResultListenerTests : IDisposable
{
	// ═══════════════════════════════════════════════════════════════
	// CONSTANTS
	// ═══════════════════════════════════════════════════════════════

	private const string ValidSummary = "This is a summary of the document content.";
	private const string ErrorMessage = "GenAI processing failed";
	private readonly Mock<IRabbitMqConsumer<GenAIEvent>> _consumer;
	private readonly Mock<IRabbitMqConsumerFactory> _consumerFactory;
	private readonly Mock<IDocumentService> _documentService;
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<GenAiResultListener> _logger;

	// ═══════════════════════════════════════════════════════════════
	// CONSTRUCTION
	// ═══════════════════════════════════════════════════════════════

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IServiceScope> _scope;

	private readonly Mock<IServiceScopeFactory> _scopeFactory;
	private readonly Mock<IServiceProvider> _serviceProvider;
	private readonly Mock<ISseStream<GenAIEvent>> _sseStream;

	public GenAiResultListenerTests()
	{
		_scopeFactory = _mocks.Create<IServiceScopeFactory>();
		_scope = _mocks.Create<IServiceScope>();
		_serviceProvider = _mocks.Create<IServiceProvider>();
		_documentService = _mocks.Create<IDocumentService>();
		_consumerFactory = _mocks.Create<IRabbitMqConsumerFactory>();
		_consumer = _mocks.Create<IRabbitMqConsumer<GenAIEvent>>();
		_sseStream = _mocks.Create<ISseStream<GenAIEvent>>();
		_logger = new FakeLogger<GenAiResultListener>(_logCollector);

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

	private GenAiResultListener CreateSut() =>
		new(_consumerFactory.Object, _scopeFactory.Object, _sseStream.Object, _logger);

	private static GenAIEvent CreateGenAiEvent(
		Guid? documentId = null,
		string? summary = ValidSummary,
		DateTimeOffset? generatedAt = null,
		string? errorMessage = null) =>
		new(documentId ?? Guid.CreateVersion7(), summary, generatedAt ?? TimeProvider.System.GetUtcNow(), errorMessage);

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessGenAiEventAsync - Success Path (With Summary)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessGenAiEventAsync_WithSummary_UpdatesDocumentAndPublishes()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent();

		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				genAiEvent.DocumentId,
				genAiEvent.Summary!,
				genAiEvent.GeneratedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		_sseStream.Setup(s => s.Publish(genAiEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Verification in Dispose via VerifyAll
	}

	[Fact]
	public async Task ProcessGenAiEventAsync_WithSummary_LogsSuccessfulUpdate()
	{
		// Arrange
		Guid documentId = Guid.CreateVersion7();
		GenAIEvent genAiEvent = CreateGenAiEvent(documentId);

		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				documentId,
				ValidSummary,
				genAiEvent.GeneratedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		_sseStream.Setup(s => s.Publish(genAiEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains("Successfully updated", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessGenAiEventAsync - Document Not Found
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessGenAiEventAsync_WithSummary_DocumentNotFound_LogsWarningAndAcks()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent();

		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				genAiEvent.DocumentId,
				genAiEvent.Summary!,
				genAiEvent.GeneratedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(DocumentErrors.NotFound(genAiEvent.DocumentId));

		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - SSE should NOT be published when document not found
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Warning &&
				l.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task ProcessGenAiEventAsync_DocumentNotFound_DoesNotPublishToSse()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent();

		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				genAiEvent.DocumentId,
				genAiEvent.Summary!,
				genAiEvent.GeneratedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(DocumentErrors.NotFound(genAiEvent.DocumentId));

		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - SSE.Publish should never be called
		_sseStream.Verify(s => s.Publish(It.IsAny<GenAIEvent>()), Times.Never);
	}

	[Fact]
	public async Task ProcessGenAiEventAsync_WithSummary_UpdateFailureNonNotFound_NacksWithoutRequeue()
	{
		// Arrange — Update fails with a non-NotFound error (infrastructure failure,
		// validation, etc.). Previously this branch silently acked, dropping the
		// message. After the fix it nack-no-requeues to send the message to the DLQ.
		GenAIEvent genAiEvent = CreateGenAiEvent();
		Error infrastructureFailure = Error.Failure(code: "Database.Unavailable",
			description: "Persistence layer unreachable");

		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				genAiEvent.DocumentId,
				genAiEvent.Summary!,
				genAiEvent.GeneratedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(infrastructureFailure);

		_consumer.Setup(c => c.NackAsync(false)).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert — Nack-no-requeue was invoked (strict mock would fail if AckAsync
		// were called instead); SSE is not published; warning is logged.
		_consumer.Verify(c => c.NackAsync(false), Times.Once);
		_consumer.Verify(c => c.AckAsync(), Times.Never);
		_sseStream.Verify(s => s.Publish(It.IsAny<GenAIEvent>()), Times.Never);
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Warning &&
				l.Message.Contains("Failed to update", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessGenAiEventAsync - Empty/Null Summary (Failed GenAI)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessGenAiEventAsync_EmptySummary_LogsWarningPublishesAndAcks()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent(summary: "", errorMessage: ErrorMessage);

		_sseStream.Setup(s => s.Publish(genAiEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - DocumentService should NOT be called
		_documentService.Verify(s => s.UpdateDocumentSummaryAsync(
			It.IsAny<Guid>(),
			It.IsAny<string>(),
			It.IsAny<DateTimeOffset>(),
			It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task ProcessGenAiEventAsync_NullSummary_LogsWarningPublishesAndAcks()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent(summary: null, errorMessage: "Unknown error");

		_sseStream.Setup(s => s.Publish(genAiEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - DocumentService should NOT be called
		_documentService.Verify(s => s.UpdateDocumentSummaryAsync(
			It.IsAny<Guid>(),
			It.IsAny<string>(),
			It.IsAny<DateTimeOffset>(),
			It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task ProcessGenAiEventAsync_WhitespaceSummary_TreatedAsEmpty()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent(summary: "   ", errorMessage: "Whitespace only");

		_sseStream.Setup(s => s.Publish(genAiEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - DocumentService should NOT be called for whitespace-only summary
		_documentService.Verify(s => s.UpdateDocumentSummaryAsync(
			It.IsAny<Guid>(),
			It.IsAny<string>(),
			It.IsAny<DateTimeOffset>(),
			It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task ProcessGenAiEventAsync_EmptySummary_LogsWarningWithErrorMessage()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent(summary: "", errorMessage: ErrorMessage);

		_sseStream.Setup(s => s.Publish(genAiEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Warning &&
				l.Message.Contains("failed", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task ProcessGenAiEventAsync_EmptySummary_NullErrorMessage_LogsUnknownError()
	{
		// Arrange - Test the ?? "Unknown error" branch when errorMessage is null
		GenAIEvent genAiEvent = CreateGenAiEvent(summary: "", errorMessage: null);

		_sseStream.Setup(s => s.Publish(genAiEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Should log "Unknown error" when ErrorMessage is null
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Warning &&
				l.Message.Contains("Unknown error", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessGenAiEventAsync - Exception Handling
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessGenAiEventAsync_ExceptionThrown_LogsErrorAndNacks()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent();
		Exception exception = new InvalidOperationException("Database error");

		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				It.IsAny<Guid>(),
				It.IsAny<string>(),
				It.IsAny<DateTimeOffset>(),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(exception);

		_consumer.Setup(c => c.NackAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Nack should be called
		_consumer.Verify(c => c.NackAsync(It.IsAny<bool>()), Times.Once);
	}

	[Fact]
	public async Task ProcessGenAiEventAsync_ExceptionThrown_DoesNotCallAck()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent();

		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				It.IsAny<Guid>(),
				It.IsAny<string>(),
				It.IsAny<DateTimeOffset>(),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException());

		_consumer.Setup(c => c.NackAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Ack should never be called on error
		_consumer.Verify(c => c.AckAsync(), Times.Never);
	}

	[Fact]
	public async Task ProcessGenAiEventAsync_ExceptionThrown_LogsError()
	{
		// Arrange
		GenAIEvent genAiEvent = CreateGenAiEvent();

		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				It.IsAny<Guid>(),
				It.IsAny<string>(),
				It.IsAny<DateTimeOffset>(),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("Database error"));

		_consumer.Setup(c => c.NackAsync(It.IsAny<bool>())).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Error &&
				l.Message.Contains("Error processing", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessGenAiEventAsync - Logging
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessGenAiEventAsync_ReceivesEvent_LogsDocumentId()
	{
		// Arrange
		Guid documentId = Guid.CreateVersion7();
		GenAIEvent genAiEvent = CreateGenAiEvent(documentId);

		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				documentId,
				ValidSummary,
				genAiEvent.GeneratedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		_sseStream.Setup(s => s.Publish(genAiEvent));
		_consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = CreateSut();

		// Act
		await sut.ProcessGenAiEventAsync(genAiEvent, _consumer.Object, TestContext.Current.CancellationToken);

		// Assert - Log should contain DocumentId
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains(documentId.ToString(), StringComparison.OrdinalIgnoreCase));
	}
}
