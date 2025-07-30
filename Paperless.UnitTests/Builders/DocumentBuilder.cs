using Microsoft.AspNetCore.Http;
using Moq;
using PaperlessREST;

namespace Paperless.UnitTests.Builders;

public sealed class DocumentBuilder
{
    private readonly Guid _id;
    private readonly string _fileName;
    private readonly DocumentStatus _status;
    private readonly DateTimeOffset _createdAt;
    private readonly string? _storagePath;
    private readonly string? _content;
    private readonly DateTimeOffset? _processedAt;

    public DocumentBuilder()
    {
        _id = Guid.NewGuid();
        _fileName = "test.pdf";
        _status = DocumentStatus.Pending;
        _createdAt = DateTimeOffset.UtcNow;
        _storagePath = null;
        _content = null;
        _processedAt = null;
    }

    private DocumentBuilder(
        Guid id,
        string fileName,
        DocumentStatus status,
        DateTimeOffset createdAt,
        string? storagePath,
        string? content,
        DateTimeOffset? processedAt)
    {
        _id = id;
        _fileName = fileName;
        _status = status;
        _createdAt = createdAt;
        _storagePath = storagePath;
        _content = content;
        _processedAt = processedAt;
    }

    public DocumentBuilder WithId(Guid id) =>
        new(id, _fileName, _status, _createdAt, _storagePath, _content, _processedAt);

    public DocumentBuilder WithFileName(string fileName) =>
        new(_id, fileName, _status, _createdAt, _storagePath, _content, _processedAt);

    public DocumentBuilder WithStatus(DocumentStatus status) =>
        new(_id, _fileName, status, _createdAt, _storagePath, _content, _processedAt);

    public DocumentBuilder WithCreatedAt(DateTimeOffset createdAt) =>
        new(_id, _fileName, _status, createdAt, _storagePath, _content, _processedAt);

    public DocumentBuilder WithStoragePath(string storagePath) =>
        new(_id, _fileName, _status, _createdAt, storagePath, _content, _processedAt);

    public DocumentBuilder WithContent(string? content) =>
        new(_id, _fileName, _status, _createdAt, _storagePath, content, _processedAt);

    public DocumentBuilder WithProcessedAt(DateTimeOffset? processedAt) =>
        new(_id, _fileName, _status, _createdAt, _storagePath, _content, processedAt);

    public Document Build()
    {
        var storagePath = _storagePath ?? $"documents/{_createdAt:yyyy-MM}/{_id}.pdf";
        return new Document(_id, _fileName, _status, _createdAt, storagePath, _content, _processedAt);
    }
}

public sealed class DocumentDtoBuilder
{
    private readonly Guid _id;
    private readonly string _fileName;
    private readonly DocumentStatus _status;
    private readonly DateTimeOffset _createdAt;
    private readonly string _storagePath;
    private readonly string? _content;
    private readonly DateTimeOffset? _processedAt;

    public DocumentDtoBuilder()
    {
        _id = Guid.NewGuid();
        _fileName = "test.pdf";
        _status = DocumentStatus.Pending;
        _createdAt = DateTimeOffset.UtcNow;
        _storagePath = "documents/test.pdf";
        _content = null;
        _processedAt = null;
    }

    private DocumentDtoBuilder(
        Guid id,
        string fileName,
        DocumentStatus status,
        DateTimeOffset createdAt,
        string storagePath,
        string? content,
        DateTimeOffset? processedAt)
    {
        _id = id;
        _fileName = fileName;
        _status = status;
        _createdAt = createdAt;
        _storagePath = storagePath;
        _content = content;
        _processedAt = processedAt;
    }

    public DocumentDtoBuilder WithId(Guid id) =>
        new(id, _fileName, _status, _createdAt, _storagePath, _content, _processedAt);

    public DocumentDtoBuilder WithFileName(string fileName) =>
        new(_id, fileName, _status, _createdAt, _storagePath, _content, _processedAt);

    public DocumentDtoBuilder WithStatus(DocumentStatus status) =>
        new(_id, _fileName, status, _createdAt, _storagePath, _content, _processedAt);

    public DocumentDtoBuilder WithCreatedAt(DateTimeOffset createdAt) =>
        new(_id, _fileName, _status, createdAt, _storagePath, _content, _processedAt);

    public DocumentDtoBuilder WithStoragePath(string storagePath) =>
        new(_id, _fileName, _status, _createdAt, storagePath, _content, _processedAt);

    public DocumentDtoBuilder WithContent(string? content) =>
        new(_id, _fileName, _status, _createdAt, _storagePath, content, _processedAt);

    public DocumentDtoBuilder WithProcessedAt(DateTimeOffset? processedAt) =>
        new(_id, _fileName, _status, _createdAt, _storagePath, _content, processedAt);

    public DocumentDto Build() => new()
    {
        Id = _id,
        FileName = _fileName,
        Status = _status.ToString(),
        CreatedAt = _createdAt,
        StoragePath = _storagePath,
        Content = _content,
        ProcessedAt = _processedAt
    };
}

public sealed class SearchQueryBuilder
{
    private readonly string _query;
    private readonly int _limit;

    public SearchQueryBuilder()
    {
        _query = "test";
        _limit = 10;
    }

    private SearchQueryBuilder(string query, int limit)
    {
        _query = query;
        _limit = limit;
    }

    public SearchQueryBuilder WithQuery(string query) =>
        new(query, _limit);

    public SearchQueryBuilder WithLimit(int limit) =>
        new(_query, limit);

    public SearchQuery Build() => new() { Query = _query, Limit = _limit };
}

public sealed class UploadDocumentRequestBuilder
{
    private readonly IFormFile _file;

    public UploadDocumentRequestBuilder()
    {
        _file = new Mock<IFormFile>().Object;
    }

    private UploadDocumentRequestBuilder(IFormFile file)
    {
        _file = file;
    }

    public UploadDocumentRequestBuilder WithFile(IFormFile file) =>
        new(file);

    public UploadDocumentRequestBuilder WithValidPdf()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns("test.pdf");
        fileMock.Setup(f => f.Length).Returns(1024);
        fileMock.Setup(f => f.ContentType).Returns("application/pdf");
        return new UploadDocumentRequestBuilder(fileMock.Object);
    }

    public UploadDocumentRequest Build() => new() { File = _file };
}