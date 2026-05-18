using System.Collections.Concurrent;

namespace PaperlessServices.Features.OcrProcessing.Infrastructure.Search;

/// <summary>
///     Service responsible for indexing documents in Elasticsearch for full-text search.
/// </summary>
/// <remarks>
///     This service indexes OCR-processed documents to enable search functionality in PaperlessREST.
///     It writes to Elasticsearch but never reads - search queries are handled by PaperlessREST.
///     Storage paths are deliberately excluded from indexing for security reasons.
/// </remarks>
public class SearchIndexService(
	ElasticsearchClient elastic,
	IOptions<ElasticsearchOptions> options,
	TimeProvider timeProvider,
	ILogger<SearchIndexService> logger)
	: ISearchIndexService, IDisposable
{
	// Instance-scoped (was static): DI registers this service as Singleton, so per-host
	// behavior is identical. Instance fields prevent state leakage when tests construct
	// the service directly with `new SearchIndexService(...)`. The DI container disposes
	// the singleton on host shutdown, which calls Dispose() below to release _initLock.
	private readonly SemaphoreSlim _initLock = new(1, 1);
	private readonly ConcurrentDictionary<string, bool> _initializedIndices = new();

	public void Dispose()
	{
		_initLock.Dispose();
		GC.SuppressFinalize(this);
	}

	/// <summary>
	///     Indexes a document in Elasticsearch after OCR processing completes.
	/// </summary>
	/// <param name="id">Document identifier.</param>
	/// <param name="fileName">Original filename of the PDF.</param>
	/// <param name="content">OCR-extracted text content.</param>
	/// <param name="createdAt">Original document upload timestamp (null falls back to processing time).</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <remarks>
	///     Indexes the document with status="Completed".
	///     Summary field is indexed as null and will be updated later by GenAI service.
	/// </remarks>
	public async Task IndexDocumentAsync(Guid id, string fileName, string content, DateTimeOffset? createdAt,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await InitializeAsync(cancellationToken);

			DateTimeOffset now = timeProvider.GetUtcNow();
			IndexResponse response = await elastic.IndexAsync(new
			{
				id,
				fileName,
				content,
				status = "Completed",
				createdAt, // null if not provided - don't fake it
				processedAt = now
				// storagePath deliberately excluded - internal detail not exposed in search results
				// summary will be added later by GenAI service
			}, i => i.Index(options.Value.DefaultIndex).Id(id.ToString()).Refresh(Refresh.True), cancellationToken);

			LogIndexResult(id, response.IsValidResponse);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex,
				"Failed to index document {DocumentId} in Elasticsearch. Proceeding without search indexing.", id);
		}
	}

	internal void LogIndexResult(Guid id, bool isValid)
	{
		if (!isValid)
		{
			logger.LogWarning("Elasticsearch indexing reported invalid response for {DocumentId}", id);
			return;
		}

		logger.LogInformation("Indexed document {DocumentId} in search index", id);
	}

	private async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		string indexName = options.Value.DefaultIndex;

		// Fast path: index already initialized
		if (_initializedIndices.ContainsKey(indexName))
		{
			return;
		}

		await _initLock.WaitAsync(cancellationToken);
		try
		{
			// Double-check after acquiring lock
			if (_initializedIndices.ContainsKey(indexName))
			{
				return;
			}

			ExistsResponse existsResponse = await elastic.Indices.ExistsAsync(indexName, cancellationToken);
			if (existsResponse.Exists)
			{
				_initializedIndices.TryAdd(indexName, true);
				return;
			}

			CreateIndexResponse createResponse = await elastic.Indices.CreateAsync(indexName,
				c => c.Mappings(m => m.Properties<object>(p =>
					p.Keyword("id")
						.Text("fileName")
						.Text("content")
						.Keyword("status")
						.Date("createdAt")
						.Date("processedAt")
						.Text("summary"))), cancellationToken);

			if (createResponse.IsValidResponse)
			{
				logger.LogInformation("Created Elasticsearch index: {IndexName}", indexName);
			}

			_initializedIndices.TryAdd(indexName, true);
		}
		finally
		{
			_initLock.Release();
		}
	}
}
