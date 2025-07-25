# OCR Service Testing - Implementation Notes

## 🎯 Key Principles to Remember

### 1. **NO MANUAL DISPOSAL FOR CONTAINERS**
- Testcontainers handles cleanup via Resource Reaper
- Only implement `IAsyncInitializer`, NOT `IAsyncDisposable` on container managers
- Resource Reaper automatically cleans up Docker resources

### 2. **USE TUNIT'S BUILT-IN PATTERNS**
```csharp
// ✅ CORRECT - Use ClassDataSource with property injection
[ClassDataSource<PaperlessServicesHost>(Shared = SharedType.PerTestSession)]
public required PaperlessServicesHost ServicesHost { get; init; }

// ❌ WRONG - Don't create custom DI attributes
public class OcrTestServicesAttribute : DependencyInjectionDataSourceAttribute<IServiceScope>
```

### 3. **SERVICE HOST PATTERN**
```csharp
public class PaperlessServicesHost : IAsyncInitializer
{
    // Container injection
    [ClassDataSource<TestContainersManager>(Shared = SharedType.PerTestSession)]
    public required TestContainersManager Containers { get; init; }

    // Service accessors
    public IOcrService OcrService => _host!.Services.GetRequiredService<IOcrService>();
    
    // Host disposal is OK - it's not a container!
    public async ValueTask DisposeAsync() { /* dispose host */ }
}
```

### 4. **MINIMAL TEST STRUCTURE**
- Test the actual service interfaces, not framework code
- Don't test `Pdf.Load()` or `Pdf.Create()` - they're battle-tested
- Focus on YOUR code behavior

## 🚀 Web App Testing Pattern (For Tomorrow)

```csharp
/// <summary>
/// Base class for integration tests.
/// </summary>
public abstract class IntegrationTestBase
{
    [ClassDataSource<PaperlessWebApplication>(Shared = SharedType.PerTestSession)]
    public required PaperlessWebApplication Application { get; init; }

    protected HttpClient CreateClient() => Application.CreateClient();
}

/// <summary>
/// Test server using WebApplicationFactory.
/// </summary>
public sealed class PaperlessWebApplication : WebApplicationFactory<Program>, IAsyncInitializer
{
    [ClassDataSource<TestContainersManager>(Shared = SharedType.PerTestSession)]
    public required TestContainersManager Containers { get; init; }

    public Task InitializeAsync() => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(Containers.GetConfiguration());
        });
    }
}
```

## ⚠️ Common Mistakes to Avoid

1. **DON'T** implement disposal for containers
2. **DON'T** create custom DI attributes when TUnit has built-in patterns
3. **DON'T** test framework code (PDF library, Minio client internals)
4. **DON'T** overcomplicate - keep tests lean and focused
5. **DON'T** reinvent the wheel - use TUnit's features

## ✅ Checklist for Web App Tests

- [ ] Use `WebApplicationFactory<Program>` for integration tests
- [ ] Inject containers via `ClassDataSource`
- [ ] Let Resource Reaper handle container cleanup
- [ ] Use property injection for test dependencies
- [ ] Keep test methods focused on one scenario
- [ ] Use actual configuration values from DI

## 📝 Example Test Structure

```csharp
public class ApiTests : IntegrationTestBase
{
    [Test]
    public async Task GetDocuments_ReturnsOkResult()
    {
        // Arrange
        var client = CreateClient();
        
        // Act
        var response = await client.GetAsync("/api/documents");
        
        // Assert
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}
```

**Remember: The framework does the heavy lifting. Focus on testing YOUR business logic, not the plumbing!**