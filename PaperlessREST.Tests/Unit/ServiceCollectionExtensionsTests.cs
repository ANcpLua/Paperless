using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Asp.Versioning.ApiExplorer;
using Hangfire.Common;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PaperlessREST.Host.Extensions;
using Scalar.AspNetCore;

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
		var sp = BuildMinioServiceProvider(minio.Object);

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
		var sp = BuildMinioServiceProvider(minio.Object);

		await sp.EnsureStorageBucketAsync(logger);

		minio.Verify(c => c.MakeBucketAsync(It.IsAny<MakeBucketArgs>(), It.IsAny<CancellationToken>()), Times.Once);
		var rec = logs.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information).Subject;
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
		var sp = BuildMinioServiceProvider(minio.Object);

		var act = async () => await sp.EnsureStorageBucketAsync(logger);

		await act.Should().NotThrowAsync();
		var rec = logs.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Debug).Subject;
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

		var sp = BuildMinioServiceProvider(minio.Object);

		var act = async () => await sp.EnsureStorageBucketAsync(NullLogger.Instance);

		var thrown = (await act.Should().ThrowAsync<ArgumentException>()).Which;
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

		var opts = MakeBatchOptions();
		var sp = BuildJobManagerServiceProvider(mgr.Object, opts);

		FakeLogCollector logs = new();
		FakeLogger logger = new(logs);

		sp.RegisterRecurringJobs(logger);

		mgr.Verify(m => m.AddOrUpdate(
				BatchOptions.JobId,
				It.IsAny<Job>(),
				"0 0 * * *",
				It.Is<RecurringJobOptions>(o => o.TimeZone!.Id == "UTC")),
			Times.Once);
		var rec = logs.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Information).Subject;
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
		var opts = MakeBatchOptions();
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
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			EnvironmentName = "Development"
		});
		var app = builder.Build();

		app.IsDev.Should().BeTrue();
	}

	[Fact]
	public void IsDev_WhenEnvironmentIsProduction_ReturnsFalse()
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			EnvironmentName = "Production"
		});
		var app = builder.Build();

		app.IsDev.Should().BeFalse();
	}

	// ──────────────────────────────────────────────────────────────────
	// AddDependencies-backed lambdas (ProblemDetails, Hangfire, OpenApi, ApiExplorer)
	// and MapEndpoints in Development environment.
	// ──────────────────────────────────────────────────────────────────

	private static WebApplicationBuilder CreateWiredBuilder(string environment)
	{
		var builder = WebApplication.CreateBuilder(new WebApplicationOptions
		{
			EnvironmentName = environment
		});
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["ConnectionStrings:PaperlessDb"] = "Host=localhost;Database=test;Username=u;Password=p",
			["ConnectionStrings:Hangfire"] = "Host=localhost;Database=hf;Username=u;Password=p",
			["RabbitMQ:Uri"] = "amqp://guest:guest@localhost:5672/",
			["Storage:Minio:Endpoint"] = "localhost:9000",
			["Storage:Minio:AccessKey"] = "k",
			["Storage:Minio:SecretKey"] = "s",
			["Storage:Minio:BucketName"] = "b",
			["Elasticsearch:Uri"] = "http://localhost:9200",
			["Elasticsearch:DefaultIndex"] = "docs",
			["BatchProcessing:InputPath"] = "/in",
			["BatchProcessing:ArchivePath"] = "/arch",
			["BatchProcessing:ErrorPath"] = "/err",
			["BatchProcessing:FilePattern"] = "*.xml",
			["BatchProcessing:CronExpression"] = "0 0 * * *",
			["BatchProcessing:TimeZoneId"] = "UTC"
		});
		builder.AddDependencies();

		// Swap PostgreSQL JobStorage for in-memory so resolving IHostedService doesn't
		// require a running database. The production lambdas under test live in
		// AddHangfireServer's opts callback — JobStorage choice is orthogonal.
		builder.Services.RemoveAll<JobStorage>();
		builder.Services.AddSingleton<JobStorage>(new MemoryStorage());
		return builder;
	}

	private static HashSet<string> CollectMappedPatterns(WebApplication app) =>
		((IEndpointRouteBuilder)app).DataSources
			.SelectMany(s => s.Endpoints)
			.OfType<RouteEndpoint>()
			.Select(e => e.RoutePattern.RawText ?? string.Empty)
			.ToHashSet(StringComparer.Ordinal);

	[Fact]
	public void MapEndpoints_WhenIsDev_RegistersDevelopmentOnlyRoutes()
	{
		var builder = CreateWiredBuilder("Development");
		var app = builder.Build();

		app.MapEndpoints();

		var patterns = CollectMappedPatterns(app);

		// Dev-only routes from the IsDev=true branch
		patterns.Should().Contain(p => p.StartsWith("/openapi/", StringComparison.Ordinal));
		patterns.Should().Contain(p => p.StartsWith("/docs/", StringComparison.Ordinal) || p == "/docs");
		patterns.Should().Contain(p => p.StartsWith("/hangfire", StringComparison.Ordinal));
		// Always-mapped routes prove MapEndpoints completed past the IsDev block
		patterns.Should().Contain("/health");
		// And the SSE / document endpoints are always mapped too
		patterns.Should().Contain(p => p.Contains("/documents", StringComparison.Ordinal));
	}

	[Fact]
	public void MapEndpoints_WhenNotDev_OmitsDevelopmentOnlyRoutes()
	{
		var builder = CreateWiredBuilder("Production");
		var app = builder.Build();

		app.MapEndpoints();

		var patterns = CollectMappedPatterns(app);

		patterns.Should().NotContain(p => p.StartsWith("/docs", StringComparison.Ordinal));
		patterns.Should().NotContain(p => p.StartsWith("/hangfire", StringComparison.Ordinal));
		patterns.Should().NotContain(p => p.StartsWith("/openapi/", StringComparison.Ordinal));
		patterns.Should().Contain("/health");
	}

	[Fact]
	public void MapEndpoints_WhenIsDev_ScalarConfigureCallback_SetsTitleServersAndTheme()
	{
		var builder = CreateWiredBuilder("Development");
		var app = builder.Build();

		app.MapEndpoints();

		// Scalar's MapScalarApiReference captures the options Action inside the request delegate
		// (it runs lazily on HTTP request, not at map time). Extract it via the documented
		// internal field path and invoke it manually to verify the production lambda body.
		var scalarEndpoint = ((IEndpointRouteBuilder)app).DataSources
			.SelectMany(s => s.Endpoints)
			.OfType<RouteEndpoint>()
			.Single(e => e.RoutePattern.RawText == "/docs/{documentName?}");

		var requestDelegate = scalarEndpoint.RequestDelegate!;
		var generatedTarget = requestDelegate.Target!;
		var handlerField = generatedTarget.GetType()
			.GetField("handler", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!;
		var handlerDelegate = (Delegate)handlerField.GetValue(generatedTarget)!;
		var scalarClosure = handlerDelegate.Target!;
		var configureField = scalarClosure.GetType()
			.GetField("configureOptions", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!;
		var productionConfigure =
			(Action<ScalarOptions, HttpContext>)configureField.GetValue(scalarClosure)!;

		ScalarOptions scalarOpts = new();
		DefaultHttpContext http = new();

		productionConfigure(scalarOpts, http);

		scalarOpts.Title.Should().Be("Paperless OCR API");
		scalarOpts.Servers.Should().NotBeNull();
		scalarOpts.Servers!.Single().Url.Should().Be("http://localhost/");
		scalarOpts.Theme.Should().Be(ScalarTheme.Kepler);
	}

	private static Action<ProblemDetailsOptions> GetInlineProblemDetailsConfigure(IServiceCollection services)
	{
		// AddProblemDetails(opts => ...) registers a ConfigureNamedOptions<ProblemDetailsOptions>
		// whose Action is the production lambda at L141-146. Find it (ImplementationInstance, NOT
		// the ProblemDetailsEnricher transient).
		foreach (var d in services)
		{
			if (d.ServiceType != typeof(IConfigureOptions<ProblemDetailsOptions>) ||
			    d.ImplementationInstance is not ConfigureNamedOptions<ProblemDetailsOptions> named ||
			    named.Action is null)
			{
				continue;
			}

			return named.Action;
		}

		throw new InvalidOperationException("Inline ProblemDetails configure action not found.");
	}

	[Fact]
	public void AddDependencies_ProblemDetailsCustomization_PopulatesTraceIdAndInstanceFromHttpContextWhenNoActivity()
	{
		var builder = CreateWiredBuilder("Production");
		var configure = GetInlineProblemDetailsConfigure(builder.Services);

		ProblemDetailsOptions opts = new();
		configure(opts);
		opts.CustomizeProblemDetails.Should().NotBeNull();

		var saved = Activity.Current;
		Activity.Current = null;
		try
		{
			DefaultHttpContext http = new();
			http.Request.Method = "POST";
			http.Request.Path = "/api/v1/documents";
			http.TraceIdentifier = "trace-from-context-42";
			ProblemDetailsContext ctx = new()
			{
				HttpContext = http,
				ProblemDetails = new ProblemDetails()
			};

			opts.CustomizeProblemDetails!(ctx);

			ctx.ProblemDetails.Extensions.Should().ContainKey("trace_id")
				.WhoseValue.Should().Be("trace-from-context-42");
			ctx.ProblemDetails.Extensions.Should().ContainKey("instance")
				.WhoseValue.Should().Be("POST /api/v1/documents");
		}
		finally
		{
			Activity.Current = saved;
		}
	}

	[Fact]
	public void AddDependencies_ProblemDetailsCustomization_UsesActivityIdWhenAvailable()
	{
		var builder = CreateWiredBuilder("Production");
		var configure = GetInlineProblemDetailsConfigure(builder.Services);

		ProblemDetailsOptions opts = new();
		configure(opts);
		opts.CustomizeProblemDetails.Should().NotBeNull();

		using Activity activity = new("unit-test-span");
		activity.Start();
		var expectedTrace = activity.Id!;

		DefaultHttpContext http = new();
		http.Request.Method = "DELETE";
		http.Request.Path = "/api/v1/documents/abc";
		http.TraceIdentifier = "would-be-fallback";
		ProblemDetailsContext ctx = new()
		{
			HttpContext = http,
			ProblemDetails = new ProblemDetails()
		};

		opts.CustomizeProblemDetails!(ctx);

		ctx.ProblemDetails.Extensions["trace_id"].Should().Be(expectedTrace);
		ctx.ProblemDetails.Extensions["instance"].Should().Be("DELETE /api/v1/documents/abc");
	}

	[Fact]
	public void AddDependencies_HangfireServerOptions_SetWorkerCountAndServerName()
	{
		var builder = CreateWiredBuilder("Production");

		// Find only the BackgroundJobServerHostedService factory and invoke it directly,
		// avoiding resolution of other IHostedService entries (RabbitMQ listeners would
		// try to dial 127.0.0.1:5672 and fail).
		var jobServerDescriptor = builder.Services.Single(d =>
			d.ServiceType == typeof(IHostedService) &&
			d.ImplementationFactory is not null &&
			d.ImplementationFactory.GetType().GenericTypeArguments[1].FullName == "Hangfire.BackgroundJobServerHostedService");

		var app = builder.Build();
		var jobServer = jobServerDescriptor.ImplementationFactory!(app.Services);
		var optionsField = jobServer.GetType().GetField("_options",
			BindingFlags.NonPublic | BindingFlags.Instance)!;
		var opts = (BackgroundJobServerOptions)optionsField.GetValue(jobServer)!;

		opts.WorkerCount.Should().Be(Environment.ProcessorCount);
		opts.ServerName.Should().StartWith(Environment.MachineName + "-");
		// Trailing token must be a 32-char "N"-format GUID with no dashes.
		var suffix = opts.ServerName![(Environment.MachineName.Length + 1)..];
		suffix.Should().HaveLength(32);
		suffix.Should().MatchRegex("^[0-9a-f]{32}$");
	}

	private static Action<OpenApiOptions> GetOpenApiConfigure(IServiceCollection services)
	{
		// AddOpenApi(Action) registers Configure<OpenApiOptions>("v1", action). Find the
		// ConfigureNamedOptions<OpenApiOptions> whose Name == "v1".
		foreach (var d in services)
		{
			if (d.ServiceType != typeof(IConfigureOptions<OpenApiOptions>) ||
			    d.ImplementationInstance is not ConfigureNamedOptions<OpenApiOptions> named ||
			    named.Action is null)
			{
				continue;
			}

			return named.Action;
		}

		throw new InvalidOperationException("OpenApi configure action not found.");
	}

	[Fact]
	public void AddDependencies_OpenApiCreateSchemaReferenceId_ReturnsNullForEnumAndDefaultForOther()
	{
		var builder = CreateWiredBuilder("Production");
		var configure = GetOpenApiConfigure(builder.Services);

		OpenApiOptions opts = new();
		configure(opts);
		opts.CreateSchemaReferenceId.Should().NotBeNull();

		JsonSerializerOptions jsonOpts = new();
		var enumInfo = JsonTypeInfo.CreateJsonTypeInfo(typeof(DayOfWeek), jsonOpts);
		var dtoInfo = JsonTypeInfo.CreateJsonTypeInfo(typeof(DocumentDto), jsonOpts);

		var enumId = opts.CreateSchemaReferenceId!(enumInfo);
		var dtoId = opts.CreateSchemaReferenceId!(dtoInfo);

		enumId.Should().BeNull();
		dtoId.Should().Be(OpenApiOptions.CreateDefaultSchemaReferenceId(dtoInfo));
		dtoId.Should().Be(nameof(DocumentDto));
	}

	[Fact]
	public async Task AddDependencies_OpenApiDocumentTransformer_SetsTitleVersionAndDescription()
	{
		var builder = CreateWiredBuilder("Production");
		var configure = GetOpenApiConfigure(builder.Services);

		OpenApiOptions opts = new();
		configure(opts);

		var transformersField = typeof(OpenApiOptions).GetField("DocumentTransformers",
			BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!;
		var transformers = (System.Collections.IList)transformersField.GetValue(opts)!;
		transformers.Count.Should().Be(1);

		var delegateTransformer = transformers[0]!;
		// DelegateOpenApiDocumentTransformer wraps the user Func in _documentTransformer.
		var delegateField = delegateTransformer.GetType().GetField("_documentTransformer",
			BindingFlags.NonPublic | BindingFlags.Instance)!;
		var productionDelegate =
			(Func<OpenApiDocument, OpenApiDocumentTransformerContext, CancellationToken, Task>)
			delegateField.GetValue(delegateTransformer)!;

		OpenApiDocument doc = new();
		OpenApiDocumentTransformerContext ctx = new()
		{
			DocumentName = "v1",
			DescriptionGroups = Array.Empty<Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescriptionGroup>(),
			ApplicationServices = new ServiceCollection().BuildServiceProvider()
		};

		await productionDelegate(doc, ctx, TestContext.Current.CancellationToken);

		doc.Info.Should().NotBeNull();
		doc.Info!.Title.Should().Be("Paperless OCR API");
		doc.Info!.Version.Should().Be("v1");
		doc.Info!.Description.Should().Be("API for uploading and processing PDF documents with OCR");
	}

	[Fact]
	public void AddDependencies_ApiExplorerOptions_SetsGroupNameFormatAndSubstituteApiVersionInUrl()
	{
		var builder = CreateWiredBuilder("Production");
		var app = builder.Build();

		var opts = app.Services.GetRequiredService<IOptions<ApiExplorerOptions>>().Value;

		opts.GroupNameFormat.Should().Be("'v'VVV");
		opts.SubstituteApiVersionInUrl.Should().BeTrue();
	}
}
