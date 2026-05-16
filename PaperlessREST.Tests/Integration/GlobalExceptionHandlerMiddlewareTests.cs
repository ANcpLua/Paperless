namespace PaperlessREST.Tests.Integration;

/// <summary>
///     Integration tests for GlobalExceptionHandler using TestServer.
///     Tests the full HTTP pipeline exception handling without external containers.
/// </summary>
public sealed class GlobalExceptionHandlerMiddlewareTests
{
	#region Tests - Successful Requests

	[Fact]
	public async Task Request_WhenNoException_ReturnsOk()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			TestEndpoint,
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	#endregion

	#region Tests - ValidationException

	[Fact]
	public async Task Request_ValidationException_Returns400WithProblemDetails()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={ExceptionTypeValidation}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypeJson);

		HttpValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Status.Should().Be(Status400BadRequest);
		problem.Type.Should().Be(ValidationErrorType);
		problem.Errors.Should().ContainKey(FieldName);
		problem.Errors[FieldName].Should().Contain(FieldError);
	}

	#endregion

	#region Tests - Forbidden Exception

	[Fact]
	public async Task Request_UnauthorizedAccessException_Returns403WithProblemDetails()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={ExceptionTypeUnauthorized}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypeJson);

		ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Status.Should().Be(Status403Forbidden);
		problem.Type.Should().Be(ForbiddenType);
	}

	#endregion

	#region Tests - Timeout Exception

	[Fact]
	public async Task Request_TimeoutException_Returns504WithProblemDetails()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={ExceptionTypeTimeout}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypeJson);

		ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Status.Should().Be(Status504GatewayTimeout);
		problem.Type.Should().Be(TimeoutType);
	}

	#endregion

	#region Tests - Internal Server Error

	[Fact]
	public async Task Request_UnhandledException_Returns500WithProblemDetails()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={ExceptionTypeNotSupported}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypeJson);

		ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Status.Should().Be(Status500InternalServerError);
		problem.Type.Should().Be(InternalErrorType);
	}

	#endregion

	#region Test Host Context

	private sealed class TestHostContext(IHost host, HttpClient client, FakeLogCollector logCollector)
		: IAsyncDisposable
	{
		public HttpClient Client { get; } = client;
		private FakeLogCollector LogCollector { get; } = logCollector;

		public async ValueTask DisposeAsync()
		{
			TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", LogCollector.GetFullLoggerText());
			Client.Dispose();
			await host.StopAsync();
			host.Dispose();
		}
	}

	#endregion

	#region Constants

	private const string TestEndpoint = "/test";
	private const string ThrowEndpoint = "/throw";
	private const string ExceptionTypeParam = "type";
	private const string ContentTypeJson = "application/problem+json";

	private const string ValidationErrorType = "urn:paperless:error:validation_error";
	private const string BadRequestType = "urn:paperless:error:bad_request";
	private const string NotFoundType = "urn:paperless:error:not_found";
	private const string ForbiddenType = "urn:paperless:error:forbidden";
	private const string TimeoutType = "urn:paperless:error:timeout";
	private const string InternalErrorType = "urn:paperless:error:internal_error";

	private const string ExceptionTypeValidation = "validation";
	private const string ExceptionTypeArgument = "argument";
	private const string ExceptionTypeArgumentNull = "argumentnull";
	private const string ExceptionTypeInvalidOperation = "invalidoperation";
	private const string ExceptionTypeKeyNotFound = "keynotfound";
	private const string ExceptionTypeFileNotFound = "filenotfound";
	private const string ExceptionTypeUnauthorized = "unauthorized";
	private const string ExceptionTypeTimeout = "timeout";
	private const string ExceptionTypeNotSupported = "notsupported";

	private const string FieldName = "Email";
	private const string FieldError = "Email is required";
	private const string NotFoundMessage = "Document not found";
	private const string BadRequestMessage = "Invalid request";
	private const string ForbiddenMessage = "Access denied";
	private const string TimeoutMessage = "Operation timed out";
	private const string InternalErrorMessage = "Something went wrong";

	private const int Status400BadRequest = StatusCodes.Status400BadRequest;
	private const int Status403Forbidden = StatusCodes.Status403Forbidden;
	private const int Status404NotFound = StatusCodes.Status404NotFound;
	private const int Status500InternalServerError = StatusCodes.Status500InternalServerError;
	private const int Status504GatewayTimeout = StatusCodes.Status504GatewayTimeout;

	#endregion

	#region Tests - BadRequest Exceptions

	public static IEnumerable<ITheoryDataRow> BadRequestExceptions()
	{
		yield return new TheoryDataRow<string>(ExceptionTypeArgument)
			.WithTestDisplayName("Argument → 400");
		yield return new TheoryDataRow<string>(ExceptionTypeArgumentNull)
			.WithTestDisplayName("ArgumentNull → 400");
		yield return new TheoryDataRow<string>(ExceptionTypeInvalidOperation)
			.WithTestDisplayName("InvalidOperation → 400");
	}

	[Theory]
	[MemberData(nameof(BadRequestExceptions))]
	public async Task Request_BadRequestException_Returns400WithProblemDetails(string exceptionType)
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={exceptionType}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypeJson);

		ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Status.Should().Be(Status400BadRequest);
		problem.Type.Should().Be(BadRequestType);
	}

	#endregion

	#region Tests - NotFound Exceptions

	public static IEnumerable<ITheoryDataRow> NotFoundExceptions()
	{
		yield return new TheoryDataRow<string>(ExceptionTypeKeyNotFound)
			.WithTestDisplayName("KeyNotFound → 404");
		yield return new TheoryDataRow<string>(ExceptionTypeFileNotFound)
			.WithTestDisplayName("FileNotFound → 404");
	}

	[Theory]
	[MemberData(nameof(NotFoundExceptions))]
	public async Task Request_NotFoundException_Returns404WithProblemDetails(string exceptionType)
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={exceptionType}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypeJson);

		ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Status.Should().Be(Status404NotFound);
		problem.Type.Should().Be(NotFoundType);
	}

	#endregion

	#region Tests - ProblemDetails Extensions

	[Fact]
	public async Task Request_Exception_ProblemDetailsContainsTraceId()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={ExceptionTypeInvalidOperation}",
			TestContext.Current.CancellationToken);

		// Assert
		ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Extensions.Should().ContainKey("trace_id");
		problem.Extensions["trace_id"].Should().NotBeNull();
	}

	[Fact]
	public async Task Request_Exception_ProblemDetailsContainsTimestamp()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={ExceptionTypeInvalidOperation}",
			TestContext.Current.CancellationToken);

		// Assert
		ProblemDetails? problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Extensions.Should().ContainKey("timestamp");
		problem.Extensions["timestamp"].Should().NotBeNull();
	}

	#endregion

	#region Helper Methods

	private static async Task<TestHostContext> CreateTestHostAsync()
	{
		IHost host = await new HostBuilder()
			.ConfigureWebHost(webBuilder =>
			{
				webBuilder
					.UseTestServer()
					.ConfigureServices(services =>
					{
						services.AddFakeLogging();
						services.AddRouting();
						services.AddProblemDetails();
						services.AddExceptionHandler<GlobalExceptionHandler>();
						services.AddSingleton<IConfigureOptions<ProblemDetailsOptions>, ProblemDetailsEnricher>();
						services.AddSingleton(TimeProvider.System);
					})
					.Configure(app =>
					{
						app.UseExceptionHandler();
						app.UseRouting();
						app.UseEndpoints(endpoints =>
						{
							endpoints.MapGet(TestEndpoint, () => Results.Ok("success"));
							endpoints.MapGet(ThrowEndpoint, ctx =>
							{
								string exType = ctx.Request.Query[ExceptionTypeParam].ToString();
								throw CreateException(exType);
							});
						});
					});
			})
			.StartAsync(TestContext.Current.CancellationToken);

		HttpClient client = host.GetTestClient();
		FakeLogCollector logCollector = host.Services.GetFakeLogCollector();
		return new TestHostContext(host, client, logCollector);
	}

	private static Exception CreateException(string exceptionType) =>
		exceptionType switch
		{
			ExceptionTypeValidation => new ValidationException(new ValidationResult(FieldError, [FieldName]), null, null),
			ExceptionTypeArgument => new ArgumentException(BadRequestMessage),
			ExceptionTypeArgumentNull => new ArgumentNullException(nameof(exceptionType), BadRequestMessage),
			ExceptionTypeInvalidOperation => new InvalidOperationException(BadRequestMessage),
			ExceptionTypeKeyNotFound => new KeyNotFoundException(NotFoundMessage),
			ExceptionTypeFileNotFound => new FileNotFoundException(NotFoundMessage),
			ExceptionTypeUnauthorized => new UnauthorizedAccessException(ForbiddenMessage),
			ExceptionTypeTimeout => new TimeoutException(TimeoutMessage),
			ExceptionTypeNotSupported => new NotSupportedException(InternalErrorMessage),
			_ => new InvalidOperationException($"Unknown exception type: {exceptionType}")
		};

	#endregion
}
