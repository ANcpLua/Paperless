namespace PaperlessREST.Tests.Integration;

public class RabbitMqExtensionsTests
{
	static RabbitMqExtensionsTests()
	{
		Env.TraversePath().Load(".env.test");
	}

	private static string GetRabbitMqConnectionString() =>
		Environment.GetEnvironmentVariable("RABBITMQ__URI")!;

	[Fact]
	public void AddPaperlessRabbitMq_WithOcrStream_ShouldRegisterSseStream()
	{
		ServiceCollection services = [];
		services.AddLogging();

		IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["RabbitMQ:Uri"] = GetRabbitMqConnectionString()
		}).Build();

		services.AddPaperlessRabbitMq(config, true);

		ServiceProvider provider = services.BuildServiceProvider();
		provider.GetService<ISseStream<OcrEvent>>().Should().NotBeNull();
	}

	[Fact]
	public void AddPaperlessRabbitMq_WithGenAiStreamEnabled_RegistersSseStream()
	{
		ServiceCollection services = [];
		services.AddLogging();

		IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["RabbitMQ:Uri"] = GetRabbitMqConnectionString()
		}).Build();

		services.AddPaperlessRabbitMq(config, includeGenAiResultStream: true);

		ServiceProvider provider = services.BuildServiceProvider();
		ISseStream<GenAIEvent>? sseStream = provider.GetService<ISseStream<GenAIEvent>>();
		sseStream.Should().NotBeNull();
	}
}
