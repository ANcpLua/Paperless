namespace PaperlessREST.Tests;

public static class FakeLoggerExtensions
{
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
}
