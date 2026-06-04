using PaperlessREST.API;

namespace PaperlessREST.Host.Extensions;

public static class ServiceCollectionExtensions
{
	public static void AddDependencies(this WebApplicationBuilder builder) => builder.Services
		.AddCrossCuttingConcerns()
		.AddInfrastructure(builder.Configuration)
		.AddApplicationServices()
		.AddApiLayer();

	// ──────────────────────────────────────────────────────────────────
	// WebApplication Extensions
	// ──────────────────────────────────────────────────────────────────

	extension(WebApplication app)
	{
		public bool IsDev => app.Environment.IsDevelopment();

		public void ConfigureMiddleware()
		{
			app.UseStaticFiles();
			app.UseExceptionHandler();
			app.UseStatusCodePages();
			app.UseHttpLogging();
			app.UseRateLimiter();
			app.UseOutputCache();
		}

		public void MapEndpoints()
		{
			if (app.IsDev)
			{
				app.MapOpenApi();
				app.MapScalarApiReference("/docs", static options =>
				{
					options.Title = "Paperless OCR API";
					options.Servers = [new ScalarServer("http://localhost/")];
					options.WithTheme(ScalarTheme.Kepler);
				});
				app.MapHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [] });
			}

			app.MapHealthChecks("/health");
			app.MapOcrEventStream();
			app.MapGenAIEventStream();
			app.MapErrorOrEndpoints();
		}

		public async Task InitializeApplicationAsync()
		{
			await using var scope = app.Services.CreateAsyncScope();
			var sp = scope.ServiceProvider;

			await sp.MigrateDatabaseAsync(app.Logger);
			await sp.EnsureStorageBucketAsync(app.Logger);
			sp.RegisterRecurringJobs(app.Logger);
		}
	}

	// ──────────────────────────────────────────────────────────────────
	// IServiceProvider Extensions (Initialization)
	// ──────────────────────────────────────────────────────────────────

	extension(IServiceProvider sp)
	{
		public IDbContextFactory<DocumentPersistence> DbFactory =>
			sp.GetRequiredService<IDbContextFactory<DocumentPersistence>>();

		public IMinioClient Minio => sp.GetRequiredService<IMinioClient>();
		public MinioOptions MinioOpts => sp.GetRequiredService<IOptions<MinioOptions>>().Value;
		public BatchOptions BatchOpts => sp.GetRequiredService<IOptions<BatchOptions>>().Value;

		public async Task MigrateDatabaseAsync(ILogger logger)
		{
			await using var db = await sp.DbFactory.CreateDbContextAsync();
			await db.Database.MigrateAsync();
			logger.LogInformation("Database migration completed");
		}

		public async Task EnsureStorageBucketAsync(ILogger logger)
		{
			var bucket = sp.MinioOpts.BucketName;

			if (await sp.Minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket)))
			{
				return;
			}

			try
			{
				await sp.Minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket));
				logger.LogInformation("MinIO bucket '{Bucket}' created", bucket);
			}
			catch (ArgumentException ex) when (ex.Message.Contains("already owned", StringComparison.OrdinalIgnoreCase))
			{
				// Race condition: bucket was created between BucketExistsAsync and MakeBucketAsync
				logger.LogDebug("MinIO bucket '{Bucket}' already exists", bucket);
			}
		}

		public void RegisterRecurringJobs(ILogger logger)
		{
			var opts = sp.BatchOpts;
			var manager = sp.GetRequiredService<IRecurringJobManager>();

			manager.AddOrUpdate<BatchOrchestrator>(
				BatchOptions.JobId,
				o => o.ProcessAsync(JobCancellationToken.Null),
				opts.CronExpression,
				new RecurringJobOptions { TimeZone = opts.TimeZone });

			logger.LogInformation("Hangfire job '{JobId}' scheduled: {Cron} ({TimeZone})",
				BatchOptions.JobId, opts.CronExpression, opts.TimeZone.Id);
		}
	}

	// ──────────────────────────────────────────────────────────────────
	// IServiceCollection Extensions (DI Registration)
	// ──────────────────────────────────────────────────────────────────

	extension(IServiceCollection services)
	{
		// ═══════════════════════════════════════════════════════════════
		// Cross-Cutting Concerns
		// ═══════════════════════════════════════════════════════════════

		private IServiceCollection AddCrossCuttingConcerns()
		{
			services.AddHttpLogging(static o => o.LoggingFields =
				HttpLoggingFields.RequestProperties |
				HttpLoggingFields.ResponseStatusCode |
				HttpLoggingFields.ResponseHeaders);

			services.ConfigureHttpJsonOptions(static o =>
				o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

			services.AddExceptionHandler<GlobalExceptionHandler>();
			services.AddProblemDetails(static options =>
				options.CustomizeProblemDetails = static ctx =>
				{
					ctx.ProblemDetails.Extensions["trace_id"] = Activity.Current?.Id ?? ctx.HttpContext.TraceIdentifier;
					ctx.ProblemDetails.Extensions["instance"] =
						$"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
				});
			services.ConfigureOptions<ProblemDetailsEnricher>();

			services.AddHealthChecks();
			services.AddMapster();

			// Rate limiting with tiered policies
			services.AddRateLimiter(static options =>
			{
				options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

				// Read operations: 100 requests per minute per IP
				options.AddFixedWindowLimiter(RateLimitPolicies.ReadOperations, limiter =>
				{
					limiter.PermitLimit = 100;
					limiter.Window = TimeSpan.FromMinutes(1);
					limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
					limiter.QueueLimit = 10;
				});

				// Write operations: 20 requests per minute per IP
				options.AddFixedWindowLimiter(RateLimitPolicies.WriteOperations, limiter =>
				{
					limiter.PermitLimit = 20;
					limiter.Window = TimeSpan.FromMinutes(1);
					limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
					limiter.QueueLimit = 5;
				});

				// Search operations: 60 requests per minute per IP
				options.AddFixedWindowLimiter(RateLimitPolicies.SearchOperations, limiter =>
				{
					limiter.PermitLimit = 60;
					limiter.Window = TimeSpan.FromMinutes(1);
					limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
					limiter.QueueLimit = 10;
				});
			});

			// Output caching for GET operations
			services.AddOutputCache(static options =>
			{
				// Document list: cache for 10 seconds, vary by query params
				options.AddPolicy(CachePolicies.DocumentList, static builder =>
					builder.Expire(TimeSpan.FromSeconds(10))
						.SetVaryByQuery("pageSize", "cursor")
						.Tag("documents"));

				// Document by ID: cache for 30 seconds, vary by route
				options.AddPolicy(CachePolicies.DocumentById, static builder =>
					builder.Expire(TimeSpan.FromSeconds(30))
						.SetVaryByRouteValue("id")
						.Tag("documents"));
			});

			return services;
		}

		// ═══════════════════════════════════════════════════════════════
		// Infrastructure
		// ═══════════════════════════════════════════════════════════════

		private IServiceCollection AddInfrastructure(IConfiguration config)
		{
			services.AddSingleton<IFileSystem, RealFileSystem>();
			services.AddSingleton(TimeProvider.System);

			return services
				.AddPostgres(config)
				.AddObjectStorage()
				.AddSearchEngine()
				.AddMessageBroker(config)
				.AddBackgroundJobs(config);
		}

		private IServiceCollection AddPostgres(IConfiguration config)
		{
			var dataSource = new NpgsqlDataSourceBuilder(config.GetConnectionString("PaperlessDb"))
				.MapEnum<DocumentStatus>("document_status")
				.Build();

			services.AddPooledDbContextFactory<DocumentPersistence>(opts =>
			{
				opts.UseNpgsql(dataSource, npgsql => npgsql.MapEnum<DocumentStatus>("document_status"))
					.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
				opts.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
			});

			return services;
		}

		private IServiceCollection AddObjectStorage()
		{
			services.AddOptionsWithValidateOnStart<MinioOptions>()
				.BindConfiguration(MinioOptions.SectionName)
				.ValidateDataAnnotations();

			services.AddSingleton<IMinioClient>(static sp =>
			{
				var opts = sp.GetRequiredService<IOptions<MinioOptions>>().Value;
				return new MinioClient()
					.WithEndpoint(opts.EndpointUri.Host, opts.EndpointUri.Port)
					.WithCredentials(opts.AccessKey, opts.SecretKey)
					.WithSSL(opts.UseSsl)
					.Build();
			});

			return services;
		}

		private IServiceCollection AddSearchEngine()
		{
			services.AddOptionsWithValidateOnStart<ElasticsearchOptions>()
				.BindConfiguration(ElasticsearchOptions.SectionName)
				.ValidateDataAnnotations();

			services.AddSingleton(static sp =>
			{
				var opts = sp.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;
				return new ElasticsearchClient(
					new ElasticsearchClientSettings(opts.Uri)
						.DefaultIndex(opts.DefaultIndex)
						.ThrowExceptions());
			});

			return services;
		}

		private IServiceCollection AddMessageBroker(IConfiguration config)
		{
			services.AddPaperlessRabbitMq(config, true, true);
			services.AddHostedService<OcrResultListener>();
			services.AddHostedService<GenAiResultListener>();

			return services;
		}

		private IServiceCollection AddBackgroundJobs(IConfiguration config)
		{
			services.AddOptionsWithValidateOnStart<BatchOptions>()
				.BindConfiguration(BatchOptions.SectionName)
				.Validate(o => o.IsValidTimeZone,
					$"{BatchOptions.SectionName}:TimeZoneId must be a valid system timezone (e.g. 'UTC', 'Europe/Vienna')")
				.Validate(o => o.HasDistinctPaths,
					$"{BatchOptions.SectionName} paths (InputPath, ArchivePath, ErrorPath) must be distinct")
				.ValidateDataAnnotations();

			services.AddHangfire(cfg =>
			{
				cfg.UseSimpleAssemblyNameTypeSerializer();
				cfg.UseRecommendedSerializerSettings();
				cfg.UsePostgreSqlStorage(opt => opt.UseNpgsqlConnection(config.GetConnectionString("Hangfire")));
			});

			services.AddHangfireServer(opts =>
			{
				opts.WorkerCount = Environment.ProcessorCount;
				opts.ServerName = $"{Environment.MachineName}-{Guid.NewGuid():N}";
			});

			services.AddScoped<BatchOrchestrator>();
			services.AddScoped<IReportProcessor, ReportProcessor>();
			services.AddScoped<IDocumentAccessRepository, DocumentAccessRepository>();

			return services;
		}

		// ═══════════════════════════════════════════════════════════════
		// Application Services
		// ═══════════════════════════════════════════════════════════════

		private IServiceCollection AddApplicationServices()
		{
			// Scoped: request-specific business logic
			services.AddScoped<IDocumentRepository, DocumentRepository>();
			services.AddScoped<IDocumentService, DocumentService>();

			// Singleton: stateless wrappers around singleton clients
			services.AddSingleton<IDocumentStorageService, DocumentStorageService>();
			services.AddSingleton<IDocumentSearchService, DocumentSearchService>();

			return services;
		}

		// ═══════════════════════════════════════════════════════════════
		// API Layer
		// ═══════════════════════════════════════════════════════════════

		private IServiceCollection AddApiLayer()
		{
			services.AddOpenApi(static o =>
			{
				o.CreateSchemaReferenceId =
					static t => t.Type.IsEnum ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(t);
				o.AddDocumentTransformer(static (doc, _, _) =>
				{
					doc.Info.Title = "Paperless OCR API";
					doc.Info.Version = "v1";
					doc.Info.Description = "API for uploading and processing PDF documents with OCR";
					return Task.CompletedTask;
				});
			});

			// ErrorOrX generates the endpoint registrations from the [Get]/[Post]/… attributes;
			// camelCase + ignore-null JSON keeps the wire format identical to the previous API.
			services.AddErrorOrEndpoints()
				.WithCamelCase()
				.WithIgnoreNulls();

			services.AddApiVersioning(static v =>
			{
				v.DefaultApiVersion = new ApiVersion(1, 0);
				v.AssumeDefaultVersionWhenUnspecified = true;
				v.ReportApiVersions = true;
			}).AddApiExplorer(static opts =>
			{
				opts.GroupNameFormat = "'v'VVV";
				opts.SubstituteApiVersionInUrl = true;
			});

			return services;
		}
	}
}
