namespace PaperlessREST.Models;

// For file uploads (POST)
public record DocumentUploadDto
{
    public string Title { get; init; } = default!;
    public IFormFile File { get; init; } = default!;
}
