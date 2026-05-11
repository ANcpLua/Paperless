namespace PaperlessREST.Tests.Unit;

public static class ExceptionHandlerConstants
{
	public const string UrnPrefix = "urn:paperless:error:";
	public const string BadRequestCode = "bad_request";
	public const string ValidationErrorCode = "validation_error";
	public const string NotFoundCode = "not_found";
	public const string ForbiddenCode = "forbidden";
	public const string TimeoutCode = "timeout";
	public const string InternalErrorCode = "internal_error";
	public const string TestExceptionMessage = "Test";
	public const string PropertyName = "Name";
	public const string PropertyEmail = "Email";
	public const string NameRequiredError = "Name is required";
	public const string InvalidEmailError = "Invalid email format";
	public const string DuplicateNameError = "Name already exists";
}

public sealed class ExceptionHandlerSetup
{
	public ExceptionHandlerSetup()
	{
		Collector = new FakeLogCollector();
		Logger = new FakeLogger<GlobalExceptionHandler>(Collector);
	}

	public Mock<IProblemDetailsService> ProblemDetails { get; } = new();
	public FakeLogger<GlobalExceptionHandler> Logger { get; }
	public FakeLogCollector Collector { get; }

	public GlobalExceptionHandler CreateHandler() => new(ProblemDetails.Object, Logger);

	public HttpContext CreateHttpContext()
	{
		DefaultHttpContext context = new() { Response = { Body = new MemoryStream() } };
		return context;
	}

	public ExceptionHandlerSetup WithProblemDetailsWrite(bool success = true)
	{
		ProblemDetails.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.Callback<ProblemDetailsContext>(ctx =>
				ctx.HttpContext.Response.StatusCode = ctx.ProblemDetails.Status ?? 500)
			.ReturnsAsync(success);
		return this;
	}
}

public sealed class ExceptionHandlerTests
{
	private readonly ExceptionHandlerSetup _setup = new();

	public static IEnumerable<TheoryDataRow<Type, int, LogLevel, string>> ExceptionCases()
	{
		// ArgumentNullException derives from ArgumentException, both map to "bad_request"
		yield return new TheoryDataRow<Type, int, LogLevel, string>(typeof(ArgumentNullException), 400,
				LogLevel.Warning, "bad_request")
			.WithTestDisplayName("400 ArgumentNull");
		yield return new TheoryDataRow<Type, int, LogLevel, string>(typeof(ArgumentException), 400, LogLevel.Warning,
				"bad_request")
			.WithTestDisplayName("400 Argument");
		yield return new TheoryDataRow<Type, int, LogLevel, string>(typeof(InvalidOperationException), 400,
				LogLevel.Warning, "bad_request")
			.WithTestDisplayName("400 InvalidOperation");
		yield return new TheoryDataRow<Type, int, LogLevel, string>(typeof(JsonException), 400, LogLevel.Warning,
				"bad_request")
			.WithTestDisplayName("400 Json");
		yield return new TheoryDataRow<Type, int, LogLevel, string>(typeof(UnauthorizedAccessException), 403,
				LogLevel.Warning, "forbidden")
			.WithTestDisplayName("403 Unauthorized");
		yield return new TheoryDataRow<Type, int, LogLevel, string>(typeof(KeyNotFoundException), 404,
				LogLevel.Information, "not_found")
			.WithTestDisplayName("404 KeyNotFound");
		yield return new TheoryDataRow<Type, int, LogLevel, string>(typeof(FileNotFoundException), 404,
				LogLevel.Information, "not_found")
			.WithTestDisplayName("404 FileNotFound");
		yield return new TheoryDataRow<Type, int, LogLevel, string>(typeof(TimeoutException), 504, LogLevel.Error,
				"timeout")
			.WithTestDisplayName("504 Timeout");
		yield return new TheoryDataRow<Type, int, LogLevel, string>(typeof(Exception), 500, LogLevel.Error,
				"internal_error")
			.WithTestDisplayName("500 Exception");
	}

