namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Search;

public interface IDocumentSearchService
{
	IAsyncEnumerable<T> SearchAsync<T>(string query, int limit = 10, CancellationToken cancellationToken = default)
		where T : class;

	Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}


/// <summary>
///     Elasticsearch search service implementation.
/// </summary>
/// <remarks>
///     Excluded from coverage because async IAsyncEnumerable generates complex state machines with
///     unreachable branches for sync/async completion paths and iterator disposal. The actual search
///     logic (query building, result iteration) is tested via integration tests.
/// </remarks>
[ExcludeFromCodeCoverage(Justification =
	"Async IAsyncEnumerable - compiler-generated dual state machine (async + iterator) creates unreachable branches for sync/async completion and disposal paths")]
public sealed class DocumentSearchService(
	ElasticsearchClient elastic,
	ILogger<DocumentSearchService> logger) : IDocumentSearchService
{
	public async IAsyncEnumerable<T> SearchAsync<T>(string query, int limit = 10,
		[EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
	{
		logger.LogInformation("Searching for query: {Query} (limit: {Limit})", query, limit);

		string searchQuery = query.Length > SearchServiceConstraints.ServiceQueryMaxLength
			? query[..SearchServiceConstraints.ServiceQueryMaxLength]
			: query;

		SearchResponse<T> response = await elastic.SearchAsync<T>(
			s => s.Indices(elastic.ElasticsearchClientSettings.DefaultIndex)
				.Query(q => q.MultiMatch(mm => mm
					.Query(searchQuery)
					.Fields("*")
					.Type(TextQueryType.BestFields)
					.Fuzziness(new Fuzziness("AUTO"))
					.Operator(Operator.Or)
					.Lenient()))
				.Size(limit)
				.TrackScores(),
			cancellationToken);

		logger.LogInformation("Found {Count} results", response.Documents.Count);

		foreach (T doc in response.Documents)
		{
			yield return doc;
		}
	}

	public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		DeleteRequest deleteRequest = new(elastic.ElasticsearchClientSettings.DefaultIndex, id.ToString());
		DeleteResponse response = await elastic.DeleteAsync(deleteRequest, cancellationToken);

		logger.LogInformation("Document {DocumentId} removed from search index", id);
		return response.IsValidResponse;
	}
}
