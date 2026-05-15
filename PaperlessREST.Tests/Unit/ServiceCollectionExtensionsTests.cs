using Hangfire.Common;
using PaperlessREST.Host.Extensions;

namespace PaperlessREST.Tests.Unit;

public sealed class ServiceCollectionExtensionsTests
{
	private const string Bucket = "paperless-test-bucket";

	// ──────────────────────────────────────────────────────────────────
	// EnsureStorageBucketAsync
	// ──────────────────────────────────────────────────────────────────

	private static IServiceProvider BuildMinioServiceProvider(IMinioClient client)
	{
		ServiceCollection services = new();
		services.AddSingleton(client);
		services.AddSingleton<IOptions<MinioOptions>>(Options.Create(new MinioOptions
		{
			Endpoint = "localhost:9000",
			AccessKey = "k",
			SecretKey = "s",
			BucketName = Bucket
		}));
		return services.BuildServiceProvider();
	}

	[Fact]
	public async Task EnsureStorageBucketAsync_BucketAlreadyExists_DoesNotCallMakeBucket()
	{
		Mock<IMinioClient> minio = new(MockBehavior.Strict);
		minio.Setup(c => c.BucketExistsAsync(
				It.IsAny<BucketExistsArgs>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);

		FakeLogCollector logs = new();
		FakeLogger logger = new(logs);
		IServiceProvider sp = BuildMinioServiceProvider(minio.Object);

		await sp.EnsureStorageBucketAsync(logger);

		minio.Verify(c => c.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task EnsureStorageBucketAsync_BucketMissing_CreatesAndLogsInformation()
	{
		Mock<IMinioClient> minio = new(MockBehavior.Strict);
		minio.Setup(c => c.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);
		minio.Setup(c => c.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		FakeLogCollector logs = new();
		FakeLogger logger = new(logs);
		IServiceProvider sp = BuildMinioServiceProvider(minio.Object);

		await sp.EnsureStorageBucketAsync(logger);

		minio.Verify(c => c.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()), Times.Once);
		FakeLogRecord rec = logs.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information).Subject;
		rec.Message.Should().Contain(Bucket).And.Contain("created");
	}

	[Fact]
	public async Task EnsureStorageBucketAsync_BucketRaceCondition_SwallowsAlreadyOwnedAndLogsDebug()
	{
		Mock<IMinioClient> minio = new(MockBehavior.Strict);
		minio.Setup(c => c.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);
		minio.Setup(c => c.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new ArgumentException("Bucket already owned by you"));

		FakeLogCollector logs = new();
		FakeLogger logger = new(logs);
		IServiceProvider sp = BuildMinioServiceProvider(minio.Object);

		Func<Task> act = async () => await sp.EnsureStorageBucketAsync(logger);

		await act.Should().NotThrowAsync();
		FakeLogRecord rec = logs.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Debug).Subject;
		rec.Message.Should().Contain(Bucket).And.Contain("already exists");
	}

	[Fact]
	public async Task EnsureStorageBucketAsync_BucketCreationFails_NonAlreadyOwnedArgumentException_Rethrows()
	{
		Mock<IMinioClient> minio = new(MockBehavior.Strict);
		minio.Setup(c => c.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(false);
		ArgumentException differentArg = new("Bucket name invalid");
		minio.Setup(c => c.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(differentArg);

		IServiceProvider sp = BuildMinioServiceProvider(minio.Object);

		Func<Task> act = async () => await sp.EnsureStorageBucketAsync(NullLogger.Instance);

		ArgumentException thrown = (await act.Should().ThrowAsync<ArgumentException>()).Which;
		thrown.Message.Should().Be("Bucket name invalid");
	}

	// ──────────────────────────────────────────────────────────────────
	// RegisterRecurringJobs
	// ──────────────────────────────────────────────────────────────────

	private static IServiceProvider BuildJobManagerServiceProvider(
		IRecurringJobManager mgr,
		BatchOptions opts)
	{
		ServiceCollection services = new();
		services.AddSingleton(mgr);
		services.AddSingleton<IOptions<BatchOptions>>(Options.Create(opts));
		return services.BuildServiceProvider();
	}

	private static BatchOptions MakeBatchOptions(string cron = "0 0 * * *") => new()
	{
		InputPath = "/in",
		ArchivePath = "/arch",
		ErrorPath = "/err",
		FilePattern = "*.xml",
		CronExpression = cron,
		TimeZoneId = "UTC"
	};

	[Fact]
	public void RegisterRecurringJobs_SchedulesJobWithConfiguredCronAndTimeZone()
	{
		Mock<IRecurringJobManager> mgr = new();

		BatchOptions opts = MakeBatchOptions();
		IServiceProvider sp = BuildJobManagerServiceProvider(mgr.Object, opts);

		FakeLogCollector logs = new();
		FakeLogger logger = new(logs);

		sp.RegisterRecurringJobs(logger);

		mgr.Verify(m => m.AddOrUpdate(
				BatchOptions.JobId,
				It.IsAny<Job>(),
				"0 0 * * *",
				It.Is<RecurringJobOptions>(o => o.TimeZone!.Id == "UTC")),
			Times.Once);
		FakeLogRecord rec = logs.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information).Subject;
		rec.Message.Should().Contain(BatchOptions.JobId)
			.And.Contain("0 0 * * *")
			.And.Contain("UTC");
	}

	// ──────────────────────────────────────────────────────────────────
	// IServiceProvider extension property accessors
	// ──────────────────────────────────────────────────────────────────

	[Fact]
	public void Minio_AccessorReturnsRegisteredClient()
	{
		Mock<IMinioClient> minio = new(MockBehavior.Strict);
		ServiceCollection services = new();
		services.AddSingleton(minio.Object);
		IServiceProvider sp = services.BuildServiceProvider();

		sp.Minio.Should().BeSameAs(minio.Object);
	}

	[Fact]
	public void MinioOpts_AccessorReturnsConfiguredOptions()
	{
		MinioOptions opts = new()
		{
			Endpoint = "host:9000",
			AccessKey = "k",
			SecretKey = "s",
			BucketName = "b"
		};
		ServiceCollection services = new();
		services.AddSingleton<IOptions<MinioOptions>>(Options.Create(opts));
		IServiceProvider sp = services.BuildServiceProvider();

		sp.MinioOpts.Should().BeSameAs(opts);
	}

	[Fact]
	public void BatchOpts_AccessorReturnsConfiguredOptions()
	{
		BatchOptions opts = MakeBatchOptions();
		ServiceCollection services = new();
		services.AddSingleton<IOptions<BatchOptions>>(Options.Create(opts));
		IServiceProvider sp = services.BuildServiceProvider();

		sp.BatchOpts.Should().BeSameAs(opts);
	}

	[Fact]
	public void DbFactory_AccessorReturnsRegisteredFactory()
	{
		Mock<IDbContextFactory<DocumentPersistence>> factory = new(MockBehavior.Strict);
		ServiceCollection services = new();
		services.AddSingleton(factory.Object);
		IServiceProvider sp = services.BuildServiceProvider();

		sp.DbFactory.Should().BeSameAs(factory.Object);
	}

	// ──────────────────────────────────────────────────────────────────
	// WebApplication.IsDev
	// ──────────────────────────────────────────────────────────────────

	[Fact]
	public void IsDev_WhenEnvironmentIsDevelopment_ReturnsTrue()
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			EnvironmentName = "Development"
		});
		WebApplication app = builder.Build();

		app.IsDev.Should().BeTrue();
	}

	[Fact]
	public void IsDev_WhenEnvironmentIsProduction_ReturnsFalse()
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			EnvironmentName = "Production"
		});
		WebApplication app = builder.Build();

		app.IsDev.Should().BeFalse();
	}
}
