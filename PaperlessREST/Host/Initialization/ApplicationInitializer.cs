namespace PaperlessREST.Host.Initialization;

/// <summary>
///     Initializes the database by running pending migrations.
/// </summary>
public sealed class DatabaseInitializer(
	IDbContextFactory<DocumentPersistence> contextFactory,
	ILogger<DatabaseInitializer> logger) : IApplicationInitializer
{
	/// <summary>Database migrations run first.</summary>
	public int Order => 0;

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		await using DocumentPersistence db = await contextFactory.CreateDbContextAsync(cancellationToken);
		await db.Database.MigrateAsync(cancellationToken);
		logger.LogInformation("Database migration completed");
	}
}

/// <summary>
///     Contract for application initialization tasks that run at startup.
///     Each initializer is responsible for a single cohesive initialization concern.
/// </summary>
public interface IApplicationInitializer
{
	/// <summary>
	///     Order in which this initializer runs. Lower values run first.
	///     Database migrations should run before other initializers.
	/// </summary>
	int Order { get; }

	/// <summary>
	///     Performs the initialization task.
	/// </summary>
	/// <param name="cancellationToken">Cancellation token.</param>
	Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     Registers recurring Hangfire jobs.
/// </summary>
public sealed class JobSchedulerInitializer(
	IRecurringJobManager jobManager,
	IOptions<BatchOptions> options,
	ILogger<JobSchedulerInitializer> logger) : IApplicationInitializer
{
	/// <summary>Job registration runs after storage is ready.</summary>
	public int Order => 20;

	public Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		BatchOptions opts = options.Value;

		jobManager.AddOrUpdate<BatchOrchestrator>(
			BatchOptions.JobId,
			o => o.ProcessAsync(JobCancellationToken.Null),
			opts.CronExpression,
			new RecurringJobOptions { TimeZone = opts.TimeZone });

		logger.LogInformation("Hangfire job '{JobId}' scheduled: {Cron} ({TimeZone})",
			BatchOptions.JobId, opts.CronExpression, opts.TimeZone.Id);

		return Task.CompletedTask;
	}
}

/// <summary>
///     Ensures the MinIO storage bucket exists.
/// </summary>
public sealed class StorageInitializer(
	IMinioClient minioClient,
	IOptions<MinioOptions> options,
	ILogger<StorageInitializer> logger) : IApplicationInitializer
{
	/// <summary>Storage initialization runs after database.</summary>
	public int Order => 10;

	public async Task InitializeAsync(CancellationToken cancellationToken = default)
	{
		string bucket = options.Value.BucketName;

		if (await minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), cancellationToken))
		{
			return;
		}

		try
		{
			await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), cancellationToken);
			logger.LogInformation("MinIO bucket '{Bucket}' created", bucket);
		}
		catch (ArgumentException ex) when (ex.Message.Contains("already owned", StringComparison.OrdinalIgnoreCase))
		{
			// Race condition: bucket was created between BucketExistsAsync and MakeBucketAsync
			logger.LogDebug("MinIO bucket '{Bucket}' already exists", bucket);
		}
	}
}
