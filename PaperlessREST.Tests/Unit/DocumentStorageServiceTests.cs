namespace PaperlessREST.Tests.Unit;

public sealed class DocumentStorageServiceTests : IDisposable
{
	private const string TestEndpoint = "http://localhost:9000";
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
			Endpoint = new Uri(TestEndpoint),
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
	public async Task DeleteAsync_WhenMinioSucceeds_Completes()
	{
		// Arrange
		_minioClient.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		IDocumentStorageService sut = CreateSut();

		await sut.DeleteAsync(ValidStoragePath, TestContext.Current.CancellationToken);

		_minioClient.Verify(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public async Task DeleteAsync_WhenMinioThrows_PropagatesOriginalException()
	{
		InvalidOperationException expected = new("boom");
		_minioClient.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(expected);

		IDocumentStorageService sut = CreateSut();
		Func<Task> act = () => sut.DeleteAsync(ValidStoragePath, TestContext.Current.CancellationToken);

		InvalidOperationException thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
		thrown.Should().BeSameAs(expected);
	}

	[Fact]
	public async Task DeleteAsync_WhenMinioCancels_PropagatesOriginalToken()
	{
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();
		OperationCanceledException expected = new(cancellation.Token);
		_minioClient.Setup(m => m.RemoveObjectAsync(It.IsAny<RemoveObjectArgs>(), cancellation.Token))
			.ThrowsAsync(expected);

		IDocumentStorageService sut = CreateSut();
		Func<Task> act = () => sut.DeleteAsync(ValidStoragePath, cancellation.Token);

		OperationCanceledException thrown = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
		thrown.Should().BeSameAs(expected);
		thrown.CancellationToken.Should().Be(cancellation.Token);
	}

	private IDocumentStorageService CreateSut() =>
		new DocumentStorageService(_minioClient.Object, _options, NullLogger<DocumentStorageService>.Instance);
}
