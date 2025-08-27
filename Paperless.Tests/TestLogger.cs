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