using System.Diagnostics;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ValidationException = FluentValidation.ValidationException;

namespace PaperlessREST.Extensions;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment env,
    IProblemDetailsService problemDetails)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken token)
    {
        if (!TryMapStatusCode(exception, out var status)) return false;

        context.Response.StatusCode = status;

        var title = ReasonPhrases.GetReasonPhrase(status);
        var detail = env.IsDevelopment() ? exception.ToString() : GetPublicMessage(exception);

        var pd = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.io/{status}",
            Detail = detail
        };

        if (exception is ValidationException fv && fv.Errors.Any())
        {
            pd.Extensions["errors"] = fv.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        var activity = context.Features.Get<IHttpActivityFeature>()?.Activity ?? Activity.Current;
        pd.Extensions.TryAdd("traceId", activity?.Id ?? context.TraceIdentifier);
        pd.Extensions.TryAdd("requestId", context.TraceIdentifier);
        pd.Extensions.TryAdd("endpoint", context.GetEndpoint()?.DisplayName ?? "unknown");
        pd.Extensions.TryAdd("exceptionType", exception.GetType().FullName);

        var wrote = await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = pd
        });

        logger.LogError(exception, "Exception mapped to {Status} {Title}", status, title);
        return wrote;
    }

    private static bool TryMapStatusCode(Exception ex, out int status)
    {
        status = ex switch
        {
            ValidationException => StatusCodes.Status400BadRequest,
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            TimeoutException => StatusCodes.Status408RequestTimeout,
            NotImplementedException => StatusCodes.Status501NotImplemented,
            OperationCanceledException => StatusCodes.Status499ClientClosedRequest,
            JsonException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };
        return true;
    }

    private static string GetPublicMessage(Exception ex) => ex switch
    {
        ValidationException => "One or more validation errors occurred.",
        ArgumentException => "The request contained invalid arguments.",
        KeyNotFoundException => "The requested resource was not found.",
        UnauthorizedAccessException => "Unauthorized.",
        TimeoutException => "The request timed out.",
        NotImplementedException => "Not implemented.",
        OperationCanceledException => "The request was cancelled by the client.",
        JsonException => "Malformed or unsupported JSON payload.",
        _ => "An unexpected error occurred."
    };
}