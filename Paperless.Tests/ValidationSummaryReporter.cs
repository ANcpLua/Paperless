using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using TUnit.Assertions.AssertConditions;

namespace Paperless.Tests;

// ═══════════════════════════════════════════════════════════════
// CORE MODELS
// ═══════════════════════════════════════════════════════════════

public sealed record ValidationError(
    string Field,
    string[] Messages,
    HttpStatusCode Status,
    string Url,
    string Method);

// ═══════════════════════════════════════════════════════════════
// SMART PROBLEM DETAILS
// ═══════════════════════════════════════════════════════════════

public sealed class SmartProblemDetails(ProblemDetails details)
{
    private readonly HttpValidationProblemDetails? _validation = details as HttpValidationProblemDetails;

    public async Task AssertValidationAsync(string field, string? contains = null)
    {
        await Assert.That(_validation).IsNotNull()
            .Because("Response did not contain HttpValidationProblemDetails.");

        var validation = _validation!;
        var present = validation.Errors.TryGetValue(field, out var msgs);
        await Assert.That(present).IsTrue()
            .Because($"Expected a validation error on '{field}' but it was missing.");

        if (contains is not null)
        {
            await Assert.That(msgs).IsNotNull().And.IsNotEmpty();
            var match = msgs!.Any(m => m.Contains(contains, StringComparison.OrdinalIgnoreCase));
            await Assert.That(match).IsTrue()
                .Because(
                    $"Expected the message for '{field}' to contain '{contains}', but got: {string.Join(", ", msgs)}.");
        }
    }

    public Task AssertValidationsAsync(params (string field, string? contains)[] expectations) =>
        Task.WhenAll(expectations.Select(e => AssertValidationAsync(e.field, e.contains)));

    public async Task AssertTitleAsync(string expectedTitle)
    {
        await Assert.That(details.Title)
            .IsEqualTo(expectedTitle)
            .Because($"Expected problem title to be '{expectedTitle}'");
    }

    public async Task AssertDetailAsync(string expectedDetail)
    {
        await Assert.That(details.Detail)
            .IsEqualTo(expectedDetail)
            .Because($"Expected problem detail to be '{expectedDetail}'");
    }
}

// ═══════════════════════════════════════════════════════════════
// CORE HTTP SPECIFICATION
// ═══════════════════════════════════════════════════════════════
public static class HttpSpec
{
    public static event Action<ValidationError>? OnValidationError;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    // Direct assertion methods that capture proper stack traces
    public static Task<T> ExpectOkAsync<T>(this Task<HttpResponseMessage> task) where T : class =>
        ExpectStatusAsync<T>(task, HttpStatusCode.OK);

    public static Task<T> ExpectCreatedAsync<T>(this Task<HttpResponseMessage> task) where T : class =>
        ExpectStatusAsync<T>(task, HttpStatusCode.Created);

    public static Task<T> ExpectAcceptedAsync<T>(this Task<HttpResponseMessage> task) where T : class =>
        ExpectStatusAsync<T>(task, HttpStatusCode.Accepted);

    public static async Task ExpectNoContentAsync(this Task<HttpResponseMessage> task)
    {
        var response = await task;
        await EnsureStatusAsync(response, HttpStatusCode.NoContent);
    }

    public static async Task ExpectNotFoundAsync(this Task<HttpResponseMessage> task)
    {
        var response = await task;
        await EnsureStatusAsync(response, HttpStatusCode.NotFound);
    }

    public static Task<SmartProblemDetails> ExpectBadRequestAsync(this Task<HttpResponseMessage> task) =>
        ExpectProblemAsync(task, HttpStatusCode.BadRequest);

    public static async Task<SmartProblemDetails> ExpectProblemAsync(
        this Task<HttpResponseMessage> task,
        HttpStatusCode expectedStatus)
    {
        var response = await task;
        await EnsureStatusAsync(response, expectedStatus);

        var text = await response.Content.ReadAsStringAsync();
        var details = JsonSerializer.Deserialize<HttpValidationProblemDetails>(text, JsonOptions) ??
                      JsonSerializer.Deserialize<ProblemDetails>(text, JsonOptions);

        if (details is null)
        {
            Assert.Fail($"Failed to deserialize response body into a ProblemDetails object. Body: {text}");
            throw new UnreachableException();
        }

        if (details is HttpValidationProblemDetails validationDetails)
        {
            RaiseValidationEvents(validationDetails, response);
        }

        return new SmartProblemDetails(details);
    }

