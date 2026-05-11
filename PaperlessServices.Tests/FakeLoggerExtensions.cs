using System.Text;

namespace PaperlessServices.Tests;

public static class FakeLoggerExtensions
{
	/// <summary>
	/// Gets full log text from the collector with optional formatting.
	/// </summary>
	public static string GetFullLoggerText(
		this FakeLogCollector source,
		Func<FakeLogRecord, string>? formatter = null)
	{
		StringBuilder sb = new();
		IReadOnlyList<FakeLogRecord> snapshot = source.GetSnapshot();
		formatter ??= record => $"{record.Level} - {record.Message}";

		foreach (FakeLogRecord record in snapshot)
		{
			sb.AppendLine(formatter(record));
		}

		return sb.ToString();
	}

	/// <summary>
	/// Waits for a log condition to be met, polling at regular intervals.
	/// Returns true if condition was met, false if timeout expired.
	/// </summary>
	public static async Task<bool> WaitForLogAsync(
		this FakeLogCollector source,
		Func<IReadOnlyList<FakeLogRecord>, bool> condition,
		TimeSpan? timeout = null,
		TimeSpan? pollInterval = null,
		CancellationToken cancellationToken = default)
	{
		timeout ??= TimeSpan.FromSeconds(5);
		pollInterval ??= TimeSpan.FromMilliseconds(25);

		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(timeout.Value);

		try
		{
			while (!cts.Token.IsCancellationRequested)
			{
				if (condition(source.GetSnapshot()))
				{
					return true;
				}

				await Task.Delay(pollInterval.Value, cts.Token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Timeout expired, not user cancellation
		}

		return condition(source.GetSnapshot()); // Final check
	}

	/// <summary>
	/// Waits for a specific number of log messages matching a predicate.
	/// </summary>
	public static Task<bool> WaitForLogCountAsync(
		this FakeLogCollector source,
		Func<FakeLogRecord, bool> predicate,
		int expectedCount,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		source.WaitForLogAsync(
			logs => logs.Count(predicate) >= expectedCount,
			timeout,
			cancellationToken: cancellationToken);
}
