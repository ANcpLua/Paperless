namespace PaperlessServices.Features.OcrProcessing.Infrastructure.Search;

public interface ISearchIndexService
{
	Task IndexDocumentAsync(Guid id, string fileName, string content, DateTimeOffset? createdAt,
		CancellationToken cancellationToken = default);
}
