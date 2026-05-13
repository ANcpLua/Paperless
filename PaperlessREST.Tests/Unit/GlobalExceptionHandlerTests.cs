using System.Diagnostics;
using ExceptionInfo = PaperlessREST.API.ExceptionInfo;

namespace PaperlessREST.Tests.Unit;

/// <summary>
///     Unit tests for GlobalExceptionHandler.
///     Follows ASP.NET Core testing patterns from ExceptionHandlerMiddlewareTest.cs
/// </summary>
public sealed class GlobalExceptionHandlerTests : IDisposable
{
	private const int Status400BadRequest = StatusCodes.Status400BadRequest;
	private const int Status403Forbidden = StatusCodes.Status403Forbidden;
	private const int Status404NotFound = StatusCodes.Status404NotFound;
	private const int Status499ClientClosedRequest = HttpStatusCodes.ClientClosedRequest;
	private const int Status500InternalServerError = StatusCodes.Status500InternalServerError;
	private const int Status504GatewayTimeout = StatusCodes.Status504GatewayTimeout;

	private const string CodeValidationError = "validation_error";
	private const string CodeBadRequest = "bad_request";
	private const string CodeForbidden = "forbidden";
	private const string CodeNotFound = "not_found";
	private const string CodeCancelled = "cancelled";
	private const string CodeTimeout = "timeout";
	private const string CodeInternalError = "internal_error";

	private const string TestMessage = "test";
	private const string TestTraceId = "test-trace-id";
	private const string TestMethod = "GET";
	private const string TestPath = "/api/test";

	private const string FieldName = "Field";
	private const string FieldError = "Error";
	private const string EmailFieldName = "Email";
	private const string NameFieldName = "Name";
	private const string EmailRequiredError = "Required";
	private const string EmailFormatError = "Invalid format";
	private const string NameLengthError = "Too short";
	private const string DocNotFoundMessage = "Doc not found";
	private const string NotFoundMessage = "Not found";
	private const string BadRequestMessage = "bad request";
	private const string TestActivityName = "Test";
	private readonly FakeLogCollector _logCollector = new();
	private readonly FakeLogger<GlobalExceptionHandler> _logger;

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<IProblemDetailsService> _problemDetailsService;

	public GlobalExceptionHandlerTests()
	{
		_logger = new FakeLogger<GlobalExceptionHandler>(_logCollector);
		_problemDetailsService = _mocks.Create<IProblemDetailsService>();
	}

	public void Dispose()
	{
		TestContext.Current.SendDiagnosticMessage("Full logs:\n{0}", _logCollector.GetFullLoggerText());
		_mocks.VerifyAll();
		_mocks.VerifyNoOtherCalls();
	}

	[Fact]
	public void FromException_ValidationException_Returns400WithValidationError()
	{
		// Arrange
		ValidationException exception = new(new ValidationResult(FieldError, [FieldName]), null, null);

		// Act
		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Assert
		info.StatusCode.Should().Be(Status400BadRequest);
		info.Level.Should().Be(LogLevel.Information);
		info.Code.Should().Be(CodeValidationError);
	}

	[Theory]
	[MemberData(nameof(BadRequestExceptions))]
	public void FromException_BadRequestExceptions_Returns400(Type exceptionType)
	{
		// Arrange
		Exception exception = (Exception)Activator.CreateInstance(exceptionType, TestMessage)!;

		// Act
		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Assert
		info.StatusCode.Should().Be(Status400BadRequest);
		info.Level.Should().Be(LogLevel.Warning);
		info.Code.Should().Be(CodeBadRequest);
	}

	public static IEnumerable<ITheoryDataRow> BadRequestExceptions()
	{
		yield return new TheoryDataRow<Type>(typeof(ArgumentException))
			.WithTestDisplayName("ArgumentException → 400");
		yield return new TheoryDataRow<Type>(typeof(ArgumentNullException))
			.WithTestDisplayName("ArgumentNullException → 400");
		yield return new TheoryDataRow<Type>(typeof(InvalidOperationException))
			.WithTestDisplayName("InvalidOperationException → 400");
		yield return new TheoryDataRow<Type>(typeof(JsonException))
			.WithTestDisplayName("JsonException → 400");
	}

