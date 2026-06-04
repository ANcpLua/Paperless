# Test-Infrastructure Uplift — Design Spec

Date: 2026-06-04
Scope: `PaperlessREST.Tests`, `PaperlessServices.Tests`, new `Paperless.TestSupport`
Kind: **refactor only** — no new test CASES, no behavior change. Build must stay 0/0 under `./build.sh Compile`; both test projects must still compile.

---

## 0. Goal & guardrails

De-duplicate the two integration-test fixtures (`SharedRestContainerFixture`, `SharedContainerFixture`) and their satellite helpers behind a small shared class library `Paperless.TestSupport`, using a template-method base fixture. Remove genuinely dead infrastructure (the REST Elasticsearch container — see §7). Replace REST env-var mutation with in-memory config injection (mirroring how the Services fixture already does it).

**Hard constraints (verified against source, must hold after the change):**

1. Every member in the SURFACE `mustPreserve` list keeps its exact name, signature, accessibility, and (for `IClassFixture`/`ICollectionFixture`) its public parameterless constructor.
   - `SharedRestContainerFixture` stays `public sealed`, `IAsyncLifetime`, with `Client`/`Services`/`DbFactory` public getters and `CreateAsyncScope()`.
   - `SharedContainerFixture` stays `public`, `IAsyncLifetime`, with `Services`, `UploadPdfAsync(string)`, `WaitForDocumentAsync<T>(…)`, `WaitForSearchResultsAsync<T>(…)`.
   - `SharedContainerCollection` keeps `[CollectionDefinition(Name)] : ICollectionFixture<SharedContainerFixture>` and `const string Name = "SharedContainer"`.
2. `DatabaseFixture` (REST) is OUT OF SCOPE for the base-class refactor. It may only adopt the new `TestEnv`/`TestContainers` helpers for its `.env.test` load and Postgres builder (§6); its public surface (`Services`, `ContextFactory`, `LogCollector`, `CreateAsyncScope`) is untouched.
3. `[assembly: CaptureConsole]` / `[assembly: CaptureTrace]` stay **exactly one pair per test assembly** and **must NOT move into `Paperless.TestSupport`** (assembly-level attributes attach to the assembly they are compiled into; moving them would attach them to the library and silently stop capturing in the test assemblies). They stay in the respective fixture files.
4. `Paperless.TestSupport` references **only** infrastructure packages. It must NOT reference `PaperlessREST.csproj` or `PaperlessServices.csproj` (WAF `<Program>` and `Host.CreateApplicationBuilder` composition stay in the derived fixtures).
5. No new `[Fact]`/`[Theory]`. The five consumer test files change only where a fixture member's *call site* is unaffected — ideally they do not change at all, except `DocumentEndpointTests` whose manual cleanup list is replaced by the RAII helper (§5, behavior-equivalent).

**Evidence that recalibrated the input analysis (do not skip — it changes the plan):**

- The SHARED/PATTERNS analysis claims `WaitForLogAsync`/`WaitForLogCountAsync` are "cross-consumed" by `PaperlessREST.Tests/Unit/ListenerLifecycleTests.cs`. **This is false.** `ListenerLifecycleTests.cs:477` defines its own `private static async Task WaitForLogAsync(FakeLogCollector source, Func<FakeLogRecord,bool> predicate, CancellationToken ct)` — a *different signature* from the Services extension `WaitForLogAsync(this FakeLogCollector, Func<IReadOnlyList<FakeLogRecord>,bool>, TimeSpan?, TimeSpan?, CancellationToken)`. The only consumer of the *extension* wait helpers is `PaperlessServices.Tests/Unit/OcrWorkerTests.cs`. Therefore moving the wait helpers to the shared lib is *proportionate convenience* (both assemblies could use them), not load-bearing — and we will move them, but we will NOT touch `ListenerLifecycleTests`' private method (out of scope, would be a behavioral risk for zero benefit).
- `GetFullLoggerText` IS genuinely shared: 8 consumer files in `PaperlessREST.Tests`, 7 in `PaperlessServices.Tests`, byte-identical bodies (only an XML doc comment differs). Safe to centralize.
- REST tests reference `ElasticsearchClient` **nowhere** except the `Document` type alias in `PaperlessREST.Tests/GlobalUsings.cs:69` (which exists *to disambiguate away from* `Elastic.Clients.Elasticsearch.Document`). No REST test queries Elastic. The REST ES container is dead weight → **remove it** (§7), do not generalize ES into the base.
- `Paperless.slnx` already lists every project flatly and the NUKE `Compile` target builds `GetSolutionPath()` (the whole solution). Adding `Paperless.TestSupport` to `Paperless.slnx` is sufficient for CI to build it — no `Pipeline/` change needed.

---

## 1. File-by-file plan (overview)

