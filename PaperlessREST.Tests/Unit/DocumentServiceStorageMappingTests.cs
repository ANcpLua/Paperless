using System.Net;
using System.Net.Sockets;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Covers the storage-exception mapping inside <see cref="DocumentService.UploadDocumentAsync" />:
///     the four <c>TryMapStorageException</c> arms, plus the rethrow path when the storage exception
///     doesn't match any known shape (the <c>_ => null</c> case).
/// </summary>
public sealed class DocumentServiceStorageMappingTests : IDisposable
{
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<DocumentService> _logger;
	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IRabbitMqPublisher> _publisher;
	private readonly Mock<IDocumentRepository> _repository;
	private readonly Mock<IDocumentSearchService> _search;
	private readonly Mock<IDocumentStorageService> _storage;
	private readonly FakeTimeProvider _timeProvider = new();

	public DocumentServiceStorageMappingTests()
	{
		_repository = _mocks.Create<IDocumentRepository>();
		_storage = _mocks.Create<IDocumentStorageService>();
		_search = _mocks.Create<IDocumentSearchService>();
		_publisher = _mocks.Create<IRabbitMqPublisher>();
		_logger = new FakeLogger<DocumentService>(_logCollector);
	}

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
	}

	private DocumentService CreateSut() =>
		new(_repository.Object, _storage.Object, _search.Object, _publisher.Object, _timeProvider, _logger);

	private void SetupStorageThrows(Exception toThrow) =>
		_storage.Setup(s => s.UploadAsync(
				It.IsAny<Stream>(),
				It.IsAny<string>(),
				It.IsAny<long>(),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(toThrow);

	[Fact]
	public async Task UploadDocumentAsync_TimeoutException_ReturnsStorageTimeoutError()
	{
		SetupStorageThrows(new TimeoutException("timed out"));

		ErrorOr<Document> result = await CreateSut().UploadDocumentAsync(
			UploadDocumentRequestBuilder.ValidPdf().Build(),
			TestContext.Current.CancellationToken);

		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.Unexpected);
		result.FirstError.Code.Should().Be("Document.StorageTimeout");
		_logCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Warning && l.Message.Contains("StorageTimeout", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task UploadDocumentAsync_HttpServerError_ReturnsStorageServerError()
	{
		SetupStorageThrows(new HttpRequestException("502 bad gateway", null, HttpStatusCode.BadGateway));

		ErrorOr<Document> result = await CreateSut().UploadDocumentAsync(
			UploadDocumentRequestBuilder.ValidPdf().Build(),
			TestContext.Current.CancellationToken);

		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.Unexpected);
		result.FirstError.Code.Should().Be("Document.StorageServerError");
		result.FirstError.Description.Should().Contain("502");
	}

	[Fact]
	public async Task UploadDocumentAsync_HttpClientError_DoesNotMatchAndRethrows()
	{
		// Only 5xx maps; 4xx fall through to the `_ => null` arm and re-throw.
		HttpRequestException original = new("400 bad request", null, HttpStatusCode.BadRequest);
		SetupStorageThrows(original);

		Func<Task> act = () => CreateSut().UploadDocumentAsync(
			UploadDocumentRequestBuilder.ValidPdf().Build(),
			TestContext.Current.CancellationToken);

		(await act.Should().ThrowAsync<HttpRequestException>())
			.Which.Should().BeSameAs(original);
	}

	[Fact]
	public async Task UploadDocumentAsync_SocketUnderlyingIOException_ReturnsConnectionFailedError()
	{
		SetupStorageThrows(new IOException("network down", new SocketException()));

		ErrorOr<Document> result = await CreateSut().UploadDocumentAsync(
			UploadDocumentRequestBuilder.ValidPdf().Build(),
			TestContext.Current.CancellationToken);

		result.IsError.Should().BeTrue();
		result.FirstError.Code.Should().Be("Document.StorageConnectionFailed");
	}

	[Fact]
	public async Task UploadDocumentAsync_UnknownException_PropagatesToCaller()
	{
		InvalidOperationException boom = new("totally unrelated");
		SetupStorageThrows(boom);

		Func<Task> act = () => CreateSut().UploadDocumentAsync(
			UploadDocumentRequestBuilder.ValidPdf().Build(),
			TestContext.Current.CancellationToken);

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.Which.Should().BeSameAs(boom);
	}

	// ─── ProcessOcrResultAsync state-transition-failure branch ──────────────

	[Fact]
	public async Task ProcessOcrResultAsync_AlreadyCompleted_ReturnsCannotCompleteError()
	{
		// Already-completed documents can't be re-completed (MarkAsCompleted rejects
		// non-Pending status). Exercises the `transitionResult.IsError` arm in
		// ProcessOcrResultAsync that the existing Pending-state tests never hit.
		Document doc = new DocumentBuilder().AsCompleted().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

		ErrorOr<Updated> result = await CreateSut().ProcessOcrResultAsync(
			doc.Id, "Completed", "ocr content",
			TestContext.Current.CancellationToken);

		result.IsError.Should().BeTrue();
		_logCollector.GetSnapshot().Should().Contain(l =>
			l.Level == LogLevel.Warning && l.Message.Contains("state transition failed", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task ProcessOcrResultAsync_AlreadyFailed_CannotBeFailedAgain()
	{
		Document doc = new DocumentBuilder().AsFailed().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

		ErrorOr<Updated> result = await CreateSut().ProcessOcrResultAsync(
			doc.Id, "Failed", null,
			TestContext.Current.CancellationToken);

		result.IsError.Should().BeTrue();
	}
}
