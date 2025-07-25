using System.ComponentModel;

namespace PaperlessREST;

public record DocumentDto
{
    [Description("Unique document identifier")]
    public Guid Id { get; init; }

    [Description("Original PDF filename")] public string FileName { get; init; } = null!;

    [Description("Processing status")] public string Status { get; init; } = null!;

    [Description("Upload timestamp")] public DateTimeOffset CreatedAt { get; init; }

    [Description("Storage path in MinIO")] public string StoragePath { get; init; } = null!;

    [Description("OCR extracted text content")]
    public string? Content { get; init; }

    [Description("Processing completion timestamp")]
    public DateTimeOffset? ProcessedAt { get; init; }
}