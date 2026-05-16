namespace PaperlessREST.Features.DocumentManagement.Infrastructure.Storage;

public interface IDocumentStorageService
{
	Task UploadAsync(Stream stream, string storagePath, long length, CancellationToken cancellationToken = default);
	Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}

public sealed class DocumentStorageService(
	IMinioClient minio,
	IOptions<MinioOptions> options,
	ILogger<DocumentStorageService> logger) : IDocumentStorageService
{
	private readonly MinioOptions _options = options.Value;

	public async Task UploadAsync(Stream stream, string storagePath, long length,
		CancellationToken cancellationToken = default)
	{
		await minio.PutObjectAsync(
			new PutObjectArgs()
				.WithBucket(_options.BucketName)
				.WithObject(storagePath)
				.WithStreamData(stream)
				.WithObjectSize(length),
			cancellationToken);

		logger.LogInformation("Document uploaded to storage at {StoragePath}", storagePath);
	}

	public async Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
	{
		try
		{
			await minio.RemoveObjectAsync(
				new RemoveObjectArgs()
					.WithBucket(_options.BucketName)
					.WithObject(storagePath),
				cancellationToken);

			logger.LogInformation("Document removed from storage at {StoragePath}", storagePath);
			return true;
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Failed to remove document from storage at {StoragePath}", storagePath);
			return false;
		}
	}
}
