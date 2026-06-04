# PaperlessUI.Blazor — live document pipeline (design)

**Date:** 2026-06-04
**Status:** Draft for review
**Author:** Claude + ancplua

## Goal

Turn the `PaperlessUI.Blazor` scaffold (currently the stock Home/Counter/Weather
template) into a clean, vanilla-Blazor port of the production demo UI — the two files
`PaperlessREST/wwwroot/index.html` + `wwwroot/app.js`. Same feature set, but driven from
C# instead of hand-rolled JS, and using modern .NET as the "interesting" part.

The signature move: consume the backend's live SSE streams **server-side** with
`System.Net.ServerSentEvents.SseParser` — the exact mirror of how the
`SWEN3.Paperless.RabbitMq` library *produces* them (`TypedResults.ServerSentEvents`).
Producer and consumer both use the .NET 10 BCL SSE API, opposite directions.

Modeled structurally on the Microsoft Agent Framework `AgentWebChat.Web` sample:
single rich page, Interactive Server, streaming-driven, no third-party component library.

## Scope (locked decisions)

- **SSE consumption:** server-side `SseParser` in the Blazor circuit. No browser
  `EventSource`, no JS interop for events.
- **UI surface:** the Blazor app **replaces** the vanilla `wwwroot` SPA as what nginx
  serves at `/`. The old `index.html`/`app.js` stay in the repo as reference but are no
  longer mounted by nginx.
- **Observability:** `aspire-dashboard` stays. It is wired and live (both app services
  export OTel to it). The new Blazor service will also export OTel to it.
- **Render mode:** Interactive Server (unchanged from the scaffold).
- **No component library:** vanilla HTML + the existing Bootstrap CSS, matching today's SPA.

## Feature set (parity with `app.js`)

1. Drag-and-drop / file-picker **PDF upload** (multipart `file`).
2. **Live status:** a card flips `Pending → Completed/Failed` when OCR finishes, then the
   **AI summary** appears when GenAI finishes — both pushed live via SSE.
3. **Document list** of cards (filename, upload/processed time, status badge + spinner,
   OCR text preview, AI summary, delete).
4. **Full-text search** + clear.
5. **Delete** a document.
6. **"Live updates disconnected"** banner when the SSE loop drops (with auto-reconnect).
7. Light/dark theme toggle (Bootstrap `data-bs-theme`), refresh button, Hangfire link.

## Backend contract (authoritative — from `DocumentEndpoints.cs`)

| Verb | Route | Returns |
|---|---|---|
| GET | `/api/v1/documents` | `PaginatedDocumentsResponse { items[], hasMore, nextCursor }` |
| GET | `/api/v1/documents/search?query=&limit=` | `DocumentSearchResultDto[]` |
| GET | `/api/v1/documents/{id}` | `DocumentDto` |
| GET | `/api/v1/documents/{id}/summary` | `SummaryDto { summary }` |
| POST | `/api/v1/documents` (multipart `file`) | `202` `CreateDocumentResponse` |
| DELETE | `/api/v1/documents/{id}` | `204` |

SSE (produced by the library via `MapSse<T>`):

| Stream | Events | Data |
|---|---|---|
| `/api/v1/ocr-results` | `ocr-completed`, `ocr-failed` | `OcrEvent` (jobId, documentId, …) |
| `/api/v1/events/genai` | `genai-completed`, `genai-failed` | `GenAIEvent` (documentId, summary, errorMessage) |

