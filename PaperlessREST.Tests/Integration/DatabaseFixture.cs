namespace PaperlessREST.Tests.Integration;

public sealed class DatabaseFixture : IAsyncLifetime
{
	#region Fields

	private readonly PostgreSqlContainer _container;

	#endregion

	#region Static Constructor

	static DatabaseFixture()
	{
		Env.TraversePath().Load(".env.test");
	}

	#endregion

	#region Constructor

	public DatabaseFixture()
	{
		string postgresImage = Environment.GetEnvironmentVariable("POSTGRES_IMAGE") ?? "postgres:16-alpine";

		_container = new PostgreSqlBuilder()
			.WithImage(postgresImage)
			.WithWaitStrategy(Wait.ForUnixContainer()
				.UntilMessageIsLogged("database system is ready to accept connections"))
			.Build();
	}

	#endregion

	#region Public Methods

	public AsyncServiceScope CreateAsyncScope() => Services.CreateAsyncScope();

	#endregion

	#region Properties

	public IServiceProvider Services { get; private set; } = null!;
	public IDbContextFactory<DocumentPersistence> ContextFactory { get; private set; } = null!;
	public FakeLogCollector LogCollector { get; private set; } = null!;

	#endregion

	#region IAsyncLifetime

	public async ValueTask InitializeAsync()
	{
		await _container.StartAsync();

		ServiceCollection services = [];

		// Use cross-platform temp directory with unique suffix to avoid test collisions
		string batchTestBase = Path.Combine(Path.GetTempPath(), $"batch-test-{Guid.NewGuid():N}");

		IConfigurationRoot configuration = new ConfigurationBuilder()
			.AddEnvironmentVariables() // Add first so in-memory values override
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["ConnectionStrings:PaperlessDb"] = _container.GetConnectionString(),
				["ConnectionStrings:Hangfire"] = _container.GetConnectionString(),
				// Batch processing paths for tests - cross-platform temp directory
				["Batch:InputPath"] = Path.Combine(batchTestBase, "input"),
				["Batch:ArchivePath"] = Path.Combine(batchTestBase, "archive"),
				["Batch:ErrorPath"] = Path.Combine(batchTestBase, "error"),
				["Batch:FilePattern"] = "*.xml",
				["Batch:CronExpression"] = "0 1 * * *",
				["Batch:TimeZoneId"] = "UTC"
			})
			.Build();

		services.AddSingleton<IConfiguration>(configuration);

		NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(_container.GetConnectionString())
			.MapEnum<DocumentStatus>("document_status")
			.Build();

		services.AddPooledDbContextFactory<DocumentPersistence>(options =>
		{
			options.UseNpgsql(dataSource, npgsql => npgsql.MapEnum<DocumentStatus>("document_status"));
			options.EnableSensitiveDataLogging();
			// Suppress pending model changes warning for test environment
			options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
		});

		services.AddOptions<BatchOptions>()
			.BindConfiguration(BatchOptions.SectionName)
			.ValidateDataAnnotations();

		services.AddSingleton<IFileSystem, RealFileSystem>();
		services.AddSingleton<JobStorage>(new MemoryStorage());
		services.AddSingleton(TimeProvider.System);
		services.AddTransient<IDocumentRepository, DocumentRepository>();

		// Batch orchestrator and services - 4 component architecture
		//services.AddValidatorsFromAssemblyContaining<BatchOptions>(ServiceLifetime.Singleton);

		services.AddSingleton<IReportProcessor, ReportProcessor>();
		services.AddSingleton<BatchOrchestrator>();

		services.AddSingleton<IDocumentAccessRepository, DocumentAccessRepository>();

		services.AddFakeLogging();

		Services = services.BuildServiceProvider();

		ContextFactory = Services.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
		LogCollector = Services.GetFakeLogCollector();

		await using DocumentPersistence context = await ContextFactory.CreateDbContextAsync();
		await context.Database.MigrateAsync();
	}

	public async ValueTask DisposeAsync() => await _container.DisposeAsync();

	#endregion
}
