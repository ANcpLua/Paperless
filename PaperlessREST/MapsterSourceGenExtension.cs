using Mapster;
using Mapster.ExtensionMethods;

namespace PaperlessREST;

public class MapsterSourceGenExtension : IRegister
{
    [MapsterExtensionMethod]
    public void Register(TypeAdapterConfig config)
    {
        // Domain ↔︎ persistence
        config.NewConfig<DocumentEntity, Document>() 
            .MapToConstructor(true); 

        config.NewConfig<Document, DocumentEntity>();

        // Domain → DTOs
        config.NewConfig<Document, DocumentDto>()
            .Map(dest => dest.Status, src => src.Status.ToString());

        config.NewConfig<Document, CreateDocumentResponse>()
            .Map(dest => dest.Status, src => src.Status.ToString());
    }
}