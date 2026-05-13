namespace PaperlessREST.Tests.Integration;

/// <summary>
///     Integration tests for DocumentRepository using real PostgreSQL via Testcontainers.
///     Tests actual database behavior including enum mapping and transactions.
/// </summary>
public sealed class DocumentRepositoryIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
	#region Constructor

	public DocumentRepositoryIntegrationTests(DatabaseFixture fixture)
	{
		_fixture = fixture;
	}

	#endregion

	#region Constants

	private const string TestFilePrefix = "repo-test";
	private const string OcrContent = "OCR extracted content";
	private const string AiSummary = "AI generated summary";
	private const string StoragePath = "documents/2025-01/test-file.pdf";

	#endregion

	#region Fields

	private readonly DatabaseFixture _fixture;
	private AsyncServiceScope _scope;
	private IDocumentRepository? _repository;
	private IDocumentRepository Repository => _repository ?? throw new InvalidOperationException("Repository not initialized.");

	#endregion

	#region IAsyncLifetime

	public ValueTask InitializeAsync()
	{
		_scope = _fixture.CreateAsyncScope();
		_repository = _scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync() => _scope.DisposeAsync();

	#endregion

	#region Tests - AddAsync

	[Fact]
	public async Task AddAsync_ValidDocument_PersistsToDatabase()
	{
		// Arrange
		Document document = new DocumentBuilder()
			.WithFileName($"{TestFilePrefix}-add-{Guid.NewGuid():N}.pdf")
			.Build();

		// Act
		Document saved = await Repository.AddAsync(document, TestContext.Current.CancellationToken);

		// Assert
		saved.Id.Should().Be(document.Id);
		saved.FileName.Should().Be(document.FileName);
		saved.Status.Should().Be(DocumentStatus.Pending);

		// Verify in database directly
		await using DocumentPersistence db = await _fixture.ContextFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);
		DocumentEntity? entity = await db.Documents.FindAsync(
			[saved.Id],
			TestContext.Current.CancellationToken);

		entity.Should().NotBeNull();
		entity!.FileName.Should().Be(document.FileName);
		entity.Status.Should().Be(DocumentStatus.Pending);
	}

	[Fact]
	public async Task AddAsync_SetsStoragePath()
	{
		// Arrange
		Document document = new DocumentBuilder()
			.WithStoragePath(StoragePath)
			.Build();

		// Act
		Document saved = await Repository.AddAsync(document, TestContext.Current.CancellationToken);

		// Assert
		saved.StoragePath.Should().Be(StoragePath);

		await using DocumentPersistence db = await _fixture.ContextFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);
		DocumentEntity? entity = await db.Documents.FindAsync(
			[saved.Id],
			TestContext.Current.CancellationToken);
		entity!.StoragePath.Should().Be(StoragePath);
	}

	#endregion

	#region Tests - GetByIdAsync

	[Fact]
	public async Task GetByIdAsync_ExistingDocument_ReturnsWithAllProperties()
	{
		// Arrange
		Document original = new DocumentBuilder()
			.WithFileName($"{TestFilePrefix}-getbyid-{Guid.NewGuid():N}.pdf")
			.WithContent(OcrContent)
			.WithSummary(AiSummary)
			.AsCompleted(OcrContent)
			.Build();
		await Repository.AddAsync(original, TestContext.Current.CancellationToken);

		// Act
		Document? found = await Repository.GetByIdAsync(original.Id, TestContext.Current.CancellationToken);

		// Assert
		found.Should().NotBeNull();
		found!.Id.Should().Be(original.Id);
		found.Status.Should().Be(DocumentStatus.Completed);
		found.Content.Should().Be(OcrContent);
	}

	[Fact]
	public async Task GetByIdAsync_NonExistent_ReturnsNull()
	{
		// Arrange
		Guid nonExistentId = Guid.CreateVersion7();

		// Act
		Document? result = await Repository.GetByIdAsync(nonExistentId, TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeNull();
	}

	#endregion

	#region Tests - UpdateAsync

	[Fact]
	public async Task UpdateAsync_MarkAsCompleted_UpdatesStatusAndContent()
	{
		// Arrange
		Document original = new DocumentBuilder()
			.WithFileName($"{TestFilePrefix}-update-{Guid.NewGuid():N}.pdf")
			.Build();
		Document saved = await Repository.AddAsync(original, TestContext.Current.CancellationToken);

		saved.MarkAsCompleted(OcrContent, TimeProvider.System);

		// Act
		bool updated = await Repository.UpdateAsync(saved, TestContext.Current.CancellationToken);

		// Assert
		updated.Should().BeTrue();

		// Verify in database
		await using DocumentPersistence db = await _fixture.ContextFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);
		DocumentEntity entity = await db.Documents.FirstAsync(
			d => d.Id == saved.Id,
			TestContext.Current.CancellationToken);
		entity.Status.Should().Be(DocumentStatus.Completed);
		entity.Content.Should().Be(OcrContent);
		entity.ProcessedAt.Should().NotBeNull();
	}

	[Fact]
	public async Task UpdateAsync_MarkAsFailed_UpdatesStatus()
	{
		// Arrange
		Document original = new DocumentBuilder()
			.WithFileName($"{TestFilePrefix}-fail-{Guid.NewGuid():N}.pdf")
			.Build();
		Document saved = await Repository.AddAsync(original, TestContext.Current.CancellationToken);

		saved.MarkAsFailed(TimeProvider.System);

		// Act
		bool updated = await Repository.UpdateAsync(saved, TestContext.Current.CancellationToken);

		// Assert
		updated.Should().BeTrue();

		// Verify in database
		await using DocumentPersistence db = await _fixture.ContextFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);
		DocumentEntity entity = await db.Documents.FirstAsync(
			d => d.Id == saved.Id,
			TestContext.Current.CancellationToken);
		entity.Status.Should().Be(DocumentStatus.Failed);
		entity.ProcessedAt.Should().NotBeNull();
		entity.Content.Should().BeNull();
	}

	[Fact]
	public async Task UpdateAsync_NonExistent_ReturnsFalse()
	{
		// Arrange
		Document document = new DocumentBuilder()
			.WithFileName($"{TestFilePrefix}-ghost-{Guid.NewGuid():N}.pdf")
			.Build();

		// Act
		bool result = await Repository.UpdateAsync(document, TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeFalse();
	}

	#endregion

	#region Tests - DeleteAsync

	[Fact]
	public async Task DeleteAsync_ExistingDocument_RemovesFromDatabase()
	{
		// Arrange
		Document document = new DocumentBuilder()
			.WithFileName($"{TestFilePrefix}-delete-{Guid.NewGuid():N}.pdf")
			.Build();
		await Repository.AddAsync(document, TestContext.Current.CancellationToken);

		// Act
		bool deleted = await Repository.DeleteAsync(document.Id, TestContext.Current.CancellationToken);

		// Assert
		deleted.Should().BeTrue();

		Document? found = await Repository.GetByIdAsync(document.Id, TestContext.Current.CancellationToken);
		found.Should().BeNull();
	}

	[Fact]
	public async Task DeleteAsync_NonExistent_ReturnsFalse()
	{
		// Arrange
		Guid nonExistentId = Guid.CreateVersion7();

		// Act
		bool deleted = await Repository.DeleteAsync(nonExistentId, TestContext.Current.CancellationToken);

		// Assert
		deleted.Should().BeFalse();
	}

	#endregion

	#region Tests - UpdateSummaryAsync

	[Fact]
	public async Task UpdateSummaryAsync_ExistingDocument_UpdatesOnlySummaryFields()
	{
		// Arrange
		Document document = new DocumentBuilder()
			.WithFileName($"{TestFilePrefix}-summary-{Guid.NewGuid():N}.pdf")
			.AsCompleted(OcrContent)
			.Build();
		await Repository.AddAsync(document, TestContext.Current.CancellationToken);

		DateTimeOffset generatedAt = TimeProvider.System.GetUtcNow();

		// Act
		bool updated = await Repository.UpdateSummaryAsync(
			document.Id,
			AiSummary,
			generatedAt,
			TestContext.Current.CancellationToken);

		// Assert
		updated.Should().BeTrue();

		Document? found = await Repository.GetByIdAsync(document.Id, TestContext.Current.CancellationToken);
		found.Should().NotBeNull();
		found!.Summary.Should().Be(AiSummary);
		found.SummaryGeneratedAt.Should().BeCloseTo(generatedAt, TimeSpan.FromSeconds(1));
		// Original fields unchanged
		found.Status.Should().Be(DocumentStatus.Completed);
		found.Content.Should().Be(OcrContent);
	}

	[Fact]
	public async Task UpdateSummaryAsync_NonExistent_ReturnsFalse()
	{
		// Arrange
		Guid nonExistentId = Guid.CreateVersion7();

		// Act
		bool updated = await Repository.UpdateSummaryAsync(
			nonExistentId,
			AiSummary,
			TimeProvider.System.GetUtcNow(),
			TestContext.Current.CancellationToken);

		// Assert
		updated.Should().BeFalse();
	}

	#endregion

	#region Tests - GetDocumentsPagedAsync

	[Fact]
	public async Task GetDocumentsPagedAsync_ReturnsNewestFirst()
	{
		// Arrange - Use GUIDv7 which is time-ordered
		string testPrefix = $"{TestFilePrefix}-paged-{Guid.NewGuid():N}";

		// Add documents with slight delays to ensure distinct GUIDv7s
		Document oldest = new DocumentBuilder().WithFileName($"{testPrefix}-old.pdf").Build();
		await Repository.AddAsync(oldest, TestContext.Current.CancellationToken);
		await Task.Delay(10, TestContext.Current.CancellationToken);

		Document middle = new DocumentBuilder().WithFileName($"{testPrefix}-mid.pdf").Build();
		await Repository.AddAsync(middle, TestContext.Current.CancellationToken);
		await Task.Delay(10, TestContext.Current.CancellationToken);

		Document newest = new DocumentBuilder().WithFileName($"{testPrefix}-new.pdf").Build();
		await Repository.AddAsync(newest, TestContext.Current.CancellationToken);

		// Act
		(List<Document> results, bool hasMore) = await Repository
			.GetDocumentsPagedAsync(50, null, TestContext.Current.CancellationToken);

		List<Document> filtered = results.Where(d => d.FileName.StartsWith(testPrefix, StringComparison.Ordinal)).ToList();

		// Assert - GUIDv7 ordering means newest first (highest GUID value)
		filtered.Should().HaveCount(3);
		filtered[0].FileName.Should().Contain("new");
		filtered[1].FileName.Should().Contain("mid");
		filtered[2].FileName.Should().Contain("old");
	}

	[Fact]
	public async Task GetDocumentsPagedAsync_RespectsPageSize()
	{
		// Arrange
		string testPrefix = $"{TestFilePrefix}-pagesize-{Guid.NewGuid():N}";

		for (int i = 0; i < 5; i++)
		{
			await Repository.AddAsync(
				new DocumentBuilder().WithFileName($"{testPrefix}-{i}.pdf").Build(),
				TestContext.Current.CancellationToken);
			await Task.Delay(5, TestContext.Current.CancellationToken); // Ensure distinct GUIDv7s
		}

		// Act
		(List<Document> results, bool hasMore) = await Repository
			.GetDocumentsPagedAsync(3, null, TestContext.Current.CancellationToken);

		// Assert
		results.Should().HaveCount(3);
		hasMore.Should().BeTrue();
	}

	[Fact]
	public async Task GetDocumentsPagedAsync_WithCursor_ReturnsNextPage()
	{
		// Arrange
		string testPrefix = $"{TestFilePrefix}-cursor-{Guid.NewGuid():N}";
		List<Document> addedDocs = [];

		for (int i = 0; i < 5; i++)
		{
			Document doc = new DocumentBuilder().WithFileName($"{testPrefix}-{i}.pdf").Build();
			Document added = await Repository.AddAsync(doc, TestContext.Current.CancellationToken);
			addedDocs.Add(added);
			await Task.Delay(5, TestContext.Current.CancellationToken);
		}

		// Act - Get first page
		(List<Document> firstPage, bool hasMoreFirst) = await Repository
			.GetDocumentsPagedAsync(2, null, TestContext.Current.CancellationToken);

		// Get second page using cursor from first page
		Guid cursor = firstPage[^1].Id;
		(List<Document> secondPage, bool hasMoreSecond) = await Repository
			.GetDocumentsPagedAsync(2, cursor, TestContext.Current.CancellationToken);

		// Assert
		firstPage.Should().HaveCount(2);
		hasMoreFirst.Should().BeTrue();
		secondPage.Should().HaveCount(2);
		// Verify no overlap between pages
		firstPage.Select(d => d.Id).Should().NotIntersectWith(secondPage.Select(d => d.Id));
	}

	[Fact]
	public async Task GetDocumentsPagedAsync_LastPage_HasMoreIsFalse()
	{
		// Arrange
		string testPrefix = $"{TestFilePrefix}-lastpage-{Guid.NewGuid():N}";

		for (int i = 0; i < 3; i++)
		{
			await Repository.AddAsync(
				new DocumentBuilder().WithFileName($"{testPrefix}-{i}.pdf").Build(),
				TestContext.Current.CancellationToken);
			await Task.Delay(5, TestContext.Current.CancellationToken);
		}

		// Act - Request more than available
		(List<Document> results, bool hasMore) = await Repository
			.GetDocumentsPagedAsync(100, null, TestContext.Current.CancellationToken);

		// Assert
		results.Should().HaveCountGreaterThanOrEqualTo(3);
		hasMore.Should().BeFalse();
	}

	#endregion
}
