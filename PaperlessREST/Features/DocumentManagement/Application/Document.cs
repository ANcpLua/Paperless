using Result = ErrorOr.Result;

namespace PaperlessREST.Features.DocumentManagement.Application;

/// <summary>
///     Represents a document in the paperless system with business logic for state transitions.
/// </summary>
/// <remarks>
///     This domain model encapsulates business rules for document lifecycle management.
///     Documents progress through states: <see cref="DocumentStatus.Pending" /> →
///     <see cref="DocumentStatus.Completed" /> or <see cref="DocumentStatus.Failed" />.
///     State transitions are enforced through <see cref="MarkAsCompleted" /> and <see cref="MarkAsFailed" />
///     to prevent invalid state changes.
/// </remarks>
public sealed class Document
{
	private const string StoragePathFormat = "documents/{0:yyyy-MM}/{1}.pdf";

	/// <summary>
	///     Gets or sets the unique <see cref="Guid" /> identifier for the document.
	/// </summary>
	public required Guid Id { get; set; }

	/// <summary>
	///     Gets or sets the original filename of the uploaded PDF.
	/// </summary>
	public required string FileName { get; set; }

	/// <summary>
	///     Gets or sets the current <see cref="DocumentStatus" /> of the document.
	/// </summary>
	public required DocumentStatus Status { get; set; }

	/// <summary>
	///     Gets or sets the UTC <see cref="DateTimeOffset" /> when the document was uploaded.
	/// </summary>
	public required DateTimeOffset CreatedAt { get; set; }

	/// <summary>
	///     Gets or sets the MinIO object storage path where the PDF file is stored.
	/// </summary>
	/// <remarks>
	///     Internal storage detail - not exposed in public API responses for security.
	///     Format: "documents/{yyyy-MM}/{guid}.pdf"
	/// </remarks>
	public required string StoragePath { get; set; }

	/// <summary>
	///     Gets or sets the OCR-extracted text content from the PDF.
	/// </summary>
	/// <remarks>
	///     <c>null</c> until OCR processing completes successfully via <see cref="MarkAsCompleted" />.
	///     Populated by the OCR microservice.
	/// </remarks>
	public string? Content { get; set; }

	/// <summary>
	///     Gets or sets the UTC <see cref="DateTimeOffset" /> when OCR processing completed.
	/// </summary>
	public DateTimeOffset? ProcessedAt { get; set; }

	/// <summary>
	///     Gets or sets the AI-generated summary of the document content.
	/// </summary>
	/// <remarks>
	///     <c>null</c> until GenAI processing completes via <see cref="UpdateSummary" />.
	///     Populated by the GenAI microservice.
	/// </remarks>
	public string? Summary { get; set; }

	/// <summary>
	///     Gets or sets the UTC <see cref="DateTimeOffset" /> when the AI summary was generated.
	/// </summary>
	public DateTimeOffset? SummaryGeneratedAt { get; set; }

	/// <summary>
	///     Creates a new <see cref="Document" /> instance from an uploaded file.
	/// </summary>
	/// <param name="fileName">The original filename of the uploaded PDF.</param>
	/// <param name="timeProvider">Provides the UTC timestamp recorded on <see cref="CreatedAt" />; inject for testability.</param>
	/// <returns>A new <see cref="Document" /> in <see cref="DocumentStatus.Pending" /> status.</returns>
	/// <remarks>
	///     This factory method initializes a document with:
	///     <list type="bullet">
	///         <item>
	///             <description>
	///                 A Version 7 <see cref="Guid" /> (time-ordered) via <see cref="Guid.CreateVersion7()" />
	///             </description>
	///         </item>
	///         <item>
	///             <description><see cref="DocumentStatus.Pending" /> status (awaiting OCR processing)</description>
	///         </item>
	///         <item>
	///             <description>Current UTC <see cref="DateTimeOffset" /></description>
	///         </item>
	///         <item>
	///             <description>Storage path: documents/{yyyy-MM}/{guid}.pdf</description>
	///         </item>
	///     </list>
	/// </remarks>
	public static Document CreateFromUpload(string fileName, TimeProvider timeProvider)
	{
		var now = timeProvider.GetUtcNow();
		var id = Guid.CreateVersion7();

		return new Document
		{
			Id = id,
			FileName = fileName,
			Status = DocumentStatus.Pending,
			CreatedAt = now,
			StoragePath = string.Format(StoragePathFormat, now.UtcDateTime, id)
		};
	}

