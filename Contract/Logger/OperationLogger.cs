using Microsoft.Extensions.Logging;

namespace Contract.Logger;

public class OperationLogger : IOperationLogger
{
    private readonly ILogger _logger;
    private readonly string _environment;

    public OperationLogger(ILogger logger, string environment)
    {
        _logger = logger;
        _environment = environment;
    }

    public async Task LogOperation(LogOperationAttribute attribute, string methodName, params object?[] parameters)
    {
        if (attribute.LogParameters && parameters is { Length: > 0 })
        {
            var paramString = string.Join(", ", parameters.Select(p => p?.ToString() ?? "null"));
            _logger.Log(attribute.Level,
                "[{Environment}][{Component}][{Category}] {Method} started - Parameters: {Parameters}",
                _environment, attribute.Component, attribute.Category, methodName, paramString);
        }
        else
        {
            _logger.Log(attribute.Level,
                "[{Environment}][{Component}][{Category}] {Method} started",
                _environment, attribute.Component, attribute.Category, methodName);
        }

        await Task.CompletedTask;
    }

    public async Task LogOperationComplete(LogOperationAttribute attribute, string methodName, object? result = null)
    {
        if (result != null && attribute.LogResponse)
        {
            _logger.Log(attribute.Level,
                "[{Environment}][{Component}][{Category}] {Method} completed - Result: {Result}",
                _environment, attribute.Component, attribute.Category, methodName, result);
        }
        else
        {
            _logger.Log(attribute.Level,
                "[{Environment}][{Component}][{Category}] {Method} completed",
                _environment, attribute.Component, attribute.Category, methodName);
        }

        await Task.CompletedTask;
    }

    public async Task LogOperationError(LogOperationAttribute attribute, string methodName, Exception ex)
    {
        _logger.LogError(ex,
            "[{Environment}][{Component}][{Category}] {Method} failed - {Error}",
            _environment, attribute.Component, attribute.Category, methodName, ex.Message);

        await Task.CompletedTask;
    }
}
