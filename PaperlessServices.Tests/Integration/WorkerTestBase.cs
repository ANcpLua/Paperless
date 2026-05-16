using PaperlessServices.Host.Extensions;

[assembly: CaptureConsole]
[assembly: CaptureTrace]

namespace PaperlessServices.Tests.Integration;

/// <summary>
///     Collection definition for shared container fixture.
///     This ensures containers only start when integration tests run.
/// </summary>
[CollectionDefinition(Name)]
public class SharedContainerCollection : ICollectionFixture<SharedContainerFixture>
{
	public const string Name = "SharedContainer";
}

public class SharedContainerFixture : IAsyncLifetime
{
	// Container ports
	private const int ElasticsearchPort = 9200;
	private const int MinioPort = 9000;

	// Default image versions - override via environment variables for CI flexibility
	private const string DefaultElasticsearchImage = "docker.elastic.co/elasticsearch/elasticsearch:9.4.1";
	private const string DefaultMinioImage = "minio/minio:RELEASE.2025-09-07T16-13-09Z";
	private const string DefaultRabbitmqImage = "rabbitmq:4.3.0-management";

	private readonly string _bucketName = $"test-{Guid.NewGuid():N}";

	private readonly ElasticsearchContainer _elastic = new ElasticsearchBuilder()
		.WithImage(Environment.GetEnvironmentVariable("ELASTIC_IMAGE") ?? DefaultElasticsearchImage)
		.WithEnvironment("discovery.type", "single-node")
		.WithEnvironment("xpack.security.enabled", "false")
		.WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
		.Build();

	private readonly string _indexName = $"test_{Guid.NewGuid():N}";

	private readonly MinioContainer _minio = new MinioBuilder()
		.WithImage(Environment.GetEnvironmentVariable("MINIO_IMAGE") ?? DefaultMinioImage)
		.Build();

	private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder()
		.WithImage(Environment.GetEnvironmentVariable("RABBITMQ_IMAGE") ?? DefaultRabbitmqImage)
		.Build();

	private IHost _host = null!;

	static SharedContainerFixture()
	{
		Env.TraversePath().Load(".env.test");
	}

	public IServiceProvider Services { get; private set; } = null!;

	public async ValueTask InitializeAsync()
	{
		await Task.WhenAll(
			_rabbit.StartAsync(),
			_minio.StartAsync(),
			_elastic.StartAsync()
		);

		// Wait for Elasticsearch to be fully ready (not just port open)
		await WaitForElasticsearchAsync();

		string minioEndpoint = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(MinioPort)}";

