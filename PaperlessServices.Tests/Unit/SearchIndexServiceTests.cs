namespace PaperlessServices.Tests.Unit;

/// <summary>
///     Unit tests for SearchIndexService verifying resilience and error handling.
///     Since ElasticsearchClient is sealed, we test against an unreachable endpoint
///     to verify the service handles failures gracefully without throwing.
/// </summary>
/// <remarks>
///     Design rationale: The SearchIndexService is designed to be resilient -
///     search indexing should never block OCR processing. These tests verify
///     that contract is maintained even when Elasticsearch is unavailable.
///     Integration tests verify actual indexing works with a real ES instance.
/// </remarks>
public sealed class SearchIndexServiceTests : IDisposable
{
	// ═══════════════════════════════════════════════════════════════
	// CONSTANTS
	// ═══════════════════════════════════════════════════════════════

	private const string UnreachableHost = "http://127.0.0.1:1";
	private const string TestIndexName = "test-documents";
	private const string TestFileName = "document.pdf";
	private const string TestContent = "This is the extracted OCR content from the document.";
	private static readonly Guid s_testDocumentId = Guid.Parse("12345678-1234-1234-1234-123456789abc");

	// ═══════════════════════════════════════════════════════════════
	// CONSTRUCTION
	// ═══════════════════════════════════════════════════════════════

	private readonly FakeLogCollector _logCollector = new();
	private readonly ElasticsearchClientSettings _settings;
	private readonly SearchIndexService _sut;
	private readonly FakeTimeProvider _timeProvider = new();

