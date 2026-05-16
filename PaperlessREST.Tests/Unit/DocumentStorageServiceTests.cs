namespace PaperlessREST.Tests.Unit;

public sealed class DocumentStorageServiceTests : IDisposable
{
	private const string TestEndpoint = "localhost:9000";
	private const string TestAccessKey = "minioadmin";
	private const string TestSecretKey = "minioadmin";
	private const string TestBucketName = "test-bucket";
	private const string ValidStoragePath = "documents/2025-09/a.pdf";
	private const int TestFileSize = 3;
	private readonly Mock<IMinioClient> _minioClient;

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly IOptions<MinioOptions> _options;

	public DocumentStorageServiceTests()
	{
		_minioClient = _mocks.Create<IMinioClient>();
		_options = Options.Create(new MinioOptions
		{
			Endpoint = TestEndpoint,
			AccessKey = TestAccessKey,
			SecretKey = TestSecretKey,
			BucketName = TestBucketName
		});
	}

	public void Dispose()
	{
		_mocks.VerifyAll();
		_mocks.VerifyNoOtherCalls();
	}

	[Fact]
	public async Task UploadAsync_PutsObjectOnce()
	{
		// Arrange
		_minioClient.Setup(m => m.PutObjectAsync(
				It.IsAny<PutObjectArgs>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync((PutObjectResponse?)null);

		IDocumentStorageService sut = CreateSut();
		await using MemoryStream stream = new(new byte[TestFileSize]);

		// Act
		await sut.UploadAsync(stream, ValidStoragePath, TestFileSize, TestContext.Current.CancellationToken);

		// Assert
		_minioClient.Verify(m => m.PutObjectAsync(It.IsAny<PutObjectArgs>(), It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public async Task DeleteAsync_WhenMinioSucceeds_ReturnsTrue()
	{
		// Arrange
		_minioClient.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		IDocumentStorageService sut = CreateSut();

		// Act
		bool ok = await sut.DeleteAsync(ValidStoragePath, TestContext.Current.CancellationToken);

		// Assert
		ok.Should().BeTrue();
		_minioClient.Verify(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public async Task DeleteAsync_WhenMinioThrows_ReturnsFalse()
	{
		// Arrange
		_minioClient.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("boom"));

		IDocumentStorageService sut = CreateSut();

		// Act
		bool ok = await sut.DeleteAsync(ValidStoragePath, TestContext.Current.CancellationToken);

		// Assert
		ok.Should().BeFalse();
		_minioClient.Verify(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()),
			Times.Once);
	}

	private IDocumentStorageService CreateSut() =>
		new DocumentStorageService(_minioClient.Object, _options, NullLogger<DocumentStorageService>.Instance);
}
