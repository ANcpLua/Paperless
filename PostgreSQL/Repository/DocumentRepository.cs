using Contract.Logger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PostgreSQL.Data;
using PostgreSQL.Entities;

namespace PostgreSQL.Repository;

public class DocumentRepository : IDocumentRepository
{
    private readonly PaperlessDbContext _context;

    public DocumentRepository(PaperlessDbContext context)
    {
        _context = context;
    }

    [LogOperation("DocumentRepository", "GetByIdAsync")]
    public async Task<Document?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    [LogOperation("DocumentRepository", "GetAllAsync")]
    public async Task<IEnumerable<Document>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    [LogOperation("DocumentRepository", "Upload")]
    public async Task<Document> Upload(Document document, CancellationToken cancellationToken)
    {
        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);
        return document;
    }

    [LogOperation("DocumentRepository", "UpdateAsync")]
    public async Task<Document> UpdateAsync(Document document, CancellationToken cancellationToken = default)
    {
        _context.Documents.Update(document);
        await _context.SaveChangesAsync(cancellationToken);
        return document;
    }

    [LogOperation("DocumentRepository", "DeleteAsync")]
    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var document = await GetByIdAsync(id, cancellationToken);
        if (document != null)
        {
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    [LogOperation("DocumentRepository", "BeginTransactionAsync")]
    public async Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Database.BeginTransactionAsync(cancellationToken);
    }
}