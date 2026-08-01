namespace PaperlessServices.Tests.Unit;

public sealed class FakeLoggerExtensionsTests
{
	[Fact]
	public async Task WaitForLogAsync_WhenConditionIsAlreadyMet_ReturnsTrue()
	{
		FakeLogCollector collector = new();
		FakeLogger<FakeLoggerExtensionsTests> logger = new(collector);
		logger.LogInformation("Ready");

		bool result = await collector.WaitForLogAsync(
			logs => logs.Any(log => log.Message == "Ready"),
			timeout: TimeSpan.FromMilliseconds(50),
			pollInterval: TimeSpan.FromMilliseconds(1),
			cancellationToken: TestContext.Current.CancellationToken);

		result.Should().BeTrue();
	}

	[Fact]
	public async Task WaitForLogAsync_WhenTimeoutExpires_ReturnsFalse()
	{
		FakeLogCollector collector = new();

		bool result = await collector.WaitForLogAsync(
			_ => false,
			timeout: TimeSpan.FromMilliseconds(20),
			pollInterval: TimeSpan.FromMilliseconds(1),
			cancellationToken: TestContext.Current.CancellationToken);

		result.Should().BeFalse();
	}

	[Fact]
	public async Task WaitForLogAsync_WhenCallerCancels_PropagatesCancellation()
	{
		FakeLogCollector collector = new();
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();

		Func<Task> act = () => collector.WaitForLogAsync(
			_ => false,
			timeout: TimeSpan.FromSeconds(1),
			pollInterval: TimeSpan.FromMilliseconds(1),
			cancellationToken: cancellation.Token);

		(await act.Should().ThrowExactlyAsync<OperationCanceledException>())
			.Which.CancellationToken.Should().Be(cancellation.Token);
	}
}
