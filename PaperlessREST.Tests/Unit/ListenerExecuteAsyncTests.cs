using System.Runtime.CompilerServices;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Lifecycle tests for the BackgroundService <see cref="GenAiResultListener.ExecuteAsync" /> and
///     <see cref="OcrResultListener.ExecuteAsync" /> overrides. Cover the consumer factory wiring,
///     the consume-loop, and the generic-exception catch branch on the GenAI listener.
///     <para>
///         Completion is signalled via <see cref="TaskCompletionSource" /> set from a mock callback
///         (per CLAUDE.md): never poll a log snapshot.
///     </para>
///     <para>
///         The OperationInterruptedException "no queue" branch is intentionally not unit-tested:
///         constructing that exception requires RabbitMQ-internal ShutdownEventArgs types that aren't
///         in the test project's surface, and the branch is a one-off shutdown helper — covered
///         operationally on a missing-queue restart, not via fake-broker reproduction here.
///     </para>
/// </summary>
public sealed class GenAiResultListenerExecuteAsyncTests : IDisposable
{
	private readonly Mock<IRabbitMqConsumer<GenAIEvent>> _consumer;
	private readonly Mock<IRabbitMqConsumerFactory> _consumerFactory;
	private readonly Mock<IDocumentService> _documentService;
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<GenAiResultListener> _logger;
	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IServiceScope> _scope;
	private readonly Mock<IServiceScopeFactory> _scopeFactory;
	private readonly Mock<IServiceProvider> _serviceProvider;
	private readonly Mock<ISseStream<GenAIEvent>> _sseStream;

	public GenAiResultListenerExecuteAsyncTests()
	{
		_scopeFactory = _mocks.Create<IServiceScopeFactory>();
		_scope = _mocks.Create<IServiceScope>();
		_serviceProvider = _mocks.Create<IServiceProvider>();
		_documentService = _mocks.Create<IDocumentService>();
		_consumerFactory = _mocks.Create<IRabbitMqConsumerFactory>();
		_consumer = _mocks.Create<IRabbitMqConsumer<GenAIEvent>>();
		_sseStream = _mocks.Create<ISseStream<GenAIEvent>>();
		_logger = new FakeLogger<GenAiResultListener>(_logCollector);

		_scope.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);
		_scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
		_scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
		_serviceProvider.Setup(p => p.GetService(typeof(IDocumentService))).Returns(_documentService.Object);
		_consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);
	}

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
	}

	private GenAiResultListener CreateSut() =>
		new(_consumerFactory.Object, _scopeFactory.Object, _sseStream.Object, _logger);

	[Fact]
	public async Task ExecuteAsync_HappyPath_LogsStartedConsumesAndStopped()
	{
		GenAIEvent evt = new(Guid.CreateVersion7(), "summary", TimeProvider.System.GetUtcNow(), null);
		TaskCompletionSource<bool> processed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		_consumerFactory.Setup(f => f.CreateConsumerAsync<GenAIEvent>())
			.ReturnsAsync(_consumer.Object);
		_consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
			.Returns((CancellationToken ct) => ListenerStreams.SingleThenWait(evt, ct));
		_documentService.Setup(s => s.UpdateDocumentSummaryAsync(
				evt.DocumentId, evt.Summary!, evt.GeneratedAt, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);
		_sseStream.Setup(s => s.Publish(evt));
		_consumer.Setup(c => c.AckAsync())
			.Returns(() =>
			{
				processed.TrySetResult(true);
				return Task.CompletedTask;
			});

		using GenAiResultListener sut = CreateSut();
		using CancellationTokenSource cts = new();

		await sut.StartAsync(cts.Token);
		await processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		await cts.CancelAsync();
		await sut.ExecuteTask!;

		_logCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Information && l.Message.Contains("GenAI Result Listener started"));
		_logCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Information && l.Message.Contains("GenAI Result Listener stopped"));
	}

	[Fact]
	public async Task ExecuteAsync_UnexpectedException_LogsErrorAndRethrows()
	{
		InvalidOperationException boom = new("broker down");
		_consumerFactory.Setup(f => f.CreateConsumerAsync<GenAIEvent>())
			.ThrowsAsync(boom);

		using GenAiResultListener sut = CreateSut();
		using CancellationTokenSource cts = new();

		Func<Task> startAndAwait = async () =>
		{
			await sut.StartAsync(cts.Token);
			await sut.ExecuteTask!;
		};

		await startAndAwait.Should().ThrowAsync<InvalidOperationException>().WithMessage("broker down");
		_logCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Error && l.Message.Contains("Unexpected error", StringComparison.OrdinalIgnoreCase));
	}

}

public sealed class OcrResultListenerExecuteAsyncTests : IDisposable
{
	private readonly Mock<IRabbitMqConsumer<OcrEvent>> _consumer;
	private readonly Mock<IRabbitMqConsumerFactory> _consumerFactory;
	private readonly Mock<IDocumentService> _documentService;
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<OcrResultListener> _logger;
	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IServiceScope> _scope;
	private readonly Mock<IServiceScopeFactory> _scopeFactory;
	private readonly Mock<IServiceProvider> _serviceProvider;
	private readonly Mock<ISseStream<OcrEvent>> _sseStream;

	public OcrResultListenerExecuteAsyncTests()
	{
		_scopeFactory = _mocks.Create<IServiceScopeFactory>();
		_scope = _mocks.Create<IServiceScope>();
		_serviceProvider = _mocks.Create<IServiceProvider>();
		_documentService = _mocks.Create<IDocumentService>();
		_consumerFactory = _mocks.Create<IRabbitMqConsumerFactory>();
		_consumer = _mocks.Create<IRabbitMqConsumer<OcrEvent>>();
		_sseStream = _mocks.Create<ISseStream<OcrEvent>>();
		_logger = new FakeLogger<OcrResultListener>(_logCollector);

		_scope.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);
		_scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
		_scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
		_serviceProvider.Setup(p => p.GetService(typeof(IDocumentService))).Returns(_documentService.Object);
		_consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);
	}

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
	}

	private OcrResultListener CreateSut() =>
		new(_consumerFactory.Object, _scopeFactory.Object, _sseStream.Object, _logger);

	[Fact]
	public async Task ExecuteAsync_HappyPath_LogsStartedConsumesAndStopped()
	{
		OcrEvent evt = new(Guid.CreateVersion7(), "Completed", "ocr text", TimeProvider.System.GetUtcNow());
		TaskCompletionSource<bool> processed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		_consumerFactory.Setup(f => f.CreateConsumerAsync<OcrEvent>())
			.ReturnsAsync(_consumer.Object);
		_consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
			.Returns((CancellationToken ct) => ListenerStreams.SingleThenWait(evt, ct));
		_documentService.Setup(s => s.ProcessOcrResultAsync(
				evt.JobId, "Completed", "ocr text", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);
		_sseStream.Setup(s => s.Publish(evt));
		_consumer.Setup(c => c.AckAsync())
			.Returns(() =>
			{
				processed.TrySetResult(true);
				return Task.CompletedTask;
			});

		using OcrResultListener sut = CreateSut();
		using CancellationTokenSource cts = new();

		await sut.StartAsync(cts.Token);
		await processed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		await cts.CancelAsync();
		await sut.ExecuteTask!;

		_logCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Information && l.Message.Contains("OCR Result Listener started"));
		_logCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Information && l.Message.Contains("OCR Result Listener stopped"));
	}

}

/// <summary>
///     Helpers for fabricating <see cref="IAsyncEnumerable{T}" /> streams in listener lifecycle tests.
///     Each stream completes cleanly on token cancellation so the consume-loop in
///     <see cref="BackgroundService" /> exits and the "stopped" log gets emitted.
/// </summary>
internal static class ListenerStreams
{
	/// <summary>Yields one item, then waits indefinitely; completes cleanly on token cancellation.</summary>
	public static async IAsyncEnumerable<T> SingleThenWait<T>(
		T item, [EnumeratorCancellation] CancellationToken ct = default)
	{
		yield return item;

		try
		{
			await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
	}

}
