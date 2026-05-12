using System.Net.Sockets;

namespace PaperlessREST.Tests.Unit;

public sealed class DocumentServiceErrorMappingTests
{
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<DocumentService> _logger;
	private readonly MockRepository _mocks = new(MockBehavior.Strict);
	private readonly Mock<IRabbitMqPublisher> _publisher;
	private readonly Mock<IDocumentRepository> _repository;
	private readonly Mock<IDocumentSearchService> _search;
	private readonly Mock<IDocumentStorageService> _storage;
	private readonly FakeTimeProvider _timeProvider = new();

	public DocumentServiceErrorMappingTests()
	{
		_repository = _mocks.Create<IDocumentRepository>();
		_storage = _mocks.Create<IDocumentStorageService>();
		_search = _mocks.Create<IDocumentSearchService>();
		_publisher = _mocks.Create<IRabbitMqPublisher>();
		_logger = new FakeLogger<DocumentService>(_logCollector);
	}

	private DocumentService CreateSut() =>
		new(_repository.Object, _storage.Object, _search.Object, _publisher.Object, _timeProvider, _logger);

	[Fact]
	public async Task UploadDocumentAsync_StorageTimeout_ReturnsStorageTimeoutError()
	{
		// Arrange
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf().Build();
		_storage.Setup(s =>
				s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new TimeoutException("MinIO timeout"));

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Document> result = await sut.UploadDocumentAsync(request, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Code.Should().Be("Document.StorageTimeout");
		result.FirstError.Description.Should().Contain("timeout");
	}

	[Fact]
	public async Task UploadDocumentAsync_Storage500_ReturnsStorageServerError()
	{
		// Arrange
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf().Build();
		_storage.Setup(s =>
				s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new HttpRequestException("Internal Server Error", null, HttpStatusCode.InternalServerError));

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Document> result = await sut.UploadDocumentAsync(request, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Code.Should().Be("Document.StorageServerError");
		result.FirstError.Description.Should().Contain("500");
	}

	[Fact]
	public async Task UploadDocumentAsync_StorageConnectionRefused_ReturnsStorageConnectionFailed()
	{
		// Arrange
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf().Build();
		SocketException inner = new((int)SocketError.ConnectionRefused);
		IOException ex = new("Connection refused", inner);

		_storage.Setup(s =>
				s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(ex);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Document> result = await sut.UploadDocumentAsync(request, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Code.Should().Be("Document.StorageConnectionFailed");
	}
}
