using System.Collections.Immutable;
using AutoMapper;
using Contract;
using Contract.Logger;
using EasyNetQ;
using Elastic.Clients.Elasticsearch;
using FluentValidation;
using FluentValidation.Results;
using Npgsql;
using PaperlessServices.Entities;
using PaperlessServices.MinIoStorage;
using PostgreSQL.Entities;
using PostgreSQL.Repository;

namespace PaperlessServices.BL;

public class DocumentService : IDocumentService
{
    private readonly IConfiguration _configuration;
    private readonly ElasticsearchClient _elasticClient;
    private readonly IOperationLogger _logger;
    private readonly IMapper _mapper;
    private readonly IBus _messageBus;
    private readonly IMinioStorageService _minioStorageService;
    private readonly IDocumentRepository _repository;
    private readonly IValidator<BlDocument> _validator;

    public DocumentService(
        IMinioStorageService minioStorageService,
        IDocumentRepository repository,
        IMapper mapper,
        ElasticsearchClient elasticClient,
        IBus messageBus,
        IConfiguration configuration,
        IValidator<BlDocument> validator,
        IOperationLogger logger)
    {
        _minioStorageService = minioStorageService;
        _repository = repository;
        _mapper = mapper;
        _elasticClient = elasticClient;
        _messageBus = messageBus;
        _configuration = configuration;
        _validator = validator;
        _logger = logger;
    }

