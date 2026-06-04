namespace Paperless.TestSupport;

/// <summary>
///     MinIO endpoint/bucket helpers shared by the fixtures. Wraps the
///     <c>$"{Hostname}:{MappedPort}"</c> endpoint string and the one-shot
///     <c>MakeBucketAsync</c> block both fixtures duplicated.
/// </summary>
public static class MinioBucket
{
	private const int MinioPort = 9000;

	/// <summary>Host:port endpoint string for a started MinIO container.</summary>
	public static string Endpoint(MinioContainer minio) =>
		$"{minio.Hostname}:{minio.GetMappedPublicPort(MinioPort)}";

	/// <summary>Creates <paramref name="bucketName" /> against the started container.</summary>
	public static async Task CreateBucketAsync(MinioContainer minio, string bucketName)
	{
		using MinioClient client = new();
		client
			.WithEndpoint(Endpoint(minio))
			.WithCredentials(minio.GetAccessKey(), minio.GetSecretKey())
			.Build();
		await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
	}
}
