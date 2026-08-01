namespace PaperlessServices.Tests.Unit;

public sealed class CreatePdfExtractorTests
{
	private readonly FakeLogger<CreatePdfExtractor> _logger = new();

	private CreatePdfExtractor CreateSut() => new(_logger);

	[Fact]
	public async Task ExtractTextAsync_WhenCallerCancels_PropagatesCancellation()
	{
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();
		await using MemoryStream pdfStream = new();

		Func<Task> act = async () => await CreateSut().ExtractTextAsync(pdfStream, cancellation.Token);

		OperationCanceledException thrown =
			(await act.Should().ThrowAsync<OperationCanceledException>()).Which;
		thrown.CancellationToken.Should().Be(cancellation.Token);
		_logger.Collector.GetSnapshot().Should().NotContain(log => log.Level == LogLevel.Error);
	}

	[Fact]
	public async Task ExtractTextAsync_WhenStreamIsDisposed_ReturnsExtractionFailureAndLogsError()
	{
		MemoryStream pdfStream = new();
		await pdfStream.DisposeAsync();

		ErrorOr<string> result =
			await CreateSut().ExtractTextAsync(pdfStream, TestContext.Current.CancellationToken);

		result.IsError.Should().BeTrue();
		result.FirstError.Code.Should().Be("Ocr.ExtractionFailed");
		_logger.Collector.GetSnapshot().Should().Contain(log =>
			log.Level == LogLevel.Error &&
			log.Message.Contains("OCR extraction failed", StringComparison.OrdinalIgnoreCase));
	}
}
