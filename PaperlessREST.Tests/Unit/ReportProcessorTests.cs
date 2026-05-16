using System.Text;
using Testably.Abstractions.Testing;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Unit tests for ReportProcessor covering parse/validation branches.
/// </summary>
public sealed class ReportProcessorTests : IDisposable
{
	private const string ValidDate = "2024-01-15";
	private const string InvalidDateUsFormat = "01/15/2024";
	private const string InvalidDateEuFormat = "15-01-2024";
	private const string InvalidDateWrongSeparator = "2024/01/15";
	private const string InvalidDateEmpty = "";
	private const string InvalidDateWhitespace = "   ";
	private const string InvalidDateInvalidMonth = "2024-13-01";
	private const string InvalidDateInvalidDay = "2024-01-32";
	private const string MalformedXmlNotXml = "this is not XML at all";
	private const string MalformedXmlUnclosed = "<?xml version=\"1.0\"?><accessReport><unclosed>";
	private const string MalformedXmlMissingClose = "<accessReport><date>2024-01-15</date>";
	private const long AccessCount10 = 10;
	private const long AccessCount20 = 20;
	private const long AccessCount5 = 5;
	private const long AggregatedAccessCount = 35; // 10 + 20 + 5

	private const string SchemaContent = """
	                                     <?xml version="1.0" encoding="UTF-8"?>
	                                     <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
	                                     	<xs:element name="accessReport">
	                                     		<xs:complexType>
	                                     			<xs:sequence>
	                                     				<xs:element name="document" minOccurs="0" maxOccurs="unbounded">
	                                     					<xs:complexType>
	                                     						<xs:attribute name="id" type="xs:string" use="required"/>
	                                     						<xs:attribute name="accessCount" use="required">
	                                     							<xs:simpleType>
	                                     								<xs:restriction base="xs:long">
	                                     									<xs:minInclusive value="0"/>
	                                     								</xs:restriction>
	                                     							</xs:simpleType>
	                                     						</xs:attribute>
	                                     					</xs:complexType>
	                                     				</xs:element>
	                                     			</xs:sequence>
	                                     			<xs:attribute name="date" type="xs:date" use="required"/>
	                                     		</xs:complexType>
	                                     	</xs:element>
	                                     </xs:schema>
	                                     """;

	private static readonly DateOnly s_validDateOnly = new(2024, 1, 15);
	private readonly MockFileSystem _fileSystem = new();
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<ReportProcessor> _logger;

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IDocumentAccessRepository> _repo;

