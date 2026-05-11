namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence;

public interface IDocumentAccessRepository
{
	Task<Guid[]> GetExistingDocumentIdsAsync(Guid[] documentIds, CancellationToken ct);
	Task UpsertDailyAccessAsync(DateOnly date, (Guid DocumentId, long AccessCount)[] items, CancellationToken ct);
}
