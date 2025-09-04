using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Paperless.UnitTests.Helpers;

public class FakeLogger<T> : ILogger<T> where T : class
{
    private readonly List<LogRecord> _records = new();

    public IReadOnlyList<LogRecord> Records => _records;

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _records.Add(new LogRecord(logLevel, message, exception, state));
    }

    // Simple API for tests
    public void VerifyLog(LogLevel level, string pattern)
    {
        var regex = BuildRegex(pattern);
        var found = _records.Any(r => r.Level == level && regex.IsMatch(r.Message));

        if (found) return;

        var logs = _records.Select(r => $"[{r.Level}] {r.Message}").ToList();
        throw new Exception($"No {level} log matching '{pattern}' found.\nActual:\n{string.Join("\n", logs)}");
    }

    public void VerifyNoOtherCalls()
    {
        /* no-op */
    }

    // Fluent API
    public FakeLoggerAssertions Should() => new(this);

    // Pattern matching that handles structured logging placeholders {Name} as wildcards
    private static Regex BuildRegex(string pattern)
    {
        // Replace {placeholders} with * first
        pattern = Regex.Replace(pattern, @"\{[^}]+\}", "*");

        // Convert wildcard pattern to regex
        var span = pattern.AsSpan();
        var sb = new StringBuilder(pattern.Length + 10);
        sb.Append('^');

        foreach (var ch in span)
        {
            sb.Append(ch switch
            {
                '*' => ".*",
                '?' => ".",
                '.' or '$' or '^' or '{' or '}' or '[' or ']' or '(' or ')' or '|' or '+' or '\\' => $"\\{ch}",
                _ => ch
            });
        }

        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    public record LogRecord(LogLevel Level, string Message, Exception? Exception, object? State);
}

public class FakeLoggerAssertions
{
    private readonly dynamic _logger;
    private LogLevel? _level;
    private string? _pattern;
    private Type? _exceptionType;
    private int? _count;

    internal FakeLoggerAssertions(object logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public FakeLoggerAssertions HaveLoggedInformation(string? pattern = null)
    {
        _level = LogLevel.Information;
        _pattern = pattern;
        return this;
    }

    public FakeLoggerAssertions HaveLoggedWarning(string? pattern = null)
    {
        _level = LogLevel.Warning;
        _pattern = pattern;
        return this;
    }

    public FakeLoggerAssertions HaveLoggedError(string? pattern = null)
    {
        _level = LogLevel.Error;
        _pattern = pattern;
        return this;
    }

    public FakeLoggerAssertions HaveLogLevel(LogLevel level)
    {
        _level = level;
        return this;
    }

    public FakeLoggerAssertions WithMessage(string pattern)
    {
        _pattern = pattern;
        return this;
    }

    public FakeLoggerAssertions WithException<TException>() where TException : Exception
    {
        _exceptionType = typeof(TException);
        return this;
    }

    public FakeLoggerAssertions Once()
    {
        _count = 1;
        return this;
    }

    public FakeLoggerAssertions Times(int count)
    {
        _count = count;
        return this;
    }

    public void Verify()
    {
        var recordsList = ((IEnumerable<dynamic>)_logger.Records).ToList();

        // Build regex if we have a pattern
        Regex? regex = null;
        if (_pattern != null)
        {
            var processed = Regex.Replace(_pattern, @"\{[^}]+\}", "*");
            var span = processed.AsSpan();
            var sb = new StringBuilder(processed.Length + 10);
            sb.Append('^');

            foreach (var ch in span)
            {
                sb.Append(ch switch
                {
                    '*' => ".*",
                    '?' => ".",
                    '.' or '$' or '^' or '{' or '}' or '[' or ']' or '(' or ')' or '|' or '+' or '\\' => $"\\{ch}",
                    _ => ch
                });
            }

            sb.Append('$');
            regex = new Regex(sb.ToString(), RegexOptions.IgnoreCase);
        }

        var matches = recordsList.Where(r =>
            (!_level.HasValue || r.Level == _level) &&
            (regex == null || regex.IsMatch(r.Message)) &&
            (_exceptionType == null || (r.Exception != null && _exceptionType.IsAssignableFrom(r.Exception.GetType())))
        ).ToList();

        var actualCount = matches.Count;
        var expectedCount = _count ?? -1; // -1 means at least one

        var valid = expectedCount == -1 ? actualCount > 0 : actualCount == expectedCount;
        if (valid) return;

        var logs = recordsList.Select(r => $"[{r.Level}] {r.Message}").ToList();
        var expectation = expectedCount == -1 ? "at least one" : expectedCount.ToString();
        throw new Exception(
            $"Expected {expectation} matching log(s), found {actualCount}.\nAll logs:\n{string.Join("\n", logs)}");
    }

    public void HaveNoLogs()
    {
        var recordsList = ((IEnumerable<dynamic>)_logger.Records).ToList();
        if (recordsList.Count == 0) return;

        var logs = recordsList.Select(r => $"[{r.Level}] {r.Message}").ToList();
        throw new Exception($"Expected no logs, found {recordsList.Count}:\n{string.Join("\n", logs)}");
    }
}

public static class FakeLoggerExtensions
{
    public static FakeLoggerAssertions Should<T>(this FakeLogger<T> logger) where T : class => logger.Should();
}