> **Finding / rot to confirm:** `wwwroot/app.js` does `const docs = await response.json();
> docs.forEach(...)` against `GET /api/v1/documents`, but that endpoint now returns the
> **paginated object** `{ items, hasMore, nextCursor }` (added in #42), not a bare array.
> The current SPA's list fetch is therefore likely broken against `main`. The Blazor port
> will use the **current** contract and read `.items`. Worth fixing `app.js` separately,
> but out of scope here.

## Architecture

```
Browser ──HTTP/WS──▶ nginx :80 ─┬─ "/"          ▶ paperless-blazor:8080   (Interactive Server circuit)
                                ├─ "/_blazor"    ▶ paperless-blazor:8080   (SignalR WebSocket)
                                ├─ "/api/"       ▶ paperless-rest:8080
                                └─ /hangfire,/docs,/openapi,/health ▶ paperless-rest:8080

paperless-blazor ──server-to-server (internal net, NOT via nginx)──▶ paperless-rest:8080
    • REST:  list / upload / search / delete
    • SSE :  SseParser over /api/v1/ocr-results + /api/v1/events/genai
```

Because SSE is consumed server-side (Blazor server → REST, internal network), there is
**no browser SSE connection and no CORS** to configure. The only browser↔server channel
is the Blazor SignalR circuit. nginx's existing SSE `location` blocks become unused by
the UI (kept for now; optional cleanup later).

## Components (each small, single-purpose)

- **`Components/Pages/Home.razor`** — the page. Upload zone, search bar, document grid.
  Subscribes to `DocumentEventStream`; on any event → refetch list → `InvokeAsync(StateHasChanged)`.
  Implements `IAsyncDisposable` to tear down subscriptions.
- **`Components/DocumentCard.razor`** — one document. Razor version of `documentCardTemplate`:
  status badge + spinner, OCR preview (first 500 chars), AI summary section, delete button.
- **`Services/PaperlessApiClient.cs`** — typed `HttpClient`. Methods: `GetDocumentsAsync`,
  `UploadAsync(IBrowserFile)`, `SearchAsync(query, limit)`, `DeleteAsync(id)`. Uses
  `JsonSerializerDefaults.Web` (camelCase). Base address from config.
- **`Services/DocumentEventStream.cs`** — the SSE consumer. Two long-running loops (OCR +
  GenAI), each: `HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)` →
  `SseParser.Create(stream)` → `await foreach (SseItem<string> item in parser.EnumerateAsync(ct))`.
  Raises a C# `event` (e.g. `OnDocumentChanged`). Reconnect with backoff (5s OCR / 10s GenAI,
  matching `app.js`) on stream end/error. Exposes a `Connected` flag for the disconnected banner.
- **`Models/DocumentDto.cs`** (+ search/summary/event DTOs) — mirror the REST contract.

> Threading note (Interactive Server): SSE callbacks run off the render thread. All UI
> mutation must go through `InvokeAsync(StateHasChanged)`. The event-stream loops are tied
> to the circuit and cancelled on component dispose.

## Data flow (upload → live)

1. User drops a PDF → `PaperlessApiClient.UploadAsync` → `POST /api/v1/documents` → `202`.
2. Card appears immediately as `Pending` (optimistic, from the upload response).
3. OCR worker finishes → REST publishes `ocr-completed` → Blazor server's `SseParser` yields
   it → `OnDocumentChanged` → page refetches list → card flips to `Completed`, OCR preview shows.
4. GenAI finishes → `genai-completed` → refetch → AI summary section fills in.
5. Failures (`ocr-failed` / `genai-failed`) → refetch (status reflects `Failed`) + toast.

(Refetch-on-event matches `app.js`. Targeted single-document updates by `documentId` from the
event payload are a possible later optimization, not in v1.)

## Error handling

- **Upload:** client checks `.pdf`; server returns `400` (validation), `503`
  (ServiceUnavailable + Retry-After), or `500`. Each surfaces a Bootstrap toast.
- **SSE drop:** loop catches, sets `Connected=false` (banner shows), waits backoff, reconnects.
- **List/search/delete failure:** toast; list left intact.
- **Circuit drop:** the scaffold's `ReconnectModal` already handles SignalR reconnection.

## Infra changes

### New: `PaperlessUI.Blazor/Dockerfile`
Multi-stage, modeled on `PaperlessREST/Dockerfile`. **Must** COPY `global.json`,
`nuget.config`, `Directory.Packages.props`, `Version.props` into the build context **before**
`dotnet restore` — the custom `ANcpLua.NET.Sdk.Web` SDK resolver fails otherwise (documented
repo gotcha). Final image runs `ASPNETCORE_URLS=http://+:8080`.