	public ReportProcessorTests()
	{
		_logger = new FakeLogger<ReportProcessor>(_logCollector);
		_repo = _mocks.Create<IDocumentAccessRepository>();
		SetupSchemaFile();
	}

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
		_mocks.VerifyAll();
		_mocks.VerifyNoOtherCalls();
	}

	public static IEnumerable<TheoryDataRow<string>> InvalidDateFormats()
	{
		yield return new TheoryDataRow<string>("invalid-date")
			.WithTestDisplayName("Text 'invalid-date'");
		yield return new TheoryDataRow<string>(InvalidDateUsFormat)
			.WithTestDisplayName("US format MM/dd/yyyy");
		yield return new TheoryDataRow<string>(InvalidDateEuFormat)
			.WithTestDisplayName("EU format dd-MM-yyyy");
		yield return new TheoryDataRow<string>(InvalidDateWrongSeparator)
			.WithTestDisplayName("Wrong separator yyyy/MM/dd");
		yield return new TheoryDataRow<string>(InvalidDateEmpty)
			.WithTestDisplayName("Empty string");
		yield return new TheoryDataRow<string>(InvalidDateWhitespace)
			.WithTestDisplayName("Whitespace only");
		yield return new TheoryDataRow<string>(InvalidDateInvalidMonth)
			.WithTestDisplayName("Invalid month 13");
		yield return new TheoryDataRow<string>(InvalidDateInvalidDay)
			.WithTestDisplayName("Invalid day 32");
	}

	public static IEnumerable<TheoryDataRow<string>> MalformedXmlContent()
	{
		yield return new TheoryDataRow<string>(MalformedXmlNotXml)
			.WithTestDisplayName("Not XML at all");
		yield return new TheoryDataRow<string>(MalformedXmlUnclosed)
			.WithTestDisplayName("Unclosed tag");
		yield return new TheoryDataRow<string>(MalformedXmlMissingClose)
			.WithTestDisplayName("Missing closing tag");
	}

	public static IEnumerable<TheoryDataRow<Guid[], Guid[], int, int>> DocumentProcessingScenarios()
	{
		Guid doc1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
		Guid doc2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

		yield return new TheoryDataRow<Guid[], Guid[], int, int>(
				[doc1, doc2], [doc1, doc2], 2, 0)
			.WithTestDisplayName("All documents exist -> 2 processed, 0 skipped");

		yield return new TheoryDataRow<Guid[], Guid[], int, int>(
				[doc1, doc2], [doc1], 1, 1)
			.WithTestDisplayName("One document unknown -> 1 processed, 1 skipped");

		yield return new TheoryDataRow<Guid[], Guid[], int, int>(
				[doc1, doc2], [], 0, 2)
			.WithTestDisplayName("All documents unknown -> 0 processed, 2 skipped");
	}

	private void SetupSchemaFile()
	{
		string schemaDir = Path.Combine(AppContext.BaseDirectory, "Schemas");
		_fileSystem.Directory.CreateDirectory(schemaDir);
		_fileSystem.File.WriteAllText(Path.Combine(schemaDir, "accessReport.xsd"), SchemaContent);
	}

	[Fact]
	public async Task ProcessAsync_FileNotFound_ReturnsNotFoundError()
	{
		// Arrange - Create directory so MockFileSystem throws FileNotFoundException (not DirectoryNotFoundException)
		string baseDir = AppContext.BaseDirectory;
		_fileSystem.Directory.CreateDirectory(baseDir);
		string missingFilePath = Path.Combine(baseDir, "nonexistent.xml");
		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result =
			await sut.ProcessAsync(missingFilePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.NotFound);
	}

	[Theory]
	[MemberData(nameof(MalformedXmlContent))]
	public async Task ProcessAsync_MalformedXml_ReturnsValidationError(string malformedXml)
	{
		// Arrange
		string filePath = CreateTestFile("malformed.xml", malformedXml);
		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.Validation);
	}

	[Theory]
	[MemberData(nameof(InvalidDateFormats))]
	public async Task ProcessAsync_InvalidDateFormat_ReturnsValidationError(string invalidDate)
	{
		// Arrange
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{invalidDate}">
		                     </accessReport>
		                     """;
		string filePath = CreateTestFile("invalid-date.xml", xmlContent);
		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.Validation);
	}

	[Fact]
	public async Task ProcessAsync_EmptyDocumentsList_ReturnsZeroProcessed()
	{
		// Arrange
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                     </accessReport>
		                     """;

		string filePath = CreateTestFile("empty.xml", xmlContent);
		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.ProcessedCount.Should().Be(0);
		result.Value.SkippedCount.Should().Be(0);
	}

	[Fact]
	public async Task ProcessAsync_EmptyDocuments_LogsInformation()
	{
		// Arrange
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                     </accessReport>
		                     """;

		string filePath = CreateTestFile("info-empty.xml", xmlContent);
		ReportProcessor sut = CreateSut();

		// Act
		await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Information &&
				l.Message.Contains("no documents"));
	}

	[Fact]
	public async Task ProcessAsync_EmptyGuidInDocument_ReturnsInvalidGuidError()
	{
		// Arrange
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                         <document id="00000000-0000-0000-0000-000000000000" accessCount="10"/>
		                     </accessReport>
		                     """;

		string filePath = CreateTestFile("empty-guid.xml", xmlContent);
		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Code.Should().Be("Report.InvalidGuid");
	}

	[Theory]
	[MemberData(nameof(DocumentProcessingScenarios))]
	public async Task ProcessAsync_DocumentScenarios_ReturnsExpectedCounts(
		Guid[] xmlDocIds, Guid[] existingDocIds, int expectedProcessed, int expectedSkipped)
	{
		// Arrange
		SetupRepositoryReturnsIds(existingDocIds);

		(Guid Id, long Count)[] documents = xmlDocIds
			.Select((id, i) => (id, (i + 1) * AccessCount10))
			.ToArray();

		string xmlContent = CreateAccessReportXml(ValidDate, documents);
		string filePath = CreateTestFile("scenarios.xml", xmlContent);
		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.ProcessedCount.Should().Be(expectedProcessed);
		result.Value.SkippedCount.Should().Be(expectedSkipped);
	}

	[Fact]
	public async Task ProcessAsync_AllDocumentsUnknown_ReturnsZeroProcessedWithSkipped()
	{
		// Arrange
		Guid unknownId1 = Guid.NewGuid();
		Guid unknownId2 = Guid.NewGuid();
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                         <document id="{unknownId1}" accessCount="{AccessCount10}"/>
		                         <document id="{unknownId2}" accessCount="{AccessCount20}"/>
		                     </accessReport>
		                     """;

		string filePath = CreateTestFile("all-unknown.xml", xmlContent);

		_repo.Setup(r => r.GetExistingDocumentIdsAsync(
				It.IsAny<Guid[]>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);

		_repo.Setup(r => r.UpsertDailyAccessAsync(
				It.IsAny<DateOnly>(),
				It.IsAny<(Guid DocumentId, long AccessCount)[]>(),
				It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.ProcessedCount.Should().Be(0);
		result.Value.SkippedCount.Should().Be(2);
	}

	[Fact]
	public async Task ProcessAsync_SomeDocumentsUnknown_ProcessesKnownAndSkipsUnknown()
	{
		// Arrange
		Guid knownId = Guid.NewGuid();
		Guid unknownId = Guid.NewGuid();
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                         <document id="{knownId}" accessCount="{AccessCount10}"/>
		                         <document id="{unknownId}" accessCount="{AccessCount20}"/>
		                     </accessReport>
		                     """;

		string filePath = CreateTestFile("mixed.xml", xmlContent);

		_repo.Setup(r => r.GetExistingDocumentIdsAsync(
				It.IsAny<Guid[]>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync([knownId]);

		_repo.Setup(r => r.UpsertDailyAccessAsync(
				It.IsAny<DateOnly>(),
				It.Is<(Guid DocumentId, long AccessCount)[]>(items =>
					items.Length == 1 && items[0].DocumentId == knownId),
				It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.ProcessedCount.Should().Be(1);
		result.Value.SkippedCount.Should().Be(1);
	}

	[Fact]
	public async Task ProcessAsync_DuplicateDocumentIds_AggregatesAccessCounts()
	{
		// Arrange
		Guid docId = Guid.NewGuid();
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                         <document id="{docId}" accessCount="{AccessCount5}"/>
		                         <document id="{docId}" accessCount="{AccessCount10}"/>
		                         <document id="{docId}" accessCount="{AccessCount20}"/>
		                     </accessReport>
		                     """;

		string filePath = CreateTestFile("duplicates.xml", xmlContent);

		_repo.Setup(r => r.GetExistingDocumentIdsAsync(
				It.IsAny<Guid[]>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync([docId]);

		_repo.Setup(r => r.UpsertDailyAccessAsync(
				It.IsAny<DateOnly>(),
				It.Is<(Guid DocumentId, long AccessCount)[]>(items =>
					items.Length == 1 && items[0].AccessCount == AggregatedAccessCount),
				It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.ProcessedCount.Should().Be(1);
	}

	[Fact]
	public async Task ProcessAsync_ValidXmlWithKnownDocuments_ProcessesSuccessfully()
	{
		// Arrange
		Guid doc1 = Guid.NewGuid();
		Guid doc2 = Guid.NewGuid();
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                         <document id="{doc1}" accessCount="{AccessCount10}"/>
		                         <document id="{doc2}" accessCount="{AccessCount20}"/>
		                     </accessReport>
		                     """;

		string filePath = CreateTestFile("valid.xml", xmlContent);

		_repo.Setup(r => r.GetExistingDocumentIdsAsync(
				It.IsAny<Guid[]>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync([doc1, doc2]);

		_repo.Setup(r => r.UpsertDailyAccessAsync(
				It.IsAny<DateOnly>(),
				It.Is<(Guid DocumentId, long AccessCount)[]>(items => items.Length == 2),
				It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		result.IsError.Should().BeFalse();
		result.Value.ProcessedCount.Should().Be(2);
		result.Value.SkippedCount.Should().Be(0);
	}

	[Fact]
	public async Task ProcessAsync_ValidDocuments_CallsRepositoryWithCorrectDate()
	{
		// Arrange
		Guid docId = Guid.NewGuid();
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                         <document id="{docId}" accessCount="{AccessCount10}"/>
		                     </accessReport>
		                     """;

		string filePath = CreateTestFile("date-check.xml", xmlContent);

		_repo.Setup(r => r.GetExistingDocumentIdsAsync(
				It.IsAny<Guid[]>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync([docId]);

		_repo.Setup(r => r.UpsertDailyAccessAsync(
				s_validDateOnly,
				It.IsAny<(Guid DocumentId, long AccessCount)[]>(),
				It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		ReportProcessor sut = CreateSut();

		// Act
		await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		_repo.Verify(
			r => r.UpsertDailyAccessAsync(
				s_validDateOnly,
				It.IsAny<(Guid, long)[]>(),
				It.IsAny<CancellationToken>()),
			Times.Once);
	}

	[Fact]
	public async Task ProcessAsync_WithSkippedDocuments_LogsWarning()
	{
		// Arrange
		Guid unknownId = Guid.NewGuid();
		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                         <document id="{unknownId}" accessCount="{AccessCount10}"/>
		                     </accessReport>
		                     """;

		string filePath = CreateTestFile("warning.xml", xmlContent);

		_repo.Setup(r => r.GetExistingDocumentIdsAsync(
				It.IsAny<Guid[]>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);

		_repo.Setup(r => r.UpsertDailyAccessAsync(
				It.IsAny<DateOnly>(),
				It.IsAny<(Guid DocumentId, long AccessCount)[]>(),
				It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		ReportProcessor sut = CreateSut();

		// Act
		await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(l =>
				l.Level == LogLevel.Warning &&
				l.Message.Contains("unknown documents"));
	}

	private ReportProcessor CreateSut() => new(_fileSystem, _repo.Object, _logger);

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessAsync - XmlSchemaException Handler Branch
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessAsync_MalformedSchema_ReturnsValidationError()
	{
		// Arrange - Create a new test instance with corrupted schema
		MockFileSystem corruptFs = new();
		string schemaDir = Path.Combine(AppContext.BaseDirectory, "Schemas");
		corruptFs.Directory.CreateDirectory(schemaDir);
		// Write invalid XSD that will cause XmlSchemaException when parsing
		corruptFs.File.WriteAllText(Path.Combine(schemaDir, "accessReport.xsd"),
			"<?xml version=\"1.0\"?><xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"><xs:invalid/></xs:schema>");

		string xmlContent = $"""
		                     <?xml version="1.0" encoding="UTF-8"?>
		                     <accessReport date="{ValidDate}">
		                     </accessReport>
		                     """;
		string baseDir = AppContext.BaseDirectory;
		corruptFs.Directory.CreateDirectory(baseDir);
		string filePath = Path.Combine(baseDir, "schema-test.xml");
		corruptFs.File.WriteAllText(filePath, xmlContent);

		ReportProcessor sut = new(corruptFs, _repo.Object, _logger);

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert - Should return validation error from XmlSchemaException handler
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.Validation);
	}

	// ═══════════════════════════════════════════════════════════════
	// TESTS: ProcessAsync - InvalidDate path AFTER schema validation passes
	// Covers ReportProcessor.cs lines 100-103: DateOnly.TryParseExact failure
	// for a date string that survives xs:date schema validation but is not
	// yyyy-MM-dd (e.g., timezone-suffixed date).
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public async Task ProcessAsync_DateWithTimezone_FailsDateOnlyTryParseExact()
	{
		// Arrange — "2024-01-15+02:00" satisfies xs:date (which permits timezone suffix)
		// but DateOnly.TryParseExact("yyyy-MM-dd", ...) rejects it.
		const string xmlContent = """
		                          <?xml version="1.0" encoding="UTF-8"?>
		                          <accessReport date="2024-01-15+02:00">
		                          </accessReport>
		                          """;

		string filePath = CreateTestFile("date-with-tz.xml", xmlContent);
		ReportProcessor sut = CreateSut();

		// Act
		ErrorOr<ProcessingResult> result = await sut.ProcessAsync(filePath, TestContext.Current.CancellationToken);

		// Assert — must be the InvalidDate factory, distinct from InvalidSchema
		result.IsError.Should().BeTrue();
		result.FirstError.Type.Should().Be(ErrorType.Validation);
		result.FirstError.Code.Should().Be("Report.InvalidDate");
		result.FirstError.Description.Should().Contain("2024-01-15+02:00");
	}

	private string CreateTestFile(string fileName, string content)
	{
		string baseDir = AppContext.BaseDirectory;
		_fileSystem.Directory.CreateDirectory(baseDir);

		string filePath = Path.Combine(baseDir, fileName);
		_fileSystem.File.WriteAllText(filePath, content);
		return filePath;
	}

	private void SetupRepositoryReturnsIds(Guid[] ids)
	{
		_repo.Setup(r => r.GetExistingDocumentIdsAsync(
				It.IsAny<Guid[]>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(ids);

		_repo.Setup(r => r.UpsertDailyAccessAsync(
				It.IsAny<DateOnly>(),
				It.IsAny<(Guid DocumentId, long AccessCount)[]>(),
				It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);
	}

	private static string CreateAccessReportXml(string date, (Guid Id, long Count)[] documents)
	{
		StringBuilder sb = new();
		sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
		sb.AppendLine($"""<accessReport date="{date}">""");

		foreach ((Guid id, long count) in documents)
		{
			sb.AppendLine($"""    <document id="{id}" accessCount="{count}"/>""");
		}

		sb.AppendLine("</accessReport>");
		return sb.ToString();
	}
}