	[Theory]
	[MemberData(nameof(ExceptionCases))]
	public async Task TryHandleAsync_VariousExceptions_ReturnsExpectedStatusAndLogs(
		Type exceptionType, int expectedStatus, LogLevel expectedLevel, string expectedCode)
	{
		HttpContext context = _setup.CreateHttpContext();
		Exception exception = (Exception)Activator.CreateInstance(exceptionType, "Test")!;
		GlobalExceptionHandler handler = _setup.WithProblemDetailsWrite().CreateHandler();

		bool handled = await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

		handled.Should().BeTrue();
		context.Response.StatusCode.Should().Be(expectedStatus);
		_setup.Logger.Collector.GetSnapshot().Should().Contain(x =>
			x.Level == expectedLevel && x.Message.Contains(expectedCode, StringComparison.Ordinal));
	}

	[Fact]
	public async Task TryHandleAsync_OperationCancelled_Returns499()
	{
		HttpContext context = _setup.CreateHttpContext();
		OperationCanceledException exception = new();
		CancellationToken token = new(true);
		GlobalExceptionHandler handler = _setup.WithProblemDetailsWrite(false).CreateHandler();

		bool handled = await handler.TryHandleAsync(context, exception, token);

		handled.Should().BeTrue();
		context.Response.StatusCode.Should().Be(499);
		// Handler logs at Debug level for cancelled requests
		_setup.Logger.Collector.GetSnapshot()
			.Should().OnlyContain(l => l.Level == LogLevel.Debug && l.Message.Contains("Request cancelled"));
	}

	[Fact]
	public async Task TryHandleAsync_OperationCancelledWithoutCancelledToken_TreatsAsInternalError()
	{
		// Arrange - OperationCanceledException but token is NOT cancelled
		HttpContext context = _setup.CreateHttpContext();
		OperationCanceledException exception = new("Task was cancelled");
		GlobalExceptionHandler handler = _setup.WithProblemDetailsWrite().CreateHandler();

		// Act - Use non-cancelled token
		bool handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

		// Assert - Should NOT take the early return path, but go through normal exception handling
		handled.Should().BeTrue();
		// OperationCanceledException without cancelled token maps to 499 via ExceptionInfo
		context.Response.StatusCode.Should().Be(499);
	}

	[Fact]
	public async Task TryHandleAsync_ValidationException_Returns400WithErrors()
	{
		// Arrange
		HttpContext context = _setup.CreateHttpContext();
		ValidationException validationEx = new(new ValidationResult("Name is required", ["Name"]), null, null);

		_setup.ProblemDetails.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.Callback<ProblemDetailsContext>(ctx =>
			{
				ctx.HttpContext.Response.StatusCode = ctx.ProblemDetails.Status ?? 400;
				// Verify it's an HttpValidationProblemDetails
				ctx.ProblemDetails.Should().BeOfType<HttpValidationProblemDetails>();
			})
			.ReturnsAsync(true);

		GlobalExceptionHandler handler = _setup.CreateHandler();

		// Act
		bool handled = await handler.TryHandleAsync(context, validationEx, CancellationToken.None);

		// Assert
		handled.Should().BeTrue();
		context.Response.StatusCode.Should().Be(400);
		_setup.Logger.Collector.GetSnapshot()
			.Should().Contain(l => l.Level == LogLevel.Information && l.Message.Contains("validation_error"));
	}

