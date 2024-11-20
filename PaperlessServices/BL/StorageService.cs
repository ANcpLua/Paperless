using Minio;
using Minio.DataModel.Args;

namespace PaperlessServices.BL;

public class StorageService : IStorageService
{
    private readonly MinioClient _minioClient;
    private readonly ILogger<StorageService> _logger;
    private readonly string _bucketName;

    public StorageService(MinioClient minioClient, ILogger<StorageService> logger, IConfiguration configuration)
    {
        _minioClient = minioClient;
        _logger = logger;
        _bucketName = configuration["MinIO:BucketName"]
                      ?? throw new InvalidOperationException("BucketName is not set in configuration.");
    }

    public async Task<string> UploadFileAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        await EnsureBucketExistsAsync(cancellationToken);
        var putObjectArgs = new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(fileName)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(GetContentType(fileName));

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
        _logger.LogInformation("Successfully uploaded file: {FileName}", fileName);
        return fileName;
    }

    public async Task<Stream> GetFileAsync(string fileName, CancellationToken cancellationToken)
    {
        await EnsureBucketExistsAsync(cancellationToken);
        var memoryStream = new MemoryStream();
        await _minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream)),
            cancellationToken);

        memoryStream.Position = 0;
        return memoryStream;
    }

    public async Task DeleteFileAsync(string fileName, CancellationToken cancellationToken)
    {
        await EnsureBucketExistsAsync(cancellationToken);
        await _minioClient.RemoveObjectAsync(
            new RemoveObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName),
            cancellationToken);

        _logger.LogInformation("Successfully deleted file: {FileName}", fileName);
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        var exists = await _minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucketName),
            cancellationToken);

        if (!exists)
        {
            await _minioClient.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_bucketName),
                cancellationToken);
            _logger.LogInformation("Created new bucket: {BucketName}", _bucketName);
        }
    }

    private string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLower();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}
