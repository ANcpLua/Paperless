using AutoMapper;
using PaperlessREST.DomainModel;
using PaperlessREST.Models;
using PostgreSQL.Persistence;

namespace PaperlessREST.Services;

public class GetDocumentService : IGetDocumentService
{
    private readonly IDocumentRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<GetDocumentService> _logger;

    public GetDocumentService(
        IDocumentRepository repository,
        IMapper mapper,
        ILogger<GetDocumentService> logger)
    {
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<DocumentDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var entityDocument = await _repository.GetByIdAsync(id, cancellationToken);
        if (entityDocument == null) return null;
            
        var document = _mapper.Map<Document>(entityDocument);
        return _mapper.Map<DocumentDto>(document);
    }

    public async Task<IEnumerable<DocumentDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entityDocuments = await _repository.GetAllAsync(cancellationToken);
        var documents = _mapper.Map<IEnumerable<Document>>(entityDocuments);
        return _mapper.Map<IEnumerable<DocumentDto>>(documents);
    }
}
