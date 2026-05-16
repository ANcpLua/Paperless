namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Persistence;

public sealed class DocumentRepository(
	IDbContextFactory<DocumentPersistence> dbf,
	ILogger<DocumentRepository> log) : IDocumentRepository
{
	/// <inheritdoc />
	public async Task<(List<Document> Items, bool HasMore)> GetDocumentsPagedAsync(
		int pageSize,
		Guid? cursor = null,
		CancellationToken ct = default)
	{
		await using DocumentPersistence db = await dbf.CreateDbContextAsync(ct);

		// GUIDv7 is time-ordered, so we can use it directly for cursor-based pagination
		// Fetch one extra to determine if more pages exist
		IQueryable<DocumentEntity> query = db.Documents
			.OrderByDescending(d => d.Id);

		if (cursor.HasValue)
		{
			query = query.Where(d => d.Id.CompareTo(cursor.Value) < 0);
		}

		List<DocumentEntity> entities = await query
			.Take(pageSize + 1)
			.ToListAsync(ct);

		bool hasMore = entities.Count > pageSize;
		if (hasMore)
		{
			entities.RemoveAt(entities.Count - 1);
		}

		return (entities.ConvertAll(e => e.ToDocument()), hasMore);
	}

	public async ValueTask<Document?> GetByIdAsync(Guid id, CancellationToken ct = default)
	{
		await using DocumentPersistence db = await dbf.CreateDbContextAsync(ct);
		DocumentEntity? entity = await db.Documents.FindAsync([id], ct);
		return entity?.ToDocument();
	}

	public async Task<Document> AddAsync(Document document, CancellationToken ct = default)
	{
		await using DocumentPersistence db = await dbf.CreateDbContextAsync(ct);
		DocumentEntity entity = document.ToDocumentEntity();
		db.Documents.Add(entity);
		await db.SaveChangesAsync(ct);

		log.LogInformation("Document {Id} persisted", entity.Id);
		return entity.ToDocument();
	}

	public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
	{
		await using DocumentPersistence db = await dbf.CreateDbContextAsync(ct);

		// Single DELETE statement - no entity loading required
		int rowsAffected = await db.Documents
			.Where(d => d.Id == id)
			.ExecuteDeleteAsync(ct);

		if (rowsAffected > 0)
		{
			log.LogInformation("Document {Id} deleted", id);
		}

		return rowsAffected > 0;
	}

	public async Task<bool> UpdateSummaryAsync(Guid id, string summary, DateTimeOffset generatedAt,
		CancellationToken ct = default)
	{
		await using DocumentPersistence db = await dbf.CreateDbContextAsync(ct);

		// Single UPDATE statement - only updates summary fields, avoids race with OCR updates
		int rowsAffected = await db.Documents
			.Where(d => d.Id == id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(d => d.Summary, summary)
				.SetProperty(d => d.SummaryGeneratedAt, generatedAt), ct);

		if (rowsAffected > 0)
		{
			log.LogInformation("Document {Id} summary updated ({Length} chars)", id, summary.Length);
		}

		return rowsAffected > 0;
	}

	public async Task<bool> UpdateAsync(Document document, CancellationToken ct = default)
	{
		await using DocumentPersistence db = await dbf.CreateDbContextAsync(ct);

		// Single UPDATE statement - no entity loading required
		int rowsAffected = await db.Documents
			.Where(d => d.Id == document.Id)
			.ExecuteUpdateAsync(s => s
				.SetProperty(d => d.FileName, document.FileName)
				.SetProperty(d => d.Status, document.Status)
				.SetProperty(d => d.Content, document.Content)
				.SetProperty(d => d.Summary, document.Summary)
				.SetProperty(d => d.ProcessedAt, document.ProcessedAt)
				.SetProperty(d => d.SummaryGeneratedAt, document.SummaryGeneratedAt), ct);

		if (rowsAffected > 0)
		{
			log.LogInformation("Document {Id} updated: Status={Status}", document.Id, document.Status);
		}

		return rowsAffected > 0;
	}
}
