using System.Net.Sockets;
using AwesomeAssertions.Execution;

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
		string? routingKeyGivenToPublisher = null;
		OcrCommand? commandGivenToPublisher = null;

		Storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), FileSize, ct))
			.Callback<Stream, string, long, CancellationToken>((stream, path, length, token) =>
			{
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

		routingKeyGivenToPublisher.Should().NotBeNullOrWhiteSpace();
		commandGivenToPublisher.Should().BeEquivalentTo(
			new OcrCommand(saved.Id, FileName, expectedStoragePath, s_uploadedAt));

		ShouldHaveLog(LogLevel.Information, "uploaded successfully", saved.Id.ToString());
	}

	public static IEnumerable<TheoryDataRow<Exception, string>> KnownStorageFailures()
	{
		yield return new TheoryDataRow<Exception, string>(
				new TimeoutException("storage timed out"), "Document.StorageTimeout")
			.WithTestDisplayName("TimeoutException => StorageTimeout (503 + retryAfter)");

		yield return new TheoryDataRow<Exception, string>(
				new HttpRequestException("storage unavailable", null, HttpStatusCode.ServiceUnavailable),
				"Document.StorageServerError")
			.WithTestDisplayName("HttpRequestException 5xx => StorageServerError (503 + retryAfter)");

		yield return new TheoryDataRow<Exception, string>(
				new IOException("socket failed", new SocketException((int)SocketError.ConnectionRefused)),
				"Document.StorageConnectionFailed")
			.WithTestDisplayName("IOException(SocketException) => StorageConnectionFailed (503 + retryAfter)");
	}

	[Theory]
	[MemberData(nameof(KnownStorageFailures))]
	public async Task UploadDocumentAsync_KnownStorageFailure_ReturnsRetriableErrorAndDoesNotPersistOrPublish(
		Exception storageException, string expectedCode)
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf()
			.WithFileName(FileName)
			.WithFileSize(FileSize)
			.Build();

		Storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), FileSize, ct))
			.ThrowsAsync(storageException);

		ErrorOr<Document> result = await CreateSut().UploadDocumentAsync(request, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeTrue();
		((int)result.FirstError.Type).Should().Be(503);
		result.FirstError.Code.Should().Be(expectedCode);
		result.FirstError.Description.Should().Contain("documents/2026-06/");
		result.FirstError.Metadata.Should().ContainKey("retryAfter").WhoseValue.Should().Be(30);
		ShouldHaveLog(LogLevel.Warning, "Storage error", expectedCode);
		// No repository/publisher setup is intentional: strict mocks prove nothing was persisted or published.
	}

	[Fact]
	public async Task UploadDocumentAsync_UnknownStorageFailure_PropagatesOriginalAndDoesNotPersistOrPublish()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		UploadDocumentRequest request = UploadDocumentRequestBuilder.ValidPdf().WithFileName(FileName).Build();
		InvalidOperationException expected = new("bug outside the mapped storage failure set");

		Storage.Setup(s => s.UploadAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<long>(), ct))
			.ThrowsAsync(expected);

		Func<Task> act = () => CreateSut().UploadDocumentAsync(request, ct);

		InvalidOperationException thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
		thrown.Should().BeSameAs(expected);
		// No repository/publisher setup is intentional: strict mocks prove nothing was persisted or published.
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
	public async Task DeleteDocumentAsync_DocumentExists_DeletesRepositoryAndStorageThenTreatsSearchDeleteAsBestEffort()
	{
		CancellationToken ct = TestContext.Current.CancellationToken;
		Document document = new DocumentBuilder().Build();

		Repository.Setup(r => r.GetByIdAsync(document.Id, ct)).ReturnsAsync(document);
		Repository.Setup(r => r.DeleteAsync(document.Id, ct)).ReturnsAsync(true);
		Storage.Setup(s => s.DeleteAsync(document.StoragePath, ct)).ReturnsAsync(true);
		Search.Setup(s => s.DeleteAsync(document.Id, ct)).ThrowsAsync(new InvalidOperationException("search down"));

		ErrorOr<Deleted> result = await CreateSut().DeleteDocumentAsync(document.Id, ct);

		using AssertionScope _ = new();
		result.IsError.Should().BeFalse();
		result.Value.Should().Be(Result.Deleted);
		ShouldHaveLog(LogLevel.Warning, "search index", document.Id.ToString());
		ShouldHaveLog(LogLevel.Information, "deleted successfully", document.Id.ToString());
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
	public async Task SearchDocumentsAsync_ForwardsExactQueryLimitAndToken_AndStreamsResults()
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

		Search.Setup(s => s.SearchAsync<DocumentSearchResult>("invoice", 25, ct))
			.Returns(expectedResults.ToAsyncEnumerable());

		List<DocumentSearchResult> actual = [];
		await foreach (DocumentSearchResult result in CreateSut().SearchDocumentsAsync("invoice", 25, ct))
		{
			actual.Add(result);
		}

		actual.Should().Equal(expectedResults);
	}
}
