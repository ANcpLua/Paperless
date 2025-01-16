using Microsoft.Extensions.Logging;

namespace Contract.Logger;

public class OperationLogger : IOperationLogger
{
    private readonly string _environment;
    private readonly ILogger _logger;

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

    public async Task LogOperationError(LogOperationAttribute attribute, string methodName, Exception ex)
    {
        _logger.LogError(ex,
            "[{Environment}][{Component}][{Category}] {Method} failed - {Error}",
            _environment, attribute.Component, attribute.Category, methodName, ex.Message);

        await Task.CompletedTask;
    }
}
