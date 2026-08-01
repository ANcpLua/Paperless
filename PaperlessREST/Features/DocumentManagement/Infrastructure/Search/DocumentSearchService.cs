using Elastic.Transport;

namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Search;

public interface IDocumentSearchService
{
	Task<IReadOnlyCollection<DocumentSearchResult>> SearchAsync(
		string query,
		int limit,
		CancellationToken cancellationToken = default);

	Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
///     Elasticsearch search service implementation.
/// </summary>
public sealed class DocumentSearchService(
	ElasticsearchClient elastic,
	ILogger<DocumentSearchService> logger) : IDocumentSearchService
{
	public async Task<IReadOnlyCollection<DocumentSearchResult>> SearchAsync(
		string query,
		int limit,
		CancellationToken cancellationToken = default)
	{
		logger.LogInformation(
			"Searching documents with query length {QueryLength} and limit {Limit}",
			query.Length,
			limit);

		var response = await ExecuteElasticsearchAsync(
			token => elastic.SearchAsync<DocumentSearchResult>(
				s => s.Indices(elastic.ElasticsearchClientSettings.DefaultIndex)
					.Query(q => q.MultiMatch(mm => mm
						.Query(query)
						.Fields("*")
						.Type(TextQueryType.BestFields)
						.Fuzziness(new Fuzziness("AUTO"))
						.Operator(Operator.Or)
						.Lenient()))
					.Size(limit)
					.TrackScores(),
				token),
			cancellationToken);

		logger.LogInformation("Found {Count} results", response.Documents.Count);
		return response.Documents;
	}

	public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
	{
		DeleteRequest deleteRequest = new(elastic.ElasticsearchClientSettings.DefaultIndex, id.ToString());
		await ExecuteElasticsearchAsync(
			token => elastic.DeleteAsync(deleteRequest, token),
			cancellationToken);

		logger.LogInformation("Document {DocumentId} removed from search index", id);
	}

	private static async Task<T> ExecuteElasticsearchAsync<T>(
		Func<CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken)
	{
		try
		{
			return await operation(cancellationToken);
		}
		catch (TransportException exception) when (
			cancellationToken.IsCancellationRequested &&
			exception.InnerException is OperationCanceledException)
		{
			throw new OperationCanceledException(
				"Elasticsearch operation was canceled.",
				exception,
				cancellationToken);
		}
	}
}
