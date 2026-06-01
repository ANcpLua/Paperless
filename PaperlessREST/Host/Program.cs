using PaperlessREST.Host.Extensions;
using System.Reflection;

namespace PaperlessREST.Host;

/// <summary>
///     Application entry point for the PaperlessREST API.
/// </summary>
/// <remarks>
///     Excluded from coverage because async Main generates a state machine with unreachable
///     branches for sync/async completion. The actual middleware pipeline and service wiring
///     is tested via integration tests using WebApplicationFactory.
/// </remarks>
[ExcludeFromCodeCoverage(Justification =
	"Async Main - state machine generates unreachable branches; middleware pipeline tested via integration tests")]
public class Program
{
	public static async Task Main(string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		if (!builder.Environment.IsEnvironment("Test"))
		{
			Env.Load(".env", new LoadOptions(false));
		}

		builder.AddDependencies();
		TypeAdapterConfig.GlobalSettings.Scan(Assembly.GetExecutingAssembly());

		var app = builder.Build();

		app.ConfigureMiddleware();
		await app.InitializeApplicationAsync();
		app.MapEndpoints();

		await app.RunAsync();
	}
}
