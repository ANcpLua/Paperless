# PaperlessUI.Blazor Live Document Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Project rule override:** This repo's CLAUDE.md says **"Do not write tests."** This plan therefore has **no test tasks**. Each task is verified by the repo's Validation hierarchy instead: clean `./build.sh Compile`, running the real compose stack, and Playwright for the live-streaming behavior.

**Goal:** Replace the stock Blazor scaffold with a clean, vanilla-Blazor port of the `wwwroot/` demo SPA — drag-drop PDF upload, live OCR + AI-summary via **server-side `System.Net.ServerSentEvents.SseParser`**, list, search, delete — served same-origin behind nginx.

**Architecture:** Blazor Web App, Interactive Server. The Blazor *server* consumes the REST API's two SSE streams (`/api/v1/ocr-results`, `/api/v1/events/genai`) over the internal network with `SseParser`, then pushes UI updates to the browser over the SignalR circuit (`InvokeAsync(StateHasChanged)`). No browser `EventSource`, no JS interop for events, no CORS. nginx fronts both the Blazor app (`/`) and the API (`/api/`).

**Tech Stack:** .NET 10.0.300, `ANcpLua.NET.Sdk.Web` 3.4.41, Blazor Interactive Server, `System.Net.ServerSentEvents` 10.0.8 (out-of-band package), Bootstrap 5.3.2 + bootstrap-icons (CDN), nginx, Docker Compose.

**Branch:** `feat/blazor-document-pipeline` (spec committed at `6f56e6d`; `Paperless.slnx` Blazor line already staged in the working tree).

---

## File Structure

| File | Responsibility |
|---|---|
| `Directory.Packages.props` (modify) | CPM version pin for `System.Net.ServerSentEvents`. |
| `PaperlessUI.Blazor.csproj` (modify) | `PackageReference` to `System.Net.ServerSentEvents`. |
| `Models/Documents.cs` (create) | Transport records mirroring the REST contract (decoupled copy). |
| `Services/PaperlessApiClient.cs` (create) | Typed `HttpClient` — list / upload / search / delete. |
| `Services/DocumentEventStream.cs` (create) | Scoped per-circuit SSE consumer; raises `OnChanged`, exposes `Connected`. |
| `Program.cs` (modify) | DI: named + typed HttpClients, scoped event stream, `Paperless:ApiBaseUrl`. |
| `appsettings.json` (modify) | Default `Paperless:ApiBaseUrl`. |
| `Components/App.razor` (modify) | Bootstrap + icons via CDN (no local `lib/`). |
| `Components/DocumentCard.razor` (create) | One document card (status, OCR preview, summary, delete). |
| `Components/Pages/Home.razor` (rewrite) | The page: theme wrapper, upload, search, toasts, list, banner. |
| `Components/Layout/NavMenu.razor` (modify) | Trim Counter/Weather links. |
| `Components/Pages/Counter.razor`, `Weather.razor` (delete) | Template cruft. |
| `wwwroot/app.css` (modify) | A few app-specific rules. |
| `PaperlessUI.Blazor/Dockerfile` (create) | Multi-stage build; custom-SDK COPY order preserved. |
| `compose.yaml` (modify) | Add `paperless-blazor`; remove `wwwroot` mount; nginx `depends_on`. |
| `docker/nginx.conf` (modify) | `paperless-blazor` upstream; `/` + `/_blazor` WebSocket; drop static root. |

---

### Task 1: Pin `System.Net.ServerSentEvents` (CPM)

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `PaperlessUI.Blazor/PaperlessUI.Blazor.csproj`

- [ ] **Step 1: Add the central version pin**

In `Directory.Packages.props`, inside the existing `<ItemGroup>` of `<PackageVersion>` entries, add (keep the list alphabetical if it is):

```xml
<PackageVersion Include="System.Net.ServerSentEvents" Version="10.0.8" />
```

(`10.0.8` matches the repo's .NET 10.0.8 runtime family; it's the newest version already in the local NuGet cache.)

- [ ] **Step 2: Reference it from the Blazor project**

Replace the body of `PaperlessUI.Blazor/PaperlessUI.Blazor.csproj` with:

