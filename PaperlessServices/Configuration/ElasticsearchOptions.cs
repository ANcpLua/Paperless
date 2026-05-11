namespace PaperlessServices.Configuration;

public sealed class ElasticsearchOptions
{
	public const string SectionName = "Elasticsearch";

	[Required(ErrorMessage = "Elasticsearch:Uri is required")]
	[Url(ErrorMessage = "Elasticsearch:Uri must be a valid URL")]
	public required string Uri { get; init; }

	[Required(ErrorMessage = "Elasticsearch:DefaultIndex is required")]
	public required string DefaultIndex { get; init; }
}
