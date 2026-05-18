namespace PaperlessServices.Tests.Unit;

/// <summary>
///     Covers the catch arm in <see cref="SearchIndexService.IndexDocumentAsync" /> by configuring
///     the client to throw on transport failure (vs. the sibling tests which use
///     <c>ThrowExceptions(false)</c> and exercise the non-throwing failure path).
/// </summary>
public sealed class SearchIndexServiceThrowingTests : IDisposable
{
	private const string UnreachableHost = "http://127.0.0.1:1";
	private const string TestIndexName = "throwing-index";

	private readonly FakeLogCollector _logCollector = new();
	private readonly ElasticsearchClientSettings _settings;
	private readonly SearchIndexService _sut;

	[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "_settings stored in field and disposed in Dispose")]
	public SearchIndexServiceThrowingTests()
	{
		FakeLogger<SearchIndexService> logger = new(_logCollector);

		_settings = new ElasticsearchClientSettings(new Uri(UnreachableHost))
			.DefaultIndex(TestIndexName)
			.RequestTimeout(TimeSpan.FromMilliseconds(50))
			.ThrowExceptions(true);

		ElasticsearchClient client = new(_settings);
		IOptions<ElasticsearchOptions> options = Options.Create(new ElasticsearchOptions
		{
			Uri = UnreachableHost, DefaultIndex = TestIndexName
		});

		_sut = new SearchIndexService(client, options, new FakeTimeProvider(), logger);
	}

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
		_sut.Dispose();
		(_settings as IDisposable)?.Dispose();
	}

	[Fact]
	public async Task IndexDocumentAsync_ClientThrows_LogsWarningAndDoesNotPropagate()
	{
		Func<Task> act = () => _sut.IndexDocumentAsync(
			Guid.CreateVersion7(), "throwing.pdf", "content",
			TimeProvider.System.GetUtcNow(),
			TestContext.Current.CancellationToken);

		await act.Should().NotThrowAsync();

		_logCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Warning &&
			l.Message.Contains("Failed to index", StringComparison.OrdinalIgnoreCase));
	}
}
