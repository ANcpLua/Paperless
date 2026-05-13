namespace PaperlessREST.Tests.Integration;

/// <summary>
///     Integration tests for DocumentAccessRepository verifying EF Core SQL translation
///     and correct query behavior against real PostgreSQL.
/// </summary>
public sealed class DocumentAccessRepositoryIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
	#region Constants

	private const string TestFilePrefix = "access-repo-test";

	#endregion

	#region Constructor

	public DocumentAccessRepositoryIntegrationTests(DatabaseFixture fixture)
	{
		_fixture = fixture;
	}

	#endregion

	#region Tests - GetExistingDocumentIdsAsync SQL Translation

	[Fact]
	public async Task GetExistingDocumentIdsAsync_TranslatesToServerSideQuery()
	{
		// Arrange
		await using DocumentPersistence db = await _fixture.ContextFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);
		Guid[] documentIds = [Guid.CreateVersion7(), Guid.CreateVersion7()];

		// Act - capture the query that EF Core generates
		IQueryable<Guid> query = db.Documents
			.Where(d => documentIds.Contains(d.Id))
			.Select(d => d.Id);

		string sql = query.ToQueryString();

		// Assert - Npgsql translates Contains to ANY operator for server-side evaluation
		// If this fails, it means EF Core is using client-side evaluation
		sql.Should().Contain("ANY", "EF Core should translate Contains to PostgreSQL ANY operator");
	}

	#endregion

	#region Helper Methods

	private async Task<Guid> SeedDocumentAsync(string fileName)
	{
		await using DocumentPersistence db = await _fixture.ContextFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);

		DocumentEntity entity = new DocumentBuilder()
			.WithFileName(fileName)
			.BuildEntity();

		db.Documents.Add(entity);
		await db.SaveChangesAsync(TestContext.Current.CancellationToken);

		_createdDocIds.Add(entity.Id);
		return entity.Id;
	}

	#endregion

	#region Fields

	private readonly DatabaseFixture _fixture;
	private readonly List<Guid> _createdDocIds = [];
	private AsyncServiceScope _scope;
	private IDocumentAccessRepository? _repository;
	private IDocumentAccessRepository Repository => _repository ?? throw new InvalidOperationException("Repository not initialized.");

	#endregion

	#region IAsyncLifetime

	public ValueTask InitializeAsync()
	{
		_scope = _fixture.CreateAsyncScope();
		_repository = _scope.ServiceProvider.GetRequiredService<IDocumentAccessRepository>();
		return ValueTask.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		await _scope.DisposeAsync();

		if (_createdDocIds.Count > 0)
		{
			await using DocumentPersistence db = await _fixture.ContextFactory.CreateDbContextAsync();
			await db.Documents.Where(d => _createdDocIds.Contains(d.Id)).ExecuteDeleteAsync();
		}
	}

	private async Task<DailyDocumentAccess?> GetDailyAccessAsync(Guid documentId, DateOnly date)
	{
		await using DocumentPersistence db = await _fixture.ContextFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);

		return await db.DailyDocumentAccesses
			.FirstOrDefaultAsync(x => x.DocumentId == documentId && x.LogDate == date,
				TestContext.Current.CancellationToken);
	}

	private async Task CleanupDailyAccessAsync(Guid documentId)
	{
		await using DocumentPersistence db = await _fixture.ContextFactory.CreateDbContextAsync();
		await db.DailyDocumentAccesses.Where(x => x.DocumentId == documentId).ExecuteDeleteAsync();
	}

	#endregion

	#region Tests - GetExistingDocumentIdsAsync Functional Behavior

	[Fact]
	public async Task GetExistingDocumentIdsAsync_ReturnsOnlyExistingIds()
	{
		// Arrange
		Guid existingId = await SeedDocumentAsync($"{TestFilePrefix}-exists-{Guid.NewGuid():N}.pdf");
		Guid nonExistingId = Guid.CreateVersion7();

		// Act
		Guid[] result = await Repository.GetExistingDocumentIdsAsync(
			[existingId, nonExistingId],
			TestContext.Current.CancellationToken);

		// Assert
		result.Should().ContainSingle().Which.Should().Be(existingId);
	}

	[Fact]
	public async Task GetExistingDocumentIdsAsync_EmptyInput_ReturnsEmptyArray()
	{
		// Arrange
		Guid[] emptyIds = [];

		// Act
		Guid[] result = await Repository.GetExistingDocumentIdsAsync(
			emptyIds,
			TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeEmpty();
	}

	[Fact]
	public async Task GetExistingDocumentIdsAsync_AllIdsExist_ReturnsAll()
	{
		// Arrange
		Guid id1 = await SeedDocumentAsync($"{TestFilePrefix}-all1-{Guid.NewGuid():N}.pdf");
		Guid id2 = await SeedDocumentAsync($"{TestFilePrefix}-all2-{Guid.NewGuid():N}.pdf");
		Guid id3 = await SeedDocumentAsync($"{TestFilePrefix}-all3-{Guid.NewGuid():N}.pdf");

		// Act
		Guid[] result = await Repository.GetExistingDocumentIdsAsync(
			[id1, id2, id3],
			TestContext.Current.CancellationToken);

		// Assert
		result.Should().HaveCount(3);
		result.Should().Contain(id1);
		result.Should().Contain(id2);
		result.Should().Contain(id3);
	}

	[Fact]
	public async Task GetExistingDocumentIdsAsync_NoIdsExist_ReturnsEmptyArray()
	{
		// Arrange
		Guid nonExisting1 = Guid.CreateVersion7();
		Guid nonExisting2 = Guid.CreateVersion7();

		// Act
		Guid[] result = await Repository.GetExistingDocumentIdsAsync(
			[nonExisting1, nonExisting2],
			TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeEmpty();
	}

	#endregion

	#region Tests - UpsertDailyAccessAsync

	[Fact]
	public async Task UpsertDailyAccessAsync_EmptyItems_DoesNothing()
	{
		// Arrange
		DateOnly date = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
		(Guid DocumentId, long AccessCount)[] emptyItems = [];

		// Act - should return without error
		await Repository.UpsertDailyAccessAsync(date, emptyItems, TestContext.Current.CancellationToken);

		// Assert - no exception thrown, method returns early
	}

	[Fact]
	public async Task UpsertDailyAccessAsync_InsertsNewRecord()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-upsert-insert-{Guid.NewGuid():N}.pdf");
		DateOnly date = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
		const long AccessCount = 42;

		try
		{
			// Act
			await Repository.UpsertDailyAccessAsync(
				date,
				[(docId, AccessCount)],
				TestContext.Current.CancellationToken);

			// Assert
			DailyDocumentAccess? record = await GetDailyAccessAsync(docId, date);
			record.Should().NotBeNull();
			record!.AccessCount.Should().Be(AccessCount);
			record.DocumentId.Should().Be(docId);
			record.LogDate.Should().Be(date);
		}
		finally
		{
			await CleanupDailyAccessAsync(docId);
		}
	}

	[Fact]
	public async Task UpsertDailyAccessAsync_UpdatesExistingRecord_IncrementsAccessCount()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-upsert-update-{Guid.NewGuid():N}.pdf");
		DateOnly date = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
		const long InitialCount = 10;
		const long AdditionalCount = 25;
		const long ExpectedTotal = InitialCount + AdditionalCount;

		try
		{
			// Insert initial record
			await Repository.UpsertDailyAccessAsync(
				date,
				[(docId, InitialCount)],
				TestContext.Current.CancellationToken);

			// Act - upsert again with additional count
			await Repository.UpsertDailyAccessAsync(
				date,
				[(docId, AdditionalCount)],
				TestContext.Current.CancellationToken);

			// Assert - count should be incremented
			DailyDocumentAccess? record = await GetDailyAccessAsync(docId, date);
			record.Should().NotBeNull();
			record!.AccessCount.Should().Be(ExpectedTotal);
		}
		finally
		{
			await CleanupDailyAccessAsync(docId);
		}
	}

	[Fact]
	public async Task UpsertDailyAccessAsync_MultipleItems_InsertsAll()
	{
		// Arrange
		Guid docId1 = await SeedDocumentAsync($"{TestFilePrefix}-upsert-multi1-{Guid.NewGuid():N}.pdf");
		Guid docId2 = await SeedDocumentAsync($"{TestFilePrefix}-upsert-multi2-{Guid.NewGuid():N}.pdf");
		DateOnly date = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
		const long Count1 = 100;
		const long Count2 = 200;

		try
		{
			// Act
			await Repository.UpsertDailyAccessAsync(
				date,
				[(docId1, Count1), (docId2, Count2)],
				TestContext.Current.CancellationToken);

			// Assert
			DailyDocumentAccess? record1 = await GetDailyAccessAsync(docId1, date);
			DailyDocumentAccess? record2 = await GetDailyAccessAsync(docId2, date);

			record1.Should().NotBeNull();
			record1!.AccessCount.Should().Be(Count1);

			record2.Should().NotBeNull();
			record2!.AccessCount.Should().Be(Count2);
		}
		finally
		{
			await CleanupDailyAccessAsync(docId1);
			await CleanupDailyAccessAsync(docId2);
		}
	}

	[Fact]
	public async Task UpsertDailyAccessAsync_SameDocumentDifferentDates_CreatesSeparateRecords()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-upsert-dates-{Guid.NewGuid():N}.pdf");
		DateOnly today = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
		DateOnly yesterday = today.AddDays(-1);
		const long TodayCount = 50;
		const long YesterdayCount = 30;

		try
		{
			// Act
			await Repository.UpsertDailyAccessAsync(
				today,
				[(docId, TodayCount)],
				TestContext.Current.CancellationToken);

			await Repository.UpsertDailyAccessAsync(
				yesterday,
				[(docId, YesterdayCount)],
				TestContext.Current.CancellationToken);

			// Assert - should have two separate records
			DailyDocumentAccess? todayRecord = await GetDailyAccessAsync(docId, today);
			DailyDocumentAccess? yesterdayRecord = await GetDailyAccessAsync(docId, yesterday);

			todayRecord.Should().NotBeNull();
			todayRecord!.AccessCount.Should().Be(TodayCount);

			yesterdayRecord.Should().NotBeNull();
			yesterdayRecord!.AccessCount.Should().Be(YesterdayCount);
		}
		finally
		{
			await CleanupDailyAccessAsync(docId);
		}
	}

	#endregion
}
