using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Paperless.Tests;

public class TestLogger(string name = "TestContainers") : ILogger
{
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        Console.WriteLine($"[{timestamp}] [{name}] [{logLevel}] {message}");

        if (exception is not null)
        {
            Console.WriteLine($"[{timestamp}] [{name}] [ERROR] {exception}");
        }
    }

    FakeLogger Provider { get; } = new();

    public FakeLogger GetFakeLogger() => Provider;

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}

public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        var status = ExceptionStatusCodeMap.Select(exception);

        httpContext.Response.StatusCode = status;

        var statusException = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            OperationCanceledException => 499, // new: Client Closed Request
            _ => StatusCodes.Status500InternalServerError
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status), // covers 499 now
            Type = $"urn:problem-type:{exception.GetType().FullName}",
            Detail = exception.Message
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Type = $"urn:problem-type:{exception.GetType().FullName}",
            Detail = exception.Message
        };

        problem.Extensions.TryAdd("typeName", exception.GetType().Name);
        problem.Extensions.TryAdd("instance", $"{httpContext.Request.Method} {httpContext.Request.Path}");

        log.LogError(exception, "Unhandled exception mapped to {Status}", status);

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            Exception = exception,
            HttpContext = httpContext,
            ProblemDetails = problem
        });
    }

    public static class ExceptionStatusCodeMap
    {
        public static int Select(Exception exception)
        {
            return exception switch
            {
                BadHttpRequestException e => e.StatusCode,
                ArgumentException => StatusCodes.Status400BadRequest,
                KeyNotFoundException => StatusCodes.Status404NotFound,
                NotSupportedException => StatusCodes.Status415UnsupportedMediaType,
                OperationCanceledException => 499,
                _ => StatusCodes.Status500InternalServerError
            };
        }
    }

    public static IServiceCollection AddDependencies(this WebApplicationBuilder builder)
    {
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                ctx.ProblemDetails.Instance =
                    $"{ctx.HttpContext.Request.Method} {ctx.HttpContext.Request.Path}";
                ctx.ProblemDetails.Extensions.TryAdd("requestId", ctx.HttpContext.TraceIdentifier);
                ctx.ProblemDetails.Extensions.TryAdd("activityId", Activity.Current?.Id);
            };
        });

        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

        return builder.Services;
    }