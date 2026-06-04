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
		_factory = new ConfiguredWebApplicationFactory(
			PostgresConnectionString,
			RabbitConnectionString,
			MinioEndpoint,
			MinioAccessKey,
			MinioSecretKey,
			BucketName,
			ElasticsearchUri,
			IndexName);

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

	private sealed class ConfiguredWebApplicationFactory(
		string postgresConnectionString,
		string rabbitConnectionString,
		string minioEndpoint,
		string minioAccessKey,
		string minioSecretKey,
		string bucketName,
		string elasticsearchUri,
		string indexName)
		: WebApplicationFactory<Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			// Replaces the old Environment.SetEnvironmentVariable(...) global mutation.
			// WebApplicationFactory reads these via the host's IConfiguration just like
			// the Services fixture's AddInMemoryCollection. Colon-keyed to match the
			// option binding (ConnectionStrings:*, Storage:Minio:*, Elasticsearch:*).
			builder.UseEnvironment("Test");
			builder.ConfigureAppConfiguration((_, config) =>
			{
				config.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["ConnectionStrings:PaperlessDb"] = postgresConnectionString,
					["ConnectionStrings:Hangfire"] = postgresConnectionString,
					["RabbitMQ:Uri"] = rabbitConnectionString,
					["Storage:Minio:Endpoint"] = minioEndpoint,
					["Storage:Minio:AccessKey"] = minioAccessKey,
					["Storage:Minio:SecretKey"] = minioSecretKey,
					["Storage:Minio:BucketName"] = bucketName,
					["Storage:Minio:UseSsl"] = "false",
					["Elasticsearch:Uri"] = elasticsearchUri,
					// [Required] + ValidateOnStart on ElasticsearchOptions.DefaultIndex → the REST host
					// throws at CreateClient() without it. Thread base IndexName through the factory ctor
					// so env-var dependence is TRULY removed — not silently satisfied by .env.test's
					// ELASTICSEARCH__DEFAULTINDEX process env.
					["Elasticsearch:DefaultIndex"] = indexName
				});
			});

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
