namespace PaperlessREST.Services;

public interface IDeleteDocumentService
{
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}
