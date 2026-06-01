namespace PaperlessServices.Tests.Integration;

/// <summary>
///     Integration tests targeting the InitializeAsync concurrency branches in
///     <see cref="SearchIndexService" /> that the standard
///     <see cref="SearchIndexIntegrationTests" /> suite does not exercise.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="SearchIndexService" /> caches initialized index names in a static
///         <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}" />
///         shared across instances. The shared dictionary survives between tests, so
///         <see cref="SharedContainerFixture" />'s default index is already initialized by
///         the time these tests run. To prove the uncovered branches we mint a fresh
///         <c>$"test-{Guid.NewGuid():N}"</c> index per test, point a freshly-constructed
///         <see cref="SearchIndexService" /> at it, and assert against
///         <see cref="FakeLogger{T}" /> output rather than indirect ES state.
///     </para>
///     <para>
///         Each test instantiates its own <see cref="SearchIndexService" /> instead of
///         pulling <see cref="ISearchIndexService" /> from DI; the DI registration is a
///         singleton bound to the fixture's default index, which would route around our
///         unique-index isolation.
///     </para>
/// </remarks>
[Collection(SharedContainerCollection.Name)]
public class SearchIndexConcurrencyTests(SharedContainerFixture fixture)
{
	private ElasticsearchClient ElasticClient => fixture.Services.GetRequiredService<ElasticsearchClient>();

	private static SearchIndexService BuildSut(
		ElasticsearchClient client,
		string indexName,
		FakeLogger<SearchIndexService> logger) =>
		new(
			client,
			Options.Create(new ElasticsearchOptions
			{
				Uri = client.ElasticsearchClientSettings.NodePool.Nodes.First().Uri.ToString(),
				DefaultIndex = indexName
			}),
			TimeProvider.System,
			logger);

	private static int CountCreateIndexLogs(FakeLogCollector logs, string indexName) =>
		logs.GetSnapshot()
			.Count(r =>
				r.Level == LogLevel.Information &&
				r.Message.Contains("Created Elasticsearch index", StringComparison.OrdinalIgnoreCase) &&
				r.Message.Contains(indexName, StringComparison.OrdinalIgnoreCase));

	// ═══════════════════════════════════════════════════════════════
	// Branch (b): double-check inside lock — two concurrent first-time callers
	// race the outer ContainsKey, the semaphore serializes them, the second
	// caller hits the inner ContainsKey and short-circuits.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InitializeAsync_ConcurrentCallers_CreateIndexExactlyOnce()
	{
		// Arrange — unique index ensures the static cache has no entry, so both
		// callers pass the outer ContainsKey check and race for the semaphore.
		var indexName = $"test-{Guid.NewGuid():N}";
		FakeLogCollector collector = new();
		FakeLogger<SearchIndexService> logger = new(collector);
		using SearchIndexService sut = BuildSut(ElasticClient, indexName, logger);

		Guid firstId = Guid.NewGuid();
		Guid secondId = Guid.NewGuid();

		// Act — launch two indexing calls in parallel; both call InitializeAsync.
		Task[] tasks =
		[
			sut.IndexDocumentAsync(firstId, "doc1.pdf", "First", TimeProvider.System.GetUtcNow(),
				TestContext.Current.CancellationToken),
			sut.IndexDocumentAsync(secondId, "doc2.pdf", "Second", TimeProvider.System.GetUtcNow(),
				TestContext.Current.CancellationToken)
		];
		await Task.WhenAll(tasks);

		// Assert — the create-log line fires exactly once. If branch (b) had not
		// taken, both callers would have entered the create path and the count
		// would be 2.
		CountCreateIndexLogs(collector, indexName).Should().Be(1,
			"the inner double-check should short-circuit the second concurrent caller after the first has created the index");
	}

	// ═══════════════════════════════════════════════════════════════
	// Branch (c): index already exists in Elasticsearch — pre-create the index,
	// then have InitializeAsync find Exists == true and short-circuit without
	// emitting the create-log line.
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task InitializeAsync_WhenIndexPreCreated_SkipsCreateAndLogsNothing()
	{
		// Arrange — pre-create the index out-of-band, before any SearchIndexService touches it.
		var indexName = $"test-{Guid.NewGuid():N}";
		Elastic.Clients.Elasticsearch.IndexManagement.CreateIndexResponse preCreate = await ElasticClient.Indices
			.CreateAsync(indexName, TestContext.Current.CancellationToken);
		preCreate.IsValidResponse.Should().BeTrue("pre-creating the index out-of-band is a precondition for this test");

		FakeLogCollector collector = new();
		FakeLogger<SearchIndexService> logger = new(collector);
		using SearchIndexService sut = BuildSut(ElasticClient, indexName, logger);

		// Act — first call into the freshly-constructed service triggers InitializeAsync.
		Guid documentId = Guid.NewGuid();
		await sut.IndexDocumentAsync(documentId, "exists.pdf", "Already there",
			TimeProvider.System.GetUtcNow(), TestContext.Current.CancellationToken);

		// Assert — no create-log line was emitted; the exists-branch was taken.
		CountCreateIndexLogs(collector, indexName).Should().Be(0,
			"InitializeAsync must skip the create path when ExistsAsync returns true");

		// Document was still indexed successfully into the SUT's pre-created index.
		// We query that index directly — fixture.WaitForDocumentAsync uses the client's
		// DefaultIndex, which is the shared fixture index, not this test's per-test index.
		GetResponse<JsonElement> response = await ElasticClient.GetAsync<JsonElement>(
			documentId.ToString(),
			g => g.Index(indexName),
			TestContext.Current.CancellationToken);
		response.Found.Should().BeTrue("document should be indexed in the pre-created index");
	}

	// ═══════════════════════════════════════════════════════════════
	// IndexDocumentAsync — explicit createdAt = null branch (no fallback).
	// Production comment line 49: "null if not provided - don't fake it".
	// Asserts the value-not-faked contract by verifying processedAt is the only
	// timestamp persisted; the createdAt key is omitted from _source by the
	// Elasticsearch client's JSON serializer (it drops nulls by default).
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task IndexDocumentAsync_WithNullCreatedAt_DoesNotFakeTimestamp()
	{
		// Arrange
		Guid documentId = Guid.NewGuid();
		ISearchIndexService searchIndex = fixture.Services.GetRequiredService<ISearchIndexService>();

		// Act — createdAt is deliberately null; production must not substitute a fallback.
		await searchIndex.IndexDocumentAsync(documentId, "no-date.pdf", "no date provided",
			createdAt: null, TestContext.Current.CancellationToken);

		// Assert — document is indexed, processedAt is set, createdAt is absent.
		GetResponse<JsonElement> response = await fixture.WaitForDocumentAsync<JsonElement>(
			documentId.ToString(), TestContext.Current.CancellationToken);

		response.Found.Should().BeTrue();
		response.Source.TryGetProperty("processedAt", out _).Should().BeTrue(
			"processedAt is the actual indexing time and must always be set");
		response.Source.TryGetProperty("createdAt", out _).Should().BeFalse(
			"a null createdAt must not be substituted with a fake timestamp; the field is dropped by the JSON serializer");
	}
}
