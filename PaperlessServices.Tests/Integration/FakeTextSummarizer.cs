namespace PaperlessServices.Tests.Integration;

internal sealed class FakeTextSummarizer : ITextSummarizer
{
	private const string SummaryPrefix = "Summary:";

	public Task<string?> SummarizeAsync(string text, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(text);
		cancellationToken.ThrowIfCancellationRequested();

		string preview = text.Length <= 64 ? text : text[..64] + "…";
		return Task.FromResult<string?>($"{SummaryPrefix} {preview}");
	}
}