    [LogOperation("Upload", "DocumentService")]
    public async Task<DocumentDto?> Upload(DocumentDto documentDto, CancellationToken cancellationToken)
    {
        var operation = new LogOperationAttribute("Upload", "DocumentService");
        var transaction = await _repository.BeginTransactionAsync(cancellationToken);

        if (documentDto.File == null)
        {
            await _logger.LogOperationError(
                operation,
                nameof(Upload),
                new InvalidOperationException("No file provided")
            );
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var document = new Document
        {
            Name = documentDto.File.FileName,
            FilePath = "temp",
            DateUploaded = DateTime.UtcNow
        };

        var blDocument = _mapper.Map<BlDocument>(document);
        var validationResult = await _validator.ValidateAsync(blDocument, cancellationToken);
        if (!validationResult.IsValid)
        {
            await LogValidationErrors(operation, "Upload", validationResult);
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        // 1) Save entity in DB
        var savedDocument = await _repository.Upload(document, cancellationToken);
        await _logger.LogOperation(
            operation,
            "Database",
            [$"Document saved with ID: {savedDocument.Id}"]
        );

        // 2) Upload the file to MinIO
        var fileName = GenerateDocumentName(documentDto.File.FileName, savedDocument.Id);
        await using var stream = documentDto.File.OpenReadStream();
        var uploadResult = await _minioStorageService.UploadFileAsync(fileName, stream, cancellationToken);

        // If MinIO upload fails
        if (string.IsNullOrEmpty(uploadResult))
        {
            await _logger.LogOperationError(
                operation,
                nameof(Upload),
                new InvalidOperationException("MinIO upload failed (empty file path returned)")
            );
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        await _logger.LogOperation(
            operation,
            "MinIO",
            [$"File uploaded: {fileName}"]
        );

        // 3) Update DB record with the real file path
        savedDocument.FilePath = fileName;
        savedDocument.DateUploaded = DateTime.UtcNow;
        blDocument = _mapper.Map<BlDocument>(savedDocument);
        validationResult = await _validator.ValidateAsync(blDocument, cancellationToken);
        if (!validationResult.IsValid)
        {
            await LogValidationErrors(operation, "Upload", validationResult);
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        // 4) Update DB entity with final data
        var updatedDoc = await _repository.UpdateAsync(savedDocument, cancellationToken);
        await _logger.LogOperation(
            operation,
            "Database",
            [$"Document metadata updated: {updatedDoc.Id}"]
        );

        // 5) Index the document in Elasticsearch
        var dto = _mapper.Map<DocumentDto>(updatedDoc);
        var indexOk = await IndexDocument(dto, cancellationToken);
        if (!indexOk)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        // 6) Commit the database transaction
        await transaction.CommitAsync(cancellationToken);

        // 7) Publish an event to the message bus
        var evt = new DocumentUploadedEvent
        {
            DocumentId = dto.Id,
            FileName = dto.FilePath,
            UploadedAt = dto.DateUploaded
        };

        await _messageBus.PubSub.PublishAsync(evt, "document.uploaded", cancellationToken);
        await _logger.LogOperation(
            operation,
            "EventBus",
            [$"Published upload event for doc {dto.Id}"]
        );

        return dto;
    }

    [LogOperation("Update", "DocumentService")]
    public async Task<DocumentDto?> UpdateDocument(DocumentDto? documentDto, CancellationToken cancellationToken)
    {
        var operation = new LogOperationAttribute("Update", "DocumentService");

        if (documentDto == null)
        {
            await _logger.LogOperationError(
                operation,
                nameof(UpdateDocument),
                new InvalidOperationException("No document provided")
            );
            return null;
        }

        // 1) Log the start
        await _logger.LogOperation(
            operation,
            "Start",
            [$"Updating doc {documentDto.Id}"]
        );

        // 2) Retrieve existing record
        var docEntity = await _repository.GetByIdAsync(documentDto.Id, cancellationToken);
        if (docEntity == null)
        {
            await _logger.LogOperationError(
                operation,
                nameof(UpdateDocument),
                new KeyNotFoundException($"Document {documentDto.Id} not found")
            );
            return null;
        }

        // 3) Update fields and validate
        docEntity.Name = documentDto.Name;
        docEntity.OcrText = documentDto.OcrText;
        var blDocument = _mapper.Map<BlDocument>(docEntity);
        var validationResult = await _validator.ValidateAsync(blDocument, cancellationToken);
        if (!validationResult.IsValid)
        {
            await LogValidationErrors(operation, "Update", validationResult);
            return null;
        }

        // 4) Update record in DB
        var updatedDoc = await _repository.UpdateAsync(docEntity, cancellationToken);
        await _logger.LogOperation(
            operation,
            "Database",
            [$"Doc {updatedDoc.Id} updated in DB"]
        );

        // 5) Re-index in Elasticsearch
        var dto = _mapper.Map<DocumentDto>(updatedDoc);
        var successIndex = await IndexDocument(dto, cancellationToken);
        if (!successIndex)
        {
            await _logger.LogOperationError(
                operation,
                nameof(UpdateDocument),
                new InvalidOperationException($"Failed to re-index doc {dto.Id}")
            );
            return null;
        }

        // 6) Finish
        await _logger.LogOperation(
            operation,
            "Finish",
            [$"Doc {dto.Id} successfully updated"]
        );

        return dto;
    }

    [LogOperation("Get", "DocumentService")]
    public async Task<DocumentDto> GetDocument(int id, CancellationToken cancellationToken)
    {
        var operation = new LogOperationAttribute("Get", "DocumentService");

        // Check DB info
        var csb = new NpgsqlConnectionStringBuilder(
            _configuration.GetConnectionString("DefaultConnection")
        );
        await _logger.LogOperation(
            operation,
            "Database",
            [$"Using Host={csb.Host}, DB={csb.Database}"]
        );

        // Retrieve doc
        var doc = await _repository.GetByIdAsync(id, cancellationToken);
        if (doc == null)
            throw new KeyNotFoundException($"Document {id} not found");

        // Map to DTO
        var mapped = _mapper.Map<DocumentDto>(doc);
        await _logger.LogOperation(
            operation,
            "Success",
            [$"Retrieved doc {mapped.Id}"]
        );

        return mapped;
    }

    [LogOperation("GetAll", "DocumentService")]
    public async Task<IEnumerable<DocumentDto>> GetAllDocuments(CancellationToken cancellationToken)
    {
        var operation = new LogOperationAttribute("GetAll", "DocumentService");
        await _logger.LogOperation(
            operation,
            "Start",
            ["Retrieving all docs"]
        );

        // Fetch from DB
        var docs = await _repository.GetAllAsync(cancellationToken);
        var mappedBlDocs = _mapper.Map<List<BlDocument>>(docs);

        // Validate each entity
        foreach (var blDoc in mappedBlDocs)
        {
            var validationResult = await _validator.ValidateAsync(blDoc, cancellationToken);
            if (!validationResult.IsValid)
            {
                await LogValidationErrors(operation, "GetAll", validationResult, blDoc.Id);
                return ImmutableList<DocumentDto>.Empty;
            }
        }

        // Map to DTO
        var finalDtos = mappedBlDocs
                        .Select(bl => _mapper.Map<DocumentDto>(bl))
                        .ToList();

        await _logger.LogOperation(
            operation,
            "Success",
            [$"Retrieved {finalDtos.Count} docs"]
        );

        return finalDtos;
    }

    [LogOperation("Delete", "DocumentService")]
    public async Task DeleteDocument(int id, CancellationToken cancellationToken)
    {
        var operation = new LogOperationAttribute("Delete", "DocumentService");
        await _logger.LogOperation(
            operation,
            "Start",
            [$"Deleting doc {id}"]
        );

        // Check entity existence
        var entity = await _repository.GetByIdAsync(id, cancellationToken);
        if (entity == null)
        {
            await _logger.LogOperationError(
                operation,
                nameof(DeleteDocument),
                new KeyNotFoundException($"Doc {id} not found")
            );
            return;
        }

        // Remove file in MinIO
        var filePath = entity.FilePath;
        await _logger.LogOperation(
            operation,
            "MinIO",
            [$"Deleting file {filePath}"]
        );
        await _minioStorageService.DeleteFileAsync(filePath, cancellationToken);

        // Remove record from DB
        await _repository.DeleteAsync(id, cancellationToken);
        await _logger.LogOperation(
            operation,
            "Database",
            [$"Doc {id} removed from DB"]
        );

        // Remove from Elasticsearch
        var deleteResponse = await _elasticClient.DeleteAsync<DocumentDto>(
            id.ToString(),
            idx => idx.Index("paperless-documents"),
            cancellationToken
        );

        if (!deleteResponse.IsValidResponse)
        {
            await _logger.LogOperationError(
                operation,
                nameof(DeleteDocument),
                new Exception($"Failed to delete doc {id} from Elasticsearch:\n{deleteResponse.DebugInformation}")
            );
            return;
        }

        await _logger.LogOperation(
            operation,
            "Finish",
            [$"Doc {id} successfully deleted"]
        );
    }

    [LogOperation("Index", "DocumentService", LogLevel.Debug)]
    private async Task<bool> IndexDocument(DocumentDto document, CancellationToken cancellationToken)
    {
        var operation = new LogOperationAttribute("Index", "DocumentService", LogLevel.Debug);

        // Send doc to Elasticsearch
        var indexResponse = await _elasticClient.IndexAsync(
            document,
            i => i.Index("paperless-documents")
                  .Id(document.Id.ToString())
                  .Refresh(Refresh.True),
            cancellationToken
        );

        // Check for server-side errors
        if (!indexResponse.IsValidResponse)
        {
            var msg = $"Indexing failed for doc {document.Id}: {indexResponse.DebugInformation}";
            await _logger.LogOperationError(operation, nameof(IndexDocument), new Exception(msg));
            return false;
        }

        await _logger.LogOperation(
            operation,
            "Success",
            [$"Doc {document.Id} indexed"]
        );
        return true;
    }

    private string GenerateDocumentName(string originalName, int documentId)
    {
        return $"{documentId}_{originalName}";
    }

    private async Task LogValidationErrors(
        LogOperationAttribute operation,
        string stage,
        ValidationResult validationResult,
        int? docId = null)
    {
        foreach (var msg in validationResult.Errors.Select(error =>
                     docId.HasValue
                         ? $"Doc ID {docId}: {error.ErrorMessage}"
                         : error.ErrorMessage))
            await _logger.LogOperationError(operation, stage, new ValidationException(msg));
    }
}
