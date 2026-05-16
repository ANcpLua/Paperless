namespace PaperlessREST.Tests.Integration;

/// <summary>
///     Integration tests for the batch orchestrator following Sprint 7 requirements.
///     Tests the complete pipeline from file discovery to database persistence and file outcome.
/// </summary>
public sealed class BatchOrchestratorIntegrationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
	#region Constructor

	public BatchOrchestratorIntegrationTests(DatabaseFixture fixture)
	{
		_fileSystem = fixture.Services.GetRequiredService<IFileSystem>();
		_orchestrator = fixture.Services.GetRequiredService<BatchOrchestrator>();
		_dbFactory = fixture.ContextFactory;
		BatchOptions batchOptions = fixture.Services.GetRequiredService<IOptions<BatchOptions>>().Value;
		_paths = new BatchPaths(batchOptions.InputPath, batchOptions.ArchivePath, batchOptions.ErrorPath);
	}

	#endregion

	#region Tests - Empty and Edge Cases

	[Fact]
	public async Task ProcessAsync_EmptyXmlFile_ArchivesWithoutDbWrite()
	{
		// Arrange
		await _fileSystem.File.WriteAllTextAsync(
			_fileSystem.Path.Combine(_paths.Input, "empty.xml"),
			EmptyXmlTemplate,
			TestContext.Current.CancellationToken);

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().ContainSingle();
		_fileSystem.Directory.GetFiles(_paths.Error).Should().BeEmpty();
	}

	#endregion

	#region Constants

	private const string TestFilePrefix = "batch-test";
	private const string MalformedXmlContent = "not xml at all";
	private const string EmptyXmlTemplate = """<?xml version="1.0"?><accessReport date="2024-01-15"></accessReport>""";

	private const string InvalidGuidXml = """
	                                      <?xml version="1.0"?>
	                                      <accessReport date="2024-01-15">
	                                          <document id="not-a-valid-guid" accessCount="10"/>
	                                      </accessReport>
	                                      """;

	#endregion

	#region Fields

	private static readonly IJobCancellationToken s_testToken = new TestJobCancellationToken();

	private readonly IDbContextFactory<DocumentPersistence> _dbFactory;
	private readonly IFileSystem _fileSystem;
	private readonly BatchOrchestrator _orchestrator;
	private readonly BatchPaths _paths;

	#endregion

	#region IAsyncLifetime

	public async ValueTask InitializeAsync()
	{
		EnsureCleanDirectories();
		await using DocumentPersistence db = await _dbFactory.CreateDbContextAsync();
		await db.DailyDocumentAccesses.ExecuteDeleteAsync();
	}

	public ValueTask DisposeAsync()
	{
		CleanDirectories();
		return ValueTask.CompletedTask;
	}

	#endregion

	#region Tests - File Processing

	[Fact]
	public async Task ProcessAsync_TwoFilesOneValid_ProcessesBothCorrectly()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-report.pdf");
		await CreateXmlFileAsync("good.xml", (docId, 10));
		await _fileSystem.File.WriteAllTextAsync(
			_fileSystem.Path.Combine(_paths.Input, "bad.xml"),
			MalformedXmlContent,
			TestContext.Current.CancellationToken);

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Archive)
			.Should().ContainSingle(f => f.Contains("good.xml"), "valid file should be archived");
		_fileSystem.Directory.GetFiles(_paths.Error)
			.Should().ContainSingle(f => f.Contains("bad.xml"), "invalid file should go to error");
	}

	[Fact]
	public async Task ProcessAsync_OrphanClaimedFile_Processes()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-orphan.pdf");
		string xmlContent = CreateXmlContent(DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime), (docId, 25));

		string claimedPath = _fileSystem.Path.Combine(_paths.Input, "orphan.xml.processing");
		await _fileSystem.File.WriteAllTextAsync(
			claimedPath,
			xmlContent,
			TestContext.Current.CancellationToken);

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Input).Should().BeEmpty("orphan should be processed");
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().ContainSingle("orphan should be archived");
		await VerifyAccessRecordAsync(docId, 25);
	}

	[Fact]
	public async Task ProcessAsync_NonMatchingFilePattern_IgnoresFile()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-pattern.pdf");
		await CreateXmlFileAsync("access_report.xml", (docId, 10));
		await _fileSystem.File.WriteAllTextAsync(
			_fileSystem.Path.Combine(_paths.Input, "readme.txt"),
			"This is not an XML file",
			TestContext.Current.CancellationToken);

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Input)
			.Should().ContainSingle(f => f.EndsWith("readme.txt"), "non-matching files should remain");
		_fileSystem.Directory.GetFiles(_paths.Archive)
			.Should().ContainSingle(f => f.Contains("access_report.xml"));
	}

	[Fact]
	public async Task ProcessAsync_InputDirectoryMissing_CreatesDirectoryAndContinues()
	{
		// Arrange
		_fileSystem.Directory.Delete(_paths.Input);
		_fileSystem.Directory.Exists(_paths.Input).Should().BeFalse("precondition: input dir should be deleted");

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.Exists(_paths.Input).Should().BeTrue("orchestrator should create input dir if missing");
	}

	[Fact]
	public async Task ProcessAsync_ValidXmlFile_MovesToArchive()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-invoice.pdf");
		await CreateXmlFileAsync("daily-report.xml", (docId, 15));

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Input).Should().BeEmpty("file should be moved out of input");
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().ContainSingle("file should be archived");
		_fileSystem.Directory.GetFiles(_paths.Error).Should().BeEmpty("no errors should occur");

		await VerifyAccessRecordAsync(docId, 15);
	}

	#endregion

	#region Tests - Document Aggregation

	[Fact]
	public async Task ProcessAsync_MultipleDocumentsInOneFile_AggregatesAndUpserts()
	{
		// Arrange
		Guid doc1 = await SeedDocumentAsync($"{TestFilePrefix}-multi1.pdf");
		Guid doc2 = await SeedDocumentAsync($"{TestFilePrefix}-multi2.pdf");
		await CreateXmlFileAsync("multi-doc.xml", (doc1, 10), (doc2, 20));

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().ContainSingle();
		await VerifyAccessRecordAsync(doc1, 10);
		await VerifyAccessRecordAsync(doc2, 20);
	}

	[Fact]
	public async Task ProcessAsync_DuplicateDocumentIdInSameFile_Aggregates()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-dup.pdf");
		await CreateXmlFileAsync("duplicates.xml", (docId, 5), (docId, 8));

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().ContainSingle();
		await VerifyAccessRecordAsync(docId, 13, "should sum 5 + 8");
	}

	[Fact]
	public async Task ProcessAsync_SameDocumentProcessedTwice_AccumulatesCounts()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-monthly.pdf");

		await CreateXmlFileAsync("morning.xml", (docId, 5));
		await _orchestrator.ProcessAsync(s_testToken);

		await CreateXmlFileAsync("afternoon.xml", (docId, 8));
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		await VerifyAccessRecordAsync(docId, 13, "should accumulate 5 + 8");
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().HaveCount(2);
	}

	#endregion

	#region Tests - Unknown Documents

	[Fact]
	public async Task ProcessAsync_UnknownDocumentId_FiltersAndArchives()
	{
		// Arrange
		Guid knownId = await SeedDocumentAsync($"{TestFilePrefix}-known.pdf");
		Guid unknownId = Guid.NewGuid();

		await CreateXmlFileAsync("mixed.xml", (knownId, 10), (unknownId, 99));

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().ContainSingle("file should still archive");
		await VerifyAccessRecordAsync(knownId, 10, "known document should be persisted");

		await using DocumentPersistence db = await _dbFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);
		DailyDocumentAccess? unknownRecord = await db.DailyDocumentAccesses
			.FirstOrDefaultAsync(a => a.DocumentId == unknownId, TestContext.Current.CancellationToken);
		unknownRecord.Should().BeNull("unknown document should not create a DB row");
	}

	[Fact]
	public async Task ProcessAsync_AllDocumentsUnknown_StillArchives()
	{
		// Arrange
		Guid unknownId1 = Guid.NewGuid();
		Guid unknownId2 = Guid.NewGuid();
		await CreateXmlFileAsync("all-unknown.xml", (unknownId1, 10), (unknownId2, 20));

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().ContainSingle("file should archive");
		_fileSystem.Directory.GetFiles(_paths.Error).Should().BeEmpty();

		await using DocumentPersistence db = await _dbFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);
		bool anyRecords = await db.DailyDocumentAccesses.AnyAsync(TestContext.Current.CancellationToken);
		anyRecords.Should().BeFalse("no records should be created for unknown documents");
	}

	#endregion

	#region Tests - Error Cases

	[Fact]
	public async Task ProcessAsync_MalformedXml_MovesToError()
	{
		// Arrange
		await _fileSystem.File.WriteAllTextAsync(
			_fileSystem.Path.Combine(_paths.Input, "corrupted.xml"),
			"<?xml version=\"1.0\"?><accessReport date=\"2024-01-15\"><document",
			TestContext.Current.CancellationToken);

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Error).Should().ContainSingle("malformed XML should go to error");
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().BeEmpty();
	}

	[Fact]
	public async Task ProcessAsync_InvalidGuid_MovesToError()
	{
		// Arrange
		await _fileSystem.File.WriteAllTextAsync(
			_fileSystem.Path.Combine(_paths.Input, "bad-guid.xml"),
			InvalidGuidXml,
			TestContext.Current.CancellationToken);

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Error).Should().ContainSingle();
		_fileSystem.Directory.GetFiles(_paths.Archive).Should().BeEmpty();
	}

	[Fact]
	public async Task ProcessAsync_InvalidDate_MovesToError()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-baddate.pdf");
		string xmlContent = $"""
		                     <?xml version="1.0"?>
		                     <accessReport date="not-a-date">
		                         <document id="{docId}" accessCount="10"/>
		                     </accessReport>
		                     """;

		await _fileSystem.File.WriteAllTextAsync(
			_fileSystem.Path.Combine(_paths.Input, "bad-date.xml"),
			xmlContent,
			TestContext.Current.CancellationToken);

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Error).Should().ContainSingle();
	}

	[Fact]
	public async Task ProcessAsync_NegativeAccessCount_MovesToError()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-negative.pdf");
		string xmlContent = $"""
		                     <?xml version="1.0"?>
		                     <accessReport date="2024-01-15">
		                         <document id="{docId}" accessCount="-10"/>
		                     </accessReport>
		                     """;

		await _fileSystem.File.WriteAllTextAsync(
			_fileSystem.Path.Combine(_paths.Input, "negative.xml"),
			xmlContent,
			TestContext.Current.CancellationToken);

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Error).Should().ContainSingle();
	}

	[Fact]
	public async Task ProcessAsync_MissingDate_MovesToError()
	{
		// Arrange
		Guid docId = await SeedDocumentAsync($"{TestFilePrefix}-nodate.pdf");
		string xmlContent = $"""
		                     <?xml version="1.0"?>
		                     <accessReport>
		                         <document id="{docId}" accessCount="10"/>
		                     </accessReport>
		                     """;

		await _fileSystem.File.WriteAllTextAsync(
			_fileSystem.Path.Combine(_paths.Input, "no-date.xml"),
			xmlContent,
			TestContext.Current.CancellationToken);

		// Act
		await _orchestrator.ProcessAsync(s_testToken);

		// Assert
		_fileSystem.Directory.GetFiles(_paths.Error).Should().ContainSingle();
	}

	#endregion

	#region Helper Methods

	private async Task<Guid> SeedDocumentAsync(string fileName)
	{
		await using DocumentPersistence db = await _dbFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);

		DocumentEntity entity = new DocumentBuilder()
			.WithFileName(fileName)
			.AsCompleted($"Content for {fileName}")
			.BuildEntity();

		db.Documents.Add(entity);
		await db.SaveChangesAsync(TestContext.Current.CancellationToken);
		return entity.Id;
	}

	private async Task CreateXmlFileAsync(string fileName, params (Guid id, int count)[] documents)
	{
		DateOnly date = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);
		string xmlContent = CreateXmlContent(date, documents);
		await _fileSystem.File.WriteAllTextAsync(
			_fileSystem.Path.Combine(_paths.Input, fileName),
			xmlContent,
			TestContext.Current.CancellationToken);
	}

	private static string CreateXmlContent(DateOnly date, params (Guid id, int count)[] documents)
	{
		string documentElements = string.Join("\n",
			documents.Select(d => $"    <document id=\"{d.id}\" accessCount=\"{d.count}\"/>"));

		return $"""
		        <?xml version="1.0" encoding="UTF-8"?>
		        <accessReport date="{date:yyyy-MM-dd}">
		        {documentElements}
		        </accessReport>
		        """;
	}

	private async Task VerifyAccessRecordAsync(Guid documentId, long expectedCount, string? because = null)
	{
		await using DocumentPersistence db = await _dbFactory.CreateDbContextAsync(
			TestContext.Current.CancellationToken);
		DateOnly date = DateOnly.FromDateTime(TimeProvider.System.GetUtcNow().UtcDateTime);

		DailyDocumentAccess? access = await db.DailyDocumentAccesses
			.FirstOrDefaultAsync(
				a => a.DocumentId == documentId && a.LogDate == date,
				TestContext.Current.CancellationToken);

		access.Should().NotBeNull(because ?? "record should exist");
		access.AccessCount.Should().Be(expectedCount, because ?? "access count should match");
	}

	private void EnsureCleanDirectories()
	{
		EnsureCleanDirectory(_paths.Input);
		EnsureCleanDirectory(_paths.Archive);
		EnsureCleanDirectory(_paths.Error);
	}

	private void CleanDirectories()
	{
		CleanDirectory(_paths.Input);
		CleanDirectory(_paths.Archive);
		CleanDirectory(_paths.Error);
	}

	private void EnsureCleanDirectory(string path)
	{
		_fileSystem.Directory.CreateDirectory(path);
		CleanDirectory(path);
	}

	private void CleanDirectory(string path)
	{
		if (_fileSystem.Directory.Exists(path))
		{
			foreach (string file in _fileSystem.Directory.GetFiles(path))
			{
				_fileSystem.File.Delete(file);
			}
		}
	}

	#endregion

	#region Nested Types

	private sealed class TestJobCancellationToken : IJobCancellationToken
	{
		public CancellationToken ShutdownToken => CancellationToken.None;
		public void ThrowIfCancellationRequested() { }
	}

	private record BatchPaths(string Input, string Archive, string Error);

	#endregion
}