| Path | Action | Responsibility |
|---|---|---|
| `Paperless.TestSupport/Paperless.TestSupport.csproj` | NEW | net10.0 lib, `ANcpLua.NET.Sdk`, infra-only package refs |
| `Paperless.TestSupport/GlobalUsings.cs` | NEW | shared usings for the lib |
| `Paperless.TestSupport/TestEnv.cs` | NEW | `.env.test` load (once) + image-name-from-env-with-default |
| `Paperless.TestSupport/TestContainers.cs` | NEW | builder factories: Postgres/RabbitMq/Minio/Elasticsearch (configured, unstarted) |
| `Paperless.TestSupport/MinioBucket.cs` | NEW | create bucket + endpoint string helper |
| `Paperless.TestSupport/AsyncCleanup.cs` | NEW | RAII `IAsyncDisposable` (mirrors `SessionCleanup`) |
| `Paperless.TestSupport/FakeLoggerExtensions.cs` | NEW | single `GetFullLoggerText` + `WaitForLogAsync` + `WaitForLogCountAsync` |
| `Paperless.TestSupport/ContainerFixtureBase.cs` | NEW | abstract template-method base: shared container lifecycle + ES poll helpers + guarded dispose |
| `Paperless.slnx` | MODIFY | add `Paperless.TestSupport/Paperless.TestSupport.csproj` |
| `PaperlessREST.Tests/PaperlessREST.Tests.csproj` | MODIFY | `ProjectReference` TestSupport; drop now-shared package refs that are only in TestSupport? (NO — keep, see §8) |
| `PaperlessServices.Tests/PaperlessServices.Tests.csproj` | MODIFY | `ProjectReference` TestSupport |
| `PaperlessREST.Tests/FakeLoggerExtensions.cs` | DELETE | moved to TestSupport |
| `PaperlessServices.Tests/FakeLoggerExtensions.cs` | DELETE | moved to TestSupport |
| `PaperlessREST.Tests/GlobalUsings.cs` | MODIFY | add `global using Paperless.TestSupport;` |
| `PaperlessServices.Tests/GlobalUsings.cs` | MODIFY | add `global using Paperless.TestSupport;` |
| `PaperlessREST.Tests/Integration/SharedRestContainerFixture.cs` | MODIFY | derive from base; in-memory config; drop ES container; keep surface |
| `PaperlessServices.Tests/Integration/WorkerTestBase.cs` | MODIFY | derive from base; keep surface + collection def |
| `PaperlessREST.Tests/Integration/DatabaseFixture.cs` | MODIFY (light) | use `TestEnv` + `TestContainers.Postgres()`; surface untouched |
| `PaperlessREST.Tests/Integration/DocumentEndpointTests.cs` | MODIFY (light) | replace `_createdDocIds` manual cleanup with `AsyncCleanup` (behavior-equivalent) |

---

## 2. `Paperless.TestSupport` project

### 2.1 `Paperless.TestSupport/Paperless.TestSupport.csproj` (NEW)

Plain `ANcpLua.NET.Sdk` (NOT `.Test` — it is a support library, not a test runner project; it must not pull MTP/xUnit-runner wiring). It is referenced by the two `.Test`/`.Web` test projects. `IsTestProject` is left unset.

```xml
<Project Sdk="ANcpLua.NET.Sdk">

	<PropertyGroup>
		<TargetFramework>net10.0</TargetFramework>
		<RootNamespace>Paperless.TestSupport</RootNamespace>
		<!-- Support library for the two test assemblies. Not a test project itself:
		     no MTP runner, no [Fact]s. xUnit primitives (IAsyncLifetime,
		     CollectionDefinition) come from the xunit.v3 package reference below. -->
		<IsPackable>false</IsPackable>
	</PropertyGroup>

	<ItemGroup>
		<!-- Infrastructure only. MUST NOT reference PaperlessREST or PaperlessServices. -->
		<PackageReference Include="DotNetEnv"/>
		<PackageReference Include="Minio"/>
		<PackageReference Include="Elastic.Clients.Elasticsearch"/>
		<!-- DI.Abstractions (IServiceProvider/IServiceScope helpers) flows transitively via
		     Diagnostics.Testing/Testcontainers — no explicit ref (avoids an unpinned CPM entry). -->
		<PackageReference Include="Microsoft.Extensions.Diagnostics.Testing"/>
		<PackageReference Include="Testcontainers.Elasticsearch"/>
		<PackageReference Include="Testcontainers.Minio"/>
		<PackageReference Include="Testcontainers.PostgreSql"/>
		<PackageReference Include="Testcontainers.RabbitMq"/>
		<!-- IAsyncLifetime + [CollectionDefinition]/ICollectionFixture come from xunit.v3.core,
		     pulled transitively via xunit.v3.mtp-v2 — the repo's pinned, runner-free lineage.
		     Do NOT reference bare xunit.v3: it has no CPM PackageVersion (NU1604) and would pull a
		     SECOND xunit core lineage alongside .mtp-v2 (the runner-mixing the repo deliberately avoids). -->
		<PackageReference Include="xunit.v3.mtp-v2"/>
	</ItemGroup>

</Project>
```

