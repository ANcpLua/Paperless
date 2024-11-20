namespace PostgreSQL.Entities;

public class Document
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } =string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime DateUploaded { get; set; } = DateTime.UtcNow;
    public string? OcrText { get; set; } = string.Empty;
}