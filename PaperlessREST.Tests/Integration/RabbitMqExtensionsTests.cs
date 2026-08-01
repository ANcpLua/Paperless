namespace PaperlessREST.Tests.Integration;

public class RabbitMqExtensionsTests
{
	private const string RabbitMqUri = "amqp://localhost:5672/";

	[Fact]
	public async Task AddPaperlessRabbitMq_WithOcrStream_ShouldRegisterSseStream()
	{
		ServiceCollection services = [];
		services.AddLogging();

		IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["RabbitMQ:Uri"] = RabbitMqUri
		}).Build();

		services.AddPaperlessRabbitMq(config, true);

		await using ServiceProvider provider = services.BuildServiceProvider();
		provider.GetService<ISseStream<OcrEvent>>().Should().NotBeNull();
	}

	[Fact]
	public async Task AddPaperlessRabbitMq_WithGenAiStreamEnabled_RegistersSseStream()
	{
		ServiceCollection services = [];
		services.AddLogging();

		IConfigurationRoot config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["RabbitMQ:Uri"] = RabbitMqUri
		}).Build();

		services.AddPaperlessRabbitMq(config, includeGenAiResultStream: true);

		await using ServiceProvider provider = services.BuildServiceProvider();
		ISseStream<GenAIEvent>? sseStream = provider.GetService<ISseStream<GenAIEvent>>();
		sseStream.Should().NotBeNull();
	}
}
