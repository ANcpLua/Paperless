using PaperlessServices.Host.Extensions;

namespace PaperlessServices.Tests.Unit;

/// <summary>
///     Unit tests for the PaperlessServices host extension methods that cover the
///     <c>AddOcrServices()</c> entry point used by Program.cs (Program.cs itself is
///     excluded from coverage) and the <c>AddGenAiServices</c> wrapper around the
///     library's <c>AddPaperlessGenAI</c>.
/// </summary>
/// <remarks>
///     Integration tests exercise <c>AddOcrServices()</c> end-to-end via the
///     fixture. These unit tests pin the smaller surfaces that the integration path
///     does not exercise.
/// </remarks>
public sealed class ServiceCollectionExtensionsTests
{
	private static IConfiguration BuildConfiguration(Dictionary<string, string?>? overrides = null)
	{
		Dictionary<string, string?> settings = new()
		{
			// MinioOptions (Storage:Minio) — required for validate-on-start
			["Storage:Minio:Endpoint"] = "http://minio:9000",
			["Storage:Minio:AccessKey"] = "minioadmin",
			["Storage:Minio:SecretKey"] = "minioadmin",
			["Storage:Minio:BucketName"] = "documents",
			// ElasticsearchOptions — required for validate-on-start
			["Elasticsearch:Uri"] = "http://elasticsearch:9200",
			["Elasticsearch:DefaultIndex"] = "documents",
			// GenAI library reads its own section; key value is irrelevant to registration
			["Gemini:ApiKey"] = "test-key",
			["Gemini:Model"] = "gemini-2.0-flash"
		};

		if (overrides is not null)
		{
			foreach (KeyValuePair<string, string?> kv in overrides)
			{
				settings[kv.Key] = kv.Value;
			}
		}

		return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
	}

	// ═══════════════════════════════════════════════════════════════
	// AddOcrServices() — no-arg overload (Program.cs path)
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void AddOcrServices_NoArg_RegistersOcrInfrastructure()
	{
		// Arrange
		ServiceCollection services = new();
		services.AddSingleton(BuildConfiguration());
		services.AddLogging();

		// Act — exercises the no-arg overload used by Program.cs
		IServiceCollection returned = services.AddOcrServices();

		// Assert — fluent API returns the same collection (NUKE-style chaining)
		returned.Should().BeSameAs(services);

		// All four OCR registrations exist
		services.Should().Contain(d => d.ServiceType == typeof(IStorageService));
		services.Should().Contain(d => d.ServiceType == typeof(ISearchIndexService));
		services.Should().Contain(d => d.ServiceType == typeof(IPdfExtractor));
		services.Should().Contain(d => d.ServiceType == typeof(IOcrProcessor));
		services.Should().Contain(d => d.ServiceType == typeof(IMinioClient));
		services.Should().Contain(d => d.ServiceType == typeof(ElasticsearchClient));
		services.Should().Contain(d => d.ServiceType == typeof(TimeProvider));
	}

	[Fact]
	public void AddOcrServices_NoArg_OcrProcessorIsScoped()
	{
		// Arrange
		ServiceCollection services = new();
		services.AddSingleton(BuildConfiguration());
		services.AddLogging();

		// Act
		services.AddOcrServices();

		// Assert — OcrWorker creates a new scope per message, so processors must be Scoped
		ServiceDescriptor processor = services.Single(d => d.ServiceType == typeof(IOcrProcessor));
		processor.Lifetime.Should().Be(ServiceLifetime.Scoped);

		ServiceDescriptor extractor = services.Single(d => d.ServiceType == typeof(IPdfExtractor));
		extractor.Lifetime.Should().Be(ServiceLifetime.Scoped);
	}

	[Fact]
	public void AddOcrServices_NoArg_MinioAndElasticAreSingletons()
	{
		// Arrange
		ServiceCollection services = new();
		services.AddSingleton(BuildConfiguration());
		services.AddLogging();

		// Act
		services.AddOcrServices();

		// Assert — clients hold connection pools and must be singletons
		services.Single(d => d.ServiceType == typeof(IMinioClient)).Lifetime
			.Should().Be(ServiceLifetime.Singleton);
		services.Single(d => d.ServiceType == typeof(ElasticsearchClient)).Lifetime
			.Should().Be(ServiceLifetime.Singleton);
		services.Single(d => d.ServiceType == typeof(ISearchIndexService)).Lifetime
			.Should().Be(ServiceLifetime.Singleton);
		services.Single(d => d.ServiceType == typeof(IStorageService)).Lifetime
			.Should().Be(ServiceLifetime.Singleton);
	}

	[Theory]
	[InlineData("http://minio.local:9000")]
	[InlineData("https://minio.local:9443")]
	public void AddOcrServices_WithAbsoluteHttpEndpoint_BuildsClient(string endpoint)
	{
		ServiceCollection services = new();
		services.AddSingleton(BuildConfiguration(new Dictionary<string, string?>
		{
			["Storage:Minio:Endpoint"] = endpoint
		}));
		services.AddLogging();
		services.AddOcrServices();

		using ServiceProvider provider = services.BuildServiceProvider();
		provider.GetRequiredService<IMinioClient>().Should().NotBeNull();
	}

	[Theory]
	[InlineData("minio.local:9000")]
	[InlineData("ftp://minio.local:21")]
	[InlineData("http://user:password@minio.local:9000")]
	[InlineData("http://minio.local:9000/prefix")]
	[InlineData("http://minio.local:9000?region=local")]
	[InlineData("http://minio.local:9000#fragment")]
	public void AddOcrServices_WithInvalidEndpoint_RejectsOptions(string endpoint)
	{
		ServiceCollection services = new();
		services.AddSingleton(BuildConfiguration(new Dictionary<string, string?>
		{
			["Storage:Minio:Endpoint"] = endpoint
		}));
		services.AddLogging();
		services.AddOcrServices();

		using ServiceProvider provider = services.BuildServiceProvider();
		Action resolve = () => _ = provider.GetRequiredService<IOptions<MinioOptions>>().Value;

		resolve.Should().Throw<OptionsValidationException>()
			.WithMessage("*absolute HTTP or HTTPS origin*");
	}

	// ═══════════════════════════════════════════════════════════════
	// AddGenAiServices(IConfiguration) — wrapper around AddPaperlessGenAI
	// ═══════════════════════════════════════════════════════════════

	[Fact]
	public void AddGenAiServices_ReturnsSameCollectionAndRegistersGenAi()
	{
		// Arrange
		ServiceCollection services = new();
		services.AddLogging();
		IConfiguration config = BuildConfiguration();

		// Act
		IServiceCollection returned = services.AddGenAiServices(config);

		// Assert — fluent return contract preserved
		returned.Should().BeSameAs(services);

		// AddPaperlessGenAI registers ITextSummarizer (the GenAI worker depends on it)
		// and binds GeminiOptions from the Gemini configuration section.
		services.Should().Contain(d => d.ServiceType == typeof(ITextSummarizer),
			"AddPaperlessGenAI registers the text-summarizer HTTP client");
		services.Should().Contain(d =>
				d.ServiceType.FullName != null &&
				d.ServiceType.FullName.Contains("GeminiOptions", StringComparison.Ordinal),
			"AddPaperlessGenAI binds GeminiOptions via BindConfiguration");
	}
}
