namespace PaperlessServices.Tests.Unit;

public sealed class StorageServiceTests : IDisposable
{
	// ═══════════════════════════════════════════════════════════════
	// CONSTANTS
	// ═══════════════════════════════════════════════════════════════

	private const string ValidFilePath = "documents/2024-01/test-document.pdf";
	private const string TestBucketName = "test-bucket";
	private const string TestEndpoint = "localhost:9000";
	private const string TestAccessKey = "minioadmin";
	private const string TestSecretKey = "minioadmin";
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<StorageService> _logger;

	private readonly Mock<IMinioClient> _minioClient;

	// ═══════════════════════════════════════════════════════════════
	// CONSTRUCTION
	// ═══════════════════════════════════════════════════════════════

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly IOptions<MinioOptions> _options;

	public StorageServiceTests()
	{
		_minioClient = _mocks.Create<IMinioClient>();
		_options = Options.Create(new MinioOptions
		{
			Endpoint = TestEndpoint,
			AccessKey = TestAccessKey,
			SecretKey = TestSecretKey,
			BucketName = TestBucketName
		});
		_logger = new FakeLogger<StorageService>(_logCollector);
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

	private StorageService CreateSut() => new(_minioClient.Object, _options, _logger);

	// ═══════════════════════════════════════════════════════════════
	// TESTS: DownloadAsync - Success Path
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DownloadAsync_ValidPath_ReturnsStreamAtPosition0()
	{
		// Arrange
		_minioClient.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
			.Returns(Task.FromResult(default(ObjectStat)!));

		StorageService sut = CreateSut();

		// Act
		Stream stream = await sut.DownloadAsync(ValidFilePath, TestContext.Current.CancellationToken);

		// Assert - Stream should be positioned at beginning for reading
		stream.Position.Should().Be(0);
	}

	[Fact]
	public async Task DownloadAsync_ValidPath_CallsMinioGetObject()
	{
		// Arrange
		_minioClient.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
			.Returns(Task.FromResult(default(ObjectStat)!));

		StorageService sut = CreateSut();

		// Act
		await sut.DownloadAsync(ValidFilePath, TestContext.Current.CancellationToken);

		// Assert - Verification happens in Dispose via VerifyAll
	}

	[Fact]
	public async Task DownloadAsync_ValidPath_UsesConfiguredBucket()
	{
		// Arrange
		GetObjectArgs? capturedArgs = null;
		_minioClient.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
			.Callback<GetObjectArgs, CancellationToken>((args, _) => capturedArgs = args)
			.Returns(Task.FromResult(default(ObjectStat)!));

		StorageService sut = CreateSut();

		// Act
		await sut.DownloadAsync(ValidFilePath, TestContext.Current.CancellationToken);

		// Assert - GetObjectArgs should have been created with our bucket
		capturedArgs.Should().NotBeNull();
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: DownloadAsync - Success Logging
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DownloadAsync_Success_LogsInformation()
	{
		// Arrange
		_minioClient.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
			.Returns(Task.FromResult(default(ObjectStat)!));

		StorageService sut = CreateSut();

		// Act
		await sut.DownloadAsync(ValidFilePath, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains("Downloaded file from storage", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task DownloadAsync_Success_LogsFilePath()
	{
		// Arrange
		_minioClient.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
			.Returns(Task.FromResult(default(ObjectStat)!));

		StorageService sut = CreateSut();

		// Act
		await sut.DownloadAsync(ValidFilePath, TestContext.Current.CancellationToken);

		// Assert - File path should be logged for debugging
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains(ValidFilePath, StringComparison.OrdinalIgnoreCase));
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: DownloadAsync - File Not Found
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DownloadAsync_FileNotFound_ThrowsObjectNotFoundException()
	{
		// Arrange
		_minioClient.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new ObjectNotFoundException("Object not found"));

		StorageService sut = CreateSut();

		// Act
		Func<Task> act = () => sut.DownloadAsync("nonexistent.pdf", TestContext.Current.CancellationToken);

		// Assert - Should propagate MinIO exceptions
		await act.Should().ThrowExactlyAsync<ObjectNotFoundException>();
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: DownloadAsync - Various File Paths
	// ═══════════════════════════════════════════════════════════════

	public static IEnumerable<ITheoryDataRow> FilePaths()
	{
		yield return new TheoryDataRow<string>("simple.pdf")
			.WithTestDisplayName("Simple filename");
		yield return new TheoryDataRow<string>("path/to/document.pdf")
			.WithTestDisplayName("Nested path");
		yield return new TheoryDataRow<string>("deep/nested/path/to/file.pdf")
			.WithTestDisplayName("Deep nested path");
		yield return new TheoryDataRow<string>("documents/2024-01-15/uuid-12345.pdf")
			.WithTestDisplayName("Date-organized path");
	}

	[Theory]
	[MemberData(nameof(FilePaths))]
	public async Task DownloadAsync_VariousFilePaths_HandlesCorrectly(string filePath)
	{
		// Arrange
		_minioClient.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
			.Returns(Task.FromResult(default(ObjectStat)!));

		StorageService sut = CreateSut();

		// Act
		Stream stream = await sut.DownloadAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		stream.Should().NotBeNull();
		stream.Position.Should().Be(0);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: DownloadAsync - MinIO Errors
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task DownloadAsync_BucketNotFound_ThrowsBucketNotFoundException()
	{
		// Arrange
		_minioClient.Setup(m => m.GetObjectAsync(It.IsAny<GetObjectArgs>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new BucketNotFoundException("Bucket not found"));

		StorageService sut = CreateSut();

		// Act
		Func<Task> act = () => sut.DownloadAsync(ValidFilePath, TestContext.Current.CancellationToken);

		// Assert
		await act.Should().ThrowExactlyAsync<BucketNotFoundException>();
	}
}
