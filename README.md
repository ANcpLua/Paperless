<div align="center">

# Paperless

**Document management with OCR, AI summarization, and full-text search.**
.NET 10 backend · Blazor demo UI (behind nginx) + React/Angular frontends · NUKE + xUnit v3 + Testcontainers.

<a href="https://github.com/ANcpLua/Paperless/actions/workflows/ci.yml">
  <img src="https://github.com/ANcpLua/Paperless/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI">
</a>
<a href="https://codecov.io/gh/ANcpLua/Paperless">
  <img src="https://codecov.io/gh/ANcpLua/Paperless/graph/badge.svg?branch=main" alt="codecov">
</a>

</div>

---

## Layout

```
Paperless.slnx                          # MSBuild slnx (modern format)
├── PaperlessREST/                      # ASP.NET Core API (REST + SSE)
├── PaperlessServices/                  # Background worker (OCR + GenAI)
├── PaperlessREST.Tests/                # xUnit v3 (unit + integration via Testcontainers)
├── PaperlessServices.Tests/            # xUnit v3 (unit + integration)
├── Paperless.TestSupport/              # Shared test lib (ContainerFixtureBase + builders + TestPdf + AsyncCleanup)
├── PaperlessREST/wwwroot/              # Original vanilla Bootstrap SPA (served via UseStaticFiles); Blazor took over nginx /
├── PaperlessUI.Blazor/                 # Blazor Web App (Interactive Server) — production demo UI behind nginx /; in slnx, built by CI
│   └── PaperlessUI.Blazor.csproj
├── PaperlessUI.React/                  # Frontend variant — Vite 8 + React 19 + TypeScript (canonical)
│   └── PaperlessUI.React.esproj
├── PaperlessUI.Angular/                # Frontend variant — Angular 21 + pnpm (parallel impl)
│   └── PaperlessUI.Angular.esproj
├── Pipeline/                           # NUKE build (`./build.sh <Target>`)
├── docker/                             # nginx config (single file: docker/nginx.conf)
├── PaperlessREST/sample-data/          # XML batch fixtures (input/archive/error), mounted by compose
└── compose.yaml                        # Local stack (postgres, minio, rabbitmq, elastic)
```

The production demo UI is **PaperlessUI.Blazor** (Interactive Server), which nginx serves at `/` in `compose.yaml` — a vanilla-Blazor port of the original `PaperlessREST/wwwroot/` SPA that it replaced at the root. React and Angular are parallel SDK-stack implementations consuming the same `/api/*` surface (built/validated in CI, not deployed in compose). The `wwwroot/` SPA still ships via `UseStaticFiles`.

Contributor & agent conventions live in [`AGENTS.md`](AGENTS.md) (`CLAUDE.md` is a symlink to it).

## Quick start

```bash
docker compose up -d                    # postgres, rabbitmq, minio, elasticsearch
./build.sh Compile                      # NUKE: builds the slnx end-to-end
./build.sh UnitTests                    # MTP v2 + xUnit v3
./build.sh IntegrationTests             # Testcontainers
./build.sh Coverage                     # Cobertura via MTP CodeCoverage
./build.sh ReportCoverage --coverage-min-line 0 --coverage-min-branch 0 --coverage-format markdown --coverage-exclude-generated-param true
```

`./build.sh` is the sole build entry point (Linux/macOS). The Windows NUKE bootstrappers (`build.cmd`/`build.ps1`) have been retired; regenerate them via NUKE setup if Windows support is ever needed.

### Run individual UIs

```bash
# React (Vite) — canonical
cd PaperlessUI.React   && pnpm install --frozen-lockfile && pnpm dev

# Angular — parallel implementation
cd PaperlessUI.Angular && pnpm install --frozen-lockfile && pnpm start

# Blazor — deployed demo UI (in slnx; also compiled by ./build.sh Compile)
dotnet run --project PaperlessUI.Blazor
```

## Architecture

