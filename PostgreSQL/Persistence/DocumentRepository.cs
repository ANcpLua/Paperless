using Microsoft.EntityFrameworkCore;
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
                             .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Document>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Documents
                             .ToListAsync(cancellationToken);
    }

    public async Task<Document> Upload(Document document, CancellationToken cancellationToken = default)
    {
        _context.Documents.Add(document);
        await _context.SaveChangesAsync(cancellationToken);
        return document;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var document = await GetByIdAsync(id, cancellationToken);
        if (document != null)
        {
            _context.Documents.Remove(document);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
    
