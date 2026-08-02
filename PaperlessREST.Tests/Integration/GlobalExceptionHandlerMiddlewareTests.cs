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
	public async Task Request_ValidationExceptionWithMembers_ReturnsEveryDistinctNonblankMember()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={ExceptionTypeValidationMembers}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypeJson);

		HttpValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Status.Should().Be(Status400BadRequest);
		problem.Type.Should().Be(ValidationErrorType);
		problem.Errors.Should().BeEquivalentTo(new Dictionary<string, string[]>
		{
			[FieldName] = [FieldError],
			[SecondFieldName] = [FieldError]
		});
	}

	[Fact]
	public async Task Request_ValidationExceptionWithoutMembers_ReturnsModelLevelError()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={ExceptionTypeValidationModel}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		HttpValidationProblemDetails? problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(
			TestContext.Current.CancellationToken);

		problem.Should().NotBeNull();
		problem!.Errors.Should().ContainSingle()
			.Which.Should().BeEquivalentTo(new KeyValuePair<string, string[]>(string.Empty, [FieldError]));
	}

	#endregion

	#region Tests - Internal Server Error

	[Theory]
	[MemberData(nameof(UnownedExceptionTypes))]
	public async Task Request_UnownedException_ReturnsSanitized500ProblemDetails(string exceptionType)
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={exceptionType}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypeJson);
		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		body.Should().NotContain(SensitiveInternalErrorMessage);

		ProblemDetails? problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonSerializerOptions.Web);

		problem.Should().NotBeNull();
		problem!.Status.Should().Be(Status500InternalServerError);
		problem.Type.Should().Be(InternalErrorType);
		problem.Detail.Should().Be(GenericInternalErrorDetail);
		problem.Extensions.Should().NotContainKey("debug");
	}

	public static IEnumerable<ITheoryDataRow> UnownedExceptionTypes()
	{
		yield return new TheoryDataRow<string>(ExceptionTypeArgument);
		yield return new TheoryDataRow<string>(ExceptionTypeArgumentNull);
		yield return new TheoryDataRow<string>(ExceptionTypeInvalidOperation);
		yield return new TheoryDataRow<string>(ExceptionTypeKeyNotFound);
		yield return new TheoryDataRow<string>(ExceptionTypeFileNotFound);
		yield return new TheoryDataRow<string>(ExceptionTypeUnauthorized);
		yield return new TheoryDataRow<string>(ExceptionTypeTimeout);
		yield return new TheoryDataRow<string>(ExceptionTypeOperationCanceled);
		yield return new TheoryDataRow<string>(ExceptionTypeNotSupported);
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
	private const string InternalErrorType = "urn:paperless:error:internal_error";

	private const string ExceptionTypeValidationMembers = "validation-members";
	private const string ExceptionTypeValidationModel = "validation-model";
	private const string ExceptionTypeBadHttpRequest = "bad-http-request";
	private const string ExceptionTypeArgument = "argument";
	private const string ExceptionTypeArgumentNull = "argumentnull";
	private const string ExceptionTypeInvalidOperation = "invalidoperation";
	private const string ExceptionTypeKeyNotFound = "keynotfound";
	private const string ExceptionTypeFileNotFound = "filenotfound";
	private const string ExceptionTypeUnauthorized = "unauthorized";
	private const string ExceptionTypeTimeout = "timeout";
	private const string ExceptionTypeOperationCanceled = "operation-canceled";
	private const string ExceptionTypeNotSupported = "notsupported";

	private const string FieldName = "Email";
	private const string SecondFieldName = "Name";
	private const string FieldError = "Email is required";
	private const string BadRequestMessage = "Malformed request containing sensitive input";
	private const string SafeBadRequestDetail = "The request was invalid.";
	private const string SensitiveInternalErrorMessage = "Database password was exposed";
	private const string GenericInternalErrorDetail =
		"An internal error occurred. Please contact support if the problem persists.";

	private const int Status400BadRequest = StatusCodes.Status400BadRequest;
	private const int Status500InternalServerError = StatusCodes.Status500InternalServerError;

	#endregion

	#region Tests - BadHttpRequestException

	[Fact]
	public async Task Request_BadHttpRequestException_ReturnsSanitized400ProblemDetails()
	{
		// Arrange
		await using TestHostContext ctx = await CreateTestHostAsync();

		// Act
		HttpResponseMessage response = await ctx.Client.GetAsync(
			$"{ThrowEndpoint}?{ExceptionTypeParam}={ExceptionTypeBadHttpRequest}",
			TestContext.Current.CancellationToken);

		// Assert
		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		response.Content.Headers.ContentType?.MediaType.Should().Be(ContentTypeJson);

		string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
		body.Should().NotContain(BadRequestMessage);
		ProblemDetails? problem = JsonSerializer.Deserialize<ProblemDetails>(body, JsonSerializerOptions.Web);

		problem.Should().NotBeNull();
		problem!.Status.Should().Be(Status400BadRequest);
		problem.Type.Should().Be(BadRequestType);
		problem.Detail.Should().Be(SafeBadRequestDetail);
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
					.UseEnvironment(Environments.Production)
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
			ExceptionTypeValidationMembers => new ValidationException(
				new ValidationResult(FieldError, [FieldName, SecondFieldName, FieldName, string.Empty, "  "]),
				null,
				null),
			ExceptionTypeValidationModel => new ValidationException(new ValidationResult(FieldError), null, null),
			ExceptionTypeBadHttpRequest => new BadHttpRequestException(BadRequestMessage),
			ExceptionTypeArgument => new ArgumentException(SensitiveInternalErrorMessage),
			ExceptionTypeArgumentNull => new ArgumentNullException(nameof(exceptionType), SensitiveInternalErrorMessage),
			ExceptionTypeInvalidOperation => new InvalidOperationException(SensitiveInternalErrorMessage),
			ExceptionTypeKeyNotFound => new KeyNotFoundException(SensitiveInternalErrorMessage),
			ExceptionTypeFileNotFound => new FileNotFoundException(SensitiveInternalErrorMessage),
			ExceptionTypeUnauthorized => new UnauthorizedAccessException(SensitiveInternalErrorMessage),
			ExceptionTypeTimeout => new TimeoutException(SensitiveInternalErrorMessage),
			ExceptionTypeOperationCanceled => new OperationCanceledException(SensitiveInternalErrorMessage),
			ExceptionTypeNotSupported => new NotSupportedException(SensitiveInternalErrorMessage),
			_ => new InvalidOperationException($"Unknown exception type: {exceptionType}")
		};

	#endregion
}
