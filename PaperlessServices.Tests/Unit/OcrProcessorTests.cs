namespace PaperlessServices.Tests.Unit;

public sealed class OcrProcessorTests : IDisposable
{
	// ═══════════════════════════════════════════════════════════════
	// CONSTANTS
	// ═══════════════════════════════════════════════════════════════

	private const string ValidFileName = "document.pdf";
	private const string ValidStoragePath = "documents/2024-01/abc123.pdf";
	private const string ExtractedOcrText = "This is the extracted text from the PDF document.";
	private static readonly Guid s_testJobId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<OcrProcessor> _logger;

	// ═══════════════════════════════════════════════════════════════
	// CONSTRUCTION
	// ═══════════════════════════════════════════════════════════════

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IPdfExtractor> _pdfExtractor;
	private readonly Mock<ISearchIndexService> _searchIndex;
	private readonly Mock<IStorageService> _storage;
	private readonly FakeTimeProvider _timeProvider = new();

	public OcrProcessorTests()
	{
		_storage = _mocks.Create<IStorageService>();
		_pdfExtractor = _mocks.Create<IPdfExtractor>();
		_searchIndex = _mocks.Create<ISearchIndexService>();
		_logger = new FakeLogger<OcrProcessor>(_logCollector);
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

	private OcrProcessor CreateSut() =>
		new(_storage.Object, _pdfExtractor.Object, _searchIndex.Object, _timeProvider, _logger);

	private static OcrCommand CreateCommand(
		Guid? jobId = null,
		string fileName = ValidFileName,
		string storagePath = ValidStoragePath,
		DateTimeOffset? createdAt = null) =>
		new(jobId ?? s_testJobId, fileName, storagePath, createdAt ?? TimeProvider.System.GetUtcNow().AddMinutes(-5));

	private static MemoryStream CreateValidPdfStream() => new([0x25, 0x50, 0x44, 0x46]); // %PDF header

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessDocumentAsync - Success Path
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessDocumentAsync_ValidCommand_DownloadsFromStorage()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(ExtractedOcrText);
		_searchIndex.Setup(i => i.IndexDocumentAsync(
				s_testJobId, ValidFileName, ExtractedOcrText, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		OcrProcessor sut = CreateSut();

		// Act
		await sut.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);

		// Assert - Verification in Dispose
	}

	[Fact]
	public async Task ProcessDocumentAsync_ValidCommand_ExtractsTextWithOcr()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(ExtractedOcrText);
		_searchIndex.Setup(i => i.IndexDocumentAsync(
				s_testJobId, ValidFileName, ExtractedOcrText, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		OcrProcessor sut = CreateSut();

		// Act
		await sut.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);

		// Assert - ExtractTextAsync must be called, verified in Dispose
	}

	[Fact]
	public async Task ProcessDocumentAsync_ValidCommand_IndexesDocumentInSearch()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(ExtractedOcrText);
		_searchIndex.Setup(i => i.IndexDocumentAsync(
				s_testJobId, ValidFileName, ExtractedOcrText, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		OcrProcessor sut = CreateSut();

		// Act
		await sut.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);

		// Assert - IndexDocumentAsync must be called with correct parameters, verified in Dispose
	}

	[Fact]
	public async Task ProcessDocumentAsync_ValidCommand_ReturnsCompletedOcrEvent()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(ExtractedOcrText);
		_searchIndex.Setup(i => i.IndexDocumentAsync(
				s_testJobId, ValidFileName, ExtractedOcrText, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		OcrProcessor sut = CreateSut();

		// Act
		ErrorOr<OcrEvent> result = await sut.ProcessDocumentAsync(command,
			TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.JobId.Should().Be(s_testJobId);
		result.Value.Status.Should().Be("Completed");
		result.Value.Text.Should().Be(ExtractedOcrText);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessDocumentAsync - Success Logging
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessDocumentAsync_Success_LogsInformation()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(ExtractedOcrText);
		_searchIndex.Setup(i => i.IndexDocumentAsync(
				s_testJobId, ValidFileName, ExtractedOcrText, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		OcrProcessor sut = CreateSut();

		// Act
		await sut.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains("Successfully processed OCR job", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task ProcessDocumentAsync_Success_LogsJobId()
	{
		// Arrange
		Guid jobId = Guid.CreateVersion7();
		OcrCommand command = CreateCommand(jobId);

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(ExtractedOcrText);
		_searchIndex.Setup(i => i.IndexDocumentAsync(
				jobId, ValidFileName, ExtractedOcrText, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		OcrProcessor sut = CreateSut();

		// Act
		await sut.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);

		// Assert - JobId should appear in logs
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains(jobId.ToString(), StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessDocumentAsync - Storage Failure
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessDocumentAsync_StorageFails_ReturnsError()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));

		OcrProcessor sut = CreateSut();

		// Act
		ErrorOr<OcrEvent> result = await sut.ProcessDocumentAsync(command,
			TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
	}

	[Fact]
	public async Task ProcessDocumentAsync_StorageFails_LogsError()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));

		OcrProcessor sut = CreateSut();

		// Act
		await sut.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Error &&
				l.Message.Contains("Failed to download", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task ProcessDocumentAsync_StorageFails_DoesNotCallOcr()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("Storage unavailable"));

		OcrProcessor sut = CreateSut();

		// Act
		await sut.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);

		// Assert - OCR should never be called when download fails
		_pdfExtractor.Verify(p => p.ExtractTextAsync(It.IsAny<Stream>()), Times.Never);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessDocumentAsync - OCR Failure
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessDocumentAsync_OcrFails_ReturnsError()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(Error.Failure("Ocr.Failed", "OCR extraction failed"));

		OcrProcessor sut = CreateSut();

		// Act
		ErrorOr<OcrEvent> result = await sut.ProcessDocumentAsync(command,
			TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Code.Should().Be("Ocr.Failed");
	}

	[Fact]
	public async Task ProcessDocumentAsync_OcrFails_DoesNotIndexDocument()
	{
		// Arrange
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(Error.Failure("Ocr.Failed", "OCR extraction failed"));

		OcrProcessor sut = CreateSut();

		// Act
		await sut.ProcessDocumentAsync(command, TestContext.Current.CancellationToken);

		// Assert - Search index should never be called when OCR fails
		_searchIndex.Verify(s => s.IndexDocumentAsync(
			It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset?>(),
			It.IsAny<CancellationToken>()), Times.Never);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessDocumentAsync - Edge Cases
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessDocumentAsync_EmptyExtractedText_StillReturnsSuccess()
	{
		// Arrange - Some PDFs might have no extractable text (images only)
		OcrCommand command = CreateCommand();

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(string.Empty);
		_searchIndex.Setup(i => i.IndexDocumentAsync(
				s_testJobId, ValidFileName, string.Empty, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		OcrProcessor sut = CreateSut();

		// Act
		ErrorOr<OcrEvent> result = await sut.ProcessDocumentAsync(command,
			TestContext.Current.CancellationToken);

		// Assert - Empty text is valid result
		result.IsError.Should().BeFalse();
		result.Value.Status.Should().Be("Completed");
		result.Value.Text.Should().BeEmpty();
	}

	[Fact]
	public async Task ProcessDocumentAsync_LargeExtractedText_HandlesCorrectly()
	{
		// Arrange - Large documents might have extensive text
		OcrCommand command = CreateCommand();
		string largeText = new('x', 100_000);

		_storage.Setup(s => s.DownloadAsync(ValidStoragePath, It.IsAny<CancellationToken>()))
			.ReturnsAsync(CreateValidPdfStream());
		_pdfExtractor.Setup(p => p.ExtractTextAsync(It.IsAny<Stream>()))
			.ReturnsAsync(largeText);
		_searchIndex.Setup(i => i.IndexDocumentAsync(
				s_testJobId, ValidFileName, largeText, It.IsAny<DateTimeOffset?>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		OcrProcessor sut = CreateSut();

		// Act
		ErrorOr<OcrEvent> result = await sut.ProcessDocumentAsync(command,
			TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.Text.Should().HaveLength(100_000);
	}
}
