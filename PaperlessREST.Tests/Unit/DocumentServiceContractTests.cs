using System.Net.Sockets;
using System.Text;
using AwesomeAssertions.Execution;
using Minio.Exceptions;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Behavior contract for <see cref="DocumentService" />: one parameterized suite over every public
///     operation, asserting the observable outcome (returned value, the entity handed to the repository,
///     the published <see cref="OcrCommand" />) rather than log substrings. Replaces the former
///     DocumentServiceTests / DocumentServiceStorageMappingTests / DocumentServiceErrorMappingTests trio.
/// </summary>
public sealed class DocumentServiceContractTests : DocumentServiceTestBase
{
	private const string FileName = "bank-statement.pdf";
	private const string OcrText = "OCR text extracted from the uploaded PDF.";
	private const string Summary = "AI summary of the uploaded document.";
	private const long FileSize = 2_048;

	private static readonly DateTimeOffset s_uploadedAt = FixedInstant;
	private static readonly DateTimeOffset s_ocrProcessedAt = new(2026, 06, 03, 10, 17, 00, TimeSpan.Zero);
	private static readonly DateTimeOffset s_summaryGeneratedAt = new(2026, 06, 03, 10, 20, 00, TimeSpan.Zero);

	// ── UploadDocumentAsync ───────────────────────────────────────────────

	[Fact]
	public async Task UploadDocumentAsync_ValidPdf_CreatesExactDocumentUploadsExactObjectAndPublishesExactOcrCommand()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf()
			.WithFileName(FileName)
			.WithFileSize(FileSize)
			.Build();

		Document? documentGivenToRepository = null;
		string? pathGivenToStorage = null;
		long lengthGivenToStorage = -1;
		CancellationToken tokenGivenToStorage = default;
		Stream? streamGivenToStorage = null;
		string? routingKeyGivenToPublisher = null;
		OcrCommand? commandGivenToPublisher = null;

		Storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), FileSize, ct))
			.Callback<Stream, string, long, CancellationToken>((stream, path, length, token) =>
			{
				streamGivenToStorage = stream;
				pathGivenToStorage = path;
				lengthGivenToStorage = length;
				tokenGivenToStorage = token;
				stream.CanRead.Should().BeTrue("the opened request file stream is the object sent to storage");
			})
			.Returns(Task.CompletedTask);

		Repository.Setup(r => r.AddAsync(It.IsAny<Document>(), ct))
			.Callback<Document, CancellationToken>((document, _) => documentGivenToRepository = Snapshot(document))
			.ReturnsAsync((Document document, CancellationToken _) => document);

		Publisher.Setup(p => p.PublishAsync(It.IsAny<string>(), It.IsAny<OcrCommand>()))
			.Callback<string, OcrCommand>((routingKey, command) =>
			{
				routingKeyGivenToPublisher = routingKey;
				commandGivenToPublisher = command;
			})
			.Returns(Task.CompletedTask);

		ErrorOr<Document> result = await CreateSut().UploadDocumentAsync(request, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeFalse();
		Document saved = result.Value;
		string expectedStoragePath = $"documents/{s_uploadedAt.UtcDateTime:yyyy-MM}/{saved.Id}.pdf";

		saved.FileName.Should().Be(FileName);
		saved.Status.Should().Be(DocumentStatus.Pending);
		saved.CreatedAt.Should().Be(s_uploadedAt);
		saved.StoragePath.Should().Be(expectedStoragePath);
		saved.Content.Should().BeNull();
		saved.ProcessedAt.Should().BeNull();
		saved.Summary.Should().BeNull();
		saved.SummaryGeneratedAt.Should().BeNull();

		documentGivenToRepository.Should().NotBeNull().And.BeEquivalentTo(saved);

		pathGivenToStorage.Should().Be(expectedStoragePath);
		lengthGivenToStorage.Should().Be(FileSize);
		tokenGivenToStorage.Should().Be(ct);
		streamGivenToStorage.Should().NotBeNull();
		streamGivenToStorage!.CanRead.Should().BeFalse("DocumentService owns the opened upload stream");

		routingKeyGivenToPublisher.Should().NotBeNullOrWhiteSpace();
		commandGivenToPublisher.Should().BeEquivalentTo(
			new OcrCommand(saved.Id, FileName, expectedStoragePath, s_uploadedAt));

		ShouldHaveLog(LogLevel.Information, "uploaded successfully", saved.Id.ToString());
	}

	[Fact]
	public async Task UploadDocumentAsync_MinIoNetworkFailure_ReturnsRetriableConnectionError()
	{
		using StubHttpMessageHandler handler = new((_, _) =>
			Task.FromException<HttpResponseMessage>(new HttpRequestException(
				"connection refused",
				new SocketException((int)SocketError.ConnectionRefused))));
		using HttpClient http = new(handler, disposeHandler: false);
		using MinioClient minio = new();
		IMinioClient client = ConfigureMinioClient(minio, http);

		ErrorOr<Document> result = await UploadThroughMinioAsync(client, TestContext.Current.CancellationToken);

		AssertRetriableStorageError(
			result,
			"Document.StorageConnectionFailed",
			"The storage service could not be reached.");
	}

	[Fact]
	public async Task UploadDocumentAsync_MinIoRequestTimeout_ReturnsRetriableTimeoutError()
	{
		using StubHttpMessageHandler handler = new(
			async (_, cancellationToken) =>
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
				return new HttpResponseMessage(HttpStatusCode.OK);
			});
		using HttpClient http = new(handler, disposeHandler: false);
		using MinioClient minio = new();
		IMinioClient client = ConfigureMinioClient(minio, http, requestTimeoutMilliseconds: 25);

		ErrorOr<Document> result = await UploadThroughMinioAsync(client, CancellationToken.None);

		AssertRetriableStorageError(
			result,
			"Document.StorageTimeout",
			"The storage operation timed out.");
	}

	[Fact]
	public async Task UploadDocumentAsync_MinIoTransientXmlError_ReturnsRetriableServerError()
	{
		using StubHttpMessageHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(
			HttpStatusCode.InternalServerError)
		{
			Content = new StringContent(
				MinioErrorXml("InternalError"),
				Encoding.UTF8,
				"application/xml")
		}));
		using HttpClient http = new(handler, disposeHandler: false);
		using MinioClient minio = new();
		IMinioClient client = ConfigureMinioClient(minio, http);

		ErrorOr<Document> result = await UploadThroughMinioAsync(client, TestContext.Current.CancellationToken);

		AssertRetriableStorageError(
			result,
			"Document.StorageServerError",
			"The storage service is temporarily unavailable.");
	}

	[Fact]
	public async Task UploadDocumentAsync_MinIoCallerCancellation_PropagatesExactToken()
	{
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();
		using StubHttpMessageHandler handler = new((_, token) =>
		{
			token.ThrowIfCancellationRequested();
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
		});
		using HttpClient http = new(handler, disposeHandler: false);
		using MinioClient minio = new();
		IMinioClient client = ConfigureMinioClient(minio, http);

		Func<Task> act = async () => await UploadThroughMinioAsync(client, cancellation.Token);

		OperationCanceledException thrown = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
		thrown.CancellationToken.Should().Be(cancellation.Token);
	}

	[Fact]
	public async Task UploadDocumentAsync_MinIoPermanentError_PropagatesSdkException()
	{
		using StubHttpMessageHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(
			HttpStatusCode.Forbidden)
		{
			Content = new StringContent(
				MinioErrorXml("InvalidAccessKeyId"),
				Encoding.UTF8,
				"application/xml")
		}));
		using HttpClient http = new(handler, disposeHandler: false);
		using MinioClient minio = new();
		IMinioClient client = ConfigureMinioClient(minio, http);

		Func<Task> act = async () =>
			await UploadThroughMinioAsync(client, TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<AuthorizationException>();
	}

	// ── ProcessOcrResultAsync ─────────────────────────────────────────────

	public static IEnumerable<TheoryDataRow<string, string?, DocumentStatus, string?>> OcrResultCases()
	{
		yield return new TheoryDataRow<string, string?, DocumentStatus, string?>(
				"Completed", OcrText, DocumentStatus.Completed, OcrText)
			.WithTestDisplayName("Completed + content => Completed, content persisted");
		yield return new TheoryDataRow<string, string?, DocumentStatus, string?>(
				"Completed", null, DocumentStatus.Failed, null)
			.WithTestDisplayName("Completed + null content => Failed");
		yield return new TheoryDataRow<string, string?, DocumentStatus, string?>(
				"Failed", null, DocumentStatus.Failed, null)
			.WithTestDisplayName("Failed + null content => Failed");
		yield return new TheoryDataRow<string, string?, DocumentStatus, string?>(
				"Failed", OcrText, DocumentStatus.Failed, null)
			.WithTestDisplayName("Failed + content => Failed, content ignored");
	}

	[Theory]
	[MemberData(nameof(OcrResultCases))]
	public async Task ProcessOcrResultAsync_PendingDocument_PersistsExactStateTransition(
		string incomingStatus, string? incomingContent, DocumentStatus expectedStatus, string? expectedContent)
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Clock.SetUtcNow(s_ocrProcessedAt);
		Document document = new DocumentBuilder().AsPending().Build();
		Document? documentGivenToRepository = null;

		Repository.Setup(r => r.GetByIdAsync(document.Id, ct)).ReturnsAsync(document);
		Repository.Setup(r => r.UpdateAsync(It.Is<Document>(d => d.Id == document.Id), ct))
			.Callback<Document, CancellationToken>((updated, _) => documentGivenToRepository = Snapshot(updated))
			.ReturnsAsync(true);

		ErrorOr<Updated> result = await CreateSut().ProcessOcrResultAsync(document.Id, incomingStatus, incomingContent, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeFalse();
		result.Value.Should().Be(Result.Updated);

		documentGivenToRepository.Should().NotBeNull();
		documentGivenToRepository!.Status.Should().Be(expectedStatus);
		documentGivenToRepository.Content.Should().Be(expectedContent);
		documentGivenToRepository.ProcessedAt.Should().Be(s_ocrProcessedAt);
	}

	public static IEnumerable<TheoryDataRow<DocumentStatus, string, string?, string>> InvalidOcrTransitions()
	{
		yield return new TheoryDataRow<DocumentStatus, string, string?, string>(
			DocumentStatus.Completed, "Completed", OcrText, "Document.CannotComplete");
		yield return new TheoryDataRow<DocumentStatus, string, string?, string>(
			DocumentStatus.Completed, "Failed", null, "Document.CannotFail");
		yield return new TheoryDataRow<DocumentStatus, string, string?, string>(
			DocumentStatus.Failed, "Completed", OcrText, "Document.CannotComplete");
		yield return new TheoryDataRow<DocumentStatus, string, string?, string>(
			DocumentStatus.Failed, "Failed", null, "Document.CannotFail");
	}

	[Theory]
	[MemberData(nameof(InvalidOcrTransitions))]
	public async Task ProcessOcrResultAsync_NonPendingDocument_ReturnsValidationErrorAndDoesNotPersist(
		DocumentStatus existingStatus, string incomingStatus, string? incomingContent, string expectedErrorCode)
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Document document = ExistingDocument(existingStatus);

		Repository.Setup(r => r.GetByIdAsync(document.Id, ct)).ReturnsAsync(document);

		ErrorOr<Updated> result = await CreateSut().ProcessOcrResultAsync(document.Id, incomingStatus, incomingContent, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.Validation);
		result.FirstError.Code.Should().Be(expectedErrorCode);
		result.FirstError.Description.Should().Contain(existingStatus.ToString());
		ShouldHaveLog(LogLevel.Warning, "state transition failed", existingStatus.ToString());
		// No UpdateAsync setup is intentional: strict mocks prove no write happens after a validation failure.
	}

	[Fact]
	public async Task ProcessOcrResultAsync_MissingDocument_ReturnsNotFoundAndDoesNotPersist()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Guid missingId = Guid.CreateVersion7();

		Repository.Setup(r => r.GetByIdAsync(missingId, ct)).ReturnsAsync((Document?)null);

		ErrorOr<Updated> result = await CreateSut().ProcessOcrResultAsync(missingId, "Completed", OcrText, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
		result.FirstError.Code.Should().Be("Document.NotFound");
		ShouldHaveLog(LogLevel.Warning, "not found", missingId.ToString());
	}

	[Fact]
	public async Task ProcessOcrResultAsync_RepositoryUpdateAffectsNoRows_ReturnsNotFoundInsteadOfClaimingUpdated()
	{
		// Concurrent-delete race: the row vanished between GetById and Update. Honoring the bool from
		// UpdateAsync is the production fix that makes this path observable (was: always Result.Updated).
		CancellationToken ct = TestContext.Current.CancellationToken;
		Document document = new DocumentBuilder().AsPending().Build();

		Repository.Setup(r => r.GetByIdAsync(document.Id, ct)).ReturnsAsync(document);
		Repository.Setup(r => r.UpdateAsync(It.Is<Document>(d => d.Id == document.Id), ct)).ReturnsAsync(false);

		ErrorOr<Updated> result = await CreateSut().ProcessOcrResultAsync(document.Id, "Completed", OcrText, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
		result.FirstError.Code.Should().Be("Document.NotFound");
		ShouldHaveLog(LogLevel.Warning, "not found", document.Id.ToString());
	}

	// ── UpdateDocumentSummaryAsync ────────────────────────────────────────

	[Fact]
	public async Task UpdateDocumentSummaryAsync_RepositoryUpdatesOneRow_ForwardsExactPayloadAndReturnsUpdated()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Guid id = Guid.CreateVersion7();

		Repository.Setup(r => r.UpdateSummaryAsync(id, Summary, s_summaryGeneratedAt, ct)).ReturnsAsync(true);

		ErrorOr<Updated> result = await CreateSut().UpdateDocumentSummaryAsync(id, Summary, s_summaryGeneratedAt, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeFalse();
		result.Value.Should().Be(Result.Updated);
		ShouldHaveLog(LogLevel.Information, "GenAI summary", Summary.Length.ToString());
	}

	[Fact]
	public async Task UpdateDocumentSummaryAsync_RepositoryUpdatesNoRows_ReturnsNotFound()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Guid missingId = Guid.CreateVersion7();

		Repository.Setup(r => r.UpdateSummaryAsync(missingId, Summary, s_summaryGeneratedAt, ct)).ReturnsAsync(false);

		ErrorOr<Updated> result = await CreateSut().UpdateDocumentSummaryAsync(missingId, Summary, s_summaryGeneratedAt, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
		result.FirstError.Code.Should().Be("Document.NotFound");
		ShouldHaveLog(LogLevel.Warning, "not found", missingId.ToString());
	}

	// ── DeleteDocumentAsync ───────────────────────────────────────────────

	[Fact]
	public async Task DeleteDocumentAsync_DocumentExists_DeletesSearchStorageAndRepository()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Document document = new DocumentBuilder().Build();

		Repository.Setup(r => r.GetByIdAsync(document.Id, ct)).ReturnsAsync(document);
		Search.Setup(s => s.DeleteAsync(document.Id, ct)).Returns(Task.CompletedTask);
		Storage.Setup(s => s.DeleteAsync(document.StoragePath, ct)).Returns(Task.CompletedTask);
		Repository.Setup(r => r.DeleteAsync(document.Id, ct)).ReturnsAsync(true);

		ErrorOr<Deleted> result = await CreateSut().DeleteDocumentAsync(document.Id, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeFalse();
		result.Value.Should().Be(Result.Deleted);
		ShouldHaveLog(LogLevel.Information, "deleted successfully", document.Id.ToString());
	}

	public static IEnumerable<TheoryDataRow<Exception>> SearchDeletionFailures()
	{
		yield return new TheoryDataRow<Exception>(new InvalidOperationException("search down"))
			.WithTestDisplayName("unexpected search failure");
		yield return new TheoryDataRow<Exception>(new OperationCanceledException("search canceled"))
			.WithTestDisplayName("search cancellation");
	}

	[Theory]
	[MemberData(nameof(SearchDeletionFailures))]
	public async Task DeleteDocumentAsync_SearchFailure_PropagatesAndLeavesStorageAndRepositoryForRetry(
		Exception expected)
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Document document = new DocumentBuilder().Build();

		Repository.Setup(r => r.GetByIdAsync(document.Id, ct)).ReturnsAsync(document);
		Search.Setup(s => s.DeleteAsync(document.Id, ct)).ThrowsAsync(expected);

		Func<Task> act = () => CreateSut().DeleteDocumentAsync(document.Id, ct);

		Exception thrown = (await act.Should().ThrowAsync<Exception>()).Which;
		thrown.Should().BeSameAs(expected);
	}

	[Fact]
	public async Task DeleteDocumentAsync_StorageFailure_PropagatesAndLeavesRepositoryForRetry()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Document document = new DocumentBuilder().Build();
		InvalidOperationException expected = new("storage down");

		Repository.Setup(r => r.GetByIdAsync(document.Id, ct)).ReturnsAsync(document);
		Search.Setup(s => s.DeleteAsync(document.Id, ct)).Returns(Task.CompletedTask);
		Storage.Setup(s => s.DeleteAsync(document.StoragePath, ct)).ThrowsAsync(expected);

		Func<Task> act = () => CreateSut().DeleteDocumentAsync(document.Id, ct);

		InvalidOperationException thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
		thrown.Should().BeSameAs(expected);
	}

	[Fact]
	public async Task DeleteDocumentAsync_StorageCancellation_PropagatesCallerTokenAndLeavesRepositoryForRetry()
	{
		using CancellationTokenSource cancellation = new();
		await cancellation.CancelAsync();
		CancellationToken ct = cancellation.Token;
		Document document = new DocumentBuilder().Build();
		OperationCanceledException expected = new(ct);

		Repository.Setup(r => r.GetByIdAsync(document.Id, ct)).ReturnsAsync(document);
		Search.Setup(s => s.DeleteAsync(document.Id, ct)).Returns(Task.CompletedTask);
		Storage.Setup(s => s.DeleteAsync(document.StoragePath, ct)).ThrowsAsync(expected);

		Func<Task> act = () => CreateSut().DeleteDocumentAsync(document.Id, ct);

		OperationCanceledException thrown = (await act.Should().ThrowAsync<OperationCanceledException>()).Which;
		thrown.Should().BeSameAs(expected);
		thrown.CancellationToken.Should().Be(ct);
	}

	[Fact]
	public async Task DeleteDocumentAsync_MissingDocument_ReturnsNotFoundAndDoesNotTouchStorageOrSearch()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Guid missingId = Guid.CreateVersion7();

		Repository.Setup(r => r.GetByIdAsync(missingId, ct)).ReturnsAsync((Document?)null);

		ErrorOr<Deleted> result = await CreateSut().DeleteDocumentAsync(missingId, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
		result.FirstError.Code.Should().Be("Document.NotFound");
		// No Delete setup is intentional: strict mocks prove storage/search are untouched.
	}

	// ── GetDocumentByIdAsync ──────────────────────────────────────────────

	[Fact]
	public async Task GetDocumentByIdAsync_DocumentExists_ReturnsSameDocument()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Document document = new DocumentBuilder().Build();

		Repository.Setup(r => r.GetByIdAsync(document.Id, ct)).ReturnsAsync(document);

		ErrorOr<Document> result = await CreateSut().GetDocumentByIdAsync(document.Id, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeFalse();
		result.Value.Should().BeSameAs(document);
	}

	[Fact]
	public async Task GetDocumentByIdAsync_MissingDocument_ReturnsExactNotFoundError()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Guid missingId = Guid.CreateVersion7();

		Repository.Setup(r => r.GetByIdAsync(missingId, ct)).ReturnsAsync((Document?)null);

		ErrorOr<Document> result = await CreateSut().GetDocumentByIdAsync(missingId, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
		result.FirstError.Code.Should().Be("Document.NotFound");
		result.FirstError.Description.Should().Contain(missingId.ToString());
	}

	// ── GetDocumentsPagedAsync ────────────────────────────────────────────

	public static IEnumerable<TheoryDataRow<int, Guid?, bool>> PagingCases()
	{
		yield return new TheoryDataRow<int, Guid?, bool>(20, null, false)
			.WithTestDisplayName("first page, no cursor, no more pages");
		yield return new TheoryDataRow<int, Guid?, bool>(10, Guid.CreateVersion7(), true)
			.WithTestDisplayName("next page via cursor, more pages remain");
	}

	[Theory]
	[MemberData(nameof(PagingCases))]
	public async Task GetDocumentsPagedAsync_ForwardsExactPageSizeCursorAndToken_AndReturnsRepositoryResult(
		int pageSize, Guid? cursor, bool hasMore)
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		List<Document> expectedItems = [new DocumentBuilder().Build(), new DocumentBuilder().Build()];

		Repository.Setup(r => r.GetDocumentsPagedAsync(pageSize, cursor, ct)).ReturnsAsync((expectedItems, hasMore));

		(List<Document> items, bool more) = await CreateSut().GetDocumentsPagedAsync(pageSize, cursor, ct);

		using AssertionScope _ = new();
		items.Should().BeSameAs(expectedItems);
		more.Should().Be(hasMore);
	}

	// ── SearchDocumentsAsync ──────────────────────────────────────────────

	[Fact]
	public async Task SearchDocumentsAsync_ForwardsExactQueryLimitAndToken_AndReturnsResults()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		DocumentSearchResult[] expectedResults =
		[
			new()
			{
				Id = Guid.CreateVersion7(), FileName = "first.pdf",
				Status = DocumentStatus.Completed.ToString(), CreatedAt = s_uploadedAt, Content = "first"
			},
			new()
			{
				Id = Guid.CreateVersion7(), FileName = "second.pdf",
				Status = DocumentStatus.Completed.ToString(), CreatedAt = s_uploadedAt, Content = "second"
			}
		];

		Search.Setup(s => s.SearchAsync("invoice", 25, ct))
			.ReturnsAsync(expectedResults);

		IReadOnlyCollection<DocumentSearchResult> actual =
			await CreateSut().SearchDocumentsAsync("invoice", 25, ct);

		actual.Should().Equal(expectedResults);
	}

	private async Task<ErrorOr<Document>> UploadThroughMinioAsync(
		IMinioClient minio,
		CancellationToken cancellationToken)
	{
		DocumentStorageService storage = new(
			minio,
			Options.Create(new MinioOptions
			{
				Endpoint = new Uri("http://minio.test:9000"),
				AccessKey = "access-key",
				SecretKey = "secret-key",
				BucketName = "test-bucket"
			}),
			NullLogger<DocumentStorageService>.Instance);
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf()
			.WithFileName(FileName)
			.WithFileSize(FileSize)
			.Build();

		return await CreateSut(storage).UploadDocumentAsync(request, cancellationToken);
	}

	private void AssertRetriableStorageError(
		ErrorOr<Document> result,
		string expectedCode,
		string expectedDescription)
	{
		using AssertionScope _ = new();
		result.IsError.Should().BeTrue();
		((int)result.FirstError.Type).Should().Be(503);
		result.FirstError.Code.Should().Be(expectedCode);
		result.FirstError.Description.Should().Be(expectedDescription);
		result.FirstError.Description.Should().NotContain("documents/");
		result.FirstError.Metadata.Should().ContainKey("retryAfter").WhoseValue.Should().Be(30);
		ShouldHaveLog(LogLevel.Warning, "Storage error", expectedCode, "documents/2026-06/");
	}

	private static IMinioClient ConfigureMinioClient(
		MinioClient minio,
		HttpClient http,
		int requestTimeoutMilliseconds = 0)
	{
		IMinioClient configured = minio
			.WithEndpoint("minio.test", 9000)
			.WithCredentials("access-key", "secret-key")
			.WithRegion("us-east-1")
			.WithHttpClient(http)
			.Build();

		return requestTimeoutMilliseconds > 0
			? configured.WithTimeout(requestTimeoutMilliseconds)
			: configured;
	}

	private static string MinioErrorXml(string code) => $$"""
		<Error>
		  <Code>{{code}}</Code>
		  <Message>storage request failed</Message>
		  <Resource>/test-bucket/document.pdf</Resource>
		  <BucketName>test-bucket</BucketName>
		  <RequestId>test-request</RequestId>
		  <HostId>test-host</HostId>
		</Error>
		""";

	private sealed class StubHttpMessageHandler(
		Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) =>
			sendAsync(request, cancellationToken);
	}
}
