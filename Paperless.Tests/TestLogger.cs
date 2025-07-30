using Microsoft.Extensions.Logging;

namespace Paperless.Tests;

public class TestLogger : ILogger
{
    private readonly string _name;

    public TestLogger(string name = "TestContainers")
    {
        _name = name;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

        Console.WriteLine($"[{timestamp}] [{_name}] [{logLevel}] {message}");

        if (exception is not null)
        {
            Console.WriteLine($"[{timestamp}] [{_name}] [ERROR] {exception}");
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}