		using MinioClient minioClient = new();
		minioClient
			.WithEndpoint(minioEndpoint)
			.WithCredentials(_minio.GetAccessKey(), _minio.GetSecretKey())
			.Build();
		await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));

		HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
		builder.Configuration.Sources.Clear();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["RabbitMQ:Uri"] = _rabbit.GetConnectionString(),
			["Storage:Minio:Endpoint"] = minioEndpoint,
			["Storage:Minio:AccessKey"] = _minio.GetAccessKey(),
			["Storage:Minio:SecretKey"] = _minio.GetSecretKey(),
			["Storage:Minio:BucketName"] = _bucketName,
			["Storage:Minio:UseSsl"] = Environment.GetEnvironmentVariable("MINIO_USE_SSL") ?? "false",
			["Elasticsearch:Uri"] = $"http://{_elastic.Hostname}:{_elastic.GetMappedPublicPort(ElasticsearchPort)}",
			["Elasticsearch:DefaultIndex"] = _indexName
		});

		builder.Services.AddLogging(b =>
		{
			b.ClearProviders();
			b.AddFakeLogging(o =>
			{
				o.OutputFormatter = r => $" [{r.Level}] {r.Category}: {r.Message}";
				o.OutputSink = Console.WriteLine;
			});
			b.SetMinimumLevel(LogLevel.Trace);
		});

		builder.Services.AddPaperlessRabbitMq(builder.Configuration);
		builder.Services.AddOcrServices(builder.Configuration);
		builder.Services.AddSingleton<ITextSummarizer, FakeTextSummarizer>();

		_host = builder.Build();
		Services = _host.Services;
		await _host.StartAsync();
	}

	public async ValueTask DisposeAsync()
	{
		await _host.StopAsync();
		_host.Dispose();

		await Task.WhenAll(
			_rabbit.DisposeAsync().AsTask(),
			_minio.DisposeAsync().AsTask(),
			_elastic.DisposeAsync().AsTask()
		);
	}

	public async Task<string> UploadPdfAsync(string content)
	{
		string fileName = $"test-{Guid.NewGuid():N}.pdf";
		string pdfPath = await Pdf.Create(Dye.White).AddText(content).SaveAsync(fileName);

		string storageKey = $"documents/{TimeProvider.System.GetUtcNow():yyyy-MM}/{Guid.NewGuid():N}/{fileName}";
		IMinioClient client = Services.GetRequiredService<IMinioClient>();

		await using FileStream stream = File.OpenRead(pdfPath);
		await client.PutObjectAsync(new PutObjectArgs()
			.WithBucket(_bucketName)
			.WithObject(storageKey)
			.WithStreamData(stream)
			.WithObjectSize(stream.Length)
			.WithContentType("application/pdf"));

		return storageKey;
	}

	/// <summary>
	///     Polls Elasticsearch until a document is found or timeout occurs.
	///     Replaces brittle Task.Delay patterns with deterministic polling.
	/// </summary>
	public async Task<GetResponse<T>> WaitForDocumentAsync<T>(
		string documentId,
		CancellationToken cancellationToken,
		TimeSpan? timeout = null,
		TimeSpan? pollInterval = null)
	{
		timeout ??= TimeSpan.FromSeconds(10);
		pollInterval ??= TimeSpan.FromMilliseconds(100);

		ElasticsearchClient client = Services.GetRequiredService<ElasticsearchClient>();
		using CancellationTokenSource cts = new(timeout.Value);
		using CancellationTokenSource linked =
			CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

		while (!linked.Token.IsCancellationRequested)
		{
			GetResponse<T> response = await client.GetAsync<T>(
				documentId,
				g => g.Index(client.ElasticsearchClientSettings.DefaultIndex),
				linked.Token);

			if (response.Found)
			{
				return response;
			}

			await Task.Delay(pollInterval.Value, linked.Token);
		}

		// Final attempt before throwing
		return await client.GetAsync<T>(
			documentId,
			g => g.Index(client.ElasticsearchClientSettings.DefaultIndex),
			cancellationToken);
	}

	/// <summary>
	///     Polls Elasticsearch search until results are found or timeout occurs.
	/// </summary>
	public async Task<SearchResponse<T>> WaitForSearchResultsAsync<T>(
		Action<SearchRequestDescriptor<T>> configureSearch,
		CancellationToken cancellationToken,
		TimeSpan? timeout = null,
		TimeSpan? pollInterval = null)
	{
		timeout ??= TimeSpan.FromSeconds(10);
		pollInterval ??= TimeSpan.FromMilliseconds(100);

		ElasticsearchClient client = Services.GetRequiredService<ElasticsearchClient>();
		using CancellationTokenSource cts = new(timeout.Value);
		using CancellationTokenSource linked =
			CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

		while (!linked.Token.IsCancellationRequested)
		{
			SearchResponse<T> response = await client.SearchAsync<T>(configureSearch, linked.Token);

			if (response.Documents.Count > 0)
			{
				return response;
			}

			await Task.Delay(pollInterval.Value, linked.Token);
		}

		// Final attempt
		return await client.SearchAsync<T>(configureSearch, cancellationToken);
	}

	private async Task WaitForElasticsearchAsync()
	{
		Uri elasticUri = new($"http://{_elastic.Hostname}:{_elastic.GetMappedPublicPort(ElasticsearchPort)}");
		using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };

		for (int i = 0; i < 30; i++)
		{
			try
			{
				HttpResponseMessage response = await http.GetAsync($"{elasticUri}_cluster/health");
				if (response.IsSuccessStatusCode)
				{
					return;
				}
			}
			catch (HttpRequestException)
			{
				// Container not ready yet
			}

			await Task.Delay(500);
		}

		throw new InvalidOperationException("Elasticsearch failed to become ready");
	}
}