	/// <summary>
	///     Marks the document as successfully processed with OCR-extracted content.
	///     <para>
	///         Sets <see cref="Status" /> to <see cref="DocumentStatus.Completed" /> and populates <see cref="Content" />.
	///     </para>
	/// </summary>
	/// <param name="content">The OCR-extracted text content from the PDF.</param>
	/// <param name="timeProvider">Time provider for recording completion timestamp.</param>
	/// <returns>
	///     <see cref="ErrorOr{T}" /> containing success if the transition was valid,
	///     or <see cref="DocumentErrors.CannotComplete" /> if the document is not in <see cref="DocumentStatus.Pending" />
	///     status.
	/// </returns>
	/// <remarks>
	///     Enforces the business rule: only <see cref="DocumentStatus.Pending" /> documents can be completed.
	///     Called by the OCR result handler after successful text extraction.
	/// </remarks>
	public ErrorOr<Success> MarkAsCompleted(string content, TimeProvider timeProvider)
	{
		if (Status != DocumentStatus.Pending)
		{
			return DocumentErrors.CannotComplete(Status);
		}

		Status = DocumentStatus.Completed;
		Content = content;
		ProcessedAt = timeProvider.GetUtcNow();
		return Result.Success;
	}

	/// <summary>
	///     Marks the document as failed due to OCR processing errors.
	///     <para>
	///         Sets <see cref="Status" /> to <see cref="DocumentStatus.Failed" />.
	///     </para>
	/// </summary>
	/// <param name="timeProvider">Time provider for recording failure timestamp.</param>
	/// <returns>
	///     <see cref="ErrorOr{T}" /> containing success if the transition was valid,
	///     or <see cref="DocumentErrors.CannotFail" /> if the document is not in <see cref="DocumentStatus.Pending" /> status.
	/// </returns>
	/// <remarks>
	///     Enforces the business rule: only <see cref="DocumentStatus.Pending" /> documents can be failed.
	///     Called by the OCR result handler when text extraction fails.
	/// </remarks>
	public ErrorOr<Success> MarkAsFailed(TimeProvider timeProvider)
	{
		if (Status != DocumentStatus.Pending)
		{
			return DocumentErrors.CannotFail(Status);
		}

		Status = DocumentStatus.Failed;
		ProcessedAt = timeProvider.GetUtcNow();
		return Result.Success;
	}

	/// <summary>
	///     Updates the document with an AI-generated summary.
	///     <para>
	///         Sets <see cref="Summary" /> and <see cref="SummaryGeneratedAt" />.
	///     </para>
	/// </summary>
	/// <param name="summary">The AI-generated summary text.</param>
	/// <param name="generatedAt">The UTC <see cref="DateTimeOffset" /> when the summary was generated.</param>
	/// <remarks>
	///     Called by the GenAI result handler after successful summary generation.
	///     Can be called multiple times to update the summary.
	/// </remarks>
	public void UpdateSummary(string summary, DateTimeOffset generatedAt)
	{
		Summary = summary;
		SummaryGeneratedAt = generatedAt;
	}
}

/// <summary>
///     Represents the processing state of a <see cref="Document" />.
/// </summary>
public enum DocumentStatus
{
	/// <summary>
	///     Document is awaiting OCR processing.
	///     Initial state set by <see cref="Document.CreateFromUpload" />.
	/// </summary>
	[Description("Document is awaiting OCR processing")]
	Pending,

	/// <summary>
	///     OCR processing completed successfully.
	///     Set by <see cref="Document.MarkAsCompleted" />.
	/// </summary>
	[Description("OCR processing completed successfully")]
	Completed,

	/// <summary>
	///     OCR processing failed.
	///     Set by <see cref="Document.MarkAsFailed" />.
	/// </summary>
	[Description("OCR processing failed")] Failed
}

/// <summary>
///     Represents a <see cref="Document" /> search result from Elasticsearch.
/// </summary>
/// <remarks>
///     This is a projection of <see cref="Document" /> data optimized for search results.
///     It excludes internal storage details (<see cref="Document.StoragePath" />) for security.
///     Used by the domain service layer to maintain proper separation from API DTOs.
/// </remarks>
[ExcludeFromCodeCoverage(Justification =
	"Record - compiler-generated Equals/GetHashCode/Clone/ToString with nullable property null-checks")]
public sealed record DocumentSearchResult
{
	/// <summary>Gets the unique <see cref="Guid" /> identifier.</summary>
	public required Guid Id { get; init; }

	/// <summary>Gets the original filename of the uploaded PDF.</summary>
	public required string FileName { get; init; }

	/// <summary>Gets the <see cref="DocumentStatus" /> as a string.</summary>
	public required string Status { get; init; }

	/// <summary>Gets the UTC <see cref="DateTimeOffset" /> when uploaded.</summary>
	public required DateTimeOffset CreatedAt { get; init; }

	/// <summary>Gets the OCR-extracted text content.</summary>
	public string? Content { get; init; }

	/// <summary>Gets the UTC <see cref="DateTimeOffset" /> when OCR completed.</summary>
	public DateTimeOffset? ProcessedAt { get; init; }

	/// <summary>Gets the AI-generated summary.</summary>
	public string? Summary { get; init; }

	/// <summary>Gets the UTC <see cref="DateTimeOffset" /> when summary was generated.</summary>
	public DateTimeOffset? SummaryGeneratedAt { get; init; }
}
