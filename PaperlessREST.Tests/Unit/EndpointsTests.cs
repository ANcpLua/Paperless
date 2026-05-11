namespace PaperlessREST.Tests.Unit;

public sealed class DocumentEndpointsTests : IDisposable
{
	private const string FileA = "a.pdf";
	private const string FileB = "b.pdf";
	private const string UploadFileName = "upload.pdf";
	private const string TestSummary = "This is a test summary of the document content.";
	private const string StatusCompleted = "Completed";
	private const string StatusPending = "Pending";
	private const string ContentA = "Content A";
	private const string TestQuery = "test";
	private const int TestLimit = 2;
	private const int ExpectedCount = 2;

	private readonly Mock<IDocumentService> _service =
		new Mock<IDocumentService>(MockBehavior.Strict).SetupAllProperties();

	public void Dispose()
	{
		_service.VerifyAll();
		_service.VerifyNoOtherCalls();
	}

	[Fact]
	public async Task GetDocumentById_CompletedDocument_ReturnsExpectedDto()
	{
		// Arrange
		Document doc = PairA.Doc;
		DocumentDto expectedDto = PairA.Dto;
		_service.Setup(s => s.GetDocumentByIdAsync(doc.Id, TestContext.Current.CancellationToken)).ReturnsAsync(doc);

		// Act
		Results<Ok<DocumentDto>, NotFound> result =
			await DocumentEndpoints.GetDocumentById(doc.Id, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeOfType<Results<Ok<DocumentDto>, NotFound>>();
		Ok<DocumentDto>? ok = result.Result.Should().BeOfType<Ok<DocumentDto>>().Subject;
		ok.Value.Should().NotBeNull();
		ok.Value!.Should().BeEquivalentTo(expectedDto, opts => opts.WithoutStrictOrdering());
	}

	[Fact]
	public async Task GetDocumentById_PendingDocument_ReturnsExpectedDto()
	{
		// Arrange
		Document doc = PairB.Doc;
		DocumentDto expectedDto = PairB.Dto;
		_service.Setup(s => s.GetDocumentByIdAsync(doc.Id, TestContext.Current.CancellationToken)).ReturnsAsync(doc);

		// Act
		Results<Ok<DocumentDto>, NotFound> result =
			await DocumentEndpoints.GetDocumentById(doc.Id, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeOfType<Results<Ok<DocumentDto>, NotFound>>();
		Ok<DocumentDto>? ok = result.Result.Should().BeOfType<Ok<DocumentDto>>().Subject;
		ok.Value.Should().NotBeNull();
		ok.Value!.Should().BeEquivalentTo(expectedDto, opts => opts.WithoutStrictOrdering());
	}

	[Fact]
	public async Task GetDocumentById_WhenMissing_ReturnsNotFound()
	{
		// Arrange
		Guid missingId = Guid.CreateVersion7();
		_service.Setup(s => s.GetDocumentByIdAsync(missingId, TestContext.Current.CancellationToken))
			.ReturnsAsync(DocumentErrors.NotFound(missingId));

		// Act
		Results<Ok<DocumentDto>, NotFound> result =
			await DocumentEndpoints.GetDocumentById(missingId, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		result.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public async Task GetDocuments_WhenCalled_ReturnsPaginatedResponse()
	{
		// Arrange
		List<Document> docs = [PairA.Doc, PairB.Doc];
		PaginationQuery pagination = new() { PageSize = 20 };
		_service.Setup(s => s.GetDocumentsPagedAsync(20, null, TestContext.Current.CancellationToken))
			.ReturnsAsync((docs, false));

		// Act
		Ok<PaginatedDocumentsResponse> response =
			await DocumentEndpoints.GetDocuments(pagination, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		response.Should().BeOfType<Ok<PaginatedDocumentsResponse>>();
		response.Value.Should().NotBeNull();
		response.Value!.Items.Should().HaveCount(ExpectedCount).And.SatisfyRespectively(
			first => first.FileName.Should().Be(FileA),
			second => second.FileName.Should().Be(FileB)
		);
		response.Value!.HasMore.Should().BeFalse();
		response.Value!.NextCursor.Should().BeNull();
	}

	[Fact]
	public async Task GetDocuments_WithMorePages_ReturnsNextCursor()
	{
		// Arrange
		List<Document> docs = [PairA.Doc, PairB.Doc];
		PaginationQuery pagination = new() { PageSize = 2 };
		_service.Setup(s => s.GetDocumentsPagedAsync(2, null, TestContext.Current.CancellationToken))
			.ReturnsAsync((docs, true));

		// Act
		Ok<PaginatedDocumentsResponse> response =
			await DocumentEndpoints.GetDocuments(pagination, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		response.Value!.HasMore.Should().BeTrue();
		response.Value!.NextCursor.Should().Be(PairB.Doc.Id);
	}

	[Fact]
	public async Task GetDocuments_WithCursor_PassesCursorToService()
	{
		// Arrange
		Guid cursor = Guid.CreateVersion7();
		List<Document> docs = [PairA.Doc];
		PaginationQuery pagination = new() { PageSize = 10, Cursor = cursor };
		_service.Setup(s => s.GetDocumentsPagedAsync(10, cursor, TestContext.Current.CancellationToken))
			.ReturnsAsync((docs, false));

		// Act
		Ok<PaginatedDocumentsResponse> response =
			await DocumentEndpoints.GetDocuments(pagination, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		response.Value!.Items.Should().HaveCount(1);
		_service.Verify(s => s.GetDocumentsPagedAsync(10, cursor, TestContext.Current.CancellationToken), Times.Once);
	}

	[Fact]
	public async Task SearchDocuments_WhenMatchesFound_ReturnsOkWithResults()
	{
		// Arrange
		SearchQuery query = new SearchQueryBuilder().WithQuery(TestQuery).WithLimit(TestLimit).Build();

		DocumentSearchResult[] hits =
		[
			new()
			{
				Id = PairA.Doc.Id,
				FileName = PairA.Doc.FileName,
				Status = StatusCompleted,
				CreatedAt = DateTimeOffset.UtcNow,
				Content = ContentA
			},
			new()
			{
				Id = PairB.Doc.Id,
				FileName = PairB.Doc.FileName,
				Status = StatusPending,
				CreatedAt = DateTimeOffset.UtcNow
			}
		];
		_service.Setup(s => s.SearchDocumentsAsync(query.Query, query.Limit, TestContext.Current.CancellationToken))
			.Returns(ToAsyncEnumerable(hits));

		// Act
		Ok<List<DocumentSearchResultDto>> response =
			await DocumentEndpoints.SearchDocuments(query, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		response.Should().BeOfType<Ok<List<DocumentSearchResultDto>>>();
		response.Value.Should().NotBeNull();
		response.Value!.Should().HaveCount(ExpectedCount);
	}

	[Fact]
	public async Task UploadDocument_WhenRequestValid_ReturnsAcceptedAtRoute()
	{
		// Arrange
		UploadDocumentRequest req = UploadDocumentRequestBuilder.ValidPdf().Build();
		Document doc = new DocumentBuilder().WithFileName(UploadFileName).Build();
		_service.Setup(s => s.UploadDocumentAsync(req, TestContext.Current.CancellationToken))
			.ReturnsAsync((ErrorOr<Document>)doc);

		// Act
		Results<AcceptedAtRoute<CreateDocumentResponse>, ValidationProblem, ProblemHttpResult> result =
			await DocumentEndpoints.UploadDocument(req, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		AcceptedAtRoute<CreateDocumentResponse> response = result.Result.Should()
			.BeOfType<AcceptedAtRoute<CreateDocumentResponse>>().Subject;
		response.RouteName.Should().Be(nameof(DocumentEndpoints.GetDocumentById));
		response.RouteValues["id"].Should().Be(doc.Id);
		response.Value.Should().NotBeNull();
		response.Value!.Id.Should().Be(doc.Id);
	}

	[Fact]
	public async Task DeleteDocument_WhenExists_ReturnsNoContent()
	{
		// Arrange
		Guid id = Guid.CreateVersion7();
		_service.Setup(s => s.DeleteDocumentAsync(id, TestContext.Current.CancellationToken))
			.ReturnsAsync(Result.Deleted);

		// Act
		Results<NoContent, NotFound> response =
			await DocumentEndpoints.DeleteDocument(id, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		response.Result.Should().BeOfType<NoContent>();
	}

	[Fact]
	public async Task DeleteDocument_WhenNotFound_ReturnsNotFound()
	{
		// Arrange
		Guid id = Guid.CreateVersion7();
		_service.Setup(s => s.DeleteDocumentAsync(id, TestContext.Current.CancellationToken))
			.ReturnsAsync(DocumentErrors.NotFound(id));

		// Act
		Results<NoContent, NotFound> response =
			await DocumentEndpoints.DeleteDocument(id, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		response.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public async Task GetSummary_WhenDocumentExists_ReturnsOkWithSummary()
	{
		// Arrange
		Document doc = new DocumentBuilder()
			.WithSummary(TestSummary)
			.Build();

		_service.Setup(s => s.GetDocumentByIdAsync(doc.Id, TestContext.Current.CancellationToken))
			.ReturnsAsync(doc);

		// Act
		Results<Ok<SummaryDto>, NotFound> response =
			await DocumentEndpoints.GetSummary(doc.Id, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		response.Should().BeOfType<Results<Ok<SummaryDto>, NotFound>>();
		Ok<SummaryDto>? ok = response.Result.Should().BeOfType<Ok<SummaryDto>>().Subject;
		ok.Value.Should().NotBeNull();
		ok.Value!.Summary.Should().Be(TestSummary);
	}

	[Fact]
	public async Task GetSummary_WhenDocumentNotFound_ReturnsNotFound()
	{
		// Arrange
		Guid missingId = Guid.CreateVersion7();
		_service.Setup(s => s.GetDocumentByIdAsync(missingId, TestContext.Current.CancellationToken))
			.ReturnsAsync(DocumentErrors.NotFound(missingId));

		// Act
		Results<Ok<SummaryDto>, NotFound> response =
			await DocumentEndpoints.GetSummary(missingId, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		response.Result.Should().BeOfType<NotFound>();
	}

	[Fact]
	public async Task GetSummary_WhenDocumentExistsWithNullSummary_ReturnsOkWithNullSummary()
	{
		// Arrange
		Document doc = new DocumentBuilder()
			.WithSummary(null)
			.Build();

		_service.Setup(s => s.GetDocumentByIdAsync(doc.Id, TestContext.Current.CancellationToken))
			.ReturnsAsync(doc);

		// Act
		Results<Ok<SummaryDto>, NotFound> response =
			await DocumentEndpoints.GetSummary(doc.Id, _service.Object, TestContext.Current.CancellationToken);

		// Assert
		response.Should().BeOfType<Results<Ok<SummaryDto>, NotFound>>();
		Ok<SummaryDto>? ok = response.Result.Should().BeOfType<Ok<SummaryDto>>().Subject;
		ok.Value.Should().NotBeNull();
		ok.Value!.Summary.Should().BeNull();
	}

	private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> source)
	{
		foreach (T item in source)
		{
			yield return item;
			await Task.Yield();
		}
	}

	private static class PairA
	{
		private static readonly DocumentBuilder Builder = new DocumentBuilder()
			.WithFileName(FileA)
			.WithStatus(DocumentStatus.Completed)
			.WithContent(ContentA);

		public static readonly Document Doc = Builder.Build();
		public static readonly DocumentDto Dto = Builder.BuildDto();
	}

	private static class PairB
	{
		private static readonly DocumentBuilder Builder = new DocumentBuilder()
			.WithFileName(FileB)
			.WithStatus(DocumentStatus.Pending);

		public static readonly Document Doc = Builder.Build();
		public static readonly DocumentDto Dto = Builder.BuildDto();
	}
}
