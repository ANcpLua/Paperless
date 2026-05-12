using PaperlessServices.Host.Extensions;

namespace PaperlessServices.Host;

/// <summary>
///     Application entry point for the PaperlessServices worker host.
/// </summary>
/// <remarks>
///     Excluded from coverage because async Main generates a state machine with unreachable
///     branches for sync/async completion. The actual service registration and host
///     configuration is tested via integration tests.
///
///     Kept as an explicit Main (mirrors PaperlessREST/Host/Program.cs) instead of top-level
///     statements: dotcov's --exclude-generated does not honour [ExcludeFromCodeCoverage]
///     on the synthesized <c>&lt;Main&gt;$</c> companion that top-level statements emit,
///     which made this file count against the score despite the attribute.
/// </remarks>
[ExcludeFromCodeCoverage(Justification =
	"Async Main - state machine generates unreachable branches; service wiring tested via integration tests")]
public class Program
{
	public static async Task Main(string[] args)
	{
		Env.Load(".env");

		// Fully qualified — `Host` would otherwise resolve to the enclosing PaperlessServices.Host namespace.
		HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);
		builder.Configuration.AddEnvironmentVariables();

		// Register shared infrastructure
		builder.Services.AddPaperlessRabbitMq(builder.Configuration);

		// Register worker services
		builder.Services.AddOcrServices();
		builder.Services.AddGenAiServices(builder.Configuration);

		IHost host = builder.Build();
		await host.RunAsync();
	}
}
