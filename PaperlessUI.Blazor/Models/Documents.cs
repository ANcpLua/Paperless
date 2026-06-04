namespace PaperlessUI.Blazor.Models;

/// <summary>Document metadata as returned by GET /api/v1/documents (items) and /{id}.</summary>
public sealed record DocumentDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
    public string? Content { get; init; }
    public string? Summary { get; init; }
    public DateTimeOffset? SummaryGeneratedAt { get; init; }
}

/// <summary>One hit from GET /api/v1/documents/search.</summary>
public sealed record DocumentSearchResultDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public string? Content { get; init; }
    public string? Summary { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Status { get; init; }
}

/// <summary>Cursor-paginated wrapper from GET /api/v1/documents.</summary>
public sealed record PaginatedDocumentsResponse
{
    public required List<DocumentDto> Items { get; init; }
    public Guid? NextCursor { get; init; }
    public bool HasMore { get; init; }
}

/// <summary>202 body from POST /api/v1/documents.</summary>
public sealed record CreateDocumentResponse
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