```mermaid
%%{init: {'theme':'dark'}}%%
flowchart LR
    subgraph Clients
      Blazor[PaperlessUI.Blazor<br/>demo UI — nginx /]
      React[PaperlessUI.React<br/>canonical]
      Angular[PaperlessUI.Angular<br/>parallel impl]
      wwwroot[PaperlessREST/wwwroot<br/>original SPA — UseStaticFiles]
    end
    Blazor & React & Angular & wwwroot -->|HTTPS / SSE| REST[PaperlessREST<br/>ASP.NET Core]
    REST -->|EF Core| PG[(PostgreSQL)]
    REST -->|S3| MIN[(MinIO)]
    REST -->|HTTP| ES[(Elasticsearch)]
    REST -->|AMQP| RMQ((RabbitMQ))
    RMQ --> WORK[PaperlessServices<br/>BackgroundService]
    WORK -->|S3| MIN
    WORK -->|HTTP| ES
    WORK -->|HTTPS| GEM[(Google Gemini)]
    WORK -->|AMQP| RMQ
```

## CI + Coverage

`Build & Test` (gate): backend unit + integration + coverage gate + Codecov upload (also compiles `PaperlessUI.Blazor`, which is in the slnx).
Two non-gating jobs build the Angular and React apps via `pnpm`.

Coverage uploads to https://codecov.io/gh/ANcpLua/Paperless via tokenless OIDC.
`codecov.yml` ignores host entry points, EF migrations, and the build pipeline so the score reflects production surface only.

## Rating-Matrix mapping

The course rubric in [`docs/99_Reference/Rating-Matrix/`](docs/99_Reference/Rating-Matrix) maps to:

| Category | Where it lives |
|---|---|
| **Use Cases / REST API** | `PaperlessREST/Features/DocumentManagement/Presentation/Endpoints/DocumentEndpoints.cs` |
| **Web Frontend** | `PaperlessUI.Blazor/` (Interactive Server) — the production demo UI nginx serves at `/` + `PaperlessUI.React/` (canonical) + `PaperlessUI.Angular/` (parallel impl). The original `PaperlessREST/wwwroot/` SPA still ships via `UseStaticFiles`. |
| **Queues** | `SWEN3.Paperless.RabbitMq` package consumed by REST + Services |
| **Logging** | `Microsoft.Extensions.Logging` everywhere; `FakeLogger` in tests |
| **Validation** | Mapster + DataAnnotations + FluentValidation at the boundary |
| **Stability** | `Microsoft.Extensions.Http.Resilience` (Polly v8) for Gemini |
| **Unit Tests** | `*.Tests/Unit/**` with `MockBehavior.Strict` repositories |
| **Integration Tests** | `*.Tests/Integration/**` on Testcontainers |
| **Clean-Code** | SOLID, ErrorOr result types, vertical-slice Feature folders |
| **Packaging** | `compose.yaml` + per-project `Dockerfile` |
| **Loose Coupling** | every cross-layer call is interface-mediated |
| **Mapper** | Mapster (`MapsterExtensions.Generator`) |
| **DI** | `IServiceCollection` extension methods per feature |
| **DAL** | EF Core 10 + repository pattern (`IDocumentRepository`) |
| **BL** | `DocumentService`, `OcrProcessor`, GenAI worker |
| **GitFlow / Issues / CI / Docs** | this README + `.github/workflows/ci.yml` + branch-protected `main` |

## Stack

| Backend | Frontends | Infra |
|---|---|---|
| .NET 10, ASP.NET Core, EF Core 10.0.x, Mapster, ErrorOr, Hangfire 1.8.23, Polly | Blazor Web App / Interactive Server (deployed demo UI), React 19.2 + Vite 8 + TypeScript 6 (canonical), Angular 21 + pnpm 10 (parallel) | PostgreSQL 17, RabbitMQ 4.3, MinIO (date-pinned), Elasticsearch 9.1, nginx |
| xUnit v3.2.x, MTP v2, Testcontainers, AwesomeAssertions, Moq | – | OrbStack / Docker Compose |

(Exact pin values live in `Version.props` and `Directory.Packages.props` — the table is commentary.)