	[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
		Justification = "_settings is stored as a field and disposed in Dispose() via the IDisposable cast; "
		                + "the analyzer can't follow disposal through the cast.")]
	public SearchIndexServiceTests()
	{
		FakeLogger<SearchIndexService> logger = new(_logCollector);

		// Configure client to fail fast - unreachable endpoint tests resilience
		_settings = new ElasticsearchClientSettings(new Uri(UnreachableHost))
			.DefaultIndex(TestIndexName)
			.DisableDirectStreaming()
			.RequestTimeout(TimeSpan.FromMilliseconds(100))
			.ThrowExceptions(false);
		ElasticsearchClient client = new(_settings);

		IOptions<ElasticsearchOptions> options = Options.Create(new ElasticsearchOptions
		{
			Uri = UnreachableHost, DefaultIndex = TestIndexName
		});

		_sut = new SearchIndexService(client, options, _timeProvider, logger);
	}

	// ═══════════════════════════════════════════════════════════════
	// DISPOSAL
	// ═══════════════════════════════════════════════════════════════

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
		(_settings as IDisposable)?.Dispose();
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: IndexDocumentAsync - Resilience (Elasticsearch Unavailable)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task IndexDocumentAsync_WhenElasticsearchUnavailable_DoesNotThrow()
	{
		// Arrange & Act
		Func<Task> act = () => _sut.IndexDocumentAsync(
			s_testDocumentId,
			TestFileName,
			TestContent,
			TimeProvider.System.GetUtcNow().AddMinutes(-5),
			TestContext.Current.CancellationToken);

		// Assert - Service is resilient to ES failures, never blocks OCR
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task IndexDocumentAsync_WhenElasticsearchUnavailable_LogsWarning()
	{
		// Act
		await _sut.IndexDocumentAsync(
			s_testDocumentId,
			TestFileName,
			TestContent,
			TimeProvider.System.GetUtcNow().AddMinutes(-5),
			TestContext.Current.CancellationToken);

		// Assert - Warning logged for observability
		IReadOnlyList<FakeLogRecord> logs = _logCollector.GetSnapshot();
		logs.Should().Contain(r => r.Level == LogLevel.Warning);
	}

	[Fact]
	public async Task IndexDocumentAsync_WhenElasticsearchUnavailable_LogsDocumentId()
	{
		// Act
		await _sut.IndexDocumentAsync(
			s_testDocumentId,
			TestFileName,
			TestContent,
			TimeProvider.System.GetUtcNow().AddMinutes(-5),
			TestContext.Current.CancellationToken);

		// Assert - Document ID in log for troubleshooting
		IReadOnlyList<FakeLogRecord> logs = _logCollector.GetSnapshot();
		logs.Select(r => r.Message)
			.Should().Contain(m => m.Contains(s_testDocumentId.ToString(), StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: IndexDocumentAsync - Null/Empty Parameters
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task IndexDocumentAsync_WithNullFileName_DoesNotThrow()
	{
		// Act
		Func<Task> act = () => _sut.IndexDocumentAsync(
			s_testDocumentId,
			null!,
			TestContent,
			TimeProvider.System.GetUtcNow().AddMinutes(-5),
			TestContext.Current.CancellationToken);

		// Assert - Service handles null gracefully
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task IndexDocumentAsync_WithEmptyContent_DoesNotThrow()
	{
		// Act - OCR might extract no text from some PDFs
		Func<Task> act = () => _sut.IndexDocumentAsync(
			s_testDocumentId,
			TestFileName,
			string.Empty,
			TimeProvider.System.GetUtcNow().AddMinutes(-5),
			TestContext.Current.CancellationToken);

		// Assert - Empty content is valid
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task IndexDocumentAsync_WithEmptyGuid_DoesNotThrow()
	{
		// Act
		Func<Task> act = () => _sut.IndexDocumentAsync(
			Guid.Empty,
			TestFileName,
			TestContent,
			TimeProvider.System.GetUtcNow().AddMinutes(-5),
			TestContext.Current.CancellationToken);

		// Assert - Invalid IDs shouldn't crash the service
		await act.Should().NotThrowAsync();
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: IndexDocumentAsync - Cancellation
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task IndexDocumentAsync_WithCancelledToken_DoesNotThrow()
	{
		// Arrange
		using CancellationTokenSource cts = new();
		await cts.CancelAsync();

		// Act
		Func<Task> act = () => _sut.IndexDocumentAsync(
			s_testDocumentId,
			TestFileName,
			TestContent,
			TimeProvider.System.GetUtcNow().AddMinutes(-5),
			cts.Token);

		// Assert - Cancelled operations handled gracefully
		await act.Should().NotThrowAsync();
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: IndexDocumentAsync - Various Content Types
	// ═══════════════════════════════════════════════════════════════

	public static IEnumerable<ITheoryDataRow> ContentTypes()
	{
		yield return new TheoryDataRow<string>("Simple text content")
			.WithTestDisplayName("Simple text");
		yield return new TheoryDataRow<string>("Content with special chars: <>&\"'")
			.WithTestDisplayName("Special characters");
		yield return new TheoryDataRow<string>("Multi\nline\ncontent")
			.WithTestDisplayName("Multi-line content");
		yield return new TheoryDataRow<string>("Unicode: éèê 中文")
			.WithTestDisplayName("Unicode content");
	}

	[Theory]
	[MemberData(nameof(ContentTypes))]
	public async Task IndexDocumentAsync_WithVariousContentTypes_DoesNotThrow(string content)
	{
		// Act
		Func<Task> act = () => _sut.IndexDocumentAsync(
			Guid.CreateVersion7(),
			TestFileName,
			content,
			TimeProvider.System.GetUtcNow().AddMinutes(-5),
			TestContext.Current.CancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task IndexDocumentAsync_WithVeryLongContent_DoesNotThrow()
	{
		// Arrange - Large OCR output (100KB)
		string longContent = new('x', 100_000);

		// Act
		Func<Task> act = () => _sut.IndexDocumentAsync(
			Guid.CreateVersion7(),
			TestFileName,
			longContent,
			TimeProvider.System.GetUtcNow().AddMinutes(-5),
			TestContext.Current.CancellationToken);

		// Assert
		await act.Should().NotThrowAsync();
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: IndexDocumentAsync - Concurrent Calls
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task IndexDocumentAsync_ConcurrentCalls_DoNotThrow()
	{
		// Arrange - Multiple documents indexed concurrently
		DateTimeOffset createdAt = TimeProvider.System.GetUtcNow().AddMinutes(-5);
		Task[] tasks =
		[
			_sut.IndexDocumentAsync(Guid.CreateVersion7(), "doc1.pdf", "Content 1", createdAt, CancellationToken.None),
			_sut.IndexDocumentAsync(Guid.CreateVersion7(), "doc2.pdf", "Content 2", createdAt, CancellationToken.None),
			_sut.IndexDocumentAsync(Guid.CreateVersion7(), "doc3.pdf", "Content 3", createdAt, CancellationToken.None)
		];

		// Act
		Func<Task> act = () => Task.WhenAll(tasks);

		// Assert
		await act.Should().NotThrowAsync();
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: LogIndexResult - Branch Coverage
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void LogIndexResult_WhenValid_LogsInformation()
	{
		// Act
		_sut.LogIndexResult(s_testDocumentId, true);

		// Assert
		IReadOnlyList<FakeLogRecord> logs = _logCollector.GetSnapshot();
		logs.Should().ContainSingle(r =>
			r.Level == LogLevel.Information &&
			r.Message.Contains(s_testDocumentId.ToString(), StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void LogIndexResult_WhenInvalid_LogsWarning()
	{
		// Act
		_sut.LogIndexResult(s_testDocumentId, false);

		// Assert
		IReadOnlyList<FakeLogRecord> logs = _logCollector.GetSnapshot();
		logs.Should().ContainSingle(r =>
			r.Level == LogLevel.Warning &&
			r.Message.Contains(s_testDocumentId.ToString(), StringComparison.OrdinalIgnoreCase));
	}
}
