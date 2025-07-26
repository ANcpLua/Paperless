namespace Paperless.Tests;

public sealed class TestContainersManager : IAsyncInitializer
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();
    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder().Build();
    private readonly MinioContainer _minio = new MinioBuilder().Build();

    private readonly ElasticsearchContainer _elasticsearch = new ElasticsearchBuilder()
        .WithImage("elasticsearch:9.0.3")
        .WithEnvironment("xpack.security.enabled", "false")
        .WithEnvironment("discovery.type", "single-node")
        .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
        .Build();

    private bool _initialized = false;
    private IReadOnlyDictionary<string, string?>? _configuration;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await Task.WhenAll(
            _postgres.StartAsync(),
            _rabbitMq.StartAsync(),
            _minio.StartAsync(),
            _elasticsearch.StartAsync()
        );

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _configuration = new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["ConnectionStrings:PaperlessDb"] = _postgres.GetConnectionString(),
            ["RabbitMQ:Uri"] = _rabbitMq.GetConnectionString(),
            ["Storage:Minio:Endpoint"] = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}",
            ["Storage:Minio:AccessKey"] = _minio.GetAccessKey(),
            ["Storage:Minio:SecretKey"] = _minio.GetSecretKey(),
            ["Storage:Minio:BucketName"] = $"test{timestamp}",
            ["Storage:Minio:UseSsl"] = "false",
            ["Elasticsearch:Uri"] = $"http://{_elasticsearch.Hostname}:{_elasticsearch.GetMappedPublicPort(9200)}",
            ["Elasticsearch:IndexName"] = "test-index"
        };

        _initialized = true;
    }

    public IReadOnlyDictionary<string, string?> GetConfiguration() => _configuration!;
}

public sealed class PaperlessWebApplication : WebApplicationFactory<Program>, IAsyncInitializer
{
    [ClassDataSource<TestContainersManager>(Shared = SharedType.PerTestSession)]
    public required TestContainersManager Containers { get; init; }

    public async Task InitializeAsync()
    {
        await Containers.InitializeAsync();
        _ = Server;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        foreach (var kvp in Containers.GetConfiguration())
        {
            builder.UseSetting(kvp.Key, kvp.Value);
        }
    }
}

public abstract class IntegrationTestBase
{
    [ClassDataSource<PaperlessWebApplication>(Shared = SharedType.PerTestSession)]
    public required PaperlessWebApplication Application { get; init; }

    /// <summary>
    /// Gets an HTTP client configured for testing.
    /// </summary>
    protected HttpClient CreateClient() => Application.CreateClient();
}

public static class PdfTestHelper
{
    /// <summary>
    /// Creates a simple PDF for testing.
    ///  All we need no multiple pages or complex layouts, just a single page with text.
    ///  use this as single source of truth for PDF generation in tests.
    ///  This will be used to test PDF upload and processing, nothing more.
    /// </summary>
    public static async Task<byte[]> CreateTestPdf()
    {
        var path = await Pdf.Create()
            .AddText("Hello World!")
            .SaveAsync("Test.Pdf");

        return await File.ReadAllBytesAsync(path);
    }
}