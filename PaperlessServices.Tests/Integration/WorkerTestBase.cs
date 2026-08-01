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

public class SharedContainerFixture() : ContainerFixtureBase(usesPostgres: false)
{
	static SharedContainerFixture() => TestEnv.Load();

	private IHost? _host;

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
		if (_host is null) return;

		try
		{
			await _host.StopAsync();
		}
		finally
		{
			_host.Dispose();
		}
	}

	public async Task<string> UploadPdfAsync(string content) =>
		await UploadPdfAsync(await TestPdf.BytesAsync(content));

	public async Task<string> UploadPdfAsync(byte[] content)
	{
		var storageKey =
			$"documents/{TimeProvider.System.GetUtcNow():yyyy-MM}/{Guid.NewGuid():N}/test-{Guid.NewGuid():N}.pdf";
		var client = Services.GetRequiredService<IMinioClient>();

		await using var stream = new MemoryStream(content, writable: false);
		await client.PutObjectAsync(new PutObjectArgs()
			.WithBucket(BucketName)
			.WithObject(storageKey)
			.WithStreamData(stream)
			.WithObjectSize(stream.Length)
			.WithContentType("application/pdf"));

		return storageKey;
	}
}
