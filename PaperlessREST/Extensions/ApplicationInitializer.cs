using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace PaperlessREST.Extensions;

public static class ApplicationInitializer
{
    public static async Task InitializeApplicationAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();

        await InitialiseDatabaseAsync(scope.ServiceProvider, app.Logger);
        await InitialiseStorageAsync(scope.ServiceProvider, app.Logger);
    }

    // ------------------------------------------------- Database
    private static async Task InitialiseDatabaseAsync(IServiceProvider sp, ILogger logger)
    {
        var factory = sp.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        logger.LogInformation("Database migration completed ({Context})", nameof(DocumentPersistence));
    }

    // ------------------------------------------------- MinIO
    private static async Task InitialiseStorageAsync(IServiceProvider sp, ILogger logger)
    {
        var minio = sp.GetRequiredService<IMinioClient>();
        var options = sp.GetRequiredService<IOptions<MinioOptions>>().Value;

        var bucketExists = await minio.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(options.BucketName));

        if (bucketExists) return;

        await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(options.BucketName));
        logger.LogInformation("MinIO bucket '{Bucket}' created", options.BucketName);
    }
}