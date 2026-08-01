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
			BadHttpRequestException => new ExceptionInfo(StatusCodes.Status400BadRequest, LogLevel.Warning,
				"bad_request"),
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

		var info = ExceptionInfo.FromException(exception);

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
				? CreateValidationProblemDetails(validationEx, info)
				: new ProblemDetails
				{
					Status = info.StatusCode,
					Type = $"urn:paperless:error:{info.Code}",
					Detail = exception is BadHttpRequestException ? "The request was invalid." : null
				}
		};

		return await problemDetails.TryWriteAsync(problemDetailsContext);
	}

	private static HttpValidationProblemDetails CreateValidationProblemDetails(
		ValidationException exception,
		ExceptionInfo info)
	{
		var message = exception.ValidationResult.ErrorMessage ?? exception.Message;
		Dictionary<string, string[]> errors = [];

		foreach (var memberName in exception.ValidationResult.MemberNames
			         .Where(static name => !string.IsNullOrWhiteSpace(name))
			         .Distinct(StringComparer.Ordinal))
		{
			errors[memberName] = [message];
		}

		if (errors.Count == 0)
		{
			errors[string.Empty] = [message];
		}

		return new HttpValidationProblemDetails(errors)
		{
			Status = info.StatusCode,
			Type = $"urn:paperless:error:{info.Code}"
		};
	}
}

public sealed class ProblemDetailsEnricher(
	IHostEnvironment env,
	TimeProvider timeProvider) : IConfigureOptions<ProblemDetailsOptions>
{
	public void Configure(ProblemDetailsOptions options) => options.CustomizeProblemDetails = Enrich;

	private void Enrich(ProblemDetailsContext context)
	{
		var pd = context.ProblemDetails;
		var httpContext = context.HttpContext;
		bool isDevelopment = env.IsDevelopment();

		pd.Extensions["trace_id"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
		pd.Extensions["instance"] = $"{httpContext.Request.Method} {httpContext.Request.Path}";
		pd.Extensions["timestamp"] = timeProvider.GetUtcNow().ToString("O");

		if (httpContext.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: var pattern })
		{
			pd.Extensions["route"] = pattern;
		}

		if (pd is HttpValidationProblemDetails validation && validation.Errors.Count > 0)
		{
			pd.Detail = $"Validation failed with {validation.Errors.Values.Sum(arr => arr.Length)} error(s).";
		}
		else if (isDevelopment && context.Exception is not null)
		{
			pd.Detail = context.Exception.Message;
		}
		else if (!isDevelopment && pd.Status >= StatusCodes.Status500InternalServerError)
		{
			pd.Detail = "An internal error occurred. Please contact support if the problem persists.";
		}
		var exception = context.Exception;
		if (!isDevelopment || exception is null ||
		    (exception.InnerException is null && exception.StackTrace is null))
		{
			return;
		}

		pd.Extensions["debug"] = new
		{
			exception_type = exception.GetType().FullName,
			inner_exception = exception.InnerException?.Message,
			stack_trace = exception.StackTrace
		};
	}
}