> Version pins: every `PackageReference` above already has a `PackageVersion` in `Directory.Packages.props` (`Testcontainers.*`, `Minio`, `Elastic.Clients.Elasticsearch`, `DotNetEnv`, `Microsoft.Extensions.Diagnostics.Testing`, and **`xunit.v3.mtp-v2`** = `$(XunitV3MtpV2Version)`). **Resolved (review fix, was §9.1):** the lib references **`xunit.v3.mtp-v2`** (the repo's pinned, runner-free lineage) — NOT bare `xunit.v3` (no CPM entry → NU1604, and it would pull a second xunit core lineage). `Microsoft.Extensions.DependencyInjection.Abstractions` is **not** referenced explicitly — it flows transitively, so no new CPM entry is needed and restore stays clean.

### 2.2 `Paperless.TestSupport/GlobalUsings.cs` (NEW)

```csharp
global using System.Diagnostics.CodeAnalysis;

global using DotNet.Testcontainers.Builders;
global using DotNet.Testcontainers.Containers;

global using DotNetEnv;

global using Elastic.Clients.Elasticsearch;

global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging.Testing;

global using Minio;
global using Minio.DataModel.Args;

global using Testcontainers.Elasticsearch;
global using Testcontainers.Minio;
global using Testcontainers.PostgreSql;
global using Testcontainers.RabbitMq;

global using Xunit;
```

### 2.3 `Paperless.TestSupport/TestEnv.cs` (NEW)

Centralizes the three duplicated `Env.TraversePath().Load(".env.test")` static-ctor blocks and the `Environment.GetEnvironmentVariable(name) ?? default` image-resolution idiom.

```csharp
namespace Paperless.TestSupport;

/// <summary>
///     One-time <c>.env.test</c> loading and environment-variable image resolution
///     shared by every integration fixture. Replaces the three duplicated static
///     constructors (REST shared fixture, REST <c>DatabaseFixture</c>, Services fixture).
/// </summary>
public static class TestEnv
{
	private static readonly object Gate = new();
	private static bool _loaded;

	/// <summary>
	///     Loads <c>.env.test</c> exactly once per process via
	///     <see cref="Env.TraversePath" />. Idempotent and thread-safe so it can be
	///     called from every fixture's static constructor without re-loading.
	/// </summary>
	public static void Load()
	{
		if (_loaded) return;
		lock (Gate)
		{
			if (_loaded) return;
			Env.TraversePath().Load(".env.test");
			_loaded = true;
		}
	}

	/// <summary>
	///     Returns the container image for <paramref name="envVar" />, falling back to
	///     <paramref name="defaultImage" /> when the variable is unset. Mirrors the
	///     <c>Environment.GetEnvironmentVariable(...) ?? "image:tag"</c> pattern the
	///     fixtures duplicated per container.
	/// </summary>
	public static string Image(string envVar, string defaultImage) =>
		Environment.GetEnvironmentVariable(envVar) ?? defaultImage;
}
```

### 2.4 `Paperless.TestSupport/TestContainers.cs` (NEW)

Single home for the four container builders. Each returns a **configured-but-unstarted** container. The Elasticsearch builder folds in the `xpack.security.http.ssl.enabled=false` fix from the Services fixture (the comment is preserved verbatim — it is load-bearing tribal knowledge per CLAUDE.md Gotchas). Default image tags are taken from the current source.

```csharp
namespace Paperless.TestSupport;

/// <summary>
///     Factory methods producing configured-but-unstarted Testcontainers.
///     Centralizes image defaults (overridable via env vars) and the
///     Elasticsearch TLS wait-strategy fix that both fixtures previously duplicated.
/// </summary>
public static class TestContainers
{
	private const string DefaultPostgresImage = "postgres:17-alpine";
	private const string DefaultRabbitmqImage = "rabbitmq:4.3.0-management";
	private const string DefaultMinioImage = "minio/minio:RELEASE.2025-09-07T16-13-09Z";
	private const string DefaultElasticsearchImage =
		"docker.elastic.co/elasticsearch/elasticsearch:9.1.3";

	public static PostgreSqlContainer Postgres() =>
		new PostgreSqlBuilder(TestEnv.Image("POSTGRES_IMAGE", DefaultPostgresImage))
			.WithWaitStrategy(Wait.ForUnixContainer()
				.UntilMessageIsLogged("database system is ready to accept connections"))
			.Build();

	public static RabbitMqContainer RabbitMq() =>
		new RabbitMqBuilder(TestEnv.Image("RABBITMQ_IMAGE", DefaultRabbitmqImage))
			.Build();

	public static MinioContainer Minio() =>
		new MinioBuilder(TestEnv.Image("MINIO_IMAGE", DefaultMinioImage))
			.Build();

	public static ElasticsearchContainer Elasticsearch() =>
		new ElasticsearchBuilder(TestEnv.Image("ELASTIC_IMAGE", DefaultElasticsearchImage))
			.WithEnvironment("discovery.type", "single-node")
			.WithEnvironment("xpack.security.enabled", "false")
			// Required so Testcontainers' ElasticsearchConfiguration.TlsEnabled evaluates to false
			// (it AND-s xpack.security.enabled with xpack.security.http.ssl.enabled). Without this,
			// the built-in wait strategy probes HTTPS while ES listens on plain HTTP, and hangs.
			.WithEnvironment("xpack.security.http.ssl.enabled", "false")
			.WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
			.WithEnvironment("bootstrap.memory_lock", "false")
			.Build();
}
```

> Note: the REST fixture previously set an explicit `.WithWaitStrategy(... "started")` on ES; that path is being deleted with the ES container (§7). The Services fixture relied on the default wait strategy + its own `WaitForElasticsearchAsync()` cluster-health poll. We keep the Services semantics: `TestContainers.Elasticsearch()` does NOT add an explicit message wait strategy (the `xpack...ssl.enabled=false` env makes the built-in strategy work), and the base fixture's ES readiness poll (§4.1) preserves `WaitForElasticsearchAsync`.

### 2.5 `Paperless.TestSupport/MinioBucket.cs` (NEW)

```csharp
namespace Paperless.TestSupport;

/// <summary>
///     MinIO endpoint/bucket helpers shared by the fixtures. Wraps the
///     <c>$"{Hostname}:{MappedPort}"</c> endpoint string and the one-shot
///     <c>MakeBucketAsync</c> block both fixtures duplicated.
/// </summary>
public static class MinioBucket
{
	private const int MinioPort = 9000;

	/// <summary>Host:port endpoint string for a started MinIO container.</summary>
	public static string Endpoint(MinioContainer minio) =>
		$"{minio.Hostname}:{minio.GetMappedPublicPort(MinioPort)}";

	/// <summary>Creates <paramref name="bucketName" /> against the started container.</summary>
	public static async Task CreateBucketAsync(MinioContainer minio, string bucketName)
	{
		using MinioClient client = new();
		client
			.WithEndpoint(Endpoint(minio))
			.WithCredentials(minio.GetAccessKey(), minio.GetSecretKey())
			.Build();
		await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
	}
}
```

### 2.6 `Paperless.TestSupport/AsyncCleanup.cs` (NEW)

Generic RAII teardown mirroring agent-framework's `SessionCleanup`. Used by `DocumentEndpointTests` to replace its manual `_createdDocIds` list. Kept minimal (YAGNI): a single `Func<ValueTask>` invoked once on dispose.

```csharp
namespace Paperless.TestSupport;

/// <summary>
///     RAII per-test cleanup: <c>await using var cleanup = new AsyncCleanup(() =&gt; ...);</c>
///     runs the delegate on scope exit even when an assertion throws. Mirrors the
///     agent-framework <c>SessionCleanup</c> pattern. The delegate runs at most once.
/// </summary>
public sealed class AsyncCleanup(Func<ValueTask> onDispose) : IAsyncDisposable
{
	private int _disposed;

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		await onDispose();
	}
}
```

### 2.7 `Paperless.TestSupport/FakeLoggerExtensions.cs` (NEW)

Single copy. `GetFullLoggerText` body is byte-identical to both current copies. `WaitForLogAsync`/`WaitForLogCountAsync` are lifted verbatim from the Services copy (the only one that has them). Namespace becomes `Paperless.TestSupport`; consumers pick it up via the added `global using` (§8.3).

```csharp
namespace Paperless.TestSupport;

public static class FakeLoggerExtensions
{
	/// <summary>
	///     Gets full log text from the collector with optional formatting.
	/// </summary>
	public static string GetFullLoggerText(
		this FakeLogCollector source,
		Func<FakeLogRecord, string>? formatter = null)
	{
		StringBuilder sb = new();
		IReadOnlyList<FakeLogRecord> snapshot = source.GetSnapshot();
		formatter ??= record => $"{record.Level} - {record.Message}";

		foreach (FakeLogRecord record in snapshot)
		{
			sb.AppendLine(formatter(record));
		}

		return sb.ToString();
	}

	/// <summary>
	///     Waits for a log condition to be met, polling at regular intervals.
	///     Returns true if condition was met, false if timeout expired.
	/// </summary>
	public static async Task<bool> WaitForLogAsync(
		this FakeLogCollector source,
		Func<IReadOnlyList<FakeLogRecord>, bool> condition,
		TimeSpan? timeout = null,
		TimeSpan? pollInterval = null,
		CancellationToken cancellationToken = default)
	{
		timeout ??= TimeSpan.FromSeconds(5);
		pollInterval ??= TimeSpan.FromMilliseconds(25);

		using CancellationTokenSource cts =
			CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(timeout.Value);

		try
		{
			while (!cts.Token.IsCancellationRequested)
			{
				if (condition(source.GetSnapshot()))
				{
					return true;
				}

				await Task.Delay(pollInterval.Value, cts.Token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			// Timeout expired, not user cancellation
		}

		return condition(source.GetSnapshot()); // Final check
	}

	/// <summary>
	///     Waits for a specific number of log messages matching a predicate.
	/// </summary>
	public static Task<bool> WaitForLogCountAsync(
		this FakeLogCollector source,
		Func<FakeLogRecord, bool> predicate,
		int expectedCount,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default) =>
		source.WaitForLogAsync(
			logs => logs.Count(predicate) >= expectedCount,
			timeout,
			cancellationToken: cancellationToken);
}
```

> `StringBuilder` needs `System.Text.Both` current copies compile because each test project's `GlobalUsings.cs` (or the SDK's implicit usings) brings in `System.Text`. `Paperless.TestSupport` uses `ANcpLua.NET.Sdk` with implicit usings (the SDK enables `<ImplicitUsings>enable</ImplicitUsings>`), so `System.Text.StringBuilder`, `System.Threading`, `System.Threading.Tasks`, and `System.Linq` are in scope. If implicit usings are NOT enabled by the SDK, add `global using System.Text;` to `Paperless.TestSupport/GlobalUsings.cs`. (§9.2 open decision — verify by build.)

