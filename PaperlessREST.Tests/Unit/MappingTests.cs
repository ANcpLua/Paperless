namespace PaperlessREST.Tests.Unit;

public sealed class MappingTests
{
	private const string StatusPending = "Pending";
	private const string StatusCompleted = "Completed";
	private const string StatusFailed = "Failed";
	private const string TestOcrContent = "OCR";
	private const string TestFileName = "invoice.pdf";
	private const string TestReportFileName = "report.pdf";
	private const string TestDocumentFileName = "document.pdf";
	private const string TestPdfFileName = "test.pdf";
	private const string TestSummary = "S";
	private const string TestContent = "C";

	public static TheoryData<DocumentStatus, string> StatusStrings =>
	[
		new TheoryDataRow<DocumentStatus, string>(DocumentStatus.Pending, StatusPending) { Label = "" }
			.WithTestDisplayName("Status Pending"),

		new TheoryDataRow<DocumentStatus, string>(DocumentStatus.Completed, StatusCompleted) { Label = "" }
			.WithTestDisplayName("Status Completed"),

		new TheoryDataRow<DocumentStatus, string>(DocumentStatus.Failed, StatusFailed) { Label = "" }
			.WithTestDisplayName("Status Failed")
	];

	[Fact]
	public void DocumentEntity_ToDocument_MapsAllProperties()
	{
		// Arrange
		DocumentEntity entity = DocumentBuilder.Completed(TestOcrContent).WithId(Guid.CreateVersion7())
			.WithFileName(TestFileName)
			.WithSummary(TestSummary, TimeProvider.System.GetUtcNow().AddHours(-1)).BuildEntity();

		// Act
		Document document = entity.ToDocument();

		// Assert
		document.Should().BeEquivalentTo(entity, o => o.ExcludingMissingMembers());
	}

	[Fact]
	public void Document_ToDocumentEntity_MapsAllProperties()
	{
		// Arrange
		Document document = DocumentBuilder.Failed().WithId(Guid.CreateVersion7()).WithFileName(TestReportFileName)
			.Build();

		// Act
		DocumentEntity entity = document.ToDocumentEntity();

		// Assert
		entity.Should().BeEquivalentTo(document, o => o.ExcludingMissingMembers());
	}

	[Theory]
	[MemberData(nameof(StatusStrings))]
	public void Document_ToDocumentDto_MapsStatusAsString(DocumentStatus status, string expected)
	{
		// Arrange
		Document document = DocumentBuilder.Pending().WithStatus(status).WithId(Guid.CreateVersion7())
			.WithFileName(TestPdfFileName).Build();

		// Act
		DocumentDto dto = document.ToDocumentDto();

		// Assert
		dto.Status.Should().Be(expected);
		dto.Should().BeEquivalentTo(document, o => o.Excluding(d => d.Status).Excluding(d => d.StoragePath));
	}

	[Fact]
	public void Document_ToCreateDocumentResponse_MapsOnlyRequiredFields()
	{
		// Arrange
		Document document = DocumentBuilder.Completed(TestContent).WithId(Guid.CreateVersion7())
			.WithFileName(TestDocumentFileName)
			.WithSummary(TestSummary).Build();

		// Act
		CreateDocumentResponse response = document.ToCreateDocumentResponse();

		// Assert
		response.Id.Should().Be(document.Id);
		response.FileName.Should().Be(document.FileName);
		response.Status.Should().Be(StatusCompleted);
		response.CreatedAt.Should().Be(document.CreatedAt);
	}
}
