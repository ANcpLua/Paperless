using Contract.Logger;
using Minio;
using Minio.DataModel.Args;

namespace PaperlessServices.MinIoStorage;

public class MinioStorageService : IMinioStorageService
{
    private readonly string? _bucketName;
    private readonly MinioClient _minioClient;

    public MinioStorageService(MinioClient minioClient, IConfiguration configuration)
    {
        _minioClient = minioClient;
        _bucketName = configuration["MinIO:BucketName"];

        // Immediately ensure bucket exists at startup
        EnsureBucketExistsAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    [LogOperation("Upload", "MinioStorage")]
    public async Task<string> UploadFileAsync(string fileName, Stream stream, CancellationToken cancellationToken)
    {
        await EnsureBucketExistsAsync(cancellationToken);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(fileName)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType("application/octet-stream");

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
        return fileName;
    }

    [LogOperation("Download", "MinioStorage")]
    public async Task<Stream> GetFileAsync(string fileName, CancellationToken cancellationToken)
    {
        await _minioClient.StatObjectAsync(
            new StatObjectArgs().WithBucket(_bucketName).WithObject(fileName),
            cancellationToken
        );

        var memoryStream = new MemoryStream();
        await _minioClient.GetObjectAsync(
            new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithCallbackStream(stream => stream.CopyTo(memoryStream)),
            cancellationToken
        );

        memoryStream.Position = 0;
        return memoryStream;
    }

    [LogOperation("Delete", "MinioStorage")]
    public async Task DeleteFileAsync(string fileName, CancellationToken cancellationToken)
    {
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(fileName);

        await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
    }

    [LogOperation("EnsureBucket", "MinioStorage", LogLevel.Debug)]
    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        var exists = await _minioClient.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_bucketName),
            cancellationToken
        );

        if (!exists)
            await _minioClient.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_bucketName),
                cancellationToken
            );
    }
}