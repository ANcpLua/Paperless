using PaperlessUI.Blazor.Components;
using PaperlessUI.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBaseUrl = builder.Configuration["Paperless:ApiBaseUrl"] ?? "http://localhost:8080";
var apiBase = new Uri(apiBaseUrl);

// Named client used by the long-lived SSE consumer. A request-scoped Polly timeout would
// sever the *open* event stream (ResponseHeadersRead returns at the headers, but the timeout
// token keeps running and cancels the body read — REST logs a 499 the instant it fires,
// dropping events). Resilience here is the reconnect-with-backoff loop in DocumentEventStream,
// so no handler is attached and the client timeout is infinite. AL1105 (require a resilience
// handler) does not apply to a streaming client and is intentionally suppressed.
#pragma warning disable AL1105
builder.Services.AddHttpClient("paperless", c =>
{
    c.BaseAddress = apiBase;
    c.Timeout = Timeout.InfiniteTimeSpan;
});
#pragma warning restore AL1105

// Typed client for short REST calls (list / upload / search / delete).
builder.Services.AddHttpClient<PaperlessApiClient>(c => c.BaseAddress = apiBase)
    .AddStandardResilienceHandler();

builder.Services.AddScoped<DocumentEventStream>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
