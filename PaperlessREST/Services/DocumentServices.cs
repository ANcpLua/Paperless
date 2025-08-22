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
    Task UploadAsync(Stream stream, string storagePath, long length, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default);
}

public sealed class DocumentStorageService(
    IMinioClient minio,
    IOptions<MinioOptions> options,
    ILogger<DocumentStorageService> logger) : IDocumentStorageService
{
    public async Task UploadAsync(Stream stream, string storagePath, long length,
        CancellationToken cancellationToken = default)
    {
        await minio.PutObjectAsync(new PutObjectArgs()
            .WithBucket(options.Value.BucketName)
            .WithObject(storagePath)
            .WithStreamData(stream)
            .WithObjectSize(length), cancellationToken);

        logger.LogInformation("Document uploaded to storage at {StoragePath}", storagePath);
    }

    public async Task<bool> DeleteAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await minio.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(options.Value.BucketName)
                .WithObject(storagePath), cancellationToken);

            logger.LogInformation("Document removed from storage at {StoragePath}", storagePath);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to remove document from storage at {StoragePath}", storagePath);
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

public class DocumentSearchService(ElasticsearchClient elastic, ILogger<DocumentSearchService> logger)
    : IDocumentSearchService
{
    public async IAsyncEnumerable<T> SearchAsync<T>(
        string query,
        int limit = 10,
        [EnumeratorCancellation] CancellationToken cancellationToken = default) where T : class
    {
        logger.LogInformation("Searching for query: {Query} (limit: {Limit})", query, limit);

        var response = await elastic.SearchAsync<T>(s => s
                .Indices(elastic.ElasticsearchClientSettings.DefaultIndex)
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

        logger.LogInformation("Found {Count} results", response.Documents.Count);

        foreach (var doc in response.Documents)
        {
            yield return doc;
        }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var deleteRequest = new DeleteRequest(elastic.ElasticsearchClientSettings.DefaultIndex, id.ToString());
        var response = await elastic.DeleteAsync(deleteRequest, cancellationToken);

        logger.LogInformation("Document {DocumentId} removed from search index", id);
        return response.IsValidResponse;
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

    Task<bool> ProcessOcrResultAsync(Guid id, string status, string? content, CancellationToken processedAt,
        CancellationToken cancellationToken = default);
}

public class DocumentService(
    IDocumentRepository repository,
    IDocumentStorageService storage,
    IDocumentSearchService search,
    IRabbitMqPublisher publisher,
    ILogger<DocumentService> logger,
    IValidator<UploadDocumentRequest> uploadValidator)
    : IDocumentService
{
    public async Task<Document> UploadDocumentAsync(UploadDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        await uploadValidator.ValidateAndThrowAsync(request, cancellationToken);

        var document = Document.CreateFromUpload(request.File.FileName);

        await using var stream = request.File.OpenReadStream();
        await storage.UploadAsync(stream, document.StoragePath, request.File.Length, cancellationToken);

        var savedDocument = await repository.AddAsync(document, cancellationToken);

        var ocrRequest = new OcrCommand(savedDocument.Id, savedDocument.FileName, savedDocument.StoragePath);
        await publisher.PublishOcrCommandAsync(ocrRequest);

        logger.LogInformation("Document {DocumentId} uploaded successfully", savedDocument.Id);
        return savedDocument;
    }

    public IAsyncEnumerable<Document> GetRecentDocumentsAsync(CancellationToken cancellationToken = default)
    {
        return repository.GetRecentDocumentsAsync(50, cancellationToken);
    }

    public IAsyncEnumerable<object> SearchDocumentsAsync(
        string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        return search.SearchAsync<object>(query, limit, cancellationToken);
    }

    public ValueTask<Document?> GetDocumentByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task DeleteDocumentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            throw new KeyNotFoundException($"Document {id} not found");
        }

        // Delete from PostgreSQL and MinIO (required)
        await Task.WhenAll(
            repository.DeleteAsync(id, cancellationToken),
            storage.DeleteAsync(document.StoragePath, cancellationToken)
        );

        // Try to delete from Elasticsearch, but don't fail if it doesn't work
        // The OCR service is responsible for managing Elasticsearch state
        try
        {
            await search.DeleteAsync(id, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to delete document {DocumentId} from search index. This is expected if the document was not indexed.",
                id);
        }

        logger.LogInformation("Document {DocumentId} deleted successfully", id);
    }

    public async Task<bool> ProcessOcrResultAsync(Guid id, string status, string? content,
        CancellationToken processedAt, CancellationToken cancellationToken = default)
    {
        var document = await repository.GetByIdAsync(id, cancellationToken);
        if (document is null)
        {
            logger.LogWarning("Document {DocumentId} not found for OCR result", id);
            return false;
        }

        if (status is "Completed" && content is not null)
            document.MarkAsCompleted(content);
        else
            document.MarkAsFailed();

        await repository.UpdateAsync(document, cancellationToken);
        logger.LogInformation("Document {DocumentId} processed with status {Status}", id, document.Status);
        return true;
    }
}