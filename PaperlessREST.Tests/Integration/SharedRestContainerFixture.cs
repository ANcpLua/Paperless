using PaperlessREST.Host;
using System.Runtime.ExceptionServices;

[assembly: CaptureConsole]
[assembly: CaptureTrace]

namespace PaperlessREST.Tests.Integration;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedRestContainerCollection : ICollectionFixture<SharedRestContainerFixture>
{
	public const string Name = "Shared REST containers";
}

public sealed class SharedRestContainerFixture() : ContainerFixtureBase(usesPostgres: true)
{
	static SharedRestContainerFixture() => TestEnv.Load();
	private readonly Dictionary<string, string?> _originalEnvironment = new(StringComparer.OrdinalIgnoreCase);

	public HttpClient Client { get; private set; } = null!;
	public IDbContextFactory<DocumentPersistence> DbFactory { get; private set; } = null!;

	public AsyncServiceScope CreateAsyncScope() => Services.CreateAsyncScope();

	private WebApplicationFactory<Program>? _factory;
	private string? _batchRoot;

	protected override async ValueTask ConfigureSutAsync()
	{
		// WebApplicationFactory's environment provider outranks test-host configuration,
		// so its process environment must contain the real container endpoints.
		OverrideEnvironmentVariable("CONNECTIONSTRINGS__PAPERLESSDB", PostgresConnectionString);
		OverrideEnvironmentVariable("CONNECTIONSTRINGS__HANGFIRE", PostgresConnectionString);
		OverrideEnvironmentVariable("RABBITMQ__URI", RabbitConnectionString);
		OverrideEnvironmentVariable("STORAGE__MINIO__ENDPOINT", MinioEndpoint);
		OverrideEnvironmentVariable("STORAGE__MINIO__ACCESSKEY", MinioAccessKey);
		OverrideEnvironmentVariable("STORAGE__MINIO__SECRETKEY", MinioSecretKey);
		OverrideEnvironmentVariable("STORAGE__MINIO__BUCKETNAME", BucketName);
		OverrideEnvironmentVariable("ELASTICSEARCH__URI", ElasticsearchUri);
		OverrideEnvironmentVariable("ELASTICSEARCH__DEFAULTINDEX", IndexName);

		_batchRoot = Path.Combine(Path.GetTempPath(), $"paperless-batch-{Guid.NewGuid():N}");
		OverrideEnvironmentVariable("BATCH__INPUTPATH", Path.Combine(_batchRoot, "input"));
		OverrideEnvironmentVariable("BATCH__ARCHIVEPATH", Path.Combine(_batchRoot, "archive"));
		OverrideEnvironmentVariable("BATCH__ERRORPATH", Path.Combine(_batchRoot, "error"));
		OverrideEnvironmentVariable("BATCH__FILEPATTERN", "*.xml");
		OverrideEnvironmentVariable("BATCH__CRONEXPRESSION", "0 2 * * *");
		OverrideEnvironmentVariable("BATCH__TIMEZONEID", "UTC");

		_factory = new ConfiguredWebApplicationFactory(PostgresConnectionString);

		Client = _factory.CreateClient();
		Services = _factory.Services;
		DbFactory = Services.GetRequiredService<IDbContextFactory<DocumentPersistence>>();

		await using var db = await DbFactory.CreateDbContextAsync();
		await db.Database.MigrateAsync();
	}

	protected override async ValueTask DisposeSutAsync()
	{
		List<Exception> failures = [];

		try
		{
			if (_factory is not null)
			{
				await _factory.DisposeAsync();
			}
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}

		foreach ((string name, string? value) in _originalEnvironment)
		{
			try
			{
				Environment.SetEnvironmentVariable(name, value);
			}
			catch (Exception exception)
			{
				failures.Add(exception);
			}
		}
		_originalEnvironment.Clear();

		try
		{
			if (_batchRoot is not null && Directory.Exists(_batchRoot))
			{
				Directory.Delete(_batchRoot, recursive: true);
			}
		}
		catch (Exception exception)
		{
			failures.Add(exception);
		}

		if (failures.Count > 1)
		{
			throw new AggregateException("REST fixture teardown failed.", failures);
		}

		if (failures.Count == 1)
		{
			ExceptionDispatchInfo.Capture(failures[0]).Throw();
		}
	}

	private void OverrideEnvironmentVariable(string name, string value)
	{
		_originalEnvironment.TryAdd(name, Environment.GetEnvironmentVariable(name));
		Environment.SetEnvironmentVariable(name, value);
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
