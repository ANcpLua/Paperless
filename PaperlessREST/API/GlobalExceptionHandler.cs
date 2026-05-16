namespace PaperlessREST.API;

internal static class HttpStatusCodes
{
	public const int ClientClosedRequest = 499;
}

internal readonly record struct ExceptionInfo(int StatusCode, LogLevel Level, string Code)
{
	public static ExceptionInfo FromException(Exception ex) =>
		ex switch
		{
			ValidationException => new ExceptionInfo(StatusCodes.Status400BadRequest, LogLevel.Information,
				"validation_error"),
			ArgumentException or InvalidOperationException or JsonException or BadHttpRequestException
				=> new ExceptionInfo(StatusCodes.Status400BadRequest, LogLevel.Warning, "bad_request"),
			UnauthorizedAccessException => new ExceptionInfo(StatusCodes.Status403Forbidden, LogLevel.Warning,
				"forbidden"),
			KeyNotFoundException or FileNotFoundException
				=> new ExceptionInfo(StatusCodes.Status404NotFound, LogLevel.Information, "not_found"),
			OperationCanceledException => new ExceptionInfo(HttpStatusCodes.ClientClosedRequest, LogLevel.Debug,
				"cancelled"),
			TimeoutException => new ExceptionInfo(StatusCodes.Status504GatewayTimeout, LogLevel.Error, "timeout"),
			_ => new ExceptionInfo(StatusCodes.Status500InternalServerError, LogLevel.Error, "internal_error")
		};
}

public sealed class GlobalExceptionHandler(
	IProblemDetailsService problemDetails,
	ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(
		HttpContext context,
		Exception exception,
		CancellationToken cancellationToken)
	{
		if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
		{
			context.Response.StatusCode = HttpStatusCodes.ClientClosedRequest;
			logger.LogDebug("Request cancelled: {Method} {Path}", context.Request.Method, context.Request.Path);
			return true;
		}

		ExceptionInfo info = ExceptionInfo.FromException(exception);

		// Set status code explicitly before writing response
		context.Response.StatusCode = info.StatusCode;

		logger.Log(info.Level, exception,
			"{Method} {Path} -> {Status} [{Code}] trace={TraceId}",
			context.Request.Method,
			context.Request.Path,
			info.StatusCode,
			info.Code,
			Activity.Current?.Id ?? context.TraceIdentifier);

		ProblemDetailsContext problemDetailsContext = new()
		{
			HttpContext = context,
			Exception = exception,
			ProblemDetails = exception is ValidationException validationEx
				? new HttpValidationProblemDetails(
					new Dictionary<string, string[]>
					{
						{
							validationEx.ValidationResult.MemberNames.FirstOrDefault()!,
							[validationEx.ValidationResult.ErrorMessage!]
						}
					})
				{
					Status = info.StatusCode, Type = $"urn:paperless:error:{info.Code}"
				}
				: new ProblemDetails { Status = info.StatusCode, Type = $"urn:paperless:error:{info.Code}" }
		};

		return await problemDetails.TryWriteAsync(problemDetailsContext);
	}
}

public sealed class ProblemDetailsEnricher(
	IHostEnvironment env,
	TimeProvider timeProvider) : IConfigureOptions<ProblemDetailsOptions>
{
	public void Configure(ProblemDetailsOptions options) => options.CustomizeProblemDetails = Enrich;

	private void Enrich(ProblemDetailsContext context)
	{
		ProblemDetails pd = context.ProblemDetails;
		HttpContext httpContext = context.HttpContext;

		pd.Extensions["trace_id"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
		pd.Extensions["timestamp"] = timeProvider.GetUtcNow().ToString("O");

		if (httpContext.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: var pattern })
		{
			pd.Extensions["route"] = pattern;
		}

		pd.Detail = (pd, context.Exception) switch
		{
			(HttpValidationProblemDetails { Errors.Count: > 0 } validation, _) =>
				$"Validation failed with {validation.Errors.Values.Sum(arr => arr.Length)} error(s).",
			(_, { } ex) when env.IsDevelopment() => ex.Message,
			({ Status: >= 500 }, _) when !env.IsDevelopment() =>
				"An internal error occurred. Please contact support if the problem persists.",
			(_, { } ex) => ex.Message,
			_ => pd.Detail
		};

		if (!env.IsDevelopment() || context.Exception is not ({ InnerException: not null } or { StackTrace: not null }))
		{
			return;
		}

		{
			Exception? ex = context.Exception;
			pd.Extensions["debug"] = new
			{
				exception_type = ex.GetType().FullName,
				inner_exception = ex.InnerException?.Message,
				stack_trace = ex.StackTrace
			};
		}
	}
}