### 2.8 `Paperless.TestSupport/ContainerFixtureBase.cs` (NEW)

The template-method base. It owns the **shared** lifecycle only: declaring which containers to start, starting them in parallel, MinIO bucket creation, the ES readiness poll, the ES polling helpers, and guarded teardown. It does **not** know about `WebApplicationFactory<Program>`, `Host.CreateApplicationBuilder`, Postgres+`MapEnum<DocumentStatus>`, config delivery, or `FakeTextSummarizer` — all of which stay in derived fixtures via the `ConfigureSutAsync` hook.

Design choices:
- `protected abstract bool UsesPostgres { get; }` (REST=true, Services=false) controls whether a Postgres container is started; REST exposes `PostgresConnectionString` to its own `ConfigureSutAsync`.
- The ES polling helpers (`WaitForDocumentAsync<T>`, `WaitForSearchResultsAsync<T>`) live here so the Services fixture inherits them unchanged. They resolve `ElasticsearchClient` from `Services`, so they only work after `Services` is set — same as today.
- `Services` is `public` with `protected set` so derived fixtures assign it in `ConfigureSutAsync`.
- Guarded teardown adopts the Services-style try/swallow per the analysis recommendation; REST loses no correctness (its old unguarded path was strictly weaker).
- The base owns container fields and the per-fixture bucket/index name so both derived fixtures stop re-declaring them.

```csharp
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
```

