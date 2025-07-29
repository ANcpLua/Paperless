using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;

namespace PaperlessREST.Extensions;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IHostEnvironment _env;
    private readonly IProblemDetailsService _problemDetails;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        IHostEnvironment env,
        IProblemDetailsService problemDetails)
    {
        _logger = logger;
        _env = env;
        _problemDetails = problemDetails;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken token)
    {
        if (!TryMapStatusCode(exception, out var status))
        {
            return false;
        }

        context.Response.StatusCode = status;

        var pdContext = new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails =
            {
                Title = ReasonPhrases.GetReasonPhrase(status),
                Detail = _env.IsDevelopment() ? exception.Message : GetPublicMessage(exception),
                Type = $"https://httpstatuses.io/{status}"
            }
        };

        // Include FluentValidation errors
        if (exception is ValidationException validationEx && validationEx.Errors.Any())
        {
            pdContext.ProblemDetails.Extensions["errors"] =
                validationEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());
        }

        // Log once
        _logger.LogError(exception, "Handled exception mapped to {StatusCode}", status);

        await _problemDetails.TryWriteAsync(pdContext);
        return true;
    }

    private static bool TryMapStatusCode(Exception ex, out int status)
    {
        status = ex switch
        {
            ValidationException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            UnauthorizedAccessException => StatusCodes.Status401Unauthorized,
            TimeoutException => StatusCodes.Status408RequestTimeout,
            NotImplementedException => StatusCodes.Status501NotImplemented,
            OperationCanceledException => StatusCodes.Status499ClientClosedRequest,
            _ => 0
        };
        return status is not 0;
    }

    private static string GetPublicMessage(Exception ex) => ex switch
    {
        ValidationException => "The request contains invalid data.",
        KeyNotFoundException => "The requested resource was not found.",
        UnauthorizedAccessException => "You are not authorized to access this resource.",
        TimeoutException => "The request timed out.",
        NotImplementedException => "This feature is not implemented.",
        OperationCanceledException => "The request was cancelled.",
        _ => "An unexpected error occurred."
    };
}