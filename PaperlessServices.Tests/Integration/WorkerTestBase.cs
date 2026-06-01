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
	private const string DefaultElasticsearchImage = "docker.elastic.co/elasticsearch/elasticsearch:9.1.3";
	private const string DefaultMinioImage = "minio/minio:RELEASE.2025-09-07T16-13-09Z";
	private const string DefaultRabbitmqImage = "rabbitmq:4.3.0-management";

	private readonly string _bucketName = $"test-{Guid.NewGuid():N}";

	private readonly ElasticsearchContainer _elastic = new ElasticsearchBuilder(
			Environment.GetEnvironmentVariable("ELASTIC_IMAGE") ?? DefaultElasticsearchImage)
		.WithEnvironment("discovery.type", "single-node")
		.WithEnvironment("xpack.security.enabled", "false")
		// Required so Testcontainers' ElasticsearchConfiguration.TlsEnabled evaluates to false
		// (it AND-s xpack.security.enabled with xpack.security.http.ssl.enabled). Without this,
		// the built-in wait strategy probes HTTPS while ES listens on plain HTTP, and hangs.
		.WithEnvironment("xpack.security.http.ssl.enabled", "false")
		.WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
		.Build();

	private readonly string _indexName = $"test_{Guid.NewGuid():N}";

	private readonly MinioContainer _minio = new MinioBuilder(
			Environment.GetEnvironmentVariable("MINIO_IMAGE") ?? DefaultMinioImage)
		.Build();

	private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder(
			Environment.GetEnvironmentVariable("RABBITMQ_IMAGE") ?? DefaultRabbitmqImage)
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

		var minioEndpoint = $"{_minio.Hostname}:{_minio.GetMappedPublicPort(MinioPort)}";

		using MinioClient minioClient = new();
		minioClient
			.WithEndpoint(minioEndpoint)
			.WithCredentials(_minio.GetAccessKey(), _minio.GetSecretKey())
			.Build();
		await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));

		var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
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
		builder.Services.AddOcrServices();
		builder.Services.AddSingleton<ITextSummarizer, FakeTextSummarizer>();

		_host = builder.Build();
		Services = _host.Services;
		await _host.StartAsync();
	}

	public async ValueTask DisposeAsync()
	{
		// _host is assigned in InitializeAsync. If init throws before that line
		// (e.g. a container wait-strategy times out), _host is still null and a
		// naive `_host.StopAsync()` here NREs — which masks the real init error
		// in xUnit's collection-fixture cleanup report. Guard the host, and
		// swallow per-container Dispose failures for the same reason.
		if (_host is not null)
		{
			try { await _host.StopAsync(); }
			catch { /* best-effort: don't mask the InitializeAsync exception */ }
			_host.Dispose();
		}

		try { await _rabbit.DisposeAsync(); } catch { /* best-effort */ }
		try { await _minio.DisposeAsync(); } catch { /* best-effort */ }
		try { await _elastic.DisposeAsync(); } catch { /* best-effort */ }
	}

	public async Task<string> UploadPdfAsync(string content)
	{
		var fileName = $"test-{Guid.NewGuid():N}.pdf";
		var pdfPath = await Pdf.Create(Dye.White).AddText(content).SaveAsync(fileName);

		var storageKey = $"documents/{TimeProvider.System.GetUtcNow():yyyy-MM}/{Guid.NewGuid():N}/{fileName}";
		var client = Services.GetRequiredService<IMinioClient>();

		await using var stream = File.OpenRead(pdfPath);
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

		var client = Services.GetRequiredService<ElasticsearchClient>();
		using CancellationTokenSource cts = new(timeout.Value);
		using var linked =
			CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

		while (!linked.Token.IsCancellationRequested)
		{
			var response = await client.GetAsync<T>(
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
		// 30s overall budget: GitHub-hosted runners are markedly slower than local
		// dev machines and the first SearchAsync after index creation can spend
		// several seconds priming query caches even after Refresh.True returns.
		timeout ??= TimeSpan.FromSeconds(30);
		pollInterval ??= TimeSpan.FromMilliseconds(100);

		var client = Services.GetRequiredService<ElasticsearchClient>();
		using CancellationTokenSource overallCts = new(timeout.Value);
		using var overallLinked =
			CancellationTokenSource.CreateLinkedTokenSource(overallCts.Token, cancellationToken);

		// Force an index-level refresh up front. SearchIndexService writes documents
		// with Refresh.True (`?refresh=true`), which is supposed to guarantee
		// immediate searchability — but on slow CI disks the per-document refresh
		// is observed to not always propagate before the first SearchAsync. The
		// explicit Indices.RefreshAsync here is defensive and idempotent: locally
		// it's a no-op (everything's already refreshed), on CI it converts an
		// invisible flake into a passing search.
		try
		{
			await client.Indices.RefreshAsync(
				r => r.Indices(client.ElasticsearchClientSettings.DefaultIndex),
				overallLinked.Token);
		}
		catch (OperationCanceledException) when (overallLinked.Token.IsCancellationRequested)
		{
			// Fall through to the final attempt below.
		}

		while (!overallLinked.Token.IsCancellationRequested)
		{
			try
			{
				var response = await client.SearchAsync<T>(configureSearch, overallLinked.Token);

				if (response.Documents.Count > 0)
				{
					return response;
				}
			}
			catch (OperationCanceledException) when (overallLinked.Token.IsCancellationRequested)
			{
				break;
			}

			try
			{
				await Task.Delay(pollInterval.Value, overallLinked.Token);
			}
			catch (OperationCanceledException)
			{
				break;
			}
		}

		// Final attempt with the caller's token only so the assertion sees real
		// "found nothing" data rather than a TaskCanceledException at the wait boundary.
		return await client.SearchAsync<T>(configureSearch, cancellationToken);
	}

	private async Task WaitForElasticsearchAsync()
	{
		Uri elasticUri = new($"http://{_elastic.Hostname}:{_elastic.GetMappedPublicPort(ElasticsearchPort)}");
		using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };

		for (var i = 0; i < 30; i++)
		{
			try
			{
				var response = await http.GetAsync($"{elasticUri}_cluster/health");
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