> The two ES polling helpers and `WaitForElasticsearchAsync` are copied verbatim (comments included) from the current `WorkerTestBase.cs`; only the index/uri sources move to base properties. `WaitForDocumentAsync`/`WaitForSearchResultsAsync` remain `public` to satisfy `mustPreserve` (Services consumers call them through the inherited member). `System.Net.Http.HttpClient` and `System.Uri` are available via SDK implicit usings; add `global using System.Net.Http;` to the lib's GlobalUsings if a build error proves otherwise (§9.2).

---

## 3. `Paperless.slnx` (MODIFY)

Add one `<Project>` line (flat — consistent with the existing layout). Order is cosmetic; place after the test projects.

```xml
<Solution>
  <Project Path="PaperlessREST/PaperlessREST.csproj" />
  <Project Path="PaperlessServices/PaperlessServices.csproj" />
  <Project Path="PaperlessREST.Tests/PaperlessREST.Tests.csproj" />
  <Project Path="PaperlessServices.Tests/PaperlessServices.Tests.csproj" />
  <Project Path="Paperless.TestSupport/Paperless.TestSupport.csproj" />
  <Project Path="PaperlessUI.Angular/PaperlessUI.Angular.esproj" />
  <Project Path="PaperlessUI.Blazor/PaperlessUI.Blazor.csproj" />
  <Project Path="PaperlessUI.React/PaperlessUI.React.esproj" />
  <Project Path="Pipeline/Build.csproj" />
</Solution>
```

> The NUKE `Compile` target builds `GetSolutionPath()` (the whole slnx), so this single line is what makes CI build `Paperless.TestSupport`. No `Pipeline/` change required.

---

## 4. `PaperlessREST.Tests/Integration/SharedRestContainerFixture.cs` (MODIFY)

> **⚠ Implementation correction (post-validation).** The "remove ALL `Environment.SetEnvironmentVariable`
> and feed config via `ConfigureAppConfiguration` in-memory" plan below **does not work for this WAF
> fixture** and was reverted. `WebApplicationFactory<Program>` (minimal hosting) builds the app's own
> configuration, whose environment-variable source — populated process-globally by `.env.test`
> (`TestEnv.Load`) — outranks anything the factory adds via `ConfigureAppConfiguration`; even
> `config.Sources.Clear()` only touches the host-config layer. Result: the REST host bound
> `RABBITMQ__URI=localhost:5672` and **every endpoint 500'd (`BrokerUnreachable`)** — caught by the
> integration suite, not by build/unit tests. The shipped fixture therefore sets the infra **env vars**
> to the Testcontainers endpoints in `ConfigureSutAsync` (the original, proven mechanism — the only
> thing the WAF host reads). The env-var-mutation removal **succeeds for the Services fixture** (a plain
> `Host.CreateApplicationBuilder` where `Configuration.Sources.Clear()` genuinely controls app config);
> it is **not achievable for the minimal-hosting WAF**. Sections 4.1/4.2 below describe the abandoned
> in-memory approach and are kept for the record.

### 4.1 What changes

- Class derives from `ContainerFixtureBase` instead of implementing `IAsyncLifetime` directly. Stays `public sealed`.
- `UsesPostgres => true`.
- **Remove the Elasticsearch container entirely** (REST has zero ES consumers — §7). *But the base always starts ES.* See §7 for the chosen resolution: the base starts ES for both; REST simply never queries it. (Alternative — a `UsesElasticsearch` flag — is documented in §9.3 as an open decision; the proportionate default keeps ES in the base since it is harmless and the Services fixture needs it, avoiding a second abstract knob for one consumer.)
- **Remove ALL `Environment.SetEnvironmentVariable(...)` mutation.** Feed `WebApplicationFactory<Program>` via `ConfigureAppConfiguration` in-memory (mirroring the Services fixture). This eliminates cross-test global env pollution.
- Static ctor delegates to `TestEnv.Load()`.
- Container fields, bucket creation, and image resolution are gone (now in base).
- `Client`/`Services`/`DbFactory`/`CreateAsyncScope` preserved exactly. `Services` now comes from the base (`protected set`); the fixture assigns `Services = Factory.Services` inside `ConfigureSutAsync`.
- `[assembly: CaptureConsole]`/`[assembly: CaptureTrace]` stay at the top of this file.

### 4.2 Full intended content

