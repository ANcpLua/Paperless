using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Minio;
using Minio.DataModel.Args;
using Testcontainers.Elasticsearch;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using TUnit.Core.Interfaces;

namespace Paperless.Tests;

/// <summary>
/// Manages the lifecycle of test containers for integration testing.
/// </summary>
public sealed class TestContainersManager : IAsyncInitializer
{
    private readonly PostgreSqlContainer _postgres;
    private readonly RabbitMqContainer _rabbitMq;
    private readonly MinioContainer _minio;
    private readonly ElasticsearchContainer _elasticsearch;
    private readonly string _minioBucket;
    private readonly string _elasticsearchIndex;

    public TestContainersManager()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase(GetRequiredConfig("POSTGRES_DB"))
            .WithUsername(GetRequiredConfig("POSTGRES_USER"))
            .WithPassword(GetRequiredConfig("POSTGRES_PASSWORD"))
            .Build();

        _rabbitMq = new RabbitMqBuilder()
            .WithUsername(GetRequiredConfig("RABBITMQ_USER"))
            .WithPassword(GetRequiredConfig("RABBITMQ_PASSWORD"))
            .Build();

        _minio = new MinioBuilder()
            .WithUsername(GetRequiredConfig("MINIO_ROOT_USER"))
            .WithPassword(GetRequiredConfig("MINIO_ROOT_PASSWORD"))
            .Build();

        _minioBucket = GetRequiredConfig("MINIO_BUCKET");

        _elasticsearch = new ElasticsearchBuilder()
            .WithImage($"elasticsearch:{GetRequiredConfig("ELASTICSEARCH_VERSION")}")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .Build();

        _elasticsearchIndex = GetRequiredConfig("ELASTICSEARCH_INDEXNAME");
    }

    /// <summary>
    /// Initializes all containers and prepares them for testing.
    /// </summary>
    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _postgres.StartAsync(),
            _rabbitMq.StartAsync(),
            _minio.StartAsync(),
            _elasticsearch.StartAsync()
        );

        await CreateMinioBucketAsync();
    }

    /// <summary>
    /// Gets the configuration values for the application under test.
    /// </summary>
    public IReadOnlyDictionary<string, string?> GetConfiguration()
    {
        return new Dictionary<string, string?>
        {
            ["ASPNETCORE_ENVIRONMENT"] = GetRequiredConfig("ASPNETCORE_ENVIRONMENT"),
            ["ConnectionStrings:PaperlessDb"] = _postgres.GetConnectionString(),
            ["PostgreSQL__ConnectionString"] = _postgres.GetConnectionString(),
            ["RabbitMQ__Uri"] = BuildRabbitMqUri(),
            ["Storage__Minio__Endpoint"] = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}",
            ["Storage__Minio__AccessKey"] = _minio.GetAccessKey(),
            ["Storage__Minio__SecretKey"] = _minio.GetSecretKey(),
            ["Storage__Minio__BucketName"] = _minioBucket,
            ["Storage__Minio__UseSsl"] = "false",
            ["Elasticsearch__Uri"] = $"http://{_elasticsearch.Hostname}:{_elasticsearch.GetMappedPublicPort(9200)}",
            ["Elasticsearch__IndexName"] = _elasticsearchIndex
        };
    }

    private async Task CreateMinioBucketAsync()
    {
        var minioClient = new MinioClient()
            .WithEndpoint(_minio.GetConnectionString().Replace("http://", ""))
            .WithCredentials(_minio.GetAccessKey(), _minio.GetSecretKey())
            .WithSSL(false)
            .Build();

        var bucketExistsArgs = new BucketExistsArgs().WithBucket(_minioBucket);
        if (!await minioClient.BucketExistsAsync(bucketExistsArgs))
        {
            var makeBucketArgs = new MakeBucketArgs().WithBucket(_minioBucket);
            await minioClient.MakeBucketAsync(makeBucketArgs);
        }
    }

    private string BuildRabbitMqUri()
    {
        var user = GetRequiredConfig("RABBITMQ_USER");
        var password = GetRequiredConfig("RABBITMQ_PASSWORD");
        var hostname = _rabbitMq.Hostname;
        var port = _rabbitMq.GetMappedPublicPort(5672);

        return $"amqp://{user}:{password}@{hostname}:{port}";
    }

    private static string GetRequiredConfig(string key)
    {
        return TestContext.Configuration.Get(key)
               ?? throw new InvalidOperationException($"Configuration key '{key}' is missing from testconfig.json");
    }
}

/// <summary>
/// Base class for integration tests providing common test infrastructure.
/// </summary>
public abstract class IntegrationTestBase
{
    [ClassDataSource<PaperlessWebApplication>(Shared = SharedType.PerTestSession)]
    public required PaperlessWebApplication Application { get; init; }

    /// <summary>
    /// Gets an HTTP client configured for testing.
    /// </summary>
    protected HttpClient CreateClient() => Application.CreateClient();
}

/// <summary>
/// Provides a configured test server for Paperless API integration tests.
/// </summary>
public sealed class PaperlessWebApplication : WebApplicationFactory<Program>, IAsyncInitializer
{
    [ClassDataSource<TestContainersManager>(Shared = SharedType.PerTestSession)]
    public required TestContainersManager Containers { get; init; }

    public Task InitializeAsync() => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(Containers.GetConfiguration());
        });
    }
}