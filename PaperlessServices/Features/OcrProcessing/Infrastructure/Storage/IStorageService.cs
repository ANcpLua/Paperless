namespace PaperlessServices.Features.OcrProcessing.Infrastructure.Storage;

public interface IStorageService
{
	Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default);
}
