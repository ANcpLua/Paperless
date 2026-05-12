using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Paperless.Contracts.Validation;

namespace Paperless.Contracts.DocumentManagement;

// All records below are pure transport DTOs. Behaviour belongs in BL/DAL/API;
// coverage on compiler-generated record members (Equals/GetHashCode/PrintMembers/
// copy ctors) just adds noise to the score without exercising real code paths.

/// <summary>
///     Query parameters for paginated document listing.
/// </summary>
/// <remarks>
///     Uses cursor-based pagination with GUIDv7 (time-ordered) document IDs.
///     Pass the last document's ID as <see cref="Cursor"/> to get the next page.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Pure transport DTO - compiler-generated record members only")]
public sealed record PaginationQuery
{
	[Range(PaginationConstraints.MinPageSize, PaginationConstraints.MaxPageSize,
		ErrorMessage = "Page size must be between 1 and 100")]
	[Description("Number of documents to return per page")]
	public int PageSize { get; init; } = PaginationConstraints.DefaultPageSize;

	[Description("Cursor for pagination (last document ID from previous page)")]
	public Guid? Cursor { get; init; }
}

/// <summary>
///     Query parameters for document search.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Pure transport DTO - compiler-generated record members only")]
public sealed record SearchQuery
{
	[StringLength(SearchConstraints.QueryMaxLength, MinimumLength = SearchConstraints.QueryMinLength,
		ErrorMessage = "Search query must be between 1 and 100 characters")]
	[Description("Search text to find in documents")]
	public required string Query { get; init; }

	[Range(1, SearchConstraints.MaxResultLimit, ErrorMessage = "Limit must be between 1 and 100")]
	[Description("Maximum number of results to return")]
	public int Limit { get; init; } = SearchConstraints.DefaultResultLimit;
}

/// <summary>
///     Represents document metadata returned by the API.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Pure transport DTO - compiler-generated record members only")]
public sealed record DocumentDto
{
	[Description("Unique document identifier")]
	public required Guid Id { get; init; }

	[Description("Original PDF filename")] public required string FileName { get; init; }

	[Description("Processing status (Pending, Completed, Failed)")]
	public required string Status { get; init; }

	[Description("Upload timestamp (UTC)")]
	public required DateTimeOffset CreatedAt { get; init; }

	[Description("Processing completion timestamp (UTC)")]
	public DateTimeOffset? ProcessedAt { get; init; }

	[Description("OCR extracted text content")]
	public string? Content { get; init; }

	[Description("AI-generated summary")] public string? Summary { get; init; }

	[Description("Timestamp when summary was generated (UTC)")]
	public DateTimeOffset? SummaryGeneratedAt { get; init; }
}

/// <summary>
///     Response returned after successfully uploading a document.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Pure transport DTO - compiler-generated record members only")]
public sealed record CreateDocumentResponse
{
	[Description("Unique document identifier")]
	public required Guid Id { get; init; }

	[Description("Original PDF filename")] public required string FileName { get; init; }

	[Description("Processing status")] public required string Status { get; init; }

	[Description("Upload timestamp (UTC)")]
	public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
///     Represents a document search result with relevant fields.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Pure transport DTO - compiler-generated record members only")]
public sealed record DocumentSearchResultDto
{
	[Description("Unique document identifier")]
	public required Guid Id { get; init; }

	[Description("Original PDF filename")] public required string FileName { get; init; }

	[Description("OCR extracted content")] public string? Content { get; init; }

	[Description("AI-generated summary")] public string? Summary { get; init; }

	[Description("Upload timestamp (UTC)")]
	public required DateTimeOffset CreatedAt { get; init; }

	[Description("Processing status")] public required string Status { get; init; }
}

/// <summary>
///     Response containing a document summary.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Pure transport DTO - compiler-generated record members only")]
public sealed record SummaryDto
{
	[Description("AI-generated summary of the document content")]
	public string? Summary { get; init; }
}

/// <summary>
///     Paginated response wrapper for document listings.
/// </summary>
/// <remarks>
///     Uses cursor-based pagination for efficient traversal of large datasets.
///     The <see cref="NextCursor"/> is null when there are no more pages.
/// </remarks>
[ExcludeFromCodeCoverage(Justification = "Pure transport DTO - compiler-generated record members only")]
public sealed record PaginatedDocumentsResponse
{
	[Description("List of documents in this page")]
	public required List<DocumentDto> Items { get; init; }

	[Description("Cursor for the next page (null if no more pages)")]
	public Guid? NextCursor { get; init; }

	[Description("Whether more documents exist after this page")]
	public required bool HasMore { get; init; }

	[Description("Number of documents in this response")]
	public int Count => Items.Count;
}
