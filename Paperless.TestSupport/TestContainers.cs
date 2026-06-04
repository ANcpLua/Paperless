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
