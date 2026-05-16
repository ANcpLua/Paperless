namespace PaperlessREST.Tests.Unit;

public sealed class DocumentSearchServiceTests : IDisposable
{
	private const string QueryInvoice = "invoice";
	private const string QueryNonexistent = "nonexistent";
	private const int LimitTen = 10;
	private const int LimitFive = 5;
	private const int ExpectedCountTwo = 2;

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IDocumentSearchService> _searchService;

	public DocumentSearchServiceTests()
	{
		_searchService = _mocks.Create<IDocumentSearchService>();
	}

	public void Dispose()
	{
		_mocks.VerifyAll();
		_mocks.VerifyNoOtherCalls();
	}

	[Fact]
	public async Task SearchAsync_ReturnsExpectedDocuments()
	{
		// Arrange
		Document doc1 = new DocumentBuilder().Build();
		Document doc2 = new DocumentBuilder().Build();

		_searchService
			.Setup(s => s.SearchAsync<Document>(QueryInvoice, LimitTen, TestContext.Current.CancellationToken))
			.Returns(new[] { doc1, doc2 }.ToAsyncEnumerable());

		// Act
		List<Document> results = [];
		await foreach (Document doc in _searchService.Object.SearchAsync<Document>(QueryInvoice, LimitTen,
			               TestContext.Current.CancellationToken))
		{
			results.Add(doc);
		}

		// Assert
		results.Should().HaveCount(ExpectedCountTwo);
		results[0].Should().BeSameAs(doc1);
		results[1].Should().BeSameAs(doc2);
	}

	[Fact]
	public async Task SearchAsync_WithEmptyResults_ReturnsEmptySequence()
	{
		// Arrange
		_searchService.Setup(s =>
				s.SearchAsync<Document>(QueryNonexistent, LimitFive, TestContext.Current.CancellationToken))
			.Returns(Array.Empty<Document>().ToAsyncEnumerable());

		// Act
		List<Document> results = [];
		await foreach (Document doc in _searchService.Object.SearchAsync<Document>(QueryNonexistent, LimitFive,
			               TestContext.Current.CancellationToken))
		{
			results.Add(doc);
		}

		// Assert
		results.Should().BeEmpty();
	}

	[Fact]
	public async Task DeleteAsync_ReturnsTrue_WhenSuccessful()
	{
		// Arrange
		Guid id = Guid.CreateVersion7();
		_searchService.Setup(s => s.DeleteAsync(id, TestContext.Current.CancellationToken)).ReturnsAsync(true);

		// Act
		bool result = await _searchService.Object.DeleteAsync(id, TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeTrue();
	}

	[Fact]
	public async Task DeleteAsync_ReturnsFalse_WhenUnsuccessful()
	{
		// Arrange
		Guid id = Guid.CreateVersion7();
		_searchService.Setup(s => s.DeleteAsync(id, TestContext.Current.CancellationToken)).ReturnsAsync(false);

		// Act
		bool result = await _searchService.Object.DeleteAsync(id, TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeFalse();
	}
}
