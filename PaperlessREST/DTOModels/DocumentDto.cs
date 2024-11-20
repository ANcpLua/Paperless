namespace PaperlessREST.Models;

// For API responses (GET)
public record DocumentDto
{
    public int Id { get; init; }
    public string Name { get; init; } = default!;
    public string FilePath { get; init; } = default!;
    public DateTime DateUploaded { get; init; }
}
