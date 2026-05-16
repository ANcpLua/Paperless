namespace PaperlessServices.Configuration;

public class MinioOptions
{
	public const string SectionName = "Storage:Minio";

	[Required(ErrorMessage = "MinIO endpoint is required")]
	public string Endpoint { get; set; } = null!;

	[Required(ErrorMessage = "MinIO access key is required")]
	public string AccessKey { get; set; } = null!;

	[Required(ErrorMessage = "MinIO secret key is required")]
	public string SecretKey { get; set; } = null!;

	[Required(ErrorMessage = "MinIO bucket name is required")]
	public string BucketName { get; set; } = null!;

	public bool UseSsl { get; set; }
}
