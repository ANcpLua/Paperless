using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Contract.Logger;

namespace PaperlessREST;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOperationLogger _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, IOperationLogger logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await _logger.LogOperationError(new LogOperationAttribute("UnhandledException", "API", LogLevel.Error),
                "UnhandledExceptionMiddleware", ex);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        context.Response.StatusCode = statusCode;

        var errorResponse = new
        {
            Message = message,
            RequestId = context.TraceIdentifier,
            Timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }
}
