using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Paperless.Tests;

public sealed class TestContainersManager : IAsyncInitializer
{
    private readonly IContainer _postgres;
    private readonly IContainer _rabbitMq;
    private readonly IContainer _minio;
    private readonly IContainer _elasticsearch;

    private bool _initialized;
    private IReadOnlyDictionary<string, string?>? _configuration;

    public TestContainersManager()
    {
        // Use generic containers to avoid built-in health checks
        _postgres = new ContainerBuilder()
            .WithImage("postgres:15.1")
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PASSWORD", "postgres")
            .WithEnvironment("POSTGRES_DB", "postgres")
            .WithLogger(new TestLogger("PostgreSQL"))
            .WithPortBinding(5432, true)
            .Build();

        _rabbitMq = new ContainerBuilder()
            .WithImage("rabbitmq:4-management-alpine")
            .WithLogger(new TestLogger("RabbitMQ"))
            .WithPortBinding(5672, true)
            .WithPortBinding(15672, true)
            .Build();

        _minio = new ContainerBuilder()
            .WithImage("minio/minio:latest")
            .WithCommand("server", "/data")
            .WithEnvironment("MINIO_ROOT_USER", "minioadmin")
            .WithEnvironment("MINIO_ROOT_PASSWORD", "minioadmin")
            .WithLogger(new TestLogger("MinIO"))
            .WithPortBinding(9000, true)
            .Build();

        _elasticsearch = new ContainerBuilder()
            .WithImage("elasticsearch:8.13.4")
            .WithEnvironment("xpack.security.enabled", "false")
            .WithEnvironment("discovery.type", "single-node")
            .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
            .WithLogger(new TestLogger("Elasticsearch"))
            .WithPortBinding(9200, true)
            .WithPortBinding(9300, true)
            .Build();
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        Console.WriteLine("Starting test containers...");

        try
        {
            await Task.WhenAll(
                _postgres.StartAsync(),
                _rabbitMq.StartAsync(),
                _minio.StartAsync(),
                _elasticsearch.StartAsync()
            );

            Console.WriteLine("All containers started. Waiting for them to be ready...");
            // Wait longer for containers to be fully ready, especially RabbitMQ and Elasticsearch
            await Task.Delay(15000);

            Console.WriteLine("Building configuration...");
            Console.WriteLine($"PostgreSQL - State: {_postgres.State}, ID: {_postgres.Id}, Name: {_postgres.Name}");
            Console.WriteLine($"RabbitMQ - State: {_rabbitMq.State}, ID: {_rabbitMq.Id}, Name: {_rabbitMq.Name}");
            Console.WriteLine($"MinIO - State: {_minio.State}, ID: {_minio.Id}, Name: {_minio.Name}");
            Console.WriteLine(
                $"Elasticsearch - State: {_elasticsearch.State}, ID: {_elasticsearch.Id}, Name: {_elasticsearch.Name}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting containers: {ex}");
            throw;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var testId = Guid.NewGuid().ToString("N").Substring(0, 8);

        var config = new Dictionary<string, string?>();

        try
        {
            Console.WriteLine("Getting PostgreSQL port mapping...");
            var pgPort = _postgres.GetMappedPublicPort(5432);
            Console.WriteLine($"PostgreSQL mapped port: {pgPort}");
            config["ASPNETCORE_ENVIRONMENT"] = "Testing";
            // Use a unique database name per test session to avoid conflicts
            config["ConnectionStrings:PaperlessDb"] =
                $"Host={_postgres.Hostname};Port={pgPort};Database=testdb_{testId};Username=postgres;Password=postgres";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting PostgreSQL port: {ex}");
            throw;
        }

        try
        {
            Console.WriteLine("Getting RabbitMQ port mapping...");
            var rabbitPort = _rabbitMq.GetMappedPublicPort(5672);
            Console.WriteLine($"RabbitMQ mapped port: {rabbitPort}");
            config["RabbitMQ:Uri"] = $"amqp://guest:guest@{_rabbitMq.Hostname}:{rabbitPort}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting RabbitMQ port: {ex}");
            throw;
        }

        try
        {
            Console.WriteLine("Getting MinIO port mapping...");
            var minioPort = _minio.GetMappedPublicPort(9000);
            Console.WriteLine($"MinIO mapped port: {minioPort}");
            config["Storage:Minio:Endpoint"] = $"{_minio.Hostname}:{minioPort}";
            config["Storage:Minio:AccessKey"] = "minioadmin";
            config["Storage:Minio:SecretKey"] = "minioadmin";
            config["Storage:Minio:BucketName"] = $"test{timestamp}";
            config["Storage:Minio:UseSsl"] = "false";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting MinIO port: {ex}");
            throw;
        }

        try
        {
            Console.WriteLine("Getting Elasticsearch port mapping...");
            var esPort = _elasticsearch.GetMappedPublicPort(9200);
            Console.WriteLine($"Elasticsearch mapped port: {esPort}");
            config["Elasticsearch:Uri"] = $"http://{_elasticsearch.Hostname}:{esPort}";
            config["Elasticsearch:IndexName"] = "test-index";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting Elasticsearch port: {ex}");
            throw;
        }

        _configuration = config;
        _initialized = true;
    }

    public IReadOnlyDictionary<string, string?> GetConfiguration() => _configuration!;
}

public sealed class PaperlessWebApplication : WebApplicationFactory<PaperlessREST.DocumentRepository>, IAsyncInitializer
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
        builder.UseEnvironment("Testing");

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

    protected HttpClient Client => Application.CreateClient();
}