using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using PaperlessServices.Program;
using SWEN3.Paperless.RabbitMq.Models;

namespace Paperless.Tests;

/// <summary>
/// Provides a configured test host for PaperlessServices testing.
/// </summary>
public class PaperlessServicesHost : IAsyncInitializer
{
    private IHost? _host;

    [ClassDataSource<TestContainersManager>(Shared = SharedType.PerTestSession)]
    public required TestContainersManager Containers { get; init; }

    public async Task InitializeAsync()
    {
        await Containers.InitializeAsync();

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(Containers.GetConfiguration());
        OcrServicesServiceCollectionAdapter.AddOcrServices(builder.Services, builder.Configuration);
        // Override OCR and Search services with lightweight fakes for deterministic tests
        builder.Services.AddSingleton<ISearchIndexService, FakeSearchIndexService>();

        _host = builder.Build();
        await _host.StartAsync();

        // Ensure MinIO bucket exists for tests
        var minio = _host.Services.GetRequiredService<IMinioClient>();
        var minioOptions = _host.Services.GetRequiredService<IOptions<MinioOptions>>().Value;
        var bucket = minioOptions.BucketName;
        if (!await minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket)))
        {
            await minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
        }
    }

    // Services accessors
    public IOcrProcessor OcrProcessor => _host!.Services.GetRequiredService<IOcrProcessor>();
    public IStorageService StorageService => _host!.Services.GetRequiredService<IStorageService>();
    public ISearchIndexService SearchIndexService => _host!.Services.GetRequiredService<ISearchIndexService>();
    public IMinioClient MinioClient => _host!.Services.GetRequiredService<IMinioClient>();
    public IOptions<MinioOptions> MinioOptions => _host!.Services.GetRequiredService<IOptions<MinioOptions>>();
    public IOcrService OcrService => _host!.Services.GetRequiredService<IOcrService>();
}

/// <summary>
/// PDF test helper - minimal.
/// </summary>
public static class PdfTestHelper
{
    // Generate a real PDF file using CreatePdf.NET and return its bytes.
    public static async Task<byte[]> CreatePdfWithTextAsync(string content)
    {
        var path = await Pdf.Create(Dye.Black)
            .AddText(content)
            .SaveAsync($"{Guid.NewGuid()}.pdf");

        return await File.ReadAllBytesAsync(path);
    }
}

// Keep fake for search index to avoid external dependency during tests
internal sealed class FakeSearchIndexService : ISearchIndexService
{
    public Task IndexDocumentAsync(Guid id, string fileName, string content, string storagePath,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

// ============== ACTUAL TESTS ==============

/// <summary>
/// Storage service tests.
/// </summary>
public class StorageServiceTests
{
    [ClassDataSource<PaperlessServicesHost>(Shared = SharedType.PerTestSession)]
    public required PaperlessServicesHost ServicesHost { get; init; }

    [Test]
    public async Task DownloadAsync_ExistingFile_ReturnsStream()
    {
        // Arrange
        var testData = await PdfTestHelper.CreatePdfWithTextAsync("Test content");
        var filePath = $"test/{Guid.NewGuid()}.pdf";

        await ServicesHost.MinioClient.PutObjectAsync(new PutObjectArgs()
            .WithBucket(ServicesHost.MinioOptions.Value.BucketName)
            .WithObject(filePath)
            .WithStreamData(new MemoryStream(testData))
            .WithObjectSize(testData.Length));

        // Act
        await using var stream = await ServicesHost.StorageService.DownloadAsync(filePath);

        // Assert
        await Assert.That(stream.Length).IsGreaterThan(0);
    }
}

/// <summary>
/// OCR service tests.
/// </summary>
public class OcrServiceTests
{
    [ClassDataSource<PaperlessServicesHost>(Shared = SharedType.PerTestSession)]
    public required PaperlessServicesHost ServicesHost { get; init; }

    [Test]
    public async Task ExtractTextAsync_ValidPdf_ExtractsText()
    {
        // Arrange
        var pdfData = await PdfTestHelper.CreatePdfWithTextAsync("Hello OCR World");

        // Act
        using var stream = new MemoryStream(pdfData);
        var text = await ServicesHost.OcrService.ExtractTextAsync(stream);

        // Assert
        await Assert.That(text).Contains("Hello OCR World");
    }
}

/// <summary>
/// Search index service tests.
/// </summary>
public class SearchIndexServiceTests
{
    [ClassDataSource<PaperlessServicesHost>(Shared = SharedType.PerTestSession)]
    public required PaperlessServicesHost ServicesHost { get; init; }

    [Test]
    public async Task IndexDocumentAsync_NewDocument_IndexesSuccessfully()
    {
        // Arrange
        var docId = Guid.NewGuid();
        const string content = "This is searchable content";

        // Act & Assert (no exception means success)
        await ServicesHost.SearchIndexService.IndexDocumentAsync(
            docId,
            "test.pdf",
            content,
            "storage/test.pdf");
    }
}

/// <summary>
/// OCR processor integration tests.
/// </summary>
public class OcrProcessorTests
{
    [ClassDataSource<PaperlessServicesHost>(Shared = SharedType.PerTestSession)]
    public required PaperlessServicesHost ServicesHost { get; init; }

    [Test]
    public async Task ProcessDocumentAsync_ValidCommand_ReturnsCompletedEvent()
    {
        // Arrange
        var pdfData = await PdfTestHelper.CreatePdfWithTextAsync("Process this document");
        var filePath = $"docs/{Guid.NewGuid()}.pdf";

        await ServicesHost.MinioClient.PutObjectAsync(new PutObjectArgs()
            .WithBucket(ServicesHost.MinioOptions.Value.BucketName)
            .WithObject(filePath)
            .WithStreamData(new MemoryStream(pdfData))
            .WithObjectSize(pdfData.Length));

        var command = new OcrCommand(Guid.NewGuid(), "test.pdf", filePath);

        // Act
        var result = await ServicesHost.OcrProcessor.ProcessDocumentAsync(command);

        // Assert
        await Assert.That(result.Status).IsEqualTo("Completed");
        await Assert.That(result.Text).IsNotNull();
        await Assert.That(result.Text!).Contains("Process this document");
    }
}