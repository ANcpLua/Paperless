using PaperlessREST.Host;

[assembly: CaptureConsole]
[assembly: CaptureTrace]

namespace PaperlessREST.Tests.Integration;

public sealed class SharedRestContainerFixture : IAsyncLifetime
{
	#region Static Constructor

	static SharedRestContainerFixture()
	{
		Env.TraversePath().Load(".env.test");
	}

	#endregion

	#region Constructor

	public SharedRestContainerFixture()
	{
		var postgresImage = Environment.GetEnvironmentVariable("POSTGRES_IMAGE") ?? "postgres:17-alpine";
		var rabbitImage = Environment.GetEnvironmentVariable("RABBITMQ_IMAGE") ?? "rabbitmq:4.3.0-management";
		var minioImage = Environment.GetEnvironmentVariable("MINIO_IMAGE") ??
		                 "minio/minio:RELEASE.2025-09-07T16-13-09Z";
		var elasticImage = Environment.GetEnvironmentVariable("ELASTIC_IMAGE") ??
		                   "docker.elastic.co/elasticsearch/elasticsearch:9.1.3";

		_postgres = new PostgreSqlBuilder(postgresImage)
			.WithWaitStrategy(Wait.ForUnixContainer()
				.UntilMessageIsLogged("database system is ready to accept connections"))
			.Build();

		_rabbit = new RabbitMqBuilder(rabbitImage)
			.Build();

		_minio = new MinioBuilder(minioImage)
			.Build();

		_elastic = new ElasticsearchBuilder(elasticImage)
			.WithEnvironment("discovery.type", "single-node")
			.WithEnvironment("xpack.security.enabled", "false")
			.WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
			.WithEnvironment("bootstrap.memory_lock", "false")
			.WithWaitStrategy(Wait.ForUnixContainer()
				.UntilMessageIsLogged("started"))
			.Build();
	}

	#endregion

	#region Public Methods

	public AsyncServiceScope CreateAsyncScope() => Services.CreateAsyncScope();

	#endregion

	#region Fields

	private readonly string _bucketName = $"test-{Guid.NewGuid():N}";
	private readonly ElasticsearchContainer _elastic;
	private readonly MinioContainer _minio;
	private readonly PostgreSqlContainer _postgres;
	private readonly RabbitMqContainer _rabbit;

	private WebApplicationFactory<Program>? _factory;
	private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Factory not initialized.");

	#endregion

	#region Properties

	public HttpClient Client { get; private set; } = null!;
	public IServiceProvider Services { get; private set; } = null!;
	public IDbContextFactory<DocumentPersistence> DbFactory { get; private set; } = null!;

	#endregion

	#region IAsyncLifetime

	public async ValueTask InitializeAsync()
	{
		await Task.WhenAll(
			_postgres.StartAsync(),
			_rabbit.StartAsync(),
			_minio.StartAsync(),
			_elastic.StartAsync()
		);

		Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
		Environment.SetEnvironmentVariable("CONNECTIONSTRINGS__PAPERLESSDB", _postgres.GetConnectionString());
		Environment.SetEnvironmentVariable("CONNECTIONSTRINGS__HANGFIRE", _postgres.GetConnectionString());
		Environment.SetEnvironmentVariable("RABBITMQ__URI", _rabbit.GetConnectionString());
		var minioEndpoint = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(9000)}";
		Environment.SetEnvironmentVariable("STORAGE__MINIO__ENDPOINT", minioEndpoint);
		Environment.SetEnvironmentVariable("STORAGE__MINIO__ACCESSKEY", _minio.GetAccessKey());
		Environment.SetEnvironmentVariable("STORAGE__MINIO__SECRETKEY", _minio.GetSecretKey());
		Environment.SetEnvironmentVariable("STORAGE__MINIO__BUCKETNAME", _bucketName);
		Environment.SetEnvironmentVariable("ELASTICSEARCH__URI",
			$"http://{_elastic.Hostname}:{_elastic.GetMappedPublicPort(9200)}");

		using MinioClient minioClient = new();
		minioClient
			.WithEndpoint(minioEndpoint)
			.WithCredentials(_minio.GetAccessKey(), _minio.GetSecretKey())
			.Build();
		await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));

		_factory = new ConfiguredWebApplicationFactory(_postgres.GetConnectionString());

		Client = Factory.CreateClient();
		Services = Factory.Services;
		DbFactory = Services.GetRequiredService<IDbContextFactory<DocumentPersistence>>();

		await using var db = await DbFactory.CreateDbContextAsync();
		await db.Database.MigrateAsync();
	}

	public async ValueTask DisposeAsync()
	{
		if (_factory is not null)
			await _factory.DisposeAsync();
		await Task.WhenAll(
			_postgres.DisposeAsync().AsTask(),
			_rabbit.DisposeAsync().AsTask(),
			_minio.DisposeAsync().AsTask(),
			_elastic.DisposeAsync().AsTask()
		);
	}

	#endregion

	private sealed class ConfiguredWebApplicationFactory(string postgresConnectionString)
		: WebApplicationFactory<Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			builder.ConfigureTestServices(services =>
			{
				services.RemoveAll<IHostedService>();

				services.RemoveAll<IDbContextFactory<DocumentPersistence>>();

				var dataSource = new NpgsqlDataSourceBuilder(postgresConnectionString)
					.MapEnum<DocumentStatus>("document_status")
					.Build();

				services.AddPooledDbContextFactory<DocumentPersistence>(opts =>
					opts.UseNpgsql(dataSource));

				services.RemoveAll<JobStorage>();
				services.AddSingleton<JobStorage>(new MemoryStorage());

				services.AddFakeLogging();
			});
		}
	}
}
