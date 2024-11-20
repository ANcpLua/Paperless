using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgreSQL.Data;
using PostgreSQL.Entities;

namespace PostgreSQL.Persistence;

public class DocumentRepository : IDocumentRepository
{
    private readonly PaperlessDbContext _context;

    public DocumentRepository(PaperlessDbContext context)
    {
        _context = context;
    }

    public async Task<Document?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<Document> Upload(Document document, CancellationToken cancellationToken)
    {
        try
        {
            await _context.Documents.AddAsync(document, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return document;
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyViolation(ex))
        {
            throw new InvalidOperationException("A document with this ID already exists.", ex);
        }
    }

    private bool IsDuplicateKeyViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && 
               pgEx.SqlState == "23505" && 
               pgEx.ConstraintName == "PK_Documents";
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var document = await GetByIdAsync(id, cancellationToken);
        if (document != null)
        {
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<Document>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Documents
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
    
    public async Task<Document> UpdateAsync(Document document, CancellationToken cancellationToken = default)
    {
        _context.Documents.Update(document);
        await _context.SaveChangesAsync(cancellationToken);

        return document;
    }
}