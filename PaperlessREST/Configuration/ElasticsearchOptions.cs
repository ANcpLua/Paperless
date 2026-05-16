namespace PaperlessREST.Configuration;

[ExcludeFromCodeCoverage(Justification = "Record - compiler-generated members; validated via integration tests")]
public sealed record ElasticsearchOptions
{
	public const string SectionName = "Elasticsearch";

	[Required(ErrorMessage = $"{SectionName}:Uri is required")]
	public required Uri Uri { get; init; }

	[Required(ErrorMessage = $"{SectionName}:DefaultIndex is required")]
	public required string DefaultIndex { get; init; }
}
