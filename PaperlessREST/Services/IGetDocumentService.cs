using PaperlessREST.Models;

namespace PaperlessREST.Services;

public interface IGetDocumentService
{
    Task<DocumentDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<DocumentDto>> GetAllAsync(CancellationToken cancellationToken = default);
}
