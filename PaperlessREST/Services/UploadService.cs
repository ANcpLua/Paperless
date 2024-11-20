using AutoMapper;
using FluentValidation;
using PaperlessREST.DomainModel;
using PaperlessREST.Models;
using PostgreSQL.Persistence;

namespace PaperlessREST.Services;

public class UploadService : IUploadService
{
    private readonly IDocumentRepository _repository;
    private readonly IValidator<Document> _validator;
    private readonly IMapper _mapper;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        IDocumentRepository repository,
        IValidator<Document> validator,
        IMapper mapper,
        ILogger<UploadService> logger)
    {
        _repository = repository;
        _validator = validator;
        _mapper = mapper;
        _logger = logger;
    }

    public async Task<DocumentDto> Upload(DocumentUploadDto uploadDto, CancellationToken cancellationToken = default)
    {
        var document = _mapper.Map<Document>(uploadDto);
            
        document.FilePath = Path.Combine("uploads", $"{Guid.NewGuid()}{Path.GetExtension(uploadDto.File.FileName)}");
        document.DateUploaded = DateTime.UtcNow;

        var validationResult = await _validator.ValidateAsync(document, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        Directory.CreateDirectory("uploads");
        using (var stream = new FileStream(document.FilePath, FileMode.Create))
        {
            await uploadDto.File.CopyToAsync(stream, cancellationToken);
        }

        var entityDocument = _mapper.Map<PostgreSQL.Entities.Document>(document);
        var uploadedEntityDocument = await _repository.Upload(entityDocument, cancellationToken);
        var uploadedDocument = _mapper.Map<Document>(uploadedEntityDocument);
            
        return _mapper.Map<DocumentDto>(uploadedDocument);
    }
}
