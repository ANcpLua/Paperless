namespace PaperlessREST.Tests.Unit;

public sealed class DocumentServiceTests : IDisposable
{
	// ═══════════════════════════════════════════════════════════════
	// CONSTANTS
	// ═══════════════════════════════════════════════════════════════

	private const string ValidFileName = "test-document.pdf";
	private const string ExtractedOcrContent = "This is the extracted OCR content.";
	private const string GenAiSummary = "This document summarizes important information.";
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<DocumentService> _logger;

	// ═══════════════════════════════════════════════════════════════
	// CONSTRUCTION
	// ═══════════════════════════════════════════════════════════════

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IRabbitMqPublisher> _publisher;

	private readonly Mock<IDocumentRepository> _repository;
	private readonly Mock<IDocumentSearchService> _search;
	private readonly Mock<IDocumentStorageService> _storage;
	private readonly FakeTimeProvider _timeProvider = new();

	public DocumentServiceTests()
	{
		_repository = _mocks.Create<IDocumentRepository>();
		_storage = _mocks.Create<IDocumentStorageService>();
		_search = _mocks.Create<IDocumentSearchService>();
		_publisher = _mocks.Create<IRabbitMqPublisher>();
		_logger = new FakeLogger<DocumentService>(_logCollector);
	}

	// ═══════════════════════════════════════════════════════════════
	// DISPOSAL
	// ═══════════════════════════════════════════════════════════════

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
		_mocks.VerifyAll();
		_mocks.VerifyNoOtherCalls();
	}

	private DocumentService CreateSut() =>
		new(_repository.Object, _storage.Object, _search.Object, _publisher.Object, _timeProvider, _logger);

	// ═══════════════════════════════════════════════════════════════
	// TESTS: UploadDocumentAsync - Success Path
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UploadDocumentAsync_ValidPdf_UploadsToStorageSavesAndPublishesOcrCommand()
	{
		// Arrange
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf()
			.WithFileName(ValidFileName)
			.WithFileSize(2048)
			.Build();

		_storage.Setup(s => s.UploadAsync(
				It.IsAny<Stream>(),
				It.Is<string>(p => p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)),
				2048,
				It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		_repository.Setup(r => r.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((Document d, CancellationToken _) => d);

		_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrCommand>()))
			.Returns(Task.CompletedTask);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Document> result = await sut.UploadDocumentAsync(request, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.FileName.Should().Be(ValidFileName);
		result.Value.Status.Should().Be(DocumentStatus.Pending);
	}

	[Fact]
	public async Task UploadDocumentAsync_Success_LogsInformation()
	{
		// Arrange
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf()
			.WithFileName(ValidFileName)
			.Build();

		_storage.Setup(s => s.UploadAsync(
				It.IsAny<Stream>(),
				It.IsAny<string>(),
				It.IsAny<long>(),
				It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		_repository.Setup(r => r.AddAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync((Document d, CancellationToken _) => d);

		_publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrCommand>()))
			.Returns(Task.CompletedTask);

		DocumentService sut = CreateSut();

		// Act
		await sut.UploadDocumentAsync(request, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains("uploaded successfully", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessOcrResultAsync - Success Paths
	// ═══════════════════════════════════════════════════════════════

	public static IEnumerable<TheoryDataRow<string, string?, DocumentStatus>> OcrStatusCases()
	{
		yield return new TheoryDataRow<string, string?, DocumentStatus>("Completed", ExtractedOcrContent,
				DocumentStatus.Completed)
			.WithTestDisplayName("Completed with content marks as Completed");
		yield return new TheoryDataRow<string, string?, DocumentStatus>("Completed", null, DocumentStatus.Failed)
			.WithTestDisplayName("Completed without content marks as Failed");
		yield return new TheoryDataRow<string, string?, DocumentStatus>("Failed", null, DocumentStatus.Failed)
			.WithTestDisplayName("Failed status marks as Failed");
		yield return new TheoryDataRow<string, string?, DocumentStatus>("Failed", ExtractedOcrContent,
				DocumentStatus.Failed)
			.WithTestDisplayName("Failed with content still marks as Failed");
	}

	[Theory]
	[MemberData(nameof(OcrStatusCases))]
	public async Task ProcessOcrResultAsync_VariousStatuses_UpdatesDocumentCorrectly(
		string status, string? content, DocumentStatus expectedStatus)
	{
		// Arrange
		Document doc = new DocumentBuilder().AsPending().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(doc);
		_repository.Setup(r => r.UpdateAsync(
				It.Is<Document>(d => d.Id == doc.Id && d.Status == expectedStatus),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Updated> result = await sut.ProcessOcrResultAsync(doc.Id, status, content,
			TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
	}

	[Fact]
	public async Task ProcessOcrResultAsync_CompletedWithContent_LogsProcessedStatus()
	{
		// Arrange
		Document doc = new DocumentBuilder().AsPending().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(doc);
		_repository.Setup(r => r.UpdateAsync(It.IsAny<Document>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		DocumentService sut = CreateSut();

		// Act
		await sut.ProcessOcrResultAsync(doc.Id, "Completed", ExtractedOcrContent,
			TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains("processed", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessOcrResultAsync - Document Not Found
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessOcrResultAsync_DocumentNotFound_ReturnsNotFoundError()
	{
		// Arrange
		Guid missingId = Guid.CreateVersion7();

		_repository.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
			.ReturnsAsync((Document?)null);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Updated> result = await sut.ProcessOcrResultAsync(missingId, "Completed", ExtractedOcrContent,
			TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
		result.FirstError.Code.Should().Be("Document.NotFound");
	}

	[Fact]
	public async Task ProcessOcrResultAsync_DocumentNotFound_LogsWarning()
	{
		// Arrange
		Guid missingId = Guid.CreateVersion7();

		_repository.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
			.ReturnsAsync((Document?)null);

		DocumentService sut = CreateSut();

		// Act
		await sut.ProcessOcrResultAsync(missingId, "Completed", ExtractedOcrContent,
			TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Warning &&
				l.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: UpdateDocumentSummaryAsync - Success Path
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UpdateDocumentSummaryAsync_DocumentFound_UpdatesAndReturnsTrue()
	{
		// Arrange
		Document doc = new DocumentBuilder().AsCompleted().Build();
		DateTimeOffset generatedAt = TimeProvider.System.GetUtcNow();

		_repository.Setup(r => r.UpdateSummaryAsync(doc.Id, GenAiSummary, generatedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Updated> result = await sut.UpdateDocumentSummaryAsync(doc.Id, GenAiSummary, generatedAt,
			TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.Should().Be(Result.Updated);
	}

	[Fact]
	public async Task UpdateDocumentSummaryAsync_DocumentFound_LogsSummaryLength()
	{
		// Arrange
		Document doc = new DocumentBuilder().AsCompleted().Build();
		DateTimeOffset generatedAt = TimeProvider.System.GetUtcNow();

		_repository.Setup(r => r.UpdateSummaryAsync(doc.Id, GenAiSummary, generatedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		DocumentService sut = CreateSut();

		// Act
		await sut.UpdateDocumentSummaryAsync(doc.Id, GenAiSummary, generatedAt,
			TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains("chars", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: UpdateDocumentSummaryAsync - Document Not Found
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UpdateDocumentSummaryAsync_DocumentNotFound_ReturnsFalse()
	{
		// Arrange
		Guid missingId = Guid.CreateVersion7();
		DateTimeOffset generatedAt = TimeProvider.System.GetUtcNow();

		_repository.Setup(r => r.UpdateSummaryAsync(missingId, GenAiSummary, generatedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Updated> result = await sut.UpdateDocumentSummaryAsync(missingId, GenAiSummary, generatedAt,
			TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
	}

	[Fact]
	public async Task UpdateDocumentSummaryAsync_DocumentNotFound_LogsWarning()
	{
		// Arrange
		Guid missingId = Guid.CreateVersion7();
		DateTimeOffset generatedAt = TimeProvider.System.GetUtcNow();

		_repository.Setup(r => r.UpdateSummaryAsync(missingId, GenAiSummary, generatedAt,
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);

		DocumentService sut = CreateSut();

		// Act
		await sut.UpdateDocumentSummaryAsync(missingId, GenAiSummary, generatedAt,
			TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Warning &&
				l.Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: GetDocumentsPagedAsync
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GetDocumentsPagedAsync_DelegatesToRepository()
	{
		// Arrange
		List<Document> documents =
		[
			new DocumentBuilder().Build(),
			new DocumentBuilder().Build()
		];

		_repository.Setup(r => r.GetDocumentsPagedAsync(20, null, It.IsAny<CancellationToken>()))
			.ReturnsAsync((documents, false));

		DocumentService sut = CreateSut();

		// Act
		(List<Document> items, bool hasMore) = await sut.GetDocumentsPagedAsync(20, null, TestContext.Current.CancellationToken);

		// Assert
		items.Should().HaveCount(2);
		hasMore.Should().BeFalse();
	}

	[Fact]
	public async Task GetDocumentsPagedAsync_WithCursor_PassesCursorToRepository()
	{
		// Arrange
		Guid cursor = Guid.CreateVersion7();
		List<Document> documents = [new DocumentBuilder().Build()];

		_repository.Setup(r => r.GetDocumentsPagedAsync(10, cursor, It.IsAny<CancellationToken>()))
			.ReturnsAsync((documents, true));

		DocumentService sut = CreateSut();

		// Act
		(List<Document> items, bool hasMore) = await sut.GetDocumentsPagedAsync(10, cursor, TestContext.Current.CancellationToken);

		// Assert
		items.Should().HaveCount(1);
		hasMore.Should().BeTrue();
		_repository.Verify(r => r.GetDocumentsPagedAsync(10, cursor, It.IsAny<CancellationToken>()), Times.Once);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: SearchDocumentsAsync
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task SearchDocumentsAsync_DelegatesToSearchService()
	{
		// Arrange
		DocumentSearchResult[] searchResults =
		[
			new DocumentSearchResult
			{
				Id = Guid.CreateVersion7(),
				FileName = "result1.pdf",
				Status = "Completed",
				CreatedAt = TimeProvider.System.GetUtcNow(),
				Content = "Content 1"
			},
			new DocumentSearchResult
			{
				Id = Guid.CreateVersion7(),
				FileName = "result2.pdf",
				Status = "Completed",
				CreatedAt = TimeProvider.System.GetUtcNow(),
				Content = "Content 2"
			}
		];

		_search.Setup(s => s.SearchAsync<DocumentSearchResult>("query", 10, It.IsAny<CancellationToken>()))
			.Returns(searchResults.ToAsyncEnumerable());

		DocumentService sut = CreateSut();

		// Act
		List<DocumentSearchResult> result = [];
		await foreach (DocumentSearchResult sr in sut.SearchDocumentsAsync("query", 10,
			               TestContext.Current.CancellationToken))
		{
			result.Add(sr);
		}

		// Assert
		result.Should().HaveCount(2);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: GetDocumentByIdAsync
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task GetDocumentByIdAsync_DocumentExists_ReturnsDocument()
	{
		// Arrange
		Document doc = new DocumentBuilder().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(doc);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Document> result = await sut.GetDocumentByIdAsync(doc.Id, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.Should().BeSameAs(doc);
	}

	[Fact]
	public async Task GetDocumentByIdAsync_DocumentNotFound_ReturnsNotFoundError()
	{
		// Arrange
		Guid missingId = Guid.CreateVersion7();

		_repository.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
			.ReturnsAsync((Document?)null);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Document> result = await sut.GetDocumentByIdAsync(missingId, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: DeleteDocumentAsync - Success Path
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DeleteDocumentAsync_DocumentExists_DeletesFromAllSources()
	{
		// Arrange
		Document doc = new DocumentBuilder().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(doc);
		_repository.Setup(r => r.DeleteAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_storage.Setup(s => s.DeleteAsync(doc.StoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_search.Setup(s => s.DeleteAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		DocumentService sut = CreateSut();

		// Act
		await sut.DeleteDocumentAsync(doc.Id, TestContext.Current.CancellationToken);

		// Assert - Verification happens in Dispose via VerifyAll
	}

	[Fact]
	public async Task DeleteDocumentAsync_Success_LogsInformation()
	{
		// Arrange
		Document doc = new DocumentBuilder().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(doc);
		_repository.Setup(r => r.DeleteAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_storage.Setup(s => s.DeleteAsync(doc.StoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_search.Setup(s => s.DeleteAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		DocumentService sut = CreateSut();

		// Act
		await sut.DeleteDocumentAsync(doc.Id, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains("deleted successfully", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: DeleteDocumentAsync - Document Not Found
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DeleteDocumentAsync_DocumentNotFound_ReturnsNotFoundError()
	{
		// Arrange
		Guid missingId = Guid.CreateVersion7();

		_repository.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
			.ReturnsAsync((Document?)null);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Deleted> result = await sut.DeleteDocumentAsync(missingId, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
		result.FirstError.Code.Should().Be("Document.NotFound");
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: DeleteDocumentAsync - Search Failure Handling
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DeleteDocumentAsync_SearchDeleteFails_ContinuesWithoutThrowing()
	{
		// Arrange
		Document doc = new DocumentBuilder().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(doc);
		_repository.Setup(r => r.DeleteAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_storage.Setup(s => s.DeleteAsync(doc.StoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_search.Setup(s => s.DeleteAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("Search index unavailable"));

		DocumentService sut = CreateSut();

		// Act
		Func<Task> act = () => sut.DeleteDocumentAsync(doc.Id, TestContext.Current.CancellationToken);

		// Assert - Should not throw; search deletion failure is expected and logged
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task DeleteDocumentAsync_SearchDeleteFails_LogsWarning()
	{
		// Arrange
		Document doc = new DocumentBuilder().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(doc);
		_repository.Setup(r => r.DeleteAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_storage.Setup(s => s.DeleteAsync(doc.StoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		_search.Setup(s => s.DeleteAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("Search index unavailable"));

		DocumentService sut = CreateSut();

		// Act
		await sut.DeleteDocumentAsync(doc.Id, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Warning &&
				l.Message.Contains("search index", StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: UploadDocumentAsync - Unknown Storage Exception (rethrown)
	// Covers DocumentService.cs lines 107-108 (TryMapStorageException returns null → throw)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task UploadDocumentAsync_UnknownStorageException_PropagatesOriginalException()
	{
		// Arrange — an exception type not handled by TryMapStorageException
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf().Build();
		InvalidOperationException expected = new("Unknown infrastructure failure");

		_storage.Setup(s => s.UploadAsync(
				It.IsAny<Stream>(),
				It.IsAny<string>(),
				It.IsAny<long>(),
				It.IsAny<CancellationToken>()))
			.ThrowsAsync(expected);

		DocumentService sut = CreateSut();

		// Act
		Func<Task> act = () => sut.UploadDocumentAsync(request, TestContext.Current.CancellationToken);

		// Assert — original exception propagates to caller, not mapped to ErrorOr
		InvalidOperationException thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
		thrown.Should().BeSameAs(expected);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessOcrResultAsync - State Transition Failure
	// Covers DocumentService.cs lines 148-151 (transitionResult.IsError branch)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessOcrResultAsync_DocumentAlreadyCompleted_ReturnsCannotCompleteError()
	{
		// Arrange — document already in Completed state, OCR re-arrival
		Document doc = new DocumentBuilder().AsCompleted().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(doc);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Updated> result = await sut.ProcessOcrResultAsync(doc.Id, "Completed", ExtractedOcrContent,
			TestContext.Current.CancellationToken);

		// Assert — exact error code from DocumentErrors.CannotComplete
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.Validation);
		result.FirstError.Code.Should().Be("Document.CannotComplete");
		result.FirstError.Description.Should().Contain("Completed");
	}

	[Fact]
	public async Task ProcessOcrResultAsync_TransitionFailure_LogsWarningAndDoesNotUpdateRepository()
	{
		// Arrange — document already in Failed state; MarkAsFailed should error
		Document doc = new DocumentBuilder().AsFailed().Build();

		_repository.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>()))
			.ReturnsAsync(doc);

		DocumentService sut = CreateSut();

		// Act
		ErrorOr<Updated> result = await sut.ProcessOcrResultAsync(doc.Id, "Failed", null,
			TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Code.Should().Be("Document.CannotFail");

		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Warning &&
				l.Message.Contains("state transition failed", StringComparison.OrdinalIgnoreCase));

		// Repository.UpdateAsync is intentionally NOT set up; MockBehavior.Strict will fail
		// the test in Dispose if it were called. This proves the short-circuit.
	}
}
