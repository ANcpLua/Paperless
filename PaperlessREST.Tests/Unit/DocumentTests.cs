namespace PaperlessREST.Tests.Unit;

public sealed class DocumentTests
{
	private const string MsgCannotCompleteCompleted = "Cannot complete document in Completed status";
	private const string MsgCannotCompleteFailed = "Cannot complete document in Failed status";
	private const string MsgCannotFailCompleted = "Cannot fail document in Completed status";
	private const string MsgCannotFailFailed = "Cannot fail document in Failed status";
	private const string TestFileName = "invoice.pdf";
	private const string TestContent = "content";
	private const string TestSummary = "S";

	private static readonly DateTimeOffset s_fixedTime = new(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
	private readonly FakeTimeProvider _timeProvider = new(s_fixedTime);

	public static IEnumerable<TheoryDataRow<DocumentStatus, string>> CompleteNotPending()
	{
		yield return new TheoryDataRow<DocumentStatus, string>(DocumentStatus.Completed, MsgCannotCompleteCompleted)
			.WithTestDisplayName("Complete invalid: Completed");
		yield return new TheoryDataRow<DocumentStatus, string>(DocumentStatus.Failed, MsgCannotCompleteFailed)
			.WithTestDisplayName("Complete invalid: Failed");
	}

	public static IEnumerable<TheoryDataRow<DocumentStatus, string>> FailNotPending()
	{
		yield return new TheoryDataRow<DocumentStatus, string>(DocumentStatus.Completed, MsgCannotFailCompleted)
			.WithTestDisplayName("Fail invalid: Completed");
		yield return new TheoryDataRow<DocumentStatus, string>(DocumentStatus.Failed, MsgCannotFailFailed)
			.WithTestDisplayName("Fail invalid: Failed");
	}

	[Fact]
	public void CreateFromUpload_SetsDefaults()
	{
		// Act
		Document document = Document.CreateFromUpload(TestFileName, _timeProvider);

		// Assert
		document.Id.Should().NotBeEmpty();
		document.FileName.Should().Be(TestFileName);
		document.Status.Should().Be(DocumentStatus.Pending);
		document.CreatedAt.Should().Be(s_fixedTime);
		document.StoragePath.Should().Match($"documents/{s_fixedTime.UtcDateTime:yyyy-MM}/{document.Id}.pdf");
		document.Content.Should().BeNull();
		document.ProcessedAt.Should().BeNull();
	}

	[Theory]
	[MemberData(nameof(CompleteNotPending))]
	public void MarkAsCompleted_WhenNotPending_ReturnsError(DocumentStatus status, string message)
	{
		// Arrange
		Document document = new DocumentBuilder().WithStatus(status).Build();

		// Act
		ErrorOr<Success> result = document.MarkAsCompleted(TestContent, _timeProvider);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Description.Should().Be(message);
	}

	[Fact]
	public void MarkAsCompleted_WhenPending_Transitions()
	{
		// Arrange
		Document document = new DocumentBuilder().AsPending().Build();

		// Act
		ErrorOr<Success> result = document.MarkAsCompleted(TestContent, _timeProvider);

		// Assert
		result.IsError.Should().BeFalse();
		document.Status.Should().Be(DocumentStatus.Completed);
		document.Content.Should().Be(TestContent);
		document.ProcessedAt.Should().Be(s_fixedTime);
	}

	[Theory]
	[MemberData(nameof(FailNotPending))]
	public void MarkAsFailed_WhenNotPending_ReturnsError(DocumentStatus status, string message)
	{
		// Arrange
		Document document = new DocumentBuilder().WithStatus(status).Build();

		// Act
		ErrorOr<Success> result = document.MarkAsFailed(_timeProvider);

		// Assert
		result.IsError.Should().BeTrue();
		result.FirstError.Description.Should().Be(message);
	}

	[Fact]
	public void MarkAsFailed_WhenPending_Transitions()
	{
		// Arrange
		Document document = new DocumentBuilder().AsPending().Build();

		// Act
		ErrorOr<Success> result = document.MarkAsFailed(_timeProvider);

		// Assert
		result.IsError.Should().BeFalse();
		document.Status.Should().Be(DocumentStatus.Failed);
		document.Content.Should().BeNull();
		document.ProcessedAt.Should().Be(s_fixedTime);
	}

	[Theory]
	[InlineData(DocumentStatus.Pending)]
	[InlineData(DocumentStatus.Completed)]
	[InlineData(DocumentStatus.Failed)]
	public void UpdateSummary_Works(DocumentStatus status)
	{
		// Arrange
		Document document = new DocumentBuilder().WithStatus(status).Build();
		DateTimeOffset at = TimeProvider.System.GetUtcNow();

		// Act
		document.UpdateSummary(TestSummary, at);

		// Assert
		document.Summary.Should().Be(TestSummary);
		document.SummaryGeneratedAt.Should().Be(at);
	}
}
