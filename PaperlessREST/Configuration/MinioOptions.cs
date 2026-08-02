namespace PaperlessREST.Configuration;

[ExcludeFromCodeCoverage(Justification = "Record - compiler-generated members; validated via integration tests")]
public sealed record MinioOptions
{
	public const string SectionName = "Storage:Minio";

	[Required(ErrorMessage = $"{SectionName}:Endpoint is required")]
	public required Uri Endpoint { get; init; }

	[Required(ErrorMessage = $"{SectionName}:AccessKey is required")]
	public required string AccessKey { get; init; }

	[Required(ErrorMessage = $"{SectionName}:SecretKey is required")]
	public required string SecretKey { get; init; }

	[Required(ErrorMessage = $"{SectionName}:BucketName is required")]
	public required string BucketName { get; init; }

}
