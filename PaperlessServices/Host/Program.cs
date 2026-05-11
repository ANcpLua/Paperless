using PaperlessServices.Host.Extensions;

Env.Load(".env");

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddEnvironmentVariables();

// Register shared infrastructure
builder.Services.AddPaperlessRabbitMq(builder.Configuration);

// Register worker services
builder.Services.AddOcrServices();
builder.Services.AddGenAiServices(builder.Configuration);

IHost host = builder.Build();
await host.RunAsync();

namespace PaperlessServices.Host
{
	/// <summary>
	///     Application entry point for the PaperlessServices worker host.
	/// </summary>
	/// <remarks>
	///     Excluded from coverage because top-level statements generate an async state machine
	///     with unreachable branches for sync/async completion. The actual service registration
	///     and host configuration is tested via integration tests.
	/// </remarks>
	[ExcludeFromCodeCoverage(Justification =
		"Top-level statements - async state machine generates unreachable branches; service wiring tested via integration tests")]
	public partial class Program;
}
