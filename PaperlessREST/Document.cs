using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace PaperlessREST;

public class Document
{
    public Guid Id { get; private set; }
    public string FileName { get; private set; } = null!;
    public DocumentStatus Status { get; private set; }
    [UsedImplicitly] public DateTimeOffset CreatedAt { get; private set; }
    public string StoragePath { get; private set; } = null!;
    [UsedImplicitly] public string? Content { get; private set; }
    [UsedImplicitly] public DateTimeOffset? ProcessedAt { get; private set; }

    // Public constructor for EF Core and Mapster

    // Factory method for creating new documents
    public static Document CreateFromUpload(string fileName)
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        return new Document
        {
            Id = id,
            FileName = fileName,
            Status = DocumentStatus.Pending,
            CreatedAt = createdAt,
            StoragePath = $"documents/{createdAt:yyyy-MM}/{id}.pdf"
        };
    }

    private Document()
    {
    }

// In Document.cs
    internal Document(Guid id, string fileName, DocumentStatus status,
        DateTimeOffset createdAt, string storagePath, string? content = null,
        DateTimeOffset? processedAt = null)
    {
        Id = id;
        FileName = fileName;
        Status = status;
        CreatedAt = createdAt;
        StoragePath = storagePath;
        Content = content;
        ProcessedAt = processedAt;
    }

    // Domain methods
    public void MarkAsCompleted(string content)
    {
        if (Status is not DocumentStatus.Pending)
            throw new InvalidOperationException($"Cannot complete document in {Status} status");

        Status = DocumentStatus.Completed;
        Content = content;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsFailed()
    {
        if (Status is not DocumentStatus.Pending)
            throw new InvalidOperationException($"Cannot fail document in {Status} status");

        Status = DocumentStatus.Failed;
        ProcessedAt = DateTimeOffset.UtcNow;
    }
}

public enum DocumentStatus
{
    Pending,
    Completed,
    Failed
}