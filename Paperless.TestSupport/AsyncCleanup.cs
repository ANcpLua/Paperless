namespace Paperless.TestSupport;

/// <summary>
///     RAII per-test cleanup: <c>await using var cleanup = new AsyncCleanup(() =&gt; ...);</c>
///     runs the delegate on scope exit even when an assertion throws. Mirrors the
///     agent-framework <c>SessionCleanup</c> pattern. The delegate runs at most once.
/// </summary>
public sealed class AsyncCleanup(Func<ValueTask> onDispose) : IAsyncDisposable
{
	private int _disposed;

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		await onDispose();
	}
}
