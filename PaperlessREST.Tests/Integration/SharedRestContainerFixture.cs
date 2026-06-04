using PaperlessREST.Host;

[assembly: CaptureConsole]
[assembly: CaptureTrace]

namespace PaperlessREST.Tests.Integration;

public sealed class SharedRestContainerFixture : ContainerFixtureBase
{
	static SharedRestContainerFixture() => TestEnv.Load();

	protected override bool UsesPostgres => true;

	public HttpClient Client { get; private set; } = null!;
	public IDbContextFactory<DocumentPersistence> DbFactory { get; private set; } = null!;

	public AsyncServiceScope CreateAsyncScope() => Services.CreateAsyncScope();

	private WebApplicationFactory<Program>? _factory;

	protected override async ValueTask ConfigureSutAsync()
	{
		// Point the REST host's infra config at the Testcontainers endpoints via environment
		// variables. This is deliberate, not a regression: WebApplicationFactory + minimal hosting
		// builds the app's own configuration (including the environment-variable source that
		// `.env.test` populates process-globally), and that source OUTRANKS anything the factory adds
		// via ConfigureAppConfiguration — even Sources.Clear() only touches the host-config layer, so
		// an in-memory override is silently beaten by `.env.test`'s RABBITMQ__URI=localhost:5672 and
		// every endpoint 500s (BrokerUnreachable). Setting the env vars to the real container values is
		// the only thing the WAF host actually reads. (The Services fixture, a plain Host builder, can
		// and does use Sources.Clear()+AddInMemoryCollection — minimal-hosting WAF cannot.)
		Environment.SetEnvironmentVariable("CONNECTIONSTRINGS__PAPERLESSDB", PostgresConnectionString);
		Environment.SetEnvironmentVariable("CONNECTIONSTRINGS__HANGFIRE", PostgresConnectionString);
		Environment.SetEnvironmentVariable("RABBITMQ__URI", RabbitConnectionString);
		Environment.SetEnvironmentVariable("STORAGE__MINIO__ENDPOINT", MinioEndpoint);
		Environment.SetEnvironmentVariable("STORAGE__MINIO__ACCESSKEY", MinioAccessKey);
		Environment.SetEnvironmentVariable("STORAGE__MINIO__SECRETKEY", MinioSecretKey);
		Environment.SetEnvironmentVariable("STORAGE__MINIO__BUCKETNAME", BucketName);
		Environment.SetEnvironmentVariable("ELASTICSEARCH__URI", ElasticsearchUri);
		Environment.SetEnvironmentVariable("ELASTICSEARCH__DEFAULTINDEX", IndexName);

		_factory = new ConfiguredWebApplicationFactory(PostgresConnectionString);

		Client = _factory.CreateClient();
		Services = _factory.Services;
		DbFactory = Services.GetRequiredService<IDbContextFactory<DocumentPersistence>>();

		await using var db = await DbFactory.CreateDbContextAsync();
		await db.Database.MigrateAsync();
	}

	protected override async ValueTask DisposeSutAsync()
	{
		if (_factory is not null)
			await _factory.DisposeAsync();
	}

	private sealed class ConfiguredWebApplicationFactory(string postgresConnectionString)
		: WebApplicationFactory<Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			builder.UseEnvironment("Test");

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
