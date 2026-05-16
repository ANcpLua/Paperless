using PaperlessREST.Host.Extensions;

namespace PaperlessREST.Tests.Unit;

public sealed class OpenApiMetadataExtensionsTests
{
	private static WebApplication BuildApp()
	{
		WebApplicationBuilder builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		return builder.Build();
	}

	private static IReadOnlyCollection<int> StatusCodesFor(WebApplication app, Action<RouteHandlerBuilder> configure)
	{
		string path = "/" + Guid.NewGuid();
		RouteHandlerBuilder route = app.MapGet(path, () => "ok");
		configure(route);

		// Materialize endpoints so Finally callbacks run and metadata is populated.
		HashSet<int> codes = [];
		IEndpointRouteBuilder erb = app;
		foreach (EndpointDataSource ds in erb.DataSources)
		{
			foreach (Endpoint endpoint in ds.Endpoints)
			{
				if (endpoint is RouteEndpoint rep && rep.RoutePattern.RawText == path)
				{
					foreach (IProducesResponseTypeMetadata m in rep.Metadata
						         .OfType<IProducesResponseTypeMetadata>())
					{
						codes.Add(m.StatusCode);
					}
				}
			}
		}

		return codes;
	}

	[Fact]
	public async Task ProducesNotFound_AddsStatus404Metadata()
	{
		await using WebApplication app = BuildApp();

		IReadOnlyCollection<int> codes = StatusCodesFor(app, r => r.ProducesNotFound());

		codes.Should().Contain(StatusCodes.Status404NotFound);
	}

	[Fact]
	public async Task ProducesConflict_AddsStatus409Metadata()
	{
		await using WebApplication app = BuildApp();

		IReadOnlyCollection<int> codes = StatusCodesFor(app, r => r.ProducesConflict());

		codes.Should().Contain(StatusCodes.Status409Conflict);
	}

	[Fact]
	public async Task ProducesServiceUnavailable_AddsStatus503Metadata()
	{
		await using WebApplication app = BuildApp();

		IReadOnlyCollection<int> codes = StatusCodesFor(app, r => r.ProducesServiceUnavailable());

		codes.Should().Contain(StatusCodes.Status503ServiceUnavailable);
	}

	[Fact]
	public async Task ProducesGetByIdErrors_AddsStatus404Metadata()
	{
		await using WebApplication app = BuildApp();

		IReadOnlyCollection<int> codes = StatusCodesFor(app, r => r.ProducesGetByIdErrors());

		codes.Should().Contain(StatusCodes.Status404NotFound);
	}

	[Fact]
	public async Task ProducesDeleteErrors_DefaultCanConflictFalse_AddsOnly404()
	{
		await using WebApplication app = BuildApp();

		IReadOnlyCollection<int> codes = StatusCodesFor(app, r => r.ProducesDeleteErrors());

		codes.Should().Contain(StatusCodes.Status404NotFound);
		codes.Should().NotContain(StatusCodes.Status409Conflict);
	}

	[Fact]
	public async Task ProducesDeleteErrors_CanConflictTrue_AddsBoth404And409()
	{
		await using WebApplication app = BuildApp();

		IReadOnlyCollection<int> codes = StatusCodesFor(app, r => r.ProducesDeleteErrors(canConflict: true));

		codes.Should().Contain(StatusCodes.Status404NotFound);
		codes.Should().Contain(StatusCodes.Status409Conflict);
	}

	[Fact]
	public async Task ProducesWriteErrors_Adds400_500_And503()
	{
		await using WebApplication app = BuildApp();

		IReadOnlyCollection<int> codes = StatusCodesFor(app, r => r.ProducesWriteErrors());

		codes.Should().Contain(StatusCodes.Status400BadRequest);
		codes.Should().Contain(StatusCodes.Status500InternalServerError);
		codes.Should().Contain(StatusCodes.Status503ServiceUnavailable);
	}

	[Fact]
	public async Task ProducesDocumentUploadErrors_DelegatesToWriteErrors_AddsSameCodes()
	{
		await using WebApplication app = BuildApp();

		IReadOnlyCollection<int> codes = StatusCodesFor(app, r => r.ProducesDocumentUploadErrors());

		codes.Should().Contain(StatusCodes.Status400BadRequest);
		codes.Should().Contain(StatusCodes.Status500InternalServerError);
		codes.Should().Contain(StatusCodes.Status503ServiceUnavailable);
	}

	[Fact]
	public async Task ProducesDeleteErrors_ReturnsBuilderForChaining()
	{
		await using WebApplication app = BuildApp();
		RouteHandlerBuilder route = app.MapGet("/chain-" + Guid.NewGuid(), () => "ok");

		RouteHandlerBuilder chained = route.ProducesDeleteErrors(canConflict: true);

		chained.Should().BeSameAs(route);
	}
}