```csharp
using PaperlessREST.Host;

[assembly: CaptureConsole]
[assembly: CaptureTrace]

namespace PaperlessREST.Tests.Integration;

public sealed class SharedRestContainerFixture : ContainerFixtureBase
{
	static SharedRestContainerFixture() => TestEnv.Load();

	protected override bool UsesPostgres => true;

	public HttpClient Client { get; private set; } = null!;
	public IDbContextFactory<DocumentPersistence> DbFactory { get; private set; } = null!;

	public AsyncServiceScope CreateAsyncScope() => Services.CreateAsyncScope();

	private WebApplicationFactory<Program>? _factory;

	protected override async ValueTask ConfigureSutAsync()
	{
		_factory = new ConfiguredWebApplicationFactory(
			PostgresConnectionString,
			RabbitConnectionString,
			MinioEndpoint,
			MinioAccessKey,
			MinioSecretKey,
			BucketName,
			ElasticsearchUri);

		Client = _factory.CreateClient();
		Services = _factory.Services;
		DbFactory = Services.GetRequiredService<IDbContextFactory<DocumentPersistence>>();

		await using var db = await DbFactory.CreateDbContextAsync();
		await db.Database.MigrateAsync();
	}

	protected override async ValueTask DisposeSutAsync()
	{
		if (_factory is not null)
			await _factory.DisposeAsync();
	}

	private sealed class ConfiguredWebApplicationFactory(
		string postgresConnectionString,
		string rabbitConnectionString,
		string minioEndpoint,
		string minioAccessKey,
		string minioSecretKey,
		string bucketName,
		string elasticsearchUri)
		: WebApplicationFactory<Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			// Replaces the old Environment.SetEnvironmentVariable(...) global mutation.
			// WebApplicationFactory reads these via the host's IConfiguration just like
			// the Services fixture's AddInMemoryCollection. Colon-keyed to match the
			// option binding (ConnectionStrings:*, Storage:Minio:*, Elasticsearch:*).
			builder.UseEnvironment("Test");
			builder.ConfigureAppConfiguration((_, config) =>
			{
				config.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["ConnectionStrings:PaperlessDb"] = postgresConnectionString,
					["ConnectionStrings:Hangfire"] = postgresConnectionString,
					["RabbitMQ:Uri"] = rabbitConnectionString,
					["Storage:Minio:Endpoint"] = minioEndpoint,
					["Storage:Minio:AccessKey"] = minioAccessKey,
					["Storage:Minio:SecretKey"] = minioSecretKey,
					["Storage:Minio:BucketName"] = bucketName,
					["Storage:Minio:UseSsl"] = "false",
					["Elasticsearch:Uri"] = elasticsearchUri,
					// [Required] + ValidateOnStart on ElasticsearchOptions.DefaultIndex → the REST host
					// throws at CreateClient() without it. Thread base IndexName through the factory ctor
					// (add an `indexName` ctor param alongside the others) so env-var dependence is TRULY
					// removed — not silently satisfied by .env.test's ELASTICSEARCH__DEFAULTINDEX process env.
					["Elasticsearch:DefaultIndex"] = indexName
				});
			});

			builder.ConfigureTestServices(services =>
			{
				services.RemoveAll<IHostedService>();

				services.RemoveAll<IDbContextFactory<DocumentPersistence>>();

				var dataSource = new NpgsqlDataSourceBuilder(postgresConnectionString)
					.MapEnum<DocumentStatus>("document_status")
					.Build();

				services.AddPooledDbContextFactory<DocumentPersistence>(opts =>
					opts.UseNpgsql(dataSource));

				services.RemoveAll<JobStorage>();
				services.AddSingleton<JobStorage>(new MemoryStorage());

				services.AddFakeLogging();
			});
		}
	}
}
```

> **Config-key verification required (§9.4):** the old code set process env vars `CONNECTIONSTRINGS__PAPERLESSDB`, `RABBITMQ__URI`, `STORAGE__MINIO__*`, `ELASTICSEARCH__URI` (double-underscore = colon). The in-memory keys above use the colon form of the SAME keys, so the app's existing option binding (`PaperlessREST/Configuration/*` + `Host/Extensions/ServiceCollectionExtensions.cs`) resolves identically. **The implementer MUST grep `PaperlessREST` for the exact config section names** (`GetConnectionString("PaperlessDb")` vs a bound `ConnectionStrings` options class, the `RabbitMQ` section key casing, `Storage:Minio` vs `Storage__Minio`) and match the keys precisely. If any binding is case- or path-sensitive in a way that differs from the documented `appsettings`/options sections, adjust the dictionary keys — do NOT reintroduce `Environment.SetEnvironmentVariable`. `ASPNETCORE_ENVIRONMENT=Test` is replaced by `builder.UseEnvironment("Test")` which is the WAF-native, non-global equivalent.

---

## 5. `PaperlessREST.Tests/Integration/DocumentEndpointTests.cs` (MODIFY, light)

Only the cleanup mechanism changes — the four `[Fact]`s, their arrange/act/assert, and all assertions are untouched. The manual `_createdDocIds` list + `IAsyncLifetime.DisposeAsync` block is replaced by an `AsyncCleanup` instance created in the constructor and disposed by xUnit. This both demonstrates the RAII helper and removes the bespoke tracking. Behavior is equivalent: the same documents are deleted via the same `ExecuteDeleteAsync`.

### 5.1 Precise edits

The class keeps `IClassFixture<SharedRestContainerFixture>, IAsyncLifetime` (xUnit invokes `IAsyncLifetime.DisposeAsync`). Replace the fields + lifecycle + helper to route through `AsyncCleanup`.

- Keep field `private readonly List<Guid> _createdDocIds = [];` (still the record of what to delete).
- Replace the `IAsyncLifetime` region:

```csharp
#region IAsyncLifetime

public ValueTask InitializeAsync() => ValueTask.CompletedTask;

public ValueTask DisposeAsync() => _cleanup.DisposeAsync();

#endregion
```

- Add the cleanup field, initialized in the constructor so it captures `_fixture` and `_createdDocIds`:

```csharp
private readonly AsyncCleanup _cleanup;

public DocumentEndpointTests(SharedRestContainerFixture fixture)
{
	_fixture = fixture;
	_cleanup = new AsyncCleanup(async () =>
	{
		if (_createdDocIds.Count == 0) return;
		await using var scope = _fixture.CreateAsyncScope();
		var factory =
			scope.ServiceProvider.GetRequiredService<IDbContextFactory<DocumentPersistence>>();
		await using var db = await factory.CreateDbContextAsync();
		await db.Documents.Where(d => _createdDocIds.Contains(d.Id)).ExecuteDeleteAsync();
	});
}
```

- `_createdDocIds.Add(...)` / `.Remove(...)` call sites inside the tests are **unchanged** (the list is still the source of truth).

