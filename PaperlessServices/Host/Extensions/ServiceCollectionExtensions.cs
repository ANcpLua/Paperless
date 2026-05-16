namespace PaperlessServices.Host.Extensions;

public static class ServiceCollectionExtensions
{
	extension(IServiceCollection services)
	{
		/// <summary>
		///     Registers all OCR-related services: storage, search indexing, and OCR processing.
		/// </summary>
		public IServiceCollection AddOcrServices() =>
			services
				.AddMinioStorage()
				.AddElasticsearchSearch()
				.AddOcrProcessing();

		/// <summary>
		///     Registers all OCR-related services. Overload that accepts IConfiguration for
		///     backwards compatibility with callers passing configuration explicitly.
		///     The provided configuration is not required because options are bound via
		///     DI using BindConfiguration, but the overload is kept to avoid breaking changes.
		/// </summary>
		public IServiceCollection AddOcrServices(IConfiguration _)
			=> AddOcrServices(services);

		/// <summary>
		///     Registers GenAI services using the library's implementation.
		///     Library provides: GeminiService with resilience policies, GenAIWorker, configuration binding.
		/// </summary>
		public IServiceCollection AddGenAiServices(IConfiguration configuration)
		{
			services.AddPaperlessGenAI(configuration);
			return services;
		}

		/// <summary>
		///     Configures MinIO object storage with validated options and client registration.
		///     Storage is owned by OcrProcessing feature but shared via DI.
		/// </summary>
		private IServiceCollection AddMinioStorage()
		{
			services
				.AddOptionsWithValidateOnStart<MinioOptions>()
				.BindConfiguration(MinioOptions.SectionName)
				.ValidateDataAnnotations();

			services.AddSingleton<IMinioClient>(sp =>
			{
				MinioOptions options = sp.GetRequiredService<IOptions<MinioOptions>>().Value;

				// Parse endpoint to handle both "host:port" and "http://host:port" formats
				string endpoint = options.Endpoint;
				if (!endpoint.Contains("://"))
				{
					endpoint = $"http://{endpoint}";
				}

				Uri uri = new(endpoint);

				return new MinioClient()
					.WithEndpoint(uri.Host, uri.Port)
					.WithCredentials(options.AccessKey, options.SecretKey)
					.WithSSL(options.UseSsl)
					.Build();
			});

			services.AddSingleton<IStorageService, StorageService>();

			return services;
		}

		/// <summary>
		///     Configures Elasticsearch search indexing with validated options and client registration.
		///     Search indexing is owned by OcrProcessing feature but shared via DI.
		/// </summary>
		private IServiceCollection AddElasticsearchSearch()
		{
			services
				.AddOptionsWithValidateOnStart<ElasticsearchOptions>()
				.BindConfiguration(ElasticsearchOptions.SectionName)
				.ValidateDataAnnotations();

			services.AddSingleton<ElasticsearchClient>(sp =>
			{
				ElasticsearchOptions options = sp.GetRequiredService<IOptions<ElasticsearchOptions>>().Value;

				return new ElasticsearchClient(
					new ElasticsearchClientSettings(new Uri(options.Uri))
						.DefaultIndex(options.DefaultIndex)
						.ThrowExceptions()
				);
			});

			services.AddSingleton<ISearchIndexService, SearchIndexService>();

			return services;
		}

		/// <summary>
		///     Registers OCR business logic services and background worker.
		///     Services are Scoped to ensure proper isolation between messages.
		///     The OcrWorker creates a new scope for each message it processes.
		/// </summary>
		private IServiceCollection AddOcrProcessing()
		{
			// System time provider for production (allows mocking in tests)
			services.AddSingleton(TimeProvider.System);

			// Application services: Scoped (message-specific processing logic)
			// Each message processed by OcrWorker gets a fresh instance via IServiceScopeFactory
			services.AddScoped<IPdfExtractor, CreatePdfExtractor>();
			services.AddScoped<IOcrProcessor, OcrProcessor>();

			// Background worker: Singleton (runs for app lifetime)
			services.AddHostedService<OcrWorker>();

			return services;
		}
	}
}