### `compose.yaml`
Add service `paperless-blazor`:
- `build`: context `.`, dockerfile `PaperlessUI.Blazor/Dockerfile`.
- `environment`: `Paperless__ApiBaseUrl=http://paperless-rest:8080`,
  `ASPNETCORE_URLS=http://+:8080`, `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889`,
  `OTEL_SERVICE_NAME=paperless-blazor`, `TZ`.
- `depends_on`: `paperless-rest` (started), `aspire-dashboard` (started).
- `nginx.depends_on` gains `paperless-blazor`.
- Remove the `./PaperlessREST/wwwroot:/usr/share/nginx/html:ro` mount (Blazor now serves the UI).

### `docker/nginx.conf`
- Add `upstream paperless-blazor { server paperless-blazor:8080; }`.
- `location / { proxy_pass http://paperless-blazor; }` with `Upgrade`/`Connection "upgrade"`,
  `proxy_http_version 1.1` (needed for the Blazor SignalR WebSocket at `/_blazor`).
- Drop the static-root config (`root`, `index`, `try_files /index.html`, the static-asset
  `location ~* \.(js|css|...)`) — Blazor serves its own assets through `/`.
- Keep `/api/`, `/hangfire`, `/docs`, `/openapi`, `/health` → `paperless-api` as-is.
- SSE `location` blocks: leave for now (unused by UI; harmless).

### `PaperlessUI.Blazor/Program.cs`
- Register `PaperlessApiClient` + `DocumentEventStream` as typed `HttpClient`s
  (`builder.Services.AddHttpClient<…>(c => c.BaseAddress = config["Paperless:ApiBaseUrl"])`).
- Add OpenTelemetry OTLP export (match the two existing services) so traces land in the dashboard.
- Keep `AddRazorComponents().AddInteractiveServerComponents()` + `MapRazorComponents<App>()`.

### Scaffold cleanup
- Delete template pages `Counter.razor`, `Weather.razor`; repurpose `Home.razor`.
- Trim `NavMenu.razor` (remove Counter/Weather links).
- Keep `Error.razor`, `NotFound.razor`, `ReconnectModal.razor`, `App.razor`.

### `Paperless.slnx` + CI
- Add `PaperlessUI.Blazor.csproj` to `Paperless.slnx` (flat — no `<Folder>` wrapper, per the
  NUKE 10 slnx gotcha). NUKE `Compile` then builds it as part of the solution.
- `.github/workflows/ci.yml`: the backend job's `Compile` now covers Blazor (it's in the slnx).
  No separate UI job needed (unlike the pnpm React/Angular jobs). This satisfies the repo
  guide's "add Blazor back to slnx + ci.yml when a page is implemented."

## Validation (per global rules, in priority order)

1. **Playwright e2e:** `docker compose up` → open `http://localhost` → upload a sample PDF
   from `PaperlessREST/sample-data/input` → assert the card flips Pending→Completed and the
   AI summary appears (live, no manual refresh). Screenshot the streamed result.
2. **Run the real artifact:** the compose stack end to end (above).
3. **Clean build floor:** `./build.sh Compile` zero errors/warnings with Blazor in the slnx.

No tests written (global rule: do not write tests).

## Out of scope (v1)

- List pagination UI ("load more" / cursor) — fetch first page only; note the `hasMore` flag.
- Auth, document detail route, batch-job UI (Hangfire link remains).
- Fixing `wwwroot/app.js`'s paginated-list bug (flagged above; separate task).
- Targeted per-document SSE updates (refetch-on-event is v1).

## Git / workspace note

Work proceeds on a dedicated feature branch (`feat/blazor-document-pipeline`). The working
tree had ~20 unrelated pre-existing modified files + 1 untracked test file at session start;
those are **not** touched by this work — only files created/changed for this feature are staged.
