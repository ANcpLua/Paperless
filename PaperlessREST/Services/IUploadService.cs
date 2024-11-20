using PaperlessREST.Models;

namespace PaperlessREST.Services;

public interface IUploadService
{
    Task<DocumentDto> Upload(DocumentUploadDto document, CancellationToken cancellationToken = default);
}
