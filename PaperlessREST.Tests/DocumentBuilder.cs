namespace PaperlessREST.Tests;

public sealed class DocumentBuilder
{
	private const string DefaultFileName = "test-document.pdf";
	private const string DefaultStoragePathFormat = "documents/{0:yyyy-MM}/{1}.pdf";
	private const string DefaultExtractedContent = "Extracted content";
	private string? _content;
	private DateTimeOffset _createdAt = TimeProvider.System.GetUtcNow();
	private string _fileName = DefaultFileName;

	private Guid _id = Guid.CreateVersion7();
	private DateTimeOffset? _processedAt;
	private DocumentStatus _status = DocumentStatus.Pending;
	private string? _storagePath;
	private string? _summary;
	private DateTimeOffset? _summaryGeneratedAt;

	public static DocumentBuilder Pending() => new();

	public static DocumentBuilder Completed(string content = DefaultExtractedContent) =>
		new DocumentBuilder().AsCompleted(content);

	public static DocumentBuilder Failed() => new DocumentBuilder().AsFailed();

	public DocumentBuilder WithId(Guid id)
	{
		_id = id;
		return this;
	}

	public DocumentBuilder WithFileName(string fileName)
	{
		_fileName = fileName;
		return this;
	}

	public DocumentBuilder WithStatus(DocumentStatus status)
	{
		_status = status;
		return this;
	}

	public DocumentBuilder WithCreatedAt(DateTimeOffset createdAt)
	{
		_createdAt = createdAt;
		return this;
	}

	public DocumentBuilder WithStoragePath(string path)
	{
		_storagePath = path;
		return this;
	}

	public DocumentBuilder WithContent(string? content)
	{
		_content = content;
		return this;
	}

	public DocumentBuilder WithProcessedAt(DateTimeOffset? processedAt)
	{
		_processedAt = processedAt;
		return this;
	}

	public DocumentBuilder WithSummary(string? summary, DateTimeOffset? generatedAt = null)
	{
		_summary = summary;
		_summaryGeneratedAt = generatedAt ?? (_summary is not null ? TimeProvider.System.GetUtcNow() : null);
		return this;
	}

	public DocumentBuilder AsPending()
	{
		_status = DocumentStatus.Pending;
		_content = null;
		_processedAt = null;
		return this;
	}

	public DocumentBuilder AsCompleted(string content = DefaultExtractedContent)
	{
		_status = DocumentStatus.Completed;
		_content = content;
		_processedAt = TimeProvider.System.GetUtcNow();
		return this;
	}

	public DocumentBuilder AsFailed()
	{
		_status = DocumentStatus.Failed;
		_content = null;
		_processedAt = TimeProvider.System.GetUtcNow();
		return this;
	}

	public Document Build() => BuildDocument();

	public Document BuildDocument() =>
		new()
		{
			Id = _id,
			FileName = _fileName,
			Status = _status,
			CreatedAt = _createdAt,
			StoragePath = GetStoragePath(),
			Content = _content,
			ProcessedAt = _processedAt,
			Summary = _summary,
			SummaryGeneratedAt = _summaryGeneratedAt
		};

	public DocumentEntity BuildEntity() =>
		new()
		{
			Id = _id,
			FileName = _fileName,
			Status = _status,
			CreatedAt = _createdAt,
			StoragePath = GetStoragePath(),
			Content = _content,
			ProcessedAt = _processedAt,
			Summary = _summary,
			SummaryGeneratedAt = _summaryGeneratedAt
		};

	public DocumentDto BuildDto() =>
		new()
		{
			Id = _id,
			FileName = _fileName,
			Status = _status.ToString(),
			CreatedAt = _createdAt,
			Content = _content,
			ProcessedAt = _processedAt,
			Summary = _summary,
			SummaryGeneratedAt = _summaryGeneratedAt
		};

	public CreateDocumentResponse BuildCreateResponse() =>
		new() { Id = _id, FileName = _fileName, Status = _status.ToString(), CreatedAt = _createdAt };

	private string GetStoragePath() =>
		_storagePath ?? string.Format(DefaultStoragePathFormat, _createdAt.UtcDateTime, _id);
}

public sealed class UploadDocumentRequestBuilder
{
	private const string DefaultContentType = "application/pdf";
	private byte[]? _content;
	private string _contentType = DefaultContentType;

	private string _fileName = "upload.pdf";
	private long _fileSize = 1024;

	public static UploadDocumentRequestBuilder ValidPdf() =>
		new UploadDocumentRequestBuilder()
			.WithFileName("document.pdf")
			.WithFileSize(2048);

	public static UploadDocumentRequestBuilder LargeFile() =>
		new UploadDocumentRequestBuilder()
			.WithFileName("large.pdf")
			.WithFileSize(100 * 1024 * 1024);

	public static UploadDocumentRequestBuilder InvalidType() =>
		new UploadDocumentRequestBuilder()
			.WithFileName("document.exe")
			.WithContentType("application/octet-stream");

	public UploadDocumentRequestBuilder WithFileName(string fileName)
	{
		_fileName = fileName;
		return this;
	}

	public UploadDocumentRequestBuilder WithFileSize(long size)
	{
		_fileSize = size;
		return this;
	}

	public UploadDocumentRequestBuilder WithContentType(string contentType)
	{
		_contentType = contentType;
		return this;
	}

	public UploadDocumentRequestBuilder WithContent(byte[] content)
	{
		_content = content;
		_fileSize = content.Length;
		return this;
	}

	public UploadDocumentRequest Build()
	{
		byte[] content = _content ?? new byte[_fileSize];
		MemoryStream stream = new(content);

		Mock<IFormFile> fileMock = new();
		fileMock.Setup(f => f.FileName).Returns(_fileName);
		fileMock.Setup(f => f.Length).Returns(_fileSize);
		fileMock.Setup(f => f.ContentType).Returns(_contentType);
		fileMock.Setup(f => f.OpenReadStream()).Returns(stream);
		fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
			.Returns((Stream target, CancellationToken ct) =>
			{
				stream.Position = 0;
				return stream.CopyToAsync(target, ct);
			});

		return new UploadDocumentRequest { File = fileMock.Object };
	}
}

public sealed class SearchQueryBuilder
{
	private int _limit = 10;
	private string _query = "search";

	public SearchQueryBuilder WithQuery(string query)
	{
		_query = query;
		return this;
	}

	public SearchQueryBuilder WithLimit(int limit)
	{
		_limit = limit;
		return this;
	}

	public SearchQuery Build() => new() { Query = _query, Limit = _limit };
}