```xml
<Project Sdk="ANcpLua.NET.Sdk.Web">

  <PropertyGroup>
    <BlazorDisableThrowNavigationException>true</BlazorDisableThrowNavigationException>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.Net.ServerSentEvents" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Restore and confirm the package resolves**

Run: `dotnet restore PaperlessUI.Blazor/PaperlessUI.Blazor.csproj`
Expected: restore succeeds, no NU1102/NU1101 for `System.Net.ServerSentEvents`.

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props PaperlessUI.Blazor/PaperlessUI.Blazor.csproj
git commit -m "build: add System.Net.ServerSentEvents to PaperlessUI.Blazor"
```

---

### Task 2: Transport models

**Files:**
- Create: `PaperlessUI.Blazor/Models/Documents.cs`

- [ ] **Step 1: Create the models**

```csharp
namespace PaperlessUI.Blazor.Models;

/// <summary>Document metadata as returned by GET /api/v1/documents (items) and /{id}.</summary>
public sealed record DocumentDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
    public string? Content { get; init; }
    public string? Summary { get; init; }
    public DateTimeOffset? SummaryGeneratedAt { get; init; }
}

/// <summary>One hit from GET /api/v1/documents/search.</summary>
public sealed record DocumentSearchResultDto
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public string? Content { get; init; }
    public string? Summary { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Status { get; init; }
}

/// <summary>Cursor-paginated wrapper from GET /api/v1/documents.</summary>
public sealed record PaginatedDocumentsResponse
{
    public required List<DocumentDto> Items { get; init; }
    public Guid? NextCursor { get; init; }
    public bool HasMore { get; init; }
}

/// <summary>202 body from POST /api/v1/documents.</summary>
public sealed record CreateDocumentResponse
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build PaperlessUI.Blazor/PaperlessUI.Blazor.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add PaperlessUI.Blazor/Models/Documents.cs
git commit -m "feat(blazor): document transport models"
```

---

### Task 3: PaperlessApiClient (typed HttpClient)

**Files:**
- Create: `PaperlessUI.Blazor/Services/PaperlessApiClient.cs`

- [ ] **Step 1: Create the client**

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;
using PaperlessUI.Blazor.Models;

namespace PaperlessUI.Blazor.Services;

/// <summary>REST calls to PaperlessREST's document API. One instance per request (transient typed client).</summary>
public sealed class PaperlessApiClient(HttpClient http)
{
    public const long MaxUploadBytes = 50L * 1024 * 1024; // matches nginx client_max_body_size 50m

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DocumentDto>> GetDocumentsAsync(CancellationToken ct = default)
    {
        var page = await http.GetFromJsonAsync<PaginatedDocumentsResponse>("/api/v1/documents", Json, ct);
        return page?.Items ?? [];
    }

    public async Task<IReadOnlyList<DocumentDto>> SearchAsync(string query, int limit = 50, CancellationToken ct = default)
    {
        var url = $"/api/v1/documents/search?query={Uri.EscapeDataString(query)}&limit={limit}";
        var hits = await http.GetFromJsonAsync<List<DocumentSearchResultDto>>(url, Json, ct);
        return hits?.ConvertAll(static h => new DocumentDto
        {
            Id = h.Id,
            FileName = h.FileName,
            Status = h.Status,
            CreatedAt = h.CreatedAt,
            Content = h.Content,
            Summary = h.Summary
        }) ?? [];
    }

