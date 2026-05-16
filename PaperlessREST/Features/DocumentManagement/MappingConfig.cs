namespace PaperlessREST.Features.DocumentManagement;

/// <summary>
///     Configures object-to-object mappings using Mapster.
/// </summary>
[UsedImplicitly]
public class MappingConfig : IRegister
{
	[Generate]
	public void Register(TypeAdapterConfig config)
	{
		// Database <-> Domain mappings
		config.NewConfig<DocumentEntity, Document>()
			.MapToConstructor(true);

		config.NewConfig<Document, DocumentEntity>();

		// Domain -> API DTO mappings (StoragePath is deliberately excluded from DTOs for security)
		config.NewConfig<Document, DocumentDto>()
			.Map(dest => dest.Status, src => src.Status.ToString());

		config.NewConfig<Document, CreateDocumentResponse>()
			.Map(dest => dest.Status, src => src.Status.ToString());

		// Search result domain -> DTO mapping (1:1 mapping, no transformations needed)
		config.NewConfig<DocumentSearchResult, DocumentSearchResultDto>();
	}
}
