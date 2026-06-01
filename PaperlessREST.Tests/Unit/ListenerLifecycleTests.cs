// Lifecycle / ExecuteAsync coverage for GenAiResultListener and OcrResultListener.
// The per-message internal helpers (ProcessGenAiEventAsync, ProcessMessage) are
// covered by GenAiResultListenerTests / OcrResultListenerTests. This file targets
// the BackgroundService.ExecuteAsync branches: started/stopped logs, the
// await-foreach driver, the OperationInterruptedException filters, the generic
// catch-and-rethrow, and the stoppingToken.IsCancellationRequested break.
//
// Synchronisation strategy follows the canonical OcrWorker.EmptyStream_CompletesGracefully
// fix documented in CLAUDE.md ("BackgroundService race in tests"): BackgroundService.StartAsync
// returns before ExecuteAsync runs, so we never wait on a log predicate that is already
// true. Completion is signalled via TaskCompletionSource set inside the fake consumer's
// DisposeAsync (which only fires after `await using` unwinds — i.e. after the foreach loop).

using System.Runtime.CompilerServices;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace PaperlessREST.Tests.Unit;

public sealed class ListenerLifecycleTests
{
	// ═══════════════════════════════════════════════════════════════
	// GenAiResultListener — ExecuteAsync branches
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GenAi_ExecuteAsync_HappyPath_LogsStartedAndStopped()
	{
		// Arrange
		GenAiHarness h = new();
		Guid documentId = Guid.CreateVersion7();
		GenAIEvent evt = new(documentId, "summary text", TimeProvider.System.GetUtcNow(), null);

		TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		h.Consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync())
			.Returns(() => { disposed.TrySetResult(); return ValueTask.CompletedTask; });

		h.ConsumerFactory.Setup(f => f.CreateConsumerAsync<GenAIEvent>())
			.ReturnsAsync(h.Consumer.Object);

		h.Consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
			.Returns((CancellationToken ct) => Yield(new[] { evt }, ct));

		h.DocumentService.Setup(s => s.UpdateDocumentSummaryAsync(
				documentId, "summary text", evt.GeneratedAt, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		h.SseStream.Setup(s => s.Publish(evt));
		h.Consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using GenAiResultListener sut = h.CreateSut();
		using CancellationTokenSource cts = new();

		// Act
		await sut.StartAsync(cts.Token);
		await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		await sut.StopAsync(CancellationToken.None);

		// Assert
		IReadOnlyList<FakeLogRecord> logs = h.LogCollector.GetSnapshot();
		logs.Should().Contain(l =>
			l.Level == LogLevel.Information &&
			l.Message.Contains("GenAI Result Listener started", StringComparison.OrdinalIgnoreCase));
		logs.Should().Contain(l =>
			l.Level == LogLevel.Information &&
			l.Message.Contains("GenAI Result Listener stopped", StringComparison.OrdinalIgnoreCase));
		h.Consumer.Verify(c => c.AckAsync(), Times.Once);
	}

	[Fact]
	public async Task GenAi_ExecuteAsync_NoQueue_LogsWarningAndWaitsForCancellation()
	{
		// Arrange — CreateConsumerAsync throws OperationInterruptedException with "no queue" message.
		// Handler should log Warning + call Task.Delay(Timeout.Infinite, stoppingToken).
		// Cancelling the stoppingToken makes ExecuteAsync return cleanly (no rethrow).
		GenAiHarness h = new();
		ShutdownEventArgs reason = new(
			ShutdownInitiator.Application, 404, "no queue 'GenAIEvent' in vhost '/'",
			cause: new object(), cancellationToken: CancellationToken.None);
		OperationInterruptedException noQueue = new(reason);

		h.ConsumerFactory.Setup(f => f.CreateConsumerAsync<GenAIEvent>())
			.ThrowsAsync(noQueue);

		using GenAiResultListener sut = h.CreateSut();
		using CancellationTokenSource cts = new();

		// Act
		await sut.StartAsync(cts.Token);

		// Wait until the "disabled" warning lands — that proves the when-filter matched.
		// Then cancel so the Task.Delay(Timeout.Infinite, stoppingToken) unblocks.
		await WaitForLogAsync(
			h.LogCollector,
			l => l.Level == LogLevel.Warning &&
			     l.Message.Contains("GenAI Result Listener disabled", StringComparison.OrdinalIgnoreCase),
			TestContext.Current.CancellationToken);

		await cts.CancelAsync();
		await sut.StopAsync(CancellationToken.None);

		// Assert
		h.LogCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Warning &&
			l.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase) &&
			l.Message.Contains("GenAIEvent queue", StringComparison.OrdinalIgnoreCase));
		// Generic error path must NOT have been taken.
		h.LogCollector.GetSnapshot().Should().NotContain(l =>
			l.Level == LogLevel.Error &&
			l.Message.Contains("Unexpected error", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task GenAi_ExecuteAsync_OperationInterrupted_WithoutNoQueue_LogsErrorAndRethrows()
	{
		// Arrange — same exception type, message does NOT contain "no queue".
		// The `when (...)` filter must be false; the generic catch logs Error and rethrows.
		GenAiHarness h = new();
		ShutdownEventArgs reason = new(
			ShutdownInitiator.Peer, 320, "connection lost",
			cause: new object(), cancellationToken: CancellationToken.None);
		OperationInterruptedException connectionLost = new(reason);

		h.ConsumerFactory.Setup(f => f.CreateConsumerAsync<GenAIEvent>())
			.ThrowsAsync(connectionLost);

		using GenAiResultListener sut = h.CreateSut();
		using CancellationTokenSource cts = new();

		// Act — BackgroundService captures the ExecuteAsync task; the throw is surfaced
		// via ExecuteTask, not StartAsync (which only awaits the first yield-point).
		await sut.StartAsync(cts.Token);

		Func<Task> awaitExecute = () => sut.ExecuteTask!;
		(await awaitExecute.Should().ThrowAsync<OperationInterruptedException>())
			.Which.Message.Should().Contain("connection lost");

		// StopAsync after a faulted ExecuteTask must not throw; the host swallows the captured fault.
		await sut.StopAsync(CancellationToken.None);

		h.LogCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Error &&
			l.Message.Contains("Unexpected error", StringComparison.OrdinalIgnoreCase));
		// "Disabled" warning path must NOT have been taken.
		h.LogCollector.GetSnapshot().Should().NotContain(l =>
			l.Level == LogLevel.Warning &&
			l.Message.Contains("disabled", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task GenAi_ExecuteAsync_ConsumeAsyncThrows_GenericCatchLogsAndRethrows()
	{
		// Arrange — non-OperationInterruptedException mid-iteration. Generic catch logs + rethrows.
		GenAiHarness h = new();

		h.Consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);

		h.ConsumerFactory.Setup(f => f.CreateConsumerAsync<GenAIEvent>())
			.ReturnsAsync(h.Consumer.Object);

		h.Consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
			.Returns(ThrowingStream<GenAIEvent>(new InvalidOperationException("rabbit died")));

		using GenAiResultListener sut = h.CreateSut();
		using CancellationTokenSource cts = new();

		// Act
		await sut.StartAsync(cts.Token);

		// The exception surfaces on the captured ExecuteTask, not on StartAsync.
		Func<Task> awaitExecute = () => sut.ExecuteTask!;
		(await awaitExecute.Should().ThrowAsync<InvalidOperationException>())
			.Which.Message.Should().Be("rabbit died");

		await sut.StopAsync(CancellationToken.None);

		h.LogCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Error &&
			l.Message.Contains("Unexpected error", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task GenAi_ExecuteAsync_StoppingTokenCancelled_BreaksLoopAfterFirstEvent()
	{
		// Arrange — gated stream: yield event #1, wait on a TCS before considering yielding #2/#3.
		// The test cancels the stoppingToken before releasing the gate, which trips
		// `if (stoppingToken.IsCancellationRequested) break;` on the next iteration boundary.
		GenAiHarness h = new();
		Guid id1 = Guid.CreateVersion7();
		Guid id2 = Guid.CreateVersion7();
		Guid id3 = Guid.CreateVersion7();
		GenAIEvent e1 = new(id1, "first", TimeProvider.System.GetUtcNow(), null);
		GenAIEvent e2 = new(id2, "second", TimeProvider.System.GetUtcNow(), null);
		GenAIEvent e3 = new(id3, "third", TimeProvider.System.GetUtcNow(), null);

		TaskCompletionSource firstAckObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		h.Consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync())
			.Returns(() => { disposed.TrySetResult(); return ValueTask.CompletedTask; });

		h.ConsumerFactory.Setup(f => f.CreateConsumerAsync<GenAIEvent>())
			.ReturnsAsync(h.Consumer.Object);

		h.Consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
			.Returns((CancellationToken ct) => GatedStream(new[] { e1, e2, e3 }, gate.Task, ct));

		h.DocumentService.Setup(s => s.UpdateDocumentSummaryAsync(
				id1, "first", e1.GeneratedAt, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		h.SseStream.Setup(s => s.Publish(e1));
		h.Consumer.Setup(c => c.AckAsync())
			.Returns(() => { firstAckObserved.TrySetResult(); return Task.CompletedTask; });

		using GenAiResultListener sut = h.CreateSut();
		using CancellationTokenSource cts = new();

		// Act
		await sut.StartAsync(cts.Token);

		// Wait for event #1 to land in AckAsync. At this point, the producer is parked on `gate`.
		await firstAckObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// Cancel BEFORE releasing the gate so the next iteration of the foreach sees a cancelled token.
		await cts.CancelAsync();
		gate.TrySetResult();

		await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		await sut.StopAsync(CancellationToken.None);

		// Assert — exactly one document processed; events #2 and #3 never touched.
		h.DocumentService.Verify(s => s.UpdateDocumentSummaryAsync(
			id1, "first", e1.GeneratedAt, It.IsAny<CancellationToken>()), Times.Once);
		h.DocumentService.Verify(s => s.UpdateDocumentSummaryAsync(
			id2, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
		h.DocumentService.Verify(s => s.UpdateDocumentSummaryAsync(
			id3, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
		h.Consumer.Verify(c => c.AckAsync(), Times.Once);
	}

	[Fact]
	public async Task GenAi_ExecuteAsync_TokenCancelledBetweenYields_BodyBreakCheckFires()
	{
		// Arrange — iterator deliberately does NOT honor the [EnumeratorCancellation] token, so
		// after the test cancels the stoppingToken the iterator still yields the next event.
		// That is what trips the `if (stoppingToken.IsCancellationRequested) { break; }` block
		// inside the foreach body (lines 20-22 of GenAiResultListener.cs) — coverage that the
		// gated/throwing iterators cannot reach because they all surface OCE before re-entering
		// the body. The clean "stopped" log proves the loop exited via `break`, not via throw.
		GenAiHarness h = new();
		Guid id1 = Guid.CreateVersion7();
		Guid id2 = Guid.CreateVersion7();
		GenAIEvent e1 = new(id1, "first", TimeProvider.System.GetUtcNow(), null);
		GenAIEvent e2 = new(id2, "second", TimeProvider.System.GetUtcNow(), null);

		TaskCompletionSource firstAckObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
		using CancellationTokenSource cts = new();

		h.Consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync())
			.Returns(() => { disposed.TrySetResult(); return ValueTask.CompletedTask; });

		h.ConsumerFactory.Setup(f => f.CreateConsumerAsync<GenAIEvent>())
			.ReturnsAsync(h.Consumer.Object);

		h.Consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
			.Returns(YieldAfterCancel(e1, e2, firstAckObserved, cts));

		h.DocumentService.Setup(s => s.UpdateDocumentSummaryAsync(
				id1, "first", e1.GeneratedAt, It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		h.SseStream.Setup(s => s.Publish(e1));
		h.Consumer.Setup(c => c.AckAsync())
			.Returns(() => { firstAckObserved.TrySetResult(); return Task.CompletedTask; });

		using GenAiResultListener sut = h.CreateSut();

		// Act
		await sut.StartAsync(cts.Token);
		await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		await sut.StopAsync(CancellationToken.None);

		// Assert — e1 processed exactly once; e2 yielded but tripped the body's break before any
		// downstream call. The "stopped" log proves the foreach exited cleanly via break (no throw).
		h.DocumentService.Verify(s => s.UpdateDocumentSummaryAsync(
			id1, "first", e1.GeneratedAt, It.IsAny<CancellationToken>()), Times.Once);
		h.DocumentService.Verify(s => s.UpdateDocumentSummaryAsync(
			id2, It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
		h.Consumer.Verify(c => c.AckAsync(), Times.Once);
		h.SseStream.Verify(s => s.Publish(e2), Times.Never);

		IReadOnlyList<FakeLogRecord> logs = h.LogCollector.GetSnapshot();
		logs.Should().Contain(l =>
			l.Level == LogLevel.Information &&
			l.Message.Contains("GenAI Result Listener stopped", StringComparison.OrdinalIgnoreCase));
		logs.Should().NotContain(l => l.Level == LogLevel.Error);
	}

	// ═══════════════════════════════════════════════════════════════
	// OcrResultListener — ExecuteAsync branches
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task Ocr_ExecuteAsync_HappyPath_LogsStartedAndStopped()
	{
		OcrHarness h = new();
		Guid jobId = Guid.CreateVersion7();
		OcrEvent evt = new(jobId, "Completed", "extracted text", TimeProvider.System.GetUtcNow());

		TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

		h.Consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync())
			.Returns(() => { disposed.TrySetResult(); return ValueTask.CompletedTask; });

		h.ConsumerFactory.Setup(f => f.CreateConsumerAsync<OcrEvent>())
			.ReturnsAsync(h.Consumer.Object);

		h.Consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
			.Returns((CancellationToken ct) => Yield(new[] { evt }, ct));

		h.DocumentService.Setup(s => s.ProcessOcrResultAsync(
				jobId, "Completed", "extracted text", It.IsAny<CancellationToken>()))
			.ReturnsAsync(Result.Updated);

		h.SseStream.Setup(s => s.Publish(evt));
		h.Consumer.Setup(c => c.AckAsync()).Returns(Task.CompletedTask);

		using OcrResultListener sut = h.CreateSut();
		using CancellationTokenSource cts = new();

		await sut.StartAsync(cts.Token);
		await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		await sut.StopAsync(CancellationToken.None);

		IReadOnlyList<FakeLogRecord> logs = h.LogCollector.GetSnapshot();
		logs.Should().Contain(l =>
			l.Level == LogLevel.Information &&
			l.Message.Contains("OCR Result Listener started", StringComparison.OrdinalIgnoreCase));
		logs.Should().Contain(l =>
			l.Level == LogLevel.Information &&
			l.Message.Contains("OCR Result Listener stopped", StringComparison.OrdinalIgnoreCase));
		h.Consumer.Verify(c => c.AckAsync(), Times.Once);
	}

	[Fact]
	public async Task Ocr_ExecuteAsync_ConsumeAsyncThrows_ExceptionPropagatesUncaught()
	{
		// OcrResultListener has NO try/catch around the foreach — exceptions propagate.
		OcrHarness h = new();

		h.Consumer.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);

		h.ConsumerFactory.Setup(f => f.CreateConsumerAsync<OcrEvent>())
			.ReturnsAsync(h.Consumer.Object);

		h.Consumer.Setup(c => c.ConsumeAsync(It.IsAny<CancellationToken>()))
			.Returns(ThrowingStream<OcrEvent>(new InvalidOperationException("ocr stream died")));

		using OcrResultListener sut = h.CreateSut();
		using CancellationTokenSource cts = new();

		await sut.StartAsync(cts.Token);

		Func<Task> awaitExecute = () => sut.ExecuteTask!;
		(await awaitExecute.Should().ThrowAsync<InvalidOperationException>())
			.Which.Message.Should().Be("ocr stream died");

		await sut.StopAsync(CancellationToken.None);

		// "stopped" log line must NOT be reached when the exception escapes mid-foreach.
		h.LogCollector.GetSnapshot().Should().NotContain(l =>
			l.Level == LogLevel.Information &&
			l.Message.Contains("OCR Result Listener stopped", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task Ocr_ExecuteAsync_CreateConsumerThrows_ExceptionPropagatesUncaught()
	{
		// Factory failure happens before the `await using`, so nothing to dispose.
		OcrHarness h = new();

		h.ConsumerFactory.Setup(f => f.CreateConsumerAsync<OcrEvent>())
			.ThrowsAsync(new InvalidOperationException("factory exploded"));

		using OcrResultListener sut = h.CreateSut();
		using CancellationTokenSource cts = new();

		await sut.StartAsync(cts.Token);

		Func<Task> awaitExecute = () => sut.ExecuteTask!;
		(await awaitExecute.Should().ThrowAsync<InvalidOperationException>())
			.Which.Message.Should().Be("factory exploded");

		await sut.StopAsync(CancellationToken.None);

		// "started" log fires before factory call; "stopped" must NOT be present.
		h.LogCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Information &&
			l.Message.Contains("OCR Result Listener started", StringComparison.OrdinalIgnoreCase));
		h.LogCollector.GetSnapshot().Should().NotContain(l =>
			l.Level == LogLevel.Information &&
			l.Message.Contains("OCR Result Listener stopped", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// Async-enumerable helpers — TaskCompletionSource-controlled streams.
	// No Task.Delay polling; cancellation is observed cooperatively.
	// ═══════════════════════════════════════════════════════════════

	private static async IAsyncEnumerable<T> Yield<T>(
		IEnumerable<T> items,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		foreach (T item in items)
		{
			ct.ThrowIfCancellationRequested();
			await Task.Yield();
			yield return item;
		}
	}

	private static async IAsyncEnumerable<T> ThrowingStream<T>(Exception exception)
	{
		await Task.Yield();
		throw exception;
#pragma warning disable CS0162 // Unreachable — required so the compiler treats this as an iterator.
		yield break;
#pragma warning restore CS0162
	}

	private static async IAsyncEnumerable<T> GatedStream<T>(
		IReadOnlyList<T> items,
		Task gate,
		[EnumeratorCancellation] CancellationToken ct = default)
	{
		// Yield the first item immediately; for each subsequent item, wait on the gate so the
		// test can deterministically cancel the token before the producer yields again.
		for (var i = 0; i < items.Count; i++)
		{
			if (i > 0)
			{
				await gate.WaitAsync(ct).ConfigureAwait(false);
			}

			ct.ThrowIfCancellationRequested();
			yield return items[i];
		}
	}

	// Deliberately non-cooperative iterator: yields the second event AFTER the stoppingToken is
	// cancelled, without honoring the [EnumeratorCancellation] token. Required to exercise the
	// body-internal `if (stoppingToken.IsCancellationRequested) break;` check, which only fires
	// when the iterator hands a yielded value back into a cancelled foreach body.
	private static async IAsyncEnumerable<T> YieldAfterCancel<T>(
		T first,
		T second,
		TaskCompletionSource firstAckObserved,
		CancellationTokenSource cts)
	{
		yield return first;
		// Wait until the body has acked the first event — without forwarding the token, since
		// honoring it would short-circuit the iterator and skip the second yield entirely.
		await firstAckObserved.Task.ConfigureAwait(false);
		await cts.CancelAsync().ConfigureAwait(false);
		// At this point the next MoveNextAsync hands `second` back to the foreach body, which
		// must observe the cancellation and break.
		yield return second;
	}

	// Local poll-and-wait for a log predicate — small, self-contained, no Task.Delay loops
	// outside of explicit polling intervals (kept short, bounded by the test cancellation token).
	private static async Task WaitForLogAsync(
		FakeLogCollector source,
		Func<FakeLogRecord, bool> predicate,
		CancellationToken ct)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
		timeout.CancelAfter(TimeSpan.FromSeconds(5));

		while (!timeout.IsCancellationRequested)
		{
			if (source.GetSnapshot().Any(predicate))
			{
				return;
			}

			try
			{
				await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}

		source.GetSnapshot().Should().Contain(l => predicate(l),
			"the expected log entry must arrive within the timeout");
	}

	// ═══════════════════════════════════════════════════════════════
	// Test harnesses — encapsulate the strict-mock graph so each test is short.
	// ═══════════════════════════════════════════════════════════════

	private sealed class GenAiHarness
	{
		public MockRepository Mocks { get; } = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
		public Mock<IServiceScopeFactory> ScopeFactory { get; }
		public Mock<IServiceScope> Scope { get; }
		public Mock<IServiceProvider> ServiceProvider { get; }
		public Mock<IDocumentService> DocumentService { get; }
		public Mock<IRabbitMqConsumerFactory> ConsumerFactory { get; }
		public Mock<IRabbitMqConsumer<GenAIEvent>> Consumer { get; }
		public Mock<ISseStream<GenAIEvent>> SseStream { get; }
		public FakeLogCollector LogCollector { get; } = new();
		public FakeLogger<GenAiResultListener> Logger { get; }

		public GenAiHarness()
		{
			ScopeFactory = Mocks.Create<IServiceScopeFactory>();
			Scope = Mocks.Create<IServiceScope>();
			ServiceProvider = Mocks.Create<IServiceProvider>();
			DocumentService = Mocks.Create<IDocumentService>();
			ConsumerFactory = Mocks.Create<IRabbitMqConsumerFactory>();
			Consumer = Mocks.Create<IRabbitMqConsumer<GenAIEvent>>();
			SseStream = Mocks.Create<ISseStream<GenAIEvent>>();
			Logger = new FakeLogger<GenAiResultListener>(LogCollector);

			// .As<>() must precede .Object access.
			Scope.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);
			ScopeFactory.Setup(f => f.CreateScope()).Returns(Scope.Object);
			Scope.Setup(s => s.ServiceProvider).Returns(ServiceProvider.Object);
			ServiceProvider.Setup(p => p.GetService(typeof(IDocumentService))).Returns(DocumentService.Object);
		}

		public GenAiResultListener CreateSut() =>
			new(ConsumerFactory.Object, ScopeFactory.Object, SseStream.Object, Logger);
	}

	private sealed class OcrHarness
	{
		public MockRepository Mocks { get; } = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
		public Mock<IServiceScopeFactory> ScopeFactory { get; }
		public Mock<IServiceScope> Scope { get; }
		public Mock<IServiceProvider> ServiceProvider { get; }
		public Mock<IDocumentService> DocumentService { get; }
		public Mock<IRabbitMqConsumerFactory> ConsumerFactory { get; }
		public Mock<IRabbitMqConsumer<OcrEvent>> Consumer { get; }
		public Mock<ISseStream<OcrEvent>> SseStream { get; }
		public FakeLogCollector LogCollector { get; } = new();
		public FakeLogger<OcrResultListener> Logger { get; }

		public OcrHarness()
		{
			ScopeFactory = Mocks.Create<IServiceScopeFactory>();
			Scope = Mocks.Create<IServiceScope>();
			ServiceProvider = Mocks.Create<IServiceProvider>();
			DocumentService = Mocks.Create<IDocumentService>();
			ConsumerFactory = Mocks.Create<IRabbitMqConsumerFactory>();
			Consumer = Mocks.Create<IRabbitMqConsumer<OcrEvent>>();
			SseStream = Mocks.Create<ISseStream<OcrEvent>>();
			Logger = new FakeLogger<OcrResultListener>(LogCollector);

			Scope.As<IAsyncDisposable>().Setup(d => d.DisposeAsync()).Returns(ValueTask.CompletedTask);
			ScopeFactory.Setup(f => f.CreateScope()).Returns(Scope.Object);
			Scope.Setup(s => s.ServiceProvider).Returns(ServiceProvider.Object);
			ServiceProvider.Setup(p => p.GetService(typeof(IDocumentService))).Returns(DocumentService.Object);
		}

		public OcrResultListener CreateSut() =>
			new(ConsumerFactory.Object, ScopeFactory.Object, SseStream.Object, Logger);
	}
}
