namespace PaperlessServices.Features.OcrProcessing.Infrastructure.Storage;

public class StorageService(
	IMinioClient minio,
	IOptions<MinioOptions> options,
	ILogger<StorageService> logger
) : IStorageService
{
	public async Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default)
	{
		MemoryStream stream = new();

		await minio.GetObjectAsync(
			new GetObjectArgs()
				.WithBucket(options.Value.BucketName)
				.WithObject(filePath)
				.WithCallbackStream(async (s, ct) => await s.CopyToAsync(stream, ct)),
			cancellationToken
		);

		stream.Position = 0;
		logger.LogInformation("Downloaded file from storage: {FilePath}", filePath);
		return stream;
	}
}