    public async Task<(CreateDocumentResponse? Doc, string? Error)> UploadAsync(IBrowserFile file, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        await using var stream = file.OpenReadStream(MaxUploadBytes, ct);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", file.Name);

        using var resp = await http.PostAsync("/api/v1/documents", content, ct);
        if (resp.IsSuccessStatusCode)
        {
            var created = await resp.Content.ReadFromJsonAsync<CreateDocumentResponse>(Json, ct);
            return (created, null);
        }

        var body = await resp.Content.ReadAsStringAsync(ct);
        return (null, $"{(int)resp.StatusCode} {resp.ReasonPhrase}: {body}");
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        using var resp = await http.DeleteAsync($"/api/v1/documents/{id}", ct);
        return resp.IsSuccessStatusCode;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build PaperlessUI.Blazor/PaperlessUI.Blazor.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add PaperlessUI.Blazor/Services/PaperlessApiClient.cs
git commit -m "feat(blazor): typed REST client for documents"
```

---

### Task 4: DocumentEventStream (server-side SSE consumer)

**Files:**
- Create: `PaperlessUI.Blazor/Services/DocumentEventStream.cs`

- [ ] **Step 1: Create the SSE consumer**

```csharp
using System.Net.ServerSentEvents;

namespace PaperlessUI.Blazor.Services;

/// <summary>
///     Consumes PaperlessREST's two SSE streams server-side with the .NET BCL <see cref="SseParser"/> —
///     the mirror of how SWEN3.Paperless.RabbitMq <em>produces</em> them via TypedResults.ServerSentEvents.
///     Scoped per Interactive Server circuit; raises <see cref="OnChanged"/> on every event so the page
///     can refetch + re-render. Reconnects with backoff on drop.
/// </summary>
public sealed class DocumentEventStream(IHttpClientFactory factory, ILogger<DocumentEventStream> logger)
    : IAsyncDisposable
{
    public event Action? OnChanged;

    /// <summary>True while at least the OCR stream is connected; drives the "disconnected" banner.</summary>
    public bool Connected { get; private set; }

    private CancellationTokenSource? _cts;

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        // Backoff intervals mirror the old wwwroot/app.js (5s OCR, 10s GenAI).
        _ = ConsumeAsync("/api/v1/ocr-results", TimeSpan.FromSeconds(5), isPrimary: true, _cts.Token);
        _ = ConsumeAsync("/api/v1/events/genai", TimeSpan.FromSeconds(10), isPrimary: false, _cts.Token);
    }

    private async Task ConsumeAsync(string path, TimeSpan retry, bool isPrimary, CancellationToken ct)
    {
        var client = factory.CreateClient("paperless");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var resp = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                if (isPrimary) { Connected = true; OnChanged?.Invoke(); }

                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                var parser = SseParser.Create(stream);
                await foreach (SseItem<string> item in parser.EnumerateAsync(ct))
                {
                    logger.LogInformation("SSE {Event} on {Path}", item.EventType, path);
                    OnChanged?.Invoke();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (isPrimary) { Connected = false; OnChanged?.Invoke(); }
                logger.LogWarning(ex, "SSE {Path} dropped; reconnecting in {Seconds}s", path, retry.TotalSeconds);
                try { await Task.Delay(retry, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        _cts.Dispose();
        _cts = null;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build PaperlessUI.Blazor/PaperlessUI.Blazor.csproj`
Expected: build succeeds (confirms `SseParser`/`SseItem<string>` resolve from the package).

- [ ] **Step 3: Commit**

```bash
git add PaperlessUI.Blazor/Services/DocumentEventStream.cs
git commit -m "feat(blazor): server-side SSE consumer via SseParser"
```

---

### Task 5: DI wiring + config

**Files:**
- Modify: `PaperlessUI.Blazor/Program.cs`
- Modify: `PaperlessUI.Blazor/appsettings.json`

- [ ] **Step 1: Set the default API base URL**

Replace `PaperlessUI.Blazor/appsettings.json` with:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Paperless": {
    "ApiBaseUrl": "http://localhost:8080"
  }
}
```

(In compose this is overridden by the env var `Paperless__ApiBaseUrl=http://paperless-rest:8080`.)

- [ ] **Step 2: Register clients + event stream**

Replace `PaperlessUI.Blazor/Program.cs` with:

```csharp
using PaperlessUI.Blazor.Components;
using PaperlessUI.Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBaseUrl = builder.Configuration["Paperless:ApiBaseUrl"] ?? "http://localhost:8080";
var apiBase = new Uri(apiBaseUrl);

// Named client used by the long-lived SSE consumer; typed client for short REST calls.
builder.Services.AddHttpClient("paperless", c => c.BaseAddress = apiBase);
builder.Services.AddHttpClient<PaperlessApiClient>(c => c.BaseAddress = apiBase);
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
```

- [ ] **Step 3: Build**

Run: `dotnet build PaperlessUI.Blazor/PaperlessUI.Blazor.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add PaperlessUI.Blazor/Program.cs PaperlessUI.Blazor/appsettings.json
git commit -m "feat(blazor): DI + config for API client and SSE stream"
```

---

### Task 6: Bootstrap via CDN (App.razor)

**Files:**
- Modify: `PaperlessUI.Blazor/Components/App.razor`

(There is no local `wwwroot/lib/bootstrap`, so the scaffold's `@Assets["lib/bootstrap/..."]` link 404s. Use the same CDN the old SPA used.)

- [ ] **Step 1: Swap the stylesheet links**

In `Components/App.razor`, replace this line:

```razor
    <link rel="stylesheet" href="@Assets["lib/bootstrap/dist/css/bootstrap.min.css"]" />
```

with:

```razor
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap@5.3.2/dist/css/bootstrap.min.css" />
    <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.1/font/bootstrap-icons.css" />
```

Leave the `app.css` and `PaperlessUI.Blazor.styles.css` links and everything else unchanged.

- [ ] **Step 2: Commit**

```bash
git add PaperlessUI.Blazor/Components/App.razor
git commit -m "feat(blazor): load Bootstrap + icons via CDN"
```

---

### Task 7: DocumentCard component

**Files:**
- Create: `PaperlessUI.Blazor/Components/DocumentCard.razor`

- [ ] **Step 1: Create the component**

```razor
@using PaperlessUI.Blazor.Models

<div class="card mb-3 shadow-sm">
    <div class="card-header d-flex justify-content-between align-items-center">
        <div class="d-flex gap-2 align-items-center flex-grow-1 min-w-0">
            <i class="bi bi-file-pdf text-danger"></i>
            <h5 class="card-title mb-0 text-truncate">@Doc.FileName</h5>
        </div>
        <button class="btn btn-sm btn-outline-danger" title="Delete" @onclick="OnDeleteClicked">
            <i class="bi bi-trash"></i>
        </button>
    </div>
    <div class="card-body">
        <p class="text-muted small mb-2">
            Uploaded: @Format(Doc.CreatedAt)
            @if (Doc.ProcessedAt is not null)
            {
                <br />

                @($"Processed: {Format(Doc.ProcessedAt)}")
            }
        </p>

        <span class="badge bg-@StatusColor">
            @if (Doc.Status == "Pending")
            {
                <span class="spinner-border spinner-border-sm me-1"></span>
            }
            @Doc.Status
        </span>

        @if (Doc.Status == "Completed" && !string.IsNullOrEmpty(Doc.Content))
        {
            <div class="mt-3">
                <h6>OCR Text Preview</h6>
                <div class="bg-body-tertiary p-2 rounded small font-monospace overflow-auto" style="max-height: 200px;">
                    @Preview(Doc.Content)
                </div>
            </div>
        }

        @if (!string.IsNullOrEmpty(Doc.Summary))
        {
            <div class="mt-3">
                <h6 class="mb-2">AI Summary</h6>
                <div class="bg-body-tertiary p-2 rounded small overflow-auto" style="max-height: 200px;">
                    @Doc.Summary
                </div>
            </div>
        }
    </div>
</div>

@code {
    [Parameter, EditorRequired] public required DocumentDto Doc { get; set; }
    [Parameter] public EventCallback<Guid> OnDelete { get; set; }

    private string StatusColor => Doc.Status switch
    {
        "Pending" => "warning",
        "Completed" => "success",
        _ => "danger"
    };

    private Task OnDeleteClicked() => OnDelete.InvokeAsync(Doc.Id);

    private static string Format(DateTimeOffset? value) =>
        value?.LocalDateTime.ToString("g") ?? "Unknown";

    private static string Preview(string content) =>
        content.Length > 500 ? content[..500] + "…" : content;
}
```

- [ ] **Step 2: Build**

Run: `dotnet build PaperlessUI.Blazor/PaperlessUI.Blazor.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add PaperlessUI.Blazor/Components/DocumentCard.razor
git commit -m "feat(blazor): document card component"
```

---

### Task 8: Home page (upload + search + live list + toasts)

**Files:**
- Rewrite: `PaperlessUI.Blazor/Components/Pages/Home.razor`

- [ ] **Step 1: Rewrite the page**

```razor
@page "/"
@rendermode InteractiveServer
@using Microsoft.AspNetCore.Components.Forms
@using PaperlessUI.Blazor.Models
@using PaperlessUI.Blazor.Services
@inject PaperlessApiClient Api
@inject DocumentEventStream Events
@implements IAsyncDisposable

<PageTitle>Paperless OCR System</PageTitle>

<div data-bs-theme="@_theme" class="container-xl py-4">
    <button class="btn btn-outline-secondary position-fixed top-0 end-0 m-3" style="z-index:1050"
            title="Toggle light / dark" @onclick="ToggleTheme">
        <i class="bi @(_theme == "dark" ? "bi-sun" : "bi-moon")"></i>
    </button>

    @if (!Events.Connected)
    {
        <div class="alert alert-danger position-fixed top-0 end-0 m-3" style="margin-top:60px!important">
            <i class="bi bi-wifi-off"></i> Live Updates Disconnected
        </div>
    }

    <h1 class="text-center mb-4"><i class="bi bi-file-earmark-text"></i> Paperless OCR System</h1>

    <div class="border border-2 border-primary rounded p-4 text-center bg-body-tertiary mb-4 position-relative">
        <i class="bi bi-cloud-upload fs-1 text-primary"></i>
        <h4 class="mt-3 text-body">Upload PDF Documents</h4>
        <p class="text-body-secondary">Drag and drop PDF files here or click to browse</p>
        <InputFile OnChange="OnFilesSelected" multiple accept=".pdf"
                   class="position-absolute top-0 start-0 w-100 h-100 opacity-0"
                   style="cursor:pointer" />
    </div>

    <div class="input-group mb-4">
        <span class="input-group-text"><i class="bi bi-search"></i></span>
        <input class="form-control" placeholder="Search documents…" @bind="_searchText"
               @bind:event="oninput" @onkeydown="OnSearchKey" />
        <button class="btn btn-outline-primary" @onclick="RunSearch">Search</button>
        <button class="btn btn-outline-secondary" @onclick="ClearSearch">Clear</button>
    </div>

    <div class="alert alert-info mb-4">
        <i class="bi bi-info-circle me-2"></i>
        <strong>Batch Processing:</strong> Monitor batch jobs via the
        <a class="alert-link" href="/hangfire">Hangfire Dashboard</a>
    </div>

    <div class="d-flex justify-content-between align-items-center mb-3">
        <h3>Documents</h3>
        <button class="btn btn-sm btn-outline-primary" @onclick="Refresh">
            <i class="bi bi-arrow-clockwise"></i> Refresh
        </button>
    </div>

    @if (_documents.Count == 0)
    {
        <div class="text-center text-muted py-5">
            <i class="bi bi-inbox fs-1"></i>
            <p>@(string.IsNullOrEmpty(_activeSearch) ? "No documents uploaded yet" : "No documents match your search")</p>
        </div>
    }
    else
    {
        @foreach (var doc in _documents)
        {
            <DocumentCard Doc="doc" OnDelete="Delete" />
        }
    }
</div>

<div class="position-fixed bottom-0 end-0 m-3" style="z-index:1050; max-width:350px">
    @foreach (var toast in _toasts)
    {
        <div class="alert alert-@toast.Level alert-dismissible show mb-2">
            @toast.Message
            <button class="btn-close" @onclick="() => _toasts.Remove(toast)"></button>
        </div>
    }
</div>

@code {
    private readonly List<DocumentDto> _documents = [];
    private readonly List<Toast> _toasts = [];
    private string _searchText = "";
    private string _activeSearch = "";
    private string _theme = "light";

    private sealed record Toast(string Message, string Level);

    protected override async Task OnInitializedAsync()
    {
        Events.OnChanged += HandleChanged;
        Events.Start();
        await ReloadAsync();
    }

    private void ToggleTheme() => _theme = _theme == "dark" ? "light" : "dark";

    // SSE callback — runs off the render thread, so marshal onto the circuit.
    // Signature matches DocumentEventStream's `event EventHandler? OnChanged` (CA1003-clean).
    private void HandleChanged(object? sender, EventArgs e) => _ = InvokeAsync(async () =>
    {
        await ReloadAsync();
        StateHasChanged();
    });

    private async Task ReloadAsync()
    {
        try
        {
            var docs = string.IsNullOrEmpty(_activeSearch)
                ? await Api.GetDocumentsAsync()
                : await Api.SearchAsync(_activeSearch);
            _documents.Clear();
            _documents.AddRange(docs);
        }
        catch (Exception ex)
        {
            Notify($"Failed to load documents: {ex.Message}", "danger");
        }
    }

    private async Task Refresh()
    {
        await ReloadAsync();
        StateHasChanged();
    }

    private async Task OnFilesSelected(InputFileChangeEventArgs e)
    {
        foreach (var file in e.GetMultipleFiles(maximumFileCount: 20))
        {
            if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            {
                Notify($"{file.Name} is not a PDF", "warning");
                continue;
            }

            var (doc, error) = await Api.UploadAsync(file);
            if (error is not null)
            {
                Notify($"Failed: {error}", "danger");
                continue;
            }

            if (doc is not null && !_documents.Exists(d => d.Id == doc.Id))
            {
                _documents.Insert(0, new DocumentDto
                {
                    Id = doc.Id, FileName = doc.FileName, Status = doc.Status, CreatedAt = doc.CreatedAt
                });
            }

            Notify($"Uploaded {file.Name}", "success");
        }

        StateHasChanged();
    }

    private async Task RunSearch()
    {
        _activeSearch = _searchText.Trim();
        await ReloadAsync();
        StateHasChanged();
    }

    private async Task ClearSearch()
    {
        _searchText = "";
        _activeSearch = "";
        await ReloadAsync();
        StateHasChanged();
    }

    private async Task OnSearchKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await RunSearch();
    }

    private async Task Delete(Guid id)
    {
        if (await Api.DeleteAsync(id))
        {
            _documents.RemoveAll(d => d.Id == id);
            Notify("Document deleted", "success");
        }
        else
        {
            Notify("Delete failed", "danger");
        }
        StateHasChanged();
    }

    private void Notify(string message, string level)
    {
        var toast = new Toast(message, level);
        _toasts.Add(toast);
        _ = Task.Delay(TimeSpan.FromSeconds(4)).ContinueWith(_ =>
            InvokeAsync(() => { _toasts.Remove(toast); StateHasChanged(); }));
    }

    public ValueTask DisposeAsync()
    {
        Events.OnChanged -= HandleChanged;
        return ValueTask.CompletedTask; // the scoped DocumentEventStream is disposed with the circuit
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build PaperlessUI.Blazor/PaperlessUI.Blazor.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add PaperlessUI.Blazor/Components/Pages/Home.razor
git commit -m "feat(blazor): document pipeline page with live SSE updates"
```

---

### Task 9: Scaffold cleanup

**Files:**
- Delete: `PaperlessUI.Blazor/Components/Pages/Counter.razor`
- Delete: `PaperlessUI.Blazor/Components/Pages/Weather.razor`
- Modify: `PaperlessUI.Blazor/Components/Layout/NavMenu.razor`
- Modify: `PaperlessUI.Blazor/wwwroot/app.css`

- [ ] **Step 1: Delete template pages**

```bash
git rm PaperlessUI.Blazor/Components/Pages/Counter.razor PaperlessUI.Blazor/Components/Pages/Weather.razor
```

- [ ] **Step 2: Trim NavMenu**

Replace the `<nav class="nav flex-column">…</nav>` block in `Components/Layout/NavMenu.razor` so only Home remains:

```razor
    <nav class="nav flex-column">
        <div class="nav-item px-3">
            <NavLink class="nav-link" href="" Match="NavLinkMatch.All">
                <span class="bi bi-house-door-fill-nav-menu" aria-hidden="true"></span> Home
            </NavLink>
        </div>
    </nav>
```

- [ ] **Step 3: Add app-specific CSS**

Append to `PaperlessUI.Blazor/wwwroot/app.css`:

```css
.min-w-0 { min-width: 0; }
```

- [ ] **Step 4: Build to confirm no dangling references**

Run: `dotnet build PaperlessUI.Blazor/PaperlessUI.Blazor.csproj`
Expected: build succeeds (no references to the deleted `Counter`/`Weather` routes remain).

- [ ] **Step 5: Commit**

```bash
git add PaperlessUI.Blazor/Components/Layout/NavMenu.razor PaperlessUI.Blazor/wwwroot/app.css
git commit -m "chore(blazor): drop template Counter/Weather pages, trim nav"
```

---

### Task 10: Dockerfile

**Files:**
- Create: `PaperlessUI.Blazor/Dockerfile`

- [ ] **Step 1: Create the Dockerfile**

(Modeled on `PaperlessREST/Dockerfile`. The custom-SDK COPY order is mandatory — see the repo gotcha. No `Paperless.Contracts` reference: the Blazor app carries its own DTOs.)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
RUN apt-get update && apt-get install -y tzdata && \
    ln -sf /usr/share/zoneinfo/Europe/Vienna /etc/localtime && \
    echo "Europe/Vienna" > /etc/timezone && \
    apt-get clean && rm -rf /var/lib/apt/lists/*
ENV TZ=Europe/Vienna

USER app
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ENV DOCKER_BUILD=true
WORKDIR /src

# Root MSBuild + NuGet config must be present BEFORE restore so the custom
# ANcpLua.NET.Sdk.Web resolver and CPM versions resolve.
COPY ["global.json", "nuget.config", "Directory.Packages.props", "Version.props", "./"]

COPY ["PaperlessUI.Blazor/PaperlessUI.Blazor.csproj", "PaperlessUI.Blazor/"]
RUN dotnet restore "PaperlessUI.Blazor/PaperlessUI.Blazor.csproj"

COPY ["PaperlessUI.Blazor/", "PaperlessUI.Blazor/"]
WORKDIR "/src/PaperlessUI.Blazor"
RUN dotnet build "PaperlessUI.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/build -p:DOCKER_BUILD=true

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaperlessUI.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false -p:DOCKER_BUILD=true

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENTRYPOINT ["dotnet", "PaperlessUI.Blazor.dll"]
```

- [ ] **Step 2: Build the image to verify the COPY order**

Run: `docker build -f PaperlessUI.Blazor/Dockerfile -t paperless-blazor:dev .`
Expected: image builds; no "Could not resolve SDK ANcpLua.NET.Sdk.Web" error.

- [ ] **Step 3: Commit**

```bash
git add PaperlessUI.Blazor/Dockerfile
git commit -m "build(blazor): Dockerfile preserving custom-SDK restore order"
```

---

### Task 11: compose.yaml — add the Blazor service

**Files:**
- Modify: `compose.yaml`

- [ ] **Step 1: Add the `paperless-blazor` service**

Under `# APPLICATION SERVICES`, after the `paperless-rest` service block, add:

```yaml
    paperless-blazor:
        container_name: paperless-blazor
        image: ${PAPERLESS_BLAZOR_IMAGE:-paperless-blazor:latest}
        build:
            context: .
            dockerfile: PaperlessUI.Blazor/Dockerfile
        depends_on:
            paperless-rest: { condition: service_started }
            aspire-dashboard: { condition: service_started }
        environment:
            ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}
            ASPNETCORE_URLS: http://+:8080
            Paperless__ApiBaseUrl: http://paperless-rest:8080
            # OTel env mirrors the sibling services for consistency (no OTel SDK is
            # wired in any app yet, so these are currently inert — kept aligned).
            OTEL_EXPORTER_OTLP_ENDPOINT: http://aspire-dashboard:18889
            OTEL_SERVICE_NAME: paperless-blazor
            TZ: ${TZ}
```

- [ ] **Step 2: Point nginx at the Blazor app and drop the static mount**

In the `nginx:` service, change `volumes:` to mount only the config (remove the `wwwroot` html mount), and add `paperless-blazor` to `depends_on`:

```yaml
    nginx:
        container_name: paperless-nginx
        image: ${NGINX_IMAGE}
        ports:
            - "80:80"
        volumes:
            - ./docker/nginx.conf:/etc/nginx/nginx.conf:ro
        depends_on:
            paperless-rest: { condition: service_started }
            paperless-blazor: { condition: service_started }
        environment:
            TZ: ${TZ}
        healthcheck:
            test: [ "CMD", "nginx", "-t" ]
            interval: 5s
            timeout: 3s
            retries: 10
```

- [ ] **Step 3: Validate compose syntax**

Run: `docker compose config >/dev/null && echo OK`
Expected: `OK` (no YAML/interpolation errors).

- [ ] **Step 4: Commit**

```bash
git add compose.yaml
git commit -m "build(compose): add paperless-blazor service, serve it via nginx"
```

---

### Task 12: nginx.conf — route `/` to Blazor with WebSocket

**Files:**
- Modify: `docker/nginx.conf`

- [ ] **Step 1: Add the Blazor upstream**

After the existing `upstream paperless-api { … }` block, add:

```nginx
    upstream paperless-blazor {
        server paperless-blazor:8080;
    }
```

- [ ] **Step 2: Replace the static-root config with a proxy to Blazor**

Remove these static-file lines from the `server { … }` block:

```nginx
        # Static files (index.html, app.js, etc.)
        root /usr/share/nginx/html;
        index index.html;

        # Main app
        location = / {
            try_files /index.html =404;
        }

        # Static assets
        location ~* \.(js|css|png|jpg|jpeg|gif|ico|svg|woff|woff2|ttf|eot)$ {
            expires 1h;
            add_header Cache-Control "public, immutable";
        }
```

and replace them with a single proxy location that handles the Blazor SignalR WebSocket (`/_blazor`) and all app assets:

```nginx
        # Blazor app (Interactive Server) — root + SignalR circuit + static assets
        location / {
            proxy_pass http://paperless-blazor;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            proxy_set_header X-Forwarded-Proto $scheme;
            proxy_buffering off;
            proxy_read_timeout 100s;
        }
```

Leave the `/api/`, SSE, `/hangfire`, `/docs`, `/openapi`, and `/health` location blocks unchanged. (The two SSE blocks are now only hit server-to-server, bypassing nginx, but are harmless to keep.)

- [ ] **Step 3: Validate nginx config syntax**

Run: `docker run --rm -v "$PWD/docker/nginx.conf:/etc/nginx/nginx.conf:ro" nginx:alpine nginx -t`
Expected: `syntax is ok` / `test is successful`.

- [ ] **Step 4: Commit**

```bash
git add docker/nginx.conf
git commit -m "build(nginx): proxy / to paperless-blazor with WebSocket upgrade"
```

---

### Task 13: Solution build + commit slnx

**Files:**
- Already staged: `Paperless.slnx` (Blazor project line)

- [ ] **Step 1: Full solution build via NUKE**

Run: `./build.sh Compile`
Expected: build succeeds with `PaperlessUI.Blazor` included (it's now in `Paperless.slnx`). Zero errors.

- [ ] **Step 2: Commit the slnx wiring**

```bash
git add Paperless.slnx
git commit -m "build: add PaperlessUI.Blazor to Paperless.slnx"
```

---

### Task 14: End-to-end validation (run the real stack + Playwright)

**Files:** none (validation only)

- [ ] **Step 1: Bring up the full stack**

Run: `docker compose up -d --build`
Then wait for health: `docker compose ps` until `paperless-rest`, `paperless-blazor`, `nginx` are up.

- [ ] **Step 2: Smoke-test the UI loads through nginx**

Run: `curl -fsS http://localhost/ | grep -i "Paperless OCR System" && echo PAGE_OK`
Expected: `PAGE_OK` (Blazor page served at `/` via nginx).

- [ ] **Step 3: Playwright — upload + live streaming**

Drive a browser through the live flow (use the Playwright MCP tools):
1. Navigate to `http://localhost/`.
2. Set the file input to a sample PDF: `PaperlessREST/sample-data/input/*.pdf`.
3. Assert a card appears with status `Pending`.
4. Wait (poll up to ~60s) for the card's badge to become `Completed` **without reloading the page** — proves the OCR SSE → circuit push works.
5. Wait for the "AI Summary" section to render — proves the GenAI SSE path.
6. Screenshot the streamed result.

Expected: status transitions Pending → Completed live; summary appears live.

- [ ] **Step 4: Tear down**

Run: `docker compose down`

- [ ] **Step 5: Push the branch and open a PR**

```bash
git push -u origin feat/blazor-document-pipeline
gh pr create --base main --head feat/blazor-document-pipeline \
  --title "PaperlessUI.Blazor: live document pipeline (vanilla Blazor port of wwwroot SPA)" \
  --body "Replaces the Blazor scaffold with a clean port of the wwwroot demo SPA: drag-drop PDF upload, live OCR + AI-summary via server-side System.Net.ServerSentEvents.SseParser, list, search, delete. Served same-origin behind nginx. See docs/superpowers/specs/2026-06-04-blazor-document-pipeline-design.md."
```

- [ ] **Step 6: Watch CI**

Run: `gh pr checks --watch`
Expected: `Build & Test (backend)` green (now compiles Blazor via the slnx).

---

## Self-Review

**Spec coverage:**
- Upload / live OCR+summary / list / search / delete / disconnected banner / theme toggle / refresh / Hangfire link → Tasks 7–8. ✓
- Server-side `SseParser` consumer → Task 4. ✓
- Replace wwwroot SPA at `/` → Tasks 11–12. ✓
- Keep aspire-dashboard; Blazor exports matching OTel env → Task 11 (with the inert-OTel finding noted). ✓
- Dockerfile w/ custom-SDK COPY order → Task 10. ✓
- slnx + CI → Task 13 (slnx already staged; CI covered because Blazor is now in the solution NUKE builds). ✓
- Validation = build + run + Playwright (no tests) → Tasks 13–14. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full content. ✓

**Type consistency:** `DocumentDto`/`CreateDocumentResponse`/`PaginatedDocumentsResponse`/`DocumentSearchResultDto` defined in Task 2 and used consistently in Tasks 3, 7, 8. `DocumentEventStream.OnChanged`/`Connected`/`Start()` defined in Task 4 and used in Task 8. `PaperlessApiClient` method names (`GetDocumentsAsync`/`SearchAsync`/`UploadAsync`/`DeleteAsync`) consistent across Tasks 3 and 8. ✓

**Open items deferred (by design, in spec's Out-of-scope):** list pagination UI, theme persistence across reloads, fixing `wwwroot/app.js`'s paginated-list bug, targeted per-document SSE updates.