    // Core method that handles all status assertions
    private static async Task<T> ExpectStatusAsync<T>(Task<HttpResponseMessage> task, HttpStatusCode expectedStatus) where T : class
    {
        var response = await task;
        await EnsureStatusAsync(response, expectedStatus);

        var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
        await Assert.That(payload).IsNotNull();
        return payload!;
    }

    // The actual assertion that will have the proper stack trace
    private static async Task EnsureStatusAsync(
        HttpResponseMessage response, 
        HttpStatusCode expectedStatus,
        [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0,
        [CallerMemberName] string? memberName = null)
    {
        if (response.StatusCode == expectedStatus)
            return;

        var message = await BuildFailureMessageAsync(response, expectedStatus);
        
        // Add caller info to the message
        var fullMessage = $"{message}\n\nAssertion made at:\n  {memberName} in {filePath}:{lineNumber}";
        
        // This Assert.Fail will have the correct stack trace
        Assert.Fail(fullMessage);
    }

    private static async Task<string> BuildFailureMessageAsync(HttpResponseMessage response, HttpStatusCode expectedStatus)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"HTTP assertion failed: expected {(int)expectedStatus} {expectedStatus}, got {(int)response.StatusCode} {response.StatusCode}");
        builder.AppendLine();

        if (response.RequestMessage is { } request)
        {
            builder.AppendLine("Request:");
            builder.AppendLine($" {request.Method} {request.RequestUri}");

            if (request.Content is not null)
            {
                var requestBody = await TryReadContentAsync(request.Content);
                if (!string.IsNullOrWhiteSpace(requestBody))
                {
                    builder.AppendLine($" Body: {FormatJson(requestBody)}");
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("Response:");
        builder.AppendLine($" Status: {(int)response.StatusCode} {response.StatusCode}");

        var responseBody = await TryReadContentAsync(response.Content);
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            builder.AppendLine($" Body: {FormatJson(responseBody)}");
        }

        return builder.ToString();
    }

    // File upload helpers remain the same
    public static Task<HttpResponseMessage> PostFileAsync(
        this HttpClient client,
        string url,
        byte[] content,
        string contentType = "application/pdf",
        string fieldName = "file",
        string filename = "test.pdf")
    {
        var form = new MultipartFormDataContent
        {
            {
                new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue(contentType) } },
                fieldName, filename
            }
        };
        return client.PostAsync(url, form);
    }

    public static Task<HttpResponseMessage> PostPdfAsync(
        this HttpClient client,
        string url,
        byte[] pdfContent,
        string filename = "test.pdf") =>
        client.PostFileAsync(url, pdfContent, "application/pdf", "file", filename);

    public static Task<HttpResponseMessage> PostTextFileAsync(
        this HttpClient client,
        string url,
        string text,
        string filename = "test.txt") =>
        client.PostFileAsync(url, Encoding.UTF8.GetBytes(text), "text/plain", "file", filename);

    private static void RaiseValidationEvents(HttpValidationProblemDetails details, HttpResponseMessage response)
    {
        if (OnValidationError is null) return;

        var request = response.RequestMessage;
        foreach (var (field, messages) in details.Errors)
        {
            OnValidationError(new ValidationError(
                field,
                messages,
                response.StatusCode,
                request?.RequestUri?.ToString() ?? "unknown",
                request?.Method.Method ?? "unknown"));
        }
    }

    private static async Task<string> TryReadContentAsync(HttpContent? content)
    {
        try
        {
            return content is null ? string.Empty : await content.ReadAsStringAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatJson(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return content;
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM ASSERTION CONDITION WITH STACK TRACE
// ═══════════════════════════════════════════════════════════════

internal sealed class HttpStatusAssertCondition : BaseAssertCondition<HttpResponseMessage>
{
    private readonly HttpStatusCode _expected;
    private readonly string? _filePath;
    private readonly int _lineNumber;
    private readonly string? _memberName;
    private readonly string _stackTrace;

    public HttpStatusAssertCondition(
        HttpStatusCode expected,
        string? filePath = null,
        int lineNumber = 0,
        string? memberName = null)
    {
        _expected = expected;
        _filePath = filePath;
        _lineNumber = lineNumber;
        _memberName = memberName;
        
        // Capture stack trace at construction time
        _stackTrace = Environment.StackTrace;
    }

    protected override string GetExpectation() => $"to have status {(int)_expected} {_expected}";

    protected override async ValueTask<AssertionResult> GetResult(
        HttpResponseMessage? actual,
        Exception? exception,
        AssertionMetadata metadata)
    {
        if (actual is null)
        {
            var nullMessage = BuildNullResponseMessage();
            return FailWithMessage(nullMessage);
        }

        if (actual.StatusCode == _expected)
            return AssertionResult.Passed;

        var message = await BuildFailureMessageAsync(actual);
        return FailWithMessage(message);
    }

    private string BuildNullResponseMessage()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Response was null");
        AppendLocationInfo(builder);
        return builder.ToString();
    }

    private async Task<string> BuildFailureMessageAsync(HttpResponseMessage response)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            $"HTTP assertion failed: expected {(int)_expected} {_expected}, got {(int)response.StatusCode} {response.StatusCode}");
        builder.AppendLine();

        if (response.RequestMessage is { } request)
        {
            builder.AppendLine("Request:");
            builder.AppendLine($" {request.Method} {request.RequestUri}");

            if (request.Content is not null)
            {
                var requestBody = await TryReadContentAsync(request.Content);
                if (!string.IsNullOrWhiteSpace(requestBody))
                {
                    builder.AppendLine($" Body: {FormatJson(requestBody)}");
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine("Response:");
        builder.AppendLine($" Status: {(int)response.StatusCode} {response.StatusCode}");

        var responseBody = await TryReadContentAsync(response.Content);
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            builder.AppendLine($" Body: {FormatJson(responseBody)}");
        }

        AppendLocationInfo(builder);

        return builder.ToString();
    }

    private void AppendLocationInfo(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("Location:");
        
        if (_filePath != null && _memberName != null)
        {
            builder.AppendLine($"  at {_memberName} in {_filePath}:{_lineNumber}");
        }
        
        // Add relevant parts of the stack trace
        var relevantStackLines = _stackTrace
            .Split('\n')
            .Where(line => !line.Contains("TUnit") && 
                          !line.Contains("System.") && 
                          line.Contains(" at "))
            .Take(5);
        
        foreach (var line in relevantStackLines)
        {
            builder.AppendLine(line.Trim());
        }
    }

    private static async Task<string> TryReadContentAsync(HttpContent? content)
    {
        try
        {
            return content is null ? string.Empty : await content.ReadAsStringAsync();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatJson(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return content;
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// TEST EVENT RECEIVERS (unchanged)
// ═══════════════════════════════════════════════════════════════

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RecordValidationAttribute : Attribute, ITestStartEventReceiver, ITestEndEventReceiver
{
    private static readonly ConditionalWeakTable<TestContext, List<ValidationError>> ErrorMap = new();

    public static IReadOnlyList<ValidationError> GetErrors(TestContext context) =>
        ErrorMap.TryGetValue(context, out var errors) ? errors : Array.Empty<ValidationError>();

    ValueTask ITestStartEventReceiver.OnTestStart(BeforeTestContext context)
    {
        var errors = ErrorMap.GetOrCreateValue(context.TestContext);
        HttpSpec.OnValidationError += errors.Add;
        return ValueTask.CompletedTask;
    }

    ValueTask ITestEndEventReceiver.OnTestEnd(AfterTestContext context)
    {
        if (ErrorMap.TryGetValue(context.TestContext, out var errors))
        {
            HttpSpec.OnValidationError -= errors.Add;
            if (errors.Count > 0)
            {
                context.TestContext.ObjectBag["ValidationErrors"] = errors.ToArray();
            }
        }

        return ValueTask.CompletedTask;
    }
}



// ═══════════════════════════════════════════════════════════════
// VALIDATION SUMMARY REPORTER
// ═══════════════════════════════════════════════════════════════

public sealed class ValidationSummaryReporter : ITestEndEventReceiver
{
    private static readonly ConcurrentBag<ValidationError> AllErrors = [];
    private static readonly Lock SummaryLock = new();
    private static bool _summaryPrinted;
    private static readonly ValidationSummaryReporter Instance = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        _ = Instance;
    }

    static ValidationSummaryReporter()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => PrintSummary();
    }

    public ValueTask OnTestEnd(AfterTestContext context)
    {
        if (context.TestContext.ObjectBag.TryGetValue("ValidationErrors", out var value) &&
            value is ValidationError[] errors)
        {
            foreach (var error in errors)
            {
                AllErrors.Add(error);
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void PrintSummary()
    {
        using (SummaryLock.EnterScope())
        {
            if (_summaryPrinted || AllErrors.IsEmpty) return;
            _summaryPrinted = true;
        }

        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    🚨 VALIDATION REPORT 🚨                ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"📊 Found {AllErrors.Count} validation errors across your test suite");
        Console.ResetColor();

        PrintTopOffendingFields();
        PrintTopOffendingEndpoints();
        PrintMethodBreakdown();
        PrintSampleMessages();

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("💡 Tip: Fix the most frequent errors first for maximum impact!");
        Console.ResetColor();
    }

    private static void PrintTopOffendingFields()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("🎯 Top Offending Fields:");
        Console.ResetColor();

        foreach (var group in AllErrors.GroupBy(e => e.Field).OrderByDescending(g => g.Count()).Take(5))
        {
            var percentage = (group.Count() * 100.0 / AllErrors.Count).ToString("F1");
            Console.WriteLine($"   • {group.Key}: {group.Count()} errors ({percentage}%)");
        }
    }

    private static void PrintTopOffendingEndpoints()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("🌐 Top Offending Endpoints:");
        Console.ResetColor();

        foreach (var group in AllErrors.GroupBy(e => $"{e.Method} {e.Url}").OrderByDescending(g => g.Count()).Take(5))
        {
            Console.WriteLine($"   • {group.Key}: {group.Count()} errors");
        }
    }

    private static void PrintMethodBreakdown()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("📈 HTTP Method Breakdown:");
        Console.ResetColor();

        foreach (var group in AllErrors.GroupBy(e => e.Method).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"   • {group.Key}: {group.Count()} errors");
        }
    }

    private static void PrintSampleMessages()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("💬 Sample Error Messages:");
        Console.ResetColor();

        var sampleMessages = AllErrors
            .SelectMany(e => e.Messages)
            .Distinct()
            .Take(3);

        foreach (var message in sampleMessages)
        {
            Console.WriteLine($"   • \"{message}\"");
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// TESTCONTEXT EXTENSIONS
// ═══════════════════════════════════════════════════════════════

public static class TestContextExtensions
{
    public static IReadOnlyList<ValidationError> GetValidationErrors(this TestContext? context) =>
        context?.ObjectBag.TryGetValue("ValidationErrors", out var value) is true && value is ValidationError[] errors
            ? errors
            : [];

    public static bool HasValidationErrors(this TestContext? context) =>
        context.GetValidationErrors().Count > 0;

    public static IEnumerable<ValidationError> GetValidationErrorsForField(this TestContext? context, string fieldName) =>
        context.GetValidationErrors().Where(e => e.Field.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
}

public class WebApplicationFactory : WebApplicationFactory<Program>, IAsyncInitializer
{
    public Task InitializeAsync()
    {
        _ = Server;

        return Task.CompletedTask;
    }
}public class Tests
{
    [ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerTestSession)]
    public required WebApplicationFactory WebApplicationFactory { get; init; }

    [Test]
    public async Task Test()
    {
        var client = WebApplicationFactory.CreateClient();

        var response = await client.GetAsync("/ping");

        var stringContent = await response.Content.ReadAsStringAsync();

        await Assert.That(stringContent).IsEqualTo("Hello, World!");
    }
}