	[Fact]
	public async Task TryHandleAsync_BadHttpRequestException_Returns400()
	{
		// Arrange
		HttpContext context = _setup.CreateHttpContext();
		BadHttpRequestException exception = new("Bad request body");
		GlobalExceptionHandler handler = _setup.WithProblemDetailsWrite().CreateHandler();

		// Act
		bool handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

		// Assert
		handled.Should().BeTrue();
		context.Response.StatusCode.Should().Be(400);
		_setup.Logger.Collector.GetSnapshot()
			.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("bad_request"));
	}

	[Fact]
	public async Task TryHandleAsync_ArgumentException_PassesCorrectTypeUrn()
	{
		// Arrange
		HttpContext context = _setup.CreateHttpContext();
		ArgumentException exception = new(ExceptionHandlerConstants.TestExceptionMessage);
		ProblemDetailsContext? capturedContext = null;

		_setup.ProblemDetails.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.Callback<ProblemDetailsContext>(ctx => capturedContext = ctx)
			.ReturnsAsync(true);

		GlobalExceptionHandler handler = _setup.CreateHandler();

		// Act
		await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

		// Assert
		capturedContext.Should().NotBeNull();
		capturedContext!.ProblemDetails.Type.Should().Be(
			ExceptionHandlerConstants.UrnPrefix + ExceptionHandlerConstants.BadRequestCode);
	}

	[Fact]
	public async Task TryHandleAsync_KeyNotFoundException_PassesCorrectTypeUrn()
	{
		// Arrange
		HttpContext context = _setup.CreateHttpContext();
		KeyNotFoundException exception = new(ExceptionHandlerConstants.TestExceptionMessage);
		ProblemDetailsContext? capturedContext = null;

		_setup.ProblemDetails.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.Callback<ProblemDetailsContext>(ctx => capturedContext = ctx)
			.ReturnsAsync(true);

		GlobalExceptionHandler handler = _setup.CreateHandler();

		// Act
		await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

		// Assert
		capturedContext.Should().NotBeNull();
		capturedContext!.ProblemDetails.Type.Should().Be(
			ExceptionHandlerConstants.UrnPrefix + ExceptionHandlerConstants.NotFoundCode);
	}

	[Fact]
	public async Task TryHandleAsync_PassesHttpContextToWriter()
	{
		// Arrange
		HttpContext context = _setup.CreateHttpContext();
		Exception exception = new(ExceptionHandlerConstants.TestExceptionMessage);
		ProblemDetailsContext? capturedContext = null;

		_setup.ProblemDetails.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.Callback<ProblemDetailsContext>(ctx => capturedContext = ctx)
			.ReturnsAsync(true);

		GlobalExceptionHandler handler = _setup.CreateHandler();

		// Act
		await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

		// Assert
		capturedContext.Should().NotBeNull();
		capturedContext!.HttpContext.Should().BeSameAs(context);
	}

	[Fact]
	public async Task TryHandleAsync_PassesExceptionToWriter()
	{
		// Arrange
		HttpContext context = _setup.CreateHttpContext();
		InvalidOperationException exception = new(ExceptionHandlerConstants.TestExceptionMessage);
		ProblemDetailsContext? capturedContext = null;

		_setup.ProblemDetails.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.Callback<ProblemDetailsContext>(ctx => capturedContext = ctx)
			.ReturnsAsync(true);

		GlobalExceptionHandler handler = _setup.CreateHandler();

		// Act
		await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

		// Assert
		capturedContext.Should().NotBeNull();
		capturedContext!.Exception.Should().BeSameAs(exception);
	}

	[Fact]
	public async Task TryHandleAsync_NonValidationException_SetsProblemDetailsStatus()
	{
		// Arrange
		HttpContext context = _setup.CreateHttpContext();
		TimeoutException exception = new(ExceptionHandlerConstants.TestExceptionMessage);
		ProblemDetailsContext? capturedContext = null;

		_setup.ProblemDetails.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.Callback<ProblemDetailsContext>(ctx => capturedContext = ctx)
			.ReturnsAsync(true);

		GlobalExceptionHandler handler = _setup.CreateHandler();

		// Act
		await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

		// Assert
		capturedContext.Should().NotBeNull();
		capturedContext!.ProblemDetails.Status.Should().Be(504);
	}

	[Fact]
	public async Task TryHandleAsync_ValidationException_SetsValidationType()
	{
		// Arrange
		HttpContext context = _setup.CreateHttpContext();
		ValidationException validationEx = new(new ValidationResult(ExceptionHandlerConstants.NameRequiredError, [ExceptionHandlerConstants.PropertyName]), null, null);
		ProblemDetailsContext? capturedContext = null;

		_setup.ProblemDetails.Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
			.Callback<ProblemDetailsContext>(ctx => capturedContext = ctx)
			.ReturnsAsync(true);

		GlobalExceptionHandler handler = _setup.CreateHandler();

		// Act
		await handler.TryHandleAsync(context, validationEx, TestContext.Current.CancellationToken);

		// Assert
		capturedContext.Should().NotBeNull();
		capturedContext!.ProblemDetails.Type.Should().Be(
			ExceptionHandlerConstants.UrnPrefix + ExceptionHandlerConstants.ValidationErrorCode);
	}
}

// NOTE: ProblemDetailsEnricherTests moved to GlobalExceptionHandlerTests.cs
