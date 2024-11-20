namespace PaperlessServices.BL;

public interface IStorageService
{
    Task<string> UploadFileAsync(string fileName, Stream stream, CancellationToken cancellationToken);
    Task<Stream> GetFileAsync(string fileName, CancellationToken cancellationToken);
    Task DeleteFileAsync(string fileName, CancellationToken cancellationToken);
}