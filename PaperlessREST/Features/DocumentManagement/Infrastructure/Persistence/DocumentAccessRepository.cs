namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence;

public sealed class DocumentAccessRepository(IDbContextFactory<DocumentPersistence> factory)
	: IDocumentAccessRepository
{
	public async Task<Guid[]> GetExistingDocumentIdsAsync(Guid[] documentIds, CancellationToken ct)
	{
		await using DocumentPersistence db = await factory.CreateDbContextAsync(ct);
		return await db.Documents
			.Where(d => documentIds.AsEnumerable().Contains(d.Id))
			.Select(d => d.Id)
			.ToArrayAsync(ct);
	}

	public async Task UpsertDailyAccessAsync(
		DateOnly date,
		(Guid DocumentId, long AccessCount)[] items,
		CancellationToken ct)
	{
		if (items.Length is 0)
		{
			return;
		}

		await using DocumentPersistence db = await factory.CreateDbContextAsync(ct);

		// Use raw SQL upsert instead of BulkExtensions to handle snake_case column naming correctly
		foreach ((Guid documentId, long accessCount) in items)
		{
			await db.Database.ExecuteSqlAsync($"""
			                                   INSERT INTO daily_document_access (id, document_id, log_date, access_count, updated_at)
			                                   VALUES (gen_random_uuid(), {documentId}, {date}, {accessCount}, CURRENT_TIMESTAMP)
			                                   ON CONFLICT (document_id, log_date)
			                                   DO UPDATE SET access_count = daily_document_access.access_count + {accessCount},
			                                                 updated_at = CURRENT_TIMESTAMP
			                                   """, ct);
		}
	}
}
