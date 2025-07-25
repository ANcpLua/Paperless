namespace PaperlessREST;

public record CreateDocumentResponse
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = null!;
    public string Status { get; init; } = null!;
    public DateTimeOffset CreatedAt { get; init; }
}