> This edit is optional-but-recommended (it is the one place the spec spends "new abstraction" budget on a consumer). If the implementer judges it adds risk for little gain, the fallback is: keep `DocumentEndpointTests` exactly as-is and let `AsyncCleanup` be exercised only by being present in the shared lib. **Recommendation: do the edit** — it is behavior-equivalent and is the canonical demonstration the uplift exists. Flagged in §9.5.

---

## 6. `PaperlessREST.Tests/Integration/DatabaseFixture.cs` (MODIFY, light)

`DatabaseFixture` is NOT migrated to the base (it is Postgres-only with a hand-built DI graph — different shape, out of scope). Two surgical de-duplications only; **public surface unchanged**:

- Static ctor: `static DatabaseFixture() => TestEnv.Load();` (was `Env.TraversePath().Load(".env.test");`).
- Constructor: `_container = TestContainers.Postgres();` (was the inline `new PostgreSqlBuilder(...).WithWaitStrategy(...).Build()` — the factory produces the identical wait strategy).

Everything else in `DatabaseFixture` (the `ConfigurationBuilder` with `AddEnvironmentVariables().AddInMemoryCollection`, `MapEnum<DocumentStatus>`, batch options, repositories, `AddFakeLogging`, migration) is left intact.

---

## 7. REST Elasticsearch container — resolution

**Finding:** No `PaperlessREST.Tests` test resolves `ElasticsearchClient` or queries Elastic. The old `SharedRestContainerFixture` started an ES container and set `ELASTICSEARCH__URI` purely so the app's DI could bind an Elastic client at startup; nothing exercised it.

**Decision (proportionate):** The base fixture (`ContainerFixtureBase`) always starts ES because the Services fixture genuinely needs it and the REST app's DI binds an `Elasticsearch:Uri` at host build time. Keeping ES in the base for both fixtures:
- preserves REST app startup (DI binds the Elastic client from `Elasticsearch:Uri`),
- avoids a second abstract knob (`UsesElasticsearch`) introduced for a single asymmetric consumer (YAGNI),
- the REST ES container was *already* being started before this refactor, so this is **not** a new cost.

The CLAUDE.md "dead weight" observation is recorded as an **open decision** (§9.3): a follow-up could add `protected virtual bool UsesElasticsearch => true;` and have REST return `false` *only after* confirming the REST host can boot without an `Elasticsearch:Uri` (today it likely cannot, because the Elastic client is registered unconditionally in REST's DI). That confirmation is a behavior question outside this refactor's no-behavior-change mandate, so ES stays started for REST in this pass.

---

## 8. Test-project edits

### 8.1 `PaperlessREST.Tests/PaperlessREST.Tests.csproj` (MODIFY)

Add the project reference next to the existing one:

```xml
<ItemGroup>
	<ProjectReference Include="..\PaperlessREST\PaperlessREST.csproj"/>
	<ProjectReference Include="..\Paperless.TestSupport\Paperless.TestSupport.csproj"/>
</ItemGroup>
```

Package refs are **kept as-is** (do NOT prune `Testcontainers.*`, `Minio`, `DotNetEnv`, `Microsoft.Extensions.Diagnostics.Testing` from the test project even though TestSupport now also references them). Reason: the test project still uses these types directly outside the fixtures (e.g. `DatabaseFixture` uses `PostgreSqlBuilder`/`NpgsqlDataSourceBuilder`, `FakeLogCollector` is used across many unit tests, `Minio` args in `UploadPdfAsync` paths). Transitive references via TestSupport would technically flow, but explicit refs are clearer and avoid `ImplicitUsings`/analyzer churn. Net new line: one `ProjectReference`.

### 8.2 `PaperlessServices.Tests/PaperlessServices.Tests.csproj` (MODIFY)

```xml
<ItemGroup>
	<ProjectReference Include="..\PaperlessServices\PaperlessServices.csproj"/>
	<ProjectReference Include="..\Paperless.TestSupport\Paperless.TestSupport.csproj"/>
</ItemGroup>
```

### 8.3 `GlobalUsings.cs` (both test projects) (MODIFY)

Add one line each so the moved `FakeLoggerExtensions`, `ContainerFixtureBase`, `AsyncCleanup`, `TestEnv`, `TestContainers`, `MinioBucket` resolve without per-file `using`:

`PaperlessREST.Tests/GlobalUsings.cs` — append:
```csharp
// Shared test support
global using Paperless.TestSupport;
```

`PaperlessServices.Tests/GlobalUsings.cs` — append:
```csharp
// Shared test support
global using Paperless.TestSupport;
```

> Both projects already have `global using Microsoft.Extensions.Logging.Testing;` (for `FakeLogCollector`/`FakeLogRecord`) and the Elastic/Testcontainers usings, so the moved members compile at their call sites.

### 8.4 Delete the duplicated extension files

- DELETE `PaperlessREST.Tests/FakeLoggerExtensions.cs`
- DELETE `PaperlessServices.Tests/FakeLoggerExtensions.cs`

All `GetFullLoggerText`/`WaitForLogAsync`/`WaitForLogCountAsync` call sites resolve to `Paperless.TestSupport.FakeLoggerExtensions` via the global using. (15 consumer files total; none change.) `ListenerLifecycleTests.cs`'s *private local* `WaitForLogAsync` is untouched and continues to shadow nothing — it has a different signature and no `this` receiver.

---

## 9. `PaperlessServices.Tests/Integration/WorkerTestBase.cs` (MODIFY)

### 9.0 What changes

- `SharedContainerFixture` derives from `ContainerFixtureBase`. Stays `public`.
- `UsesPostgres => false`.
- Containers, image consts, ports, `_bucketName`/`_indexName`/`_host`, `WaitForElasticsearchAsync`, and the two ES polling helpers move to the base — DELETED from this file.
- `Services` comes from the base; the fixture assigns it in `ConfigureSutAsync` (`Services = _host.Services`).
- `UploadPdfAsync(string)` stays here (Services-specific; uses `IMinioClient` from `Services` and the base's `BucketName`).
- Config injection (`AddInMemoryCollection` with `Storage:Minio:*`, `Elasticsearch:*`, `RabbitMQ:Uri`) stays here — uses base properties (`RabbitConnectionString`, `MinioEndpoint`, etc.) instead of local container fields.
- `FakeTextSummarizer` registration stays here.
- `SharedContainerCollection` (`[CollectionDefinition(Name)]`, `const string Name = "SharedContainer"`) is **unchanged** — kept in this file verbatim.
- `[assembly: CaptureConsole]`/`[assembly: CaptureTrace]` stay at the top of this file.

### 9.1 Full intended content

```csharp
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

public class SharedContainerFixture : ContainerFixtureBase
{
	static SharedContainerFixture() => TestEnv.Load();

	protected override bool UsesPostgres => false;

	private IHost _host = null!;

	protected override async ValueTask ConfigureSutAsync()
	{
		var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
		builder.Configuration.Sources.Clear();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["RabbitMQ:Uri"] = RabbitConnectionString,
			["Storage:Minio:Endpoint"] = MinioEndpoint,
			["Storage:Minio:AccessKey"] = MinioAccessKey,
			["Storage:Minio:SecretKey"] = MinioSecretKey,
			["Storage:Minio:BucketName"] = BucketName,
			["Storage:Minio:UseSsl"] = Environment.GetEnvironmentVariable("MINIO_USE_SSL") ?? "false",
			["Elasticsearch:Uri"] = ElasticsearchUri,
			["Elasticsearch:DefaultIndex"] = IndexName
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

	protected override async ValueTask DisposeSutAsync()
	{
		// _host is assigned in ConfigureSutAsync. If init throws before that line
		// (e.g. a container wait-strategy times out), _host is still null; the base
		// guards this call so a naive _host.StopAsync() NRE cannot mask the real
		// InitializeAsync exception in xUnit's collection-fixture cleanup report.
		if (_host is not null)
		{
			try { await _host.StopAsync(); }
			catch { /* best-effort: don't mask the InitializeAsync exception */ }
			_host.Dispose();
		}
	}

	public async Task<string> UploadPdfAsync(string content)
	{
		var fileName = $"test-{Guid.NewGuid():N}.pdf";
		var pdfPath = await Pdf.Create(Dye.White).AddText(content).SaveAsync(fileName);

		var storageKey = $"documents/{TimeProvider.System.GetUtcNow():yyyy-MM}/{Guid.NewGuid():N}/{fileName}";
		var client = Services.GetRequiredService<IMinioClient>();

		await using var stream = File.OpenRead(pdfPath);
		await client.PutObjectAsync(new PutObjectArgs()
			.WithBucket(BucketName)
			.WithObject(storageKey)
			.WithStreamData(stream)
			.WithObjectSize(stream.Length)
			.WithContentType("application/pdf"));

		return storageKey;
	}
}
```

> `WaitForDocumentAsync<T>`/`WaitForSearchResultsAsync<T>` are now inherited from `ContainerFixtureBase` (public), so the four `[Collection]`-bound tests that call `fixture.WaitFor*` compile unchanged. `Services` is inherited (public). `UploadPdfAsync` resolves `IMinioClient` from the Services DI graph (registered by `AddOcrServices`/`AddPaperlessRabbitMq`) using `BucketName` from the base.

---

## 10. Verification plan

1. `./build.sh Compile` — must stay 0 errors / 0 warnings. This is the floor. Confirms: TestSupport compiles infra-only; both test projects resolve moved members via global usings; no `mustPreserve` member changed signature (a break would surface as a compile error in a consumer).
2. Grep gate (no regressions of the constraints):
   - `[assembly: CaptureConsole]` appears exactly twice repo-wide (once per test assembly), never in `Paperless.TestSupport/`.
   - `Environment.SetEnvironmentVariable` appears 0 times in `SharedRestContainerFixture.cs`.
   - `Env.TraversePath` appears only inside `Paperless.TestSupport/TestEnv.cs`.
   - `FakeLoggerExtensions` defined exactly once (in TestSupport).
3. `./build.sh IntegrationTests` under OrbStack (per MEMORY: verify locally, don't wait on CI) — the five integration test classes must pass unchanged. Particularly validates §4.2's config-key migration (if a key is wrong, REST endpoint tests fail at startup/DB connect) and §5's `AsyncCleanup` (documents still deleted).
4. `./build.sh UnitTests` — confirms the moved `GetFullLoggerText`/`WaitForLog*` extensions still resolve for the 15 unit-test consumers and `OcrWorkerTests`.

---

## 11. Out of scope (explicitly NOT changed)

- `FakeTextSummarizer` (stays internal in Services tests, registered by the Services fixture).
- The Postgres + `DocumentPersistence` + `MapEnum<DocumentStatus>` DAL setup (REST-only, stays in `ConfiguredWebApplicationFactory`).
- `ListenerLifecycleTests.cs` private `WaitForLogAsync` local method.
- `DatabaseFixture` public surface and DI graph (only `.env.test` load + Postgres builder de-duplicated).
- Any `Pipeline/` file (whole-solution build picks up the new project from `Paperless.slnx`).
- Adding/removing/renaming any `[Fact]`/`[Theory]`.
