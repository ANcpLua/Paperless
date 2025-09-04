using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace PaperlessREST.Extensions;

public sealed class TimeOffsetOptions
{
    public TimeSpan Offset { get; set; } = TimeSpan.Zero;
}

internal static class ExceptionClassifier
{
    public static int SelectStatusCode(Exception ex) => ex switch
    {
        ArgumentException or InvalidOperationException or JsonException => StatusCodes.Status400BadRequest,
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        KeyNotFoundException or FileNotFoundException => StatusCodes.Status404NotFound,
        OperationCanceledException => 499,
        NotImplementedException => StatusCodes.Status501NotImplemented,
        TimeoutException => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status500InternalServerError
    };

    public static LogLevel SelectLogLevel(Exception ex)
    {
        var status = SelectStatusCode(ex);
        return status switch
        {
            >= 500 => LogLevel.Error,
            StatusCodes.Status400BadRequest or StatusCodes.Status403Forbidden => LogLevel.Warning,
            _ => LogLevel.Information
        };
    }
}

internal static class Log
{
    private static readonly Action<ILogger, int, string, Exception?> Error =
        LoggerMessage.Define<int, string>(LogLevel.Error, new EventId(500, "ServerError"),
            "Unhandled server error. Status: {StatusCode}. Path: {Path}");

    private static readonly Action<ILogger, int, string, Exception?> Warning =
        LoggerMessage.Define<int, string>(LogLevel.Warning, new EventId(400, "ClientError"),
            "Client request error. Status: {StatusCode}. Path: {Path}");

    private static readonly Action<ILogger, int, string, Exception?> Info =
        LoggerMessage.Define<int, string>(LogLevel.Information, new EventId(404, "RequestIssue"),
            "Request issue. Status: {StatusCode}. Path: {Path}");

    private static readonly Action<ILogger, string, Exception?> Cancelled =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(499, "RequestCancelled"),
            "Request cancelled by client (499). Path: {Path}");

    public static void ExceptionOccurred(ILogger logger, Exception ex, int status, LogLevel level, string path)
    {
        switch (level)
        {
            case LogLevel.Error: Error(logger, status, path, ex); break;
            case LogLevel.Warning: Warning(logger, status, path, ex); break;
            case LogLevel.Information: Info(logger, status, path, ex); break;
            default:
                logger.Log(level, ex, "Exception occurred. Status: {StatusCode}. Path: {Path}", status,
                    path); break;
        }
    }

    public static void RequestCancelled(ILogger logger, Exception ex, string path) => Cancelled(logger, path, ex);
}

public sealed class OptimizedExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<OptimizedExceptionHandler> _logger;

    public OptimizedExceptionHandler(IProblemDetailsService problemDetails,
        ILogger<OptimizedExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        var path = context.Request.Path.Value ?? "/";
        if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
            Log.RequestCancelled(_logger, exception, path);
            return true;
        }

        var status = ExceptionClassifier.SelectStatusCode(exception);
        var level = ExceptionClassifier.SelectLogLevel(exception);
        Log.ExceptionOccurred(_logger, exception, status, level, path);
        context.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
            { HttpContext = context, Exception = exception });
    }
}

public sealed class ProblemDetailsCustomization : IConfigureOptions<ProblemDetailsOptions>
{
    private readonly bool _isDevelopment;
    private readonly IOptionsMonitor<Microsoft.AspNetCore.Http.Json.JsonOptions> _json;
    private readonly TimeSpan _offset;

    public ProblemDetailsCustomization(IHostEnvironment env,
        IOptionsMonitor<Microsoft.AspNetCore.Http.Json.JsonOptions> json, IOptions<TimeOffsetOptions> offset)
    {
        _isDevelopment = env.IsDevelopment();
        _json = json;
        _offset = offset.Value.Offset;
    }

    public void Configure(ProblemDetailsOptions options) => options.CustomizeProblemDetails = Customize;

    private void Customize(ProblemDetailsContext ctx)
    {
        var pd = ctx.ProblemDetails;
        SetInstance(pd, ctx.HttpContext);
        AddTimestamps(pd);
        AddRequestData(pd, ctx.HttpContext);
        AddTracingData(pd, ctx.HttpContext);
        AddErrorCode(pd, ctx.Exception);
        if (pd is HttpValidationProblemDetails v) SummarizeValidation(v);
        AddExceptionDetail(pd, ctx.Exception);
    }

    private static void SetInstance(ProblemDetails pd, HttpContext http) =>
        pd.Instance ??= $"{http.Request.Method} {http.Request.Path}";

    private void AddTimestamps(ProblemDetails pd)
    {
        var now = DateTimeOffset.UtcNow;
        pd.Extensions.TryAdd("timestampUtc", now.ToString("o"));
        if (_offset != TimeSpan.Zero)
        {
            pd.Extensions.TryAdd("offset", _offset.ToString());
            pd.Extensions.TryAdd("timestampLocal", now.ToOffset(_offset).ToString("o"));
        }
    }

    private static void AddRequestData(ProblemDetails pd, HttpContext http)
    {
        pd.Extensions.TryAdd("requestId", http.TraceIdentifier);
        pd.Extensions.TryAdd("endpoint", http.GetEndpoint()?.DisplayName ?? "unknown");
        pd.Extensions.TryAdd("method", http.Request.Method);
        if (http.GetEndpoint() is RouteEndpoint routeEp)
            pd.Extensions.TryAdd("route", routeEp.RoutePattern.RawText);
    }

    private static void AddTracingData(ProblemDetails pd, HttpContext http)
    {
        var act = http.Features.Get<IHttpActivityFeature>()?.Activity ?? Activity.Current;
        var trace = act?.TraceId.ToString();
        if (!string.IsNullOrEmpty(trace)) pd.Extensions.TryAdd("traceId", trace);
    }

    private static void AddErrorCode(ProblemDetails pd, Exception? ex)
    {
        var code = ex switch
        {
            ArgumentNullException => "E_ARG_NULL",
            KeyNotFoundException => "E_NOT_FOUND",
            TimeoutException => "E_TIMEOUT",
            _ => null
        };
        if (code is not null)
        {
            pd.Extensions["code"] = code;
            pd.Type ??= $"urn:app:error:{code}";
        }
    }

    private void SummarizeValidation(HttpValidationProblemDetails v)
    {
        var count = v.Errors.Values.Sum(x => x.Length);
        v.Detail = $"Validation failed with {count} error(s).";
        var policy = _json.CurrentValue.SerializerOptions.PropertyNamingPolicy;
        if (policy is null) return;
        var entries = v.Errors.ToArray();
        v.Errors.Clear();
        foreach (var kvp in entries) v.Errors[policy.ConvertName(kvp.Key)] = kvp.Value;
    }

    private void AddExceptionDetail(ProblemDetails pd, Exception? ex)
    {
        if (ex is null) return;
        if (_isDevelopment)
        {
            pd.Detail ??= ex.Message;
            pd.Extensions.TryAdd("exceptionDetails", new
            {
                type = ex.GetType().FullName,
                message = ex.Message,
                stackTrace = ex.StackTrace?.Split(Environment.NewLine),
                innerException = ex.InnerException?.Message
            });
        }
        else
        {
            if ((pd.Status ?? 500) >= 500)
                pd.Detail = "An internal server error occurred. Use the requestId to investigate.";
            else pd.Detail ??= ex.Message;
        }
    }
}