using Testcontainers.Elasticsearch;
using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Paperless.Tests.TUnit.Infrastructure;

/// <summary>
/// Test factory that provides containerized infrastructure and web application setup for integration tests
/// </summary>
public sealed class PaperlessTestFactory : WebApplicationFactory<PaperlessREST.Program>, IAsyncInitializer, IAsyncDisposable
{
    // Test containers
    private readonly PostgreSqlContainer _postgres;
    private readonly RabbitMqContainer _rabbitMq;
    private readonly MinioContainer _minio;
    private readonly ElasticsearchContainer _elasticsearch;
    
    // Worker host for background services
    private IHost? _workerHost;
    
    // Configuration values from testconfig.json
    private readonly string _bucketName;
    private readonly string _indexName;
    private readonly LogLevel _minimumLogLevel;
    
    public PaperlessTestFactory()
    {
        // Load configuration from testconfig.json
        var config = TestContext.Configuration;
        _bucketName = config.Get("MinIO:BucketName") ?? "test-documents";
        _indexName = config.Get("Elasticsearch:IndexName") ?? "test-index";
        _minimumLogLevel = Enum.Parse<LogLevel>(config.Get("Logging:MinimumLevel") ?? "Information");
        
        // Initialize containers
        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:15.1")
            .Build();
            
        _rabbitMq = new RabbitMqBuilder()
            .WithImage("rabbitmq:4-management-alpine")
            .Build();
            
        _minio = new MinioBuilder()
            .WithImage("minio/minio:latest")
            .Build();
            
        _elasticsearch = new ElasticsearchBuilder()
            .WithImage("elasticsearch:9.0.3")
            .WithEnvironment("xpack.security.enabled", "false")
            .Build();
    }
    
    /// <summary>
    /// Initialize all containers and the web server
    /// </summary>
    public async Task InitializeAsync()
    {
        // Start all containers in parallel
        await Task.WhenAll(
            _postgres.StartAsync(),
            _rabbitMq.StartAsync(),
            _minio.StartAsync(),
            _elasticsearch.StartAsync()
        );
        
        // Force server initialization (thread-safe)
        _ = Server;
    }
    
    /// <summary>
    /// Configure the web host with test container connection strings
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        
        // Configure test settings
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PaperlessDb"] = _postgres.GetConnectionString(),
                ["RabbitMQ:Uri"] = _rabbitMq.GetConnectionString(),
                ["Storage:Minio:Endpoint"] = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}",
                ["Storage:Minio:AccessKey"] = _minio.GetAccessKey(),
                ["Storage:Minio:SecretKey"] = _minio.GetSecretKey(),
                ["Storage:Minio:BucketName"] = _bucketName,
                ["Storage:Minio:UseSsl"] = "false",
                ["Elasticsearch:Uri"] = $"http://{_elasticsearch.Hostname}:{_elasticsearch.GetMappedPublicPort(9200)}",
                ["Elasticsearch:IndexName"] = _indexName
            });
        });
        
        // Configure services (override if needed for testing)
        builder.ConfigureServices(services =>
        {
            // Add any service overrides here
            // For example, you could mock specific services for certain test scenarios
        });
        
        // Configure logging to use TUnit's output
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new TUnitLoggerProvider(_minimumLogLevel));
            logging.SetMinimumLevel(_minimumLogLevel);
        });
    }
    
    /// <summary>
    /// Start the worker host for background services
    /// </summary>
    public void StartWorkerHost()
    {
        if (_workerHost is not null)
            return;
            
        var builder = Host.CreateApplicationBuilder();
        
        // Configure worker with test container settings
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Uri"] = _rabbitMq.GetConnectionString(),
            ["Storage:Minio:Endpoint"] = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}",
            ["Storage:Minio:AccessKey"] = _minio.GetAccessKey(),
            ["Storage:Minio:SecretKey"] = _minio.GetSecretKey(),
            ["Storage:Minio:BucketName"] = _bucketName,
            ["Storage:Minio:UseSsl"] = "false",
            ["Elasticsearch:Uri"] = $"http://{_elasticsearch.Hostname}:{_elasticsearch.GetMappedPublicPort(9200)}",
            ["Elasticsearch:IndexName"] = _indexName
        });
        
        // Configure logging
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new TUnitLoggerProvider(_minimumLogLevel));
        builder.Logging.SetMinimumLevel(_minimumLogLevel);
        
        // Add OCR services (this method should be in PaperlessServices project)
        // builder.Services.AddOcrServices(builder.Configuration);
        
        _workerHost = builder.Build();
        _workerHost.Start();
    }
    
    /// <summary>
    /// Get connection details for direct container access if needed
    /// </summary>
    public ContainerConnectionInfo GetConnectionInfo() => new()
    {
        PostgresConnectionString = _postgres.GetConnectionString(),
        RabbitMqUri = _rabbitMq.GetConnectionString(),
        MinioEndpoint = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}",
        MinioAccessKey = _minio.GetAccessKey(),
        MinioSecretKey = _minio.GetSecretKey(),
        ElasticsearchUri = $"http://{_elasticsearch.Hostname}:{_elasticsearch.GetMappedPublicPort(9200)}"
    };
    
    /// <summary>
    /// Clean up all resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_workerHost is not null)
        {
            await _workerHost.StopAsync();
            _workerHost.Dispose();
        }
        
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _rabbitMq.DisposeAsync().AsTask(),
            _minio.DisposeAsync().AsTask(),
            _elasticsearch.DisposeAsync().AsTask()
        );
        
        // Dispose the base WebApplicationFactory
        base.Dispose();
    }
}

/// <summary>
/// Container connection information for tests that need direct access
/// </summary>
public record ContainerConnectionInfo
{
    public required string PostgresConnectionString { get; init; }
    public required string RabbitMqUri { get; init; }
    public required string MinioEndpoint { get; init; }
    public required string MinioAccessKey { get; init; }
    public required string MinioSecretKey { get; init; }
    public required string ElasticsearchUri { get; init; }
}
