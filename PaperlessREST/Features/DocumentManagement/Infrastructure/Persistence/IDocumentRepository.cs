namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence;

public interface IDocumentRepository
{
	/// <summary>
	///     Gets documents with cursor-based pagination using GUIDv7 ordering.
	/// </summary>
	/// <param name="pageSize">Number of documents to return.</param>
	/// <param name="cursor">Last document ID from previous page (null for first page).</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Tuple of documents and whether more exist.</returns>
	Task<(List<Document> Items, bool HasMore)> GetDocumentsPagedAsync(
		int pageSize,
		Guid? cursor = null,
		CancellationToken ct = default);

	ValueTask<Document?> GetByIdAsync(Guid id, CancellationToken ct = default);
	Task<Document> AddAsync(Document document, CancellationToken ct = default);
	Task<bool> UpdateAsync(Document document, CancellationToken ct = default);
	Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
	Task<bool> UpdateSummaryAsync(Guid id, string summary, DateTimeOffset generatedAt, CancellationToken ct = default);
}
