using Microsoft.Extensions.Http.Resilience;
using PaperlessUI.Blazor.Components;
using PaperlessUI.Blazor.Services;
using Polly;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBaseUrl = builder.Configuration["Paperless:ApiBaseUrl"] ?? "http://localhost:8080";
var apiBase = new Uri(apiBaseUrl);

// Named client used by the long-lived SSE consumer.
// No retry: DocumentEventStream.ConsumeAsync owns reconnection with backoff.
// Timeout only: cap the initial connect attempt so a dead server doesn't stall the circuit forever.
builder.Services.AddHttpClient("paperless", c => c.BaseAddress = apiBase)
    .AddResilienceHandler("sse-connect", b =>
        b.AddTimeout(TimeSpan.FromSeconds(30)));

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