	[Fact]
	public void FromException_BadHttpRequestException_Returns400()
	{
		// Arrange
		BadHttpRequestException exception = new(BadRequestMessage);

		// Act
		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Assert
		info.StatusCode.Should().Be(Status400BadRequest);
		info.Code.Should().Be(CodeBadRequest);
	}

	[Fact]
	public void FromException_UnauthorizedAccessException_Returns403()
	{
		// Arrange
		UnauthorizedAccessException exception = new();

		// Act
		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Assert
		info.StatusCode.Should().Be(Status403Forbidden);
		info.Level.Should().Be(LogLevel.Warning);
		info.Code.Should().Be(CodeForbidden);
	}

	[Theory]
	[MemberData(nameof(NotFoundExceptions))]
	public void FromException_NotFoundExceptions_Returns404(Type exceptionType)
	{
		// Arrange
		Exception exception = (Exception)Activator.CreateInstance(exceptionType, NotFoundMessage)!;

		// Act
		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Assert
		info.StatusCode.Should().Be(Status404NotFound);
		info.Level.Should().Be(LogLevel.Information);
		info.Code.Should().Be(CodeNotFound);
	}

	public static IEnumerable<ITheoryDataRow> NotFoundExceptions()
	{
		yield return new TheoryDataRow<Type>(typeof(KeyNotFoundException))
			.WithTestDisplayName("KeyNotFoundException → 404");
		yield return new TheoryDataRow<Type>(typeof(FileNotFoundException))
			.WithTestDisplayName("FileNotFoundException → 404");
	}

	[Fact]
	public void FromException_OperationCanceledException_Returns499()
	{
		// Arrange
		OperationCanceledException exception = new();

		// Act
		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Assert
		info.StatusCode.Should().Be(Status499ClientClosedRequest);
		info.Level.Should().Be(LogLevel.Debug);
		info.Code.Should().Be(CodeCancelled);
	}

	[Fact]
	public void FromException_TaskCanceledException_Returns499()
	{
		// Arrange - TaskCanceledException derives from OperationCanceledException
		TaskCanceledException exception = new();

		// Act
		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Assert
		info.StatusCode.Should().Be(Status499ClientClosedRequest);
		info.Level.Should().Be(LogLevel.Debug);
		info.Code.Should().Be(CodeCancelled);
	}

	[Fact]
	public void FromException_TimeoutException_Returns504()
	{
		// Arrange
		TimeoutException exception = new();

		// Act
		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Assert
		info.StatusCode.Should().Be(Status504GatewayTimeout);
		info.Level.Should().Be(LogLevel.Error);
		info.Code.Should().Be(CodeTimeout);
	}

	[Fact]
	public void FromException_UnknownException_Returns500()
	{
		// Arrange
		NotSupportedException exception = new();

		// Act
		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Assert
		info.StatusCode.Should().Be(Status500InternalServerError);
		info.Level.Should().Be(LogLevel.Error);
		info.Code.Should().Be(CodeInternalError);
	}

	[Fact]
	public async Task TryHandleAsync_OperationCanceledWithCancellationRequested_Returns499AndSkipsProblemDetails()
	{
		// Arrange
		using CancellationTokenSource cts = new();
		await cts.CancelAsync();

		DefaultHttpContext httpContext = CreateHttpContext();
		Mock<IProblemDetailsService> strictMock = _mocks.Create<IProblemDetailsService>();
		GlobalExceptionHandler sut = new(strictMock.Object, _logger);

		// Act
		bool result = await sut.TryHandleAsync(httpContext, new OperationCanceledException(), cts.Token);

		// Assert
		result.Should().BeTrue();
		httpContext.Response.StatusCode.Should().Be(Status499ClientClosedRequest);
		// Strict mock verifies TryWriteAsync was never called
	}

	[Fact]
	public async Task TryHandleAsync_OperationCanceledWithoutCancellationRequested_CallsProblemDetails()
	{
		// Arrange
		DefaultHttpContext httpContext = CreateHttpContext();
		SetupProblemDetailsService();
		GlobalExceptionHandler sut = CreateSut();

		// Act
		bool result = await sut.TryHandleAsync(httpContext, new OperationCanceledException(),
			TestContext.Current.CancellationToken);

		// Assert
		result.Should().BeTrue();
		_problemDetailsService.Verify(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()), Times.Once);
	}

	[Fact]
	public async Task TryHandleAsync_ValidationException_CreatesHttpValidationProblemDetails()
	{
		// Arrange
		ValidationException exception = new(new ValidationResult(EmailRequiredError, [EmailFieldName]), null, null);
		DefaultHttpContext httpContext = CreateHttpContext();

		ProblemDetailsContext? captured = null;
		SetupProblemDetailsServiceWithCapture(ctx => captured = ctx);
		GlobalExceptionHandler sut = CreateSut();

		// Act
		await sut.TryHandleAsync(httpContext, exception, TestContext.Current.CancellationToken);

		// Assert
		captured.Should().NotBeNull();
		captured!.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>();

		HttpValidationProblemDetails validation = (HttpValidationProblemDetails)captured.ProblemDetails;
		validation.Errors.Should().ContainKey(EmailFieldName).WhoseValue.Should().ContainSingle();
		validation.Type.Should().Contain(CodeValidationError);
	}

	[Fact]
	public async Task TryHandleAsync_NonValidationException_CreatesStandardProblemDetails()
	{
		// Arrange
		DefaultHttpContext httpContext = CreateHttpContext();
		ProblemDetailsContext? captured = null;
		SetupProblemDetailsServiceWithCapture(ctx => captured = ctx);
		GlobalExceptionHandler sut = CreateSut();

		// Act
		await sut.TryHandleAsync(httpContext, new KeyNotFoundException(NotFoundMessage),
			TestContext.Current.CancellationToken);

		// Assert
		captured.Should().NotBeNull();
		captured!.ProblemDetails.Should().NotBeOfType<HttpValidationProblemDetails>();
		captured.ProblemDetails.Status.Should().Be(Status404NotFound);
	}

	[Fact]
	public async Task TryHandleAsync_LogsWithCorrectLevelAndCode()
	{
		// Arrange
		DefaultHttpContext httpContext = CreateHttpContext();
		SetupProblemDetailsService();
		GlobalExceptionHandler sut = CreateSut();

		// Act
		await sut.TryHandleAsync(httpContext, new KeyNotFoundException(DocNotFoundMessage),
			TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(log =>
				log.Level == LogLevel.Information &&
				log.Message.Contains(CodeNotFound, StringComparison.Ordinal));
	}

	[Fact]
	public async Task TryHandleAsync_UsesActivityIdWhenAvailable()
	{
		// Arrange
		using var activity = new Activity(TestActivityName);
		activity.Start();
		DefaultHttpContext httpContext = CreateHttpContext();
		SetupProblemDetailsService();
		GlobalExceptionHandler sut = CreateSut();

		// Act
		await sut.TryHandleAsync(httpContext, new InvalidOperationException(), TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(log => log.Message.Contains(activity.Id!, StringComparison.Ordinal));
	}

	[Fact]
	public async Task TryHandleAsync_UsesTraceIdentifierWhenNoActivity()
	{
		// Arrange
		Activity.Current = null;
		DefaultHttpContext httpContext = CreateHttpContext();
		httpContext.TraceIdentifier = TestTraceId;
		SetupProblemDetailsService();
		GlobalExceptionHandler sut = CreateSut();

		// Act
		await sut.TryHandleAsync(httpContext, new InvalidOperationException(), TestContext.Current.CancellationToken);

		// Assert
		_logCollector.GetSnapshot()
			.Should().Contain(log => log.Message.Contains(TestTraceId, StringComparison.Ordinal));
	}

	private GlobalExceptionHandler CreateSut() =>
		new(_problemDetailsService.Object, _logger);

	private void SetupProblemDetailsService() =>
		_problemDetailsService
			.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.ReturnsAsync(true);

	private void SetupProblemDetailsServiceWithCapture(Action<ProblemDetailsContext> callback) =>
		_problemDetailsService
			.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.Callback(callback)
			.ReturnsAsync(true);

	private static DefaultHttpContext CreateHttpContext() =>
		new() { Request = { Method = TestMethod, Path = TestPath }, TraceIdentifier = Guid.NewGuid().ToString("N") };
}

/// <summary>
///     Unit tests for ProblemDetailsEnricher.
/// </summary>
public sealed class ProblemDetailsEnricherTests : IDisposable
{
	private const string ExtensionKeyTraceId = "trace_id";
	private const string ExtensionKeyTimestamp = "timestamp";
	private const string ExtensionKeyRoute = "route";
	private const string ExtensionKeyDebug = "debug";

	private const string DefaultTraceIdentifier = "default-trace";
	private const string TestTraceId = "trace-123";
	private const string TestActivityName = "Test";
	private const string TestExceptionMessage = "Test";
	private const string OuterExceptionMessage = "Outer";
	private const string InnerExceptionMessage = "Inner";
	private const string OriginalDetailMessage = "Original";
	private const string DetailedDevError = "Detailed dev error";
	private const string DocumentNotFoundMessage = "Document not found";
	private const string TestEndpointName = "TestEndpoint";
	private const string RoutePatternDocumentsId = "/api/documents/{id}";

	private const string Field1Name = "Field1";
	private const string Field2Name = "Field2";
	private const string Error1 = "Error1";
	private const string Error2 = "Error2";
	private const string Error3 = "Error3";

	private const int Status400 = 400;
	private const int Status404 = 404;
	private const int Status499ClientClosed = 499;
	private const int Status500 = 500;
	private const int Status503ServiceUnavailable = 503;
	private const int ExpectedValidationErrorCount = 3;
	private const int RouteOrder = 0;

	private const string ExpectedTimestamp = "2024-11-29T12:00:00.0000000+00:00";
	private const string GenericInternalErrorSubstring = "internal error";

	private static readonly DateTimeOffset s_fixedTime = new(2024, 11, 29, 12, 0, 0, TimeSpan.Zero);
	private readonly Mock<IHostEnvironment> _hostEnvironment;

	private readonly MockRepository _mocks = new(MockBehavior.Strict) { DefaultValue = DefaultValue.Empty };
	private readonly Mock<TimeProvider> _timeProvider;

	public ProblemDetailsEnricherTests()
	{
		_hostEnvironment = _mocks.Create<IHostEnvironment>();
		_timeProvider = _mocks.Create<TimeProvider>();
		_timeProvider.Setup(t => t.GetUtcNow()).Returns(s_fixedTime);
		// Default environment - tests override when needed
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
	}

	public void Dispose()
	{
		_mocks.VerifyAll();
		_mocks.VerifyNoOtherCalls();
	}

	[Fact]
	public void Enrich_WithActivity_UsesActivityId()
	{
		// Arrange
		using var activity = new Activity(TestActivityName);
		activity.Start();
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext();

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Extensions[ExtensionKeyTraceId].Should().Be(activity.Id);
	}

	[Fact]
	public void Enrich_WithoutActivity_UsesTraceIdentifier()
	{
		// Arrange
		Activity.Current = null;
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(traceIdentifier: TestTraceId);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Extensions[ExtensionKeyTraceId].Should().Be(TestTraceId);
	}

	[Fact]
	public void Enrich_AddsTimestamp()
	{
		// Arrange
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext();

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Extensions[ExtensionKeyTimestamp].Should().Be(ExpectedTimestamp);
	}

	[Fact]
	public void Enrich_WithRouteEndpoint_AddsRoutePattern()
	{
		// Arrange
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) =
			CreateSutAndContext(routePattern: RoutePatternDocumentsId);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Extensions.Should().ContainKey(ExtensionKeyRoute)
			.WhoseValue.Should().Be(RoutePatternDocumentsId);
	}

	[Fact]
	public void Enrich_WithoutEndpoint_DoesNotAddRoute()
	{
		// Arrange
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext();

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Extensions.Should().NotContainKey(ExtensionKeyRoute);
	}

	[Fact]
	public void Enrich_HttpValidationProblemDetails_SetsErrorCount()
	{
		// Arrange
		HttpValidationProblemDetails validationPd = new(new Dictionary<string, string[]>
		{
			[Field1Name] = [Error1, Error2], [Field2Name] = [Error3]
		});
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(validationPd);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Detail.Should().Be($"Validation failed with {ExpectedValidationErrorCount} error(s).");
	}

	[Fact]
	public void Enrich_InDevelopment_ShowsExceptionMessage()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
		InvalidOperationException exception = new(DetailedDevError);
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(exception: exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Detail.Should().Be(DetailedDevError);
	}

	[Fact]
	public void Enrich_InProduction_With500_HidesDetails()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
		ProblemDetails pd = new() { Status = Status500 };
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(pd);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Detail.Should().Contain(GenericInternalErrorSubstring);
	}

	[Fact]
	public void Enrich_InProduction_Non500_ShowsExceptionMessage()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
		ProblemDetails pd = new() { Status = Status404 };
		KeyNotFoundException exception = new(DocumentNotFoundMessage);
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(pd, exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Detail.Should().Be(DocumentNotFoundMessage);
	}

	[Fact]
	public void Enrich_NoException_KeepsOriginalDetail()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
		ProblemDetails pd = new() { Status = Status400, Detail = OriginalDetailMessage };
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(pd);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Detail.Should().Be(OriginalDetailMessage);
	}

	[Fact]
	public void Enrich_HttpValidationProblemDetails_EmptyErrors_FallsToExceptionMessage()
	{
		// Arrange - Covers Errors.Count is 0 branch
		HttpValidationProblemDetails validationPd = new(); // Empty errors
		InvalidOperationException exception = new(DetailedDevError);
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(
			validationPd, exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert - Falls through to exception message branch
		context.ProblemDetails.Detail.Should().Be(DetailedDevError);
	}

	[Fact]
	public void Enrich_InDevelopment_With500_ShowsExceptionMessage()
	{
		// Arrange - Covers Status >= 500 in Development (guard fails, uses exception message)
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
		ProblemDetails pd = new() { Status = Status500 };
		InvalidOperationException exception = new(DetailedDevError);
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(
			pd, exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert - In dev, always shows exception message regardless of status
		context.ProblemDetails.Detail.Should().Be(DetailedDevError);
	}

	[Fact]
	public void Enrich_InProduction_Non500_NoException_KeepsOriginalDetail()
	{
		// Arrange - Covers the default "_ => pd.Detail" branch
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
		ProblemDetails pd = new() { Status = Status404, Detail = OriginalDetailMessage };
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(pd);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Detail.Should().Be(OriginalDetailMessage);
	}

	[Fact]
	public void Enrich_InProduction_500_NoException_ShowsGenericError()
	{
		// Arrange - Status 500 without exception in production
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
		ProblemDetails pd = new() { Status = Status500 };
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(pd);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Detail.Should().Contain(GenericInternalErrorSubstring);
	}

	[Fact]
	public void Enrich_InProduction_Status503_NoException_ShowsGenericError()
	{
		// Arrange - Status > 500 without exception in production (covers >= 500 branch)
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
		ProblemDetails pd = new() { Status = Status503ServiceUnavailable };
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(pd);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Detail.Should().Contain(GenericInternalErrorSubstring);
	}

	[Fact]
	public void Enrich_InProduction_Status499_NoException_KeepsOriginalDetail()
	{
		// Arrange - Status < 500 (just below threshold) in production
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
		ProblemDetails pd = new() { Status = Status499ClientClosed, Detail = OriginalDetailMessage };
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(pd);

		// Act
		InvokeEnrich(sut, context);

		// Assert - Should not match >= 500 pattern, falls to default
		context.ProblemDetails.Detail.Should().Be(OriginalDetailMessage);
	}

	[Fact]
	public void Enrich_NullProblemDetailsStatus_WithException_ShowsExceptionMessage()
	{
		// Arrange - ProblemDetails with null status (edge case)
		ProblemDetails pd = new() { Status = null };
		InvalidOperationException exception = new(DetailedDevError);
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(
			pd, exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Detail.Should().Be(DetailedDevError);
	}

	[Fact]
	public void Enrich_InDevelopment_WithStackTrace_AddsDebugInfo()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
		Exception exception = CaptureExceptionWithStackTrace();
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(exception: exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Extensions.Should().ContainKey(ExtensionKeyDebug);
	}

	[Fact]
	public void Enrich_InDevelopment_WithInnerException_AddsDebugInfo()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
		InvalidOperationException exception = new(OuterExceptionMessage, new ArgumentException(InnerExceptionMessage));
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(exception: exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Extensions.Should().ContainKey(ExtensionKeyDebug);
	}

	[Fact]
	public void Enrich_InProduction_NoDebugInfo()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Production);
		Exception exception = CaptureExceptionWithStackTrace();
		ProblemDetails pd = new() { Status = Status404 };
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(pd, exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Extensions.Should().NotContainKey(ExtensionKeyDebug);
	}

	[Fact]
	public void Enrich_InDevelopment_NoStackTraceOrInner_NoDebugInfo()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
		// Exception created without throw has no stack trace
		InvalidOperationException exception = new(TestExceptionMessage);
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(exception: exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		context.ProblemDetails.Extensions.Should().NotContainKey(ExtensionKeyDebug);
	}

	[Fact]
	public void Enrich_InDevelopment_WithStackTrace_DebugInfoContainsExceptionType()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
		Exception exception = CaptureExceptionWithStackTrace();
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(exception: exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		object debugInfo = context.ProblemDetails.Extensions[ExtensionKeyDebug]!;
		dynamic debug = debugInfo;
		string exceptionType = debug.exception_type;
		exceptionType.Should().Be(typeof(InvalidOperationException).FullName);
	}

	[Fact]
	public void Enrich_InDevelopment_WithStackTrace_DebugInfoContainsStackTrace()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
		Exception exception = CaptureExceptionWithStackTrace();
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(exception: exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		object debugInfo = context.ProblemDetails.Extensions[ExtensionKeyDebug]!;
		dynamic debug = debugInfo;
		string? stackTrace = debug.stack_trace;
		stackTrace.Should().NotBeNullOrEmpty();
		stackTrace.Should().Contain(nameof(CaptureExceptionWithStackTrace));
	}

	[Fact]
	public void Enrich_InDevelopment_WithInnerException_DebugInfoContainsInnerMessage()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
		InvalidOperationException exception = new(OuterExceptionMessage, new ArgumentException(InnerExceptionMessage));
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(exception: exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		object debugInfo = context.ProblemDetails.Extensions[ExtensionKeyDebug]!;
		dynamic debug = debugInfo;
		string? innerMessage = debug.inner_exception;
		innerMessage.Should().Be(InnerExceptionMessage);
	}

	[Fact]
	public void Enrich_InDevelopment_WithoutInnerException_DebugInfoHasNullInner()
	{
		// Arrange
		_hostEnvironment.Setup(e => e.EnvironmentName).Returns(Environments.Development);
		Exception exception = CaptureExceptionWithStackTrace();
		(ProblemDetailsEnricher sut, ProblemDetailsContext context) = CreateSutAndContext(exception: exception);

		// Act
		InvokeEnrich(sut, context);

		// Assert
		object debugInfo = context.ProblemDetails.Extensions[ExtensionKeyDebug]!;
		dynamic debug = debugInfo;
		string? innerMessage = debug.inner_exception;
		innerMessage.Should().BeNull();
	}

	private ProblemDetailsEnricher CreateSut() =>
		new(_hostEnvironment.Object, _timeProvider.Object);

	private (ProblemDetailsEnricher Sut, ProblemDetailsContext Context) CreateSutAndContext(
		ProblemDetails? problemDetails = null,
		Exception? exception = null,
		string? routePattern = null,
		string traceIdentifier = DefaultTraceIdentifier)
	{
		DefaultHttpContext httpContext = new() { TraceIdentifier = traceIdentifier };

		if (routePattern is not null)
		{
			RouteEndpoint endpoint = new(
				_ => Task.CompletedTask,
				RoutePatternFactory.Parse(routePattern),
				RouteOrder,
				new EndpointMetadataCollection(),
				TestEndpointName);
			httpContext.SetEndpoint(endpoint);
		}

		ProblemDetailsContext context = new()
		{
			HttpContext = httpContext,
			ProblemDetails = problemDetails ?? new ProblemDetails(),
			Exception = exception
		};

		ProblemDetailsEnricher sut = CreateSut();
		return (sut, context);
	}

	private static void InvokeEnrich(ProblemDetailsEnricher sut, ProblemDetailsContext context)
	{
		ProblemDetailsOptions options = new();
		sut.Configure(options);
		options.CustomizeProblemDetails!(context);
	}

	private static Exception CaptureExceptionWithStackTrace()
	{
		try
		{
			throw new InvalidOperationException(TestExceptionMessage);
		}
		catch (Exception ex)
		{
			return ex;
		}
	}
}
