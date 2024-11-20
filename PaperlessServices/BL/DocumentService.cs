using AutoMapper;
using Contract;
using EasyNetQ;
using Elastic.Clients.Elasticsearch;
using PostgreSQL.Entities;
using PostgreSQL.Persistence;
using PaperlessServices.Entities;

namespace PaperlessServices.BL;

public class DocumentService : IDocumentService
{
    private readonly IStorageService _storageService;
    private readonly IDocumentRepository _repository;
    private readonly IMapper _mapper;
    private readonly ILogger<DocumentService> _logger;
    private readonly ElasticsearchClient _elasticClient;
    private readonly IBus _messageBus;

    public DocumentService(
        IStorageService storageService,
        IDocumentRepository repository,
        IMapper mapper,
        ILogger<DocumentService> logger,
        ElasticsearchClient elasticClient,
        IBus messageBus)
    {
        _storageService = storageService;
        _repository = repository;
        _mapper = mapper;
        _logger = logger;
        _elasticClient = elasticClient;
        _messageBus = messageBus;
    }

    public async Task<DocumentDto> Upload(DocumentDto documentDto, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting document upload for {DocumentName}", documentDto.Name);

        if (documentDto.File == null)
            throw new ArgumentException("File is required for upload");

        documentDto.Name = GenerateUniqueDocumentName(documentDto.File.FileName);

        var fileName = documentDto.Name;
        await using var stream = documentDto.File.OpenReadStream();
        await _storageService.UploadFileAsync(fileName, stream, cancellationToken);

        var blDocument = _mapper.Map<BlDocument>(documentDto);
        blDocument.FilePath = fileName;
        blDocument.DateUploaded = DateTime.UtcNow;

        var document = _mapper.Map<Document>(blDocument);
        var savedDocument = await _repository.Upload(document, cancellationToken);
        var mappedDocument = _mapper.Map<BlDocument>(savedDocument);
        var dto = _mapper.Map<DocumentDto>(mappedDocument);

        await IndexDocument(dto, cancellationToken);

        var documentUploadedEvent = new DocumentUploadedEvent
        {
            DocumentId = dto.Id,
            FileName = dto.FilePath,
            UploadedAt = dto.DateUploaded
        };

        await _messageBus.PubSub.PublishAsync(documentUploadedEvent, "document.uploaded", cancellationToken);
        _logger.LogInformation("Published DocumentUploadedEvent for DocumentId {DocumentId}", dto.Id);

        return dto;
    }

    private string GenerateUniqueDocumentName(string originalName)
    {
        var uniqueId = Guid.NewGuid().ToString();
        return $"{uniqueId}_{originalName}";
    }

    private async Task IndexDocument(DocumentDto document, CancellationToken cancellationToken)
    {
        var indexResponse = await _elasticClient.IndexAsync(
            document,
            request => request
                .Index("paperless-documents")
                .Id(document.Id.ToString())
                .Refresh(Refresh.True),
            cancellationToken);

        if (!indexResponse.IsValidResponse)
        {
            _logger.LogError("Failed to index document: {Error}", indexResponse.DebugInformation);
            throw new Exception($"Failed to index document: {indexResponse.DebugInformation}");
        }
    }

    public async Task<DocumentDto> UpdateDocument(DocumentDto documentDto, CancellationToken cancellationToken)
    {
        var document = await _repository.GetByIdAsync(documentDto.Id, cancellationToken);
        if (document == null)
            throw new KeyNotFoundException($"Document with ID {documentDto.Id} not found");

        document.Name = documentDto.Name;
        document.OcrText = documentDto.OcrText;
        var updatedDocument = await _repository.UpdateAsync(document, cancellationToken);
        var mappedDocument = _mapper.Map<BlDocument>(updatedDocument);
        var dto = _mapper.Map<DocumentDto>(mappedDocument);
        await IndexDocument(dto, cancellationToken);
        return dto;
    }

    public async Task<DocumentDto> GetDocument(int id, CancellationToken cancellationToken)
    {
        var document = await _repository.GetByIdAsync(id, cancellationToken);
        if (document == null)
            throw new KeyNotFoundException($"Document with ID {id} not found");

        var mappedDocument = _mapper.Map<BlDocument>(document);
        return _mapper.Map<DocumentDto>(mappedDocument);
    }

    public async Task<IEnumerable<DocumentDto>> GetAllDocuments(CancellationToken cancellationToken)
    {
        var documents = await _repository.GetAllAsync(cancellationToken);
        var mappedDocs = _mapper.Map<IEnumerable<BlDocument>>(documents);
        return _mapper.Map<IEnumerable<DocumentDto>>(mappedDocs);
    }

    public async Task DeleteDocument(int id, CancellationToken cancellationToken)
    {
        var documentEntity = await _repository.GetByIdAsync(id, cancellationToken);
        if (documentEntity == null)
            throw new KeyNotFoundException($"Document with ID {id} not found");

        await _repository.DeleteAsync(id, cancellationToken);

        var deleteResponse = await _elasticClient.DeleteAsync<DocumentDto>(
            id.ToString(),
            idx => idx.Index("paperless-documents"),
            cancellationToken);

        if (!deleteResponse.IsValidResponse)
        {
            _logger.LogError("Failed to delete document {DocumentId} from Elasticsearch: {Error}", id,
                deleteResponse.DebugInformation);
            throw new Exception(
                $"Failed to delete document {id} from Elasticsearch: {deleteResponse.DebugInformation}");
        }

        _logger.LogInformation("Document with ID {DocumentId} deleted successfully", id);
    }
}
