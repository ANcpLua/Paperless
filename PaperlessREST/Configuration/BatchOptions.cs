namespace PaperlessREST.Configuration;

[ExcludeFromCodeCoverage(Justification = "Record - compiler-generated members; validated via integration tests")]
public sealed record BatchOptions
{
	public const string SectionName = "Batch";
	public const string JobId = "xml-daily-access-import";

	[Required(ErrorMessage = $"{SectionName}:InputPath is required")]
	public required string InputPath { get; init; }

	[Required(ErrorMessage = $"{SectionName}:ArchivePath is required")]
	public required string ArchivePath { get; init; }

	[Required(ErrorMessage = $"{SectionName}:ErrorPath is required")]
	public required string ErrorPath { get; init; }

	[Required(ErrorMessage = $"{SectionName}:FilePattern is required")]
	public required string FilePattern { get; init; }

	[Required(ErrorMessage = $"{SectionName}:CronExpression is required")]
	public required string CronExpression { get; init; }

	[Required(ErrorMessage = $"{SectionName}:TimeZoneId is required")]
	public required string TimeZoneId { get; init; }
}

public static class BatchOptionsExtensions
{
	extension(BatchOptions opts)
	{
		public TimeZoneInfo TimeZone =>
			TimeZoneInfo.FindSystemTimeZoneById(opts.TimeZoneId);

		public bool IsValidTimeZone =>
			TimeZoneInfo.TryFindSystemTimeZoneById(opts.TimeZoneId, out _);

		public bool HasDistinctPaths =>
			new HashSet<string>(PathNormalization.PlatformComparer)
			{
				PathNormalization.Normalize(opts.InputPath),
				PathNormalization.Normalize(opts.ArchivePath),
				PathNormalization.Normalize(opts.ErrorPath)
			}.Count == 3;
	}
}
