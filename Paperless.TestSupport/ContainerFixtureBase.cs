namespace Paperless.TestSupport;

/// <summary>
///     Template-method base for the integration-test container fixtures. Owns the
///     shared container lifecycle (RabbitMQ + MinIO + Elasticsearch, plus an optional
///     Postgres), MinIO bucket creation, Elasticsearch readiness polling, the
///     Elasticsearch document/search polling helpers, and guarded teardown.
///     <para>
///         Derived fixtures supply the system-under-test by overriding
///         <see cref="ConfigureSutAsync" /> (which must assign <see cref="Services" />)
///         and tear it down in <see cref="DisposeSutAsync" />. The base never references
///         PaperlessREST or PaperlessServices.
///     </para>
/// </summary>
public abstract class ContainerFixtureBase : IAsyncLifetime
{
	private const int ElasticsearchPort = 9200;

	private readonly PostgreSqlContainer? _postgres;
	private readonly RabbitMqContainer _rabbit = TestContainers.RabbitMq();
	private readonly MinioContainer _minio = TestContainers.Minio();
	private readonly ElasticsearchContainer _elastic = TestContainers.Elasticsearch();

	protected ContainerFixtureBase()
	{
		_postgres = UsesPostgres ? TestContainers.Postgres() : null;
	}

	/// <summary>Whether to start a Postgres container (REST = true, Services = false).</summary>
	protected abstract bool UsesPostgres { get; }

	/// <summary>Unique per-fixture bucket name; the bucket is created during init.</summary>
	protected string BucketName { get; } = $"test-{Guid.NewGuid():N}";

	/// <summary>Unique per-fixture default Elasticsearch index name.</summary>
	protected string IndexName { get; } = $"test_{Guid.NewGuid():N}";

	/// <summary>MinIO host:port endpoint string (valid after containers start).</summary>
	protected string MinioEndpoint => MinioBucket.Endpoint(_minio);

	protected string MinioAccessKey => _minio.GetAccessKey();
	protected string MinioSecretKey => _minio.GetSecretKey();
	protected string RabbitConnectionString => _rabbit.GetConnectionString();

	protected string ElasticsearchUri =>
		$"http://{_elastic.Hostname}:{_elastic.GetMappedPublicPort(ElasticsearchPort)}";

	/// <summary>Postgres connection string; throws if <see cref="UsesPostgres" /> is false.</summary>
	protected string PostgresConnectionString =>
		(_postgres ?? throw new InvalidOperationException(
			"This fixture did not request a Postgres container (UsesPostgres == false)."))
		.GetConnectionString();

	/// <summary>Service provider for the constructed SUT. Assigned by <see cref="ConfigureSutAsync" />.</summary>
	public IServiceProvider Services { get; protected set; } = null!;

	public async ValueTask InitializeAsync()
	{
		var starts = new List<Task>
		{
			_rabbit.StartAsync(),
			_minio.StartAsync(),
			_elastic.StartAsync()
		};
		if (_postgres is not null) starts.Add(_postgres.StartAsync());
		await Task.WhenAll(starts);

		await WaitForElasticsearchAsync();
		await MinioBucket.CreateBucketAsync(_minio, BucketName);

		await ConfigureSutAsync();
	}

	public async ValueTask DisposeAsync()
	{
		// Guard SUT teardown and swallow per-container Dispose failures so that an
		// exception thrown during InitializeAsync (e.g. a wait-strategy timeout) is
		// not masked by a secondary NRE/dispose error during xUnit fixture cleanup.
		try { await DisposeSutAsync(); } catch { /* best-effort */ }

		try { await _rabbit.DisposeAsync(); } catch { /* best-effort */ }
		try { await _minio.DisposeAsync(); } catch { /* best-effort */ }
		try { await _elastic.DisposeAsync(); } catch { /* best-effort */ }
		if (_postgres is not null)
		{
			try { await _postgres.DisposeAsync(); } catch { /* best-effort */ }
		}
	}

	/// <summary>
	///     Builds the system under test and assigns <see cref="Services" />.
	///     Runs after containers are started and the bucket is created.
	/// </summary>
	protected abstract ValueTask ConfigureSutAsync();

	/// <summary>Tears down the system under test (host/factory). Default: no-op.</summary>
	protected virtual ValueTask DisposeSutAsync() => ValueTask.CompletedTask;

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
		return await client.SearchAsync(configureSearch, cancellationToken);
	}

	private async Task WaitForElasticsearchAsync()
	{
		Uri elasticUri = new(ElasticsearchUri + "/");
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
