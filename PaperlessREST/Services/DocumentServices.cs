using System.Runtime.CompilerServices;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using FluentValidation;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using SWEN3.Paperless.RabbitMq.Models;
using SWEN3.Paperless.RabbitMq.Publishing;

namespace PaperlessREST.Services;

public interface IDocumentStorageService
{
    Task<string> UploadAsync(Stream stream, string storagePath, long fileSize,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}

public class DocumentStorageService : IDocumentStorageService
{
    private readonly IMinioClient _minio;
    private readonly IOptions<MinioOptions> _options;
    private readonly ILogger<DocumentStorageService> _logger;

    public DocumentStorageService(IMinioClient minio, IOptions<MinioOptions> options,
        ILogger<DocumentStorageService> logger)
    {
        _minio = minio;
        _options = options;
        _logger = logger;
    }

    public async Task<string> UploadAsync(Stream stream, string storagePath, long fileSize,
        CancellationToken cancellationToken = default)
    {
        await _minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_options.Value.BucketName)
            .WithObject(storagePath)
            .WithStreamData(stream)
            .WithObjectSize(fileSize)
            .WithContentType("application/pdf"), cancellationToken);

        _logger.LogInformation("Document uploaded to storage at {StoragePath}", storagePath);
        return storagePath;
    }

    public async Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await _minio.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_options.Value.BucketName)
                .WithObject(storagePath), cancellationToken);

            _logger.LogInformation("Document removed from storage at {StoragePath}", storagePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove document from storage at {StoragePath}", storagePath);
            return false;
        }
    }
}

public interface IDocumentSearchService
{
    IAsyncEnumerable<T> SearchAsync<T>(string query, int limit = 10, CancellationToken cancellationToken = default)
        where T : class;

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public class DocumentSearchService : IDocumentSearchService
{
    private readonly ElasticsearchClient _elastic;
    private readonly ILogger<DocumentSearchService> _logger;

    public DocumentSearchService(ElasticsearchClient elastic, ILogger<DocumentSearchService> logger)
    {
        _elastic = elastic;
        _logger = logger;
    }

    public async IAsyncEnumerable<T> SearchAsync<T>(
        string query,
        int limit = 10,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        _logger.LogInformation("Searching for query: {Query} (limit: {Limit})", query, limit);

        var response = await _elastic.SearchAsync<T>(s => s
                .Indices(_elastic.ElasticsearchClientSettings.DefaultIndex)
                .Query(q => q
                    .QueryString(qs => qs
                        .Query(query)
                        .DefaultField("*")
                        .Type(TextQueryType.BestFields)
                        .Fuzziness(new Fuzziness("AUTO"))
                        .Lenient()
                    )
                )
                .Size(limit)
                .TrackScores()
            , cancellationToken);

        if (!response.IsValidResponse)
        {
            _logger.LogError("Search failed: {Error}", response.ElasticsearchServerError?.Error.Reason);
            yield break;
        }

        _logger.LogInformation("Found {Count} results", response.Documents.Count);

        foreach (var doc in response.Documents)
        {
            yield return doc;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deleteRequest = new DeleteRequest(_elastic.ElasticsearchClientSettings.DefaultIndex, id.ToString());
        var response = await _elastic.DeleteAsync(deleteRequest, cancellationToken);

        if (!response.IsValidResponse)
        {
            _logger.LogError("Delete failed: {Error}", response.ElasticsearchServerError?.Error.Reason);
            return false;
        }

        _logger.LogInformation("Document {DocumentId} removed from search index", id);
        return true;
    }
}

public interface IDocumentService
{
    IAsyncEnumerable<Document> GetRecentDocumentsAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<object> SearchDocumentsAsync(string query, int limit,
        CancellationToken cancellationToken = default);

    ValueTask<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Document> UploadDocumentAsync(UploadDocumentRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ProcessOcrResultAsync(Guid id, string status, string? content, DateTimeOffset processedAt,
        CancellationToken cancellationToken = default);
}

public class DocumentService : IDocumentService
{
    private readonly IDocumentRepository _repository;
    private readonly IDocumentStorageService _storage;
    private readonly IDocumentSearchService _search;
    private readonly IRabbitMqPublisher _publisher;
    private readonly ILogger<DocumentService> _logger;
    private readonly IValidator<UploadDocumentRequest> _uploadValidator;

    public DocumentService(
        IDocumentRepository repository,
        IDocumentStorageService storage,
        IDocumentSearchService search,
        IRabbitMqPublisher publisher,
        ILogger<DocumentService> logger,
        IValidator<UploadDocumentRequest> uploadValidator)
    {
        _repository = repository;
        _storage = storage;
        _search = search;
        _publisher = publisher;
        _logger = logger;
        _uploadValidator = uploadValidator;
    }

    public async Task<Document> UploadDocumentAsync(UploadDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        await _uploadValidator.ValidateAndThrowAsync(request, cancellationToken);

        var document = Document.CreateFromUpload(request.File.FileName);

        await using var stream = request.File.OpenReadStream();
        await _storage.UploadAsync(stream, document.StoragePath, request.File.Length, cancellationToken);

        var savedDocument = await _repository.AddAsync(document, cancellationToken);

        var ocrRequest = new OcrCommand(savedDocument.Id, savedDocument.FileName, savedDocument.StoragePath);
        await _publisher.PublishOcrCommandAsync(ocrRequest);

        _logger.LogInformation("Document {DocumentId} uploaded successfully", savedDocument.Id);
        return savedDocument;
    }

    public IAsyncEnumerable<Document> GetRecentDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return _repository.GetRecentDocumentsAsync(50, cancellationToken);
    }

    public IAsyncEnumerable<object> SearchDocumentsAsync(
        string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        return _search.SearchAsync<object>(query, limit, cancellationToken);
    }

    public ValueTask<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return _repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await _repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            throw new KeyNotFoundException($"Document {id} not found");
        }

        await Task.WhenAll(
            _repository.DeleteAsync(id, cancellationToken),
            _storage.DeleteAsync(document.StoragePath, cancellationToken),
            _search.DeleteAsync(id, cancellationToken)
        );

        _logger.LogInformation("Document {DocumentId} deleted successfully", id);
    }

    public async Task<bool> ProcessOcrResultAsync(Guid id, string status, string? content,
        DateTimeOffset processedAt, CancellationToken cancellationToken = default)
    {
        var document = await _repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            _logger.LogWarning("Document {DocumentId} not found for OCR result", id);
            return false;
        }

        if (status is "Completed" && content is not null)
            document.MarkAsCompleted(content);
        else
            document.MarkAsFailed();

        await _repository.UpdateAsync(document, cancellationToken);
        _logger.LogInformation("Document {DocumentId} processed with status {Status}", id, document.Status);
        return true;
    }
}