namespace PaperlessREST;

public class DocumentEntity
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = null!;
    public DocumentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string StoragePath { get; set; } = null!;
    public string? Content { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}