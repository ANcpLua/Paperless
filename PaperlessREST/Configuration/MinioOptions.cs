namespace PaperlessREST.Configuration;

[ExcludeFromCodeCoverage(Justification = "Record - compiler-generated members; validated via integration tests")]
public sealed record MinioOptions
{
	public const string SectionName = "Storage:Minio";

	[Required(ErrorMessage = $"{SectionName}:Endpoint is required")]
	public required string Endpoint { get; init; }

	[Required(ErrorMessage = $"{SectionName}:AccessKey is required")]
	public required string AccessKey { get; init; }

	[Required(ErrorMessage = $"{SectionName}:SecretKey is required")]
	public required string SecretKey { get; init; }

	[Required(ErrorMessage = $"{SectionName}:BucketName is required")]
	public required string BucketName { get; init; }

	public bool UseSsl { get; init; }
}

public static class MinioOptionsExtensions
{
	extension(MinioOptions opts)
	{
		/// <summary>
		///     Parses the Endpoint string into a Uri, adding http(s):// scheme if missing.
		/// </summary>
		public Uri EndpointUri
		{
			get
			{
				string endpoint = opts.Endpoint.Contains("://", StringComparison.Ordinal)
					? opts.Endpoint
					: $"{(opts.UseSsl ? "https" : "http")}://{opts.Endpoint}";
				return new Uri(endpoint);
			}
		}
	}
}
