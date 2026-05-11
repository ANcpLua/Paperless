<div align="center">

# Paperless

**Document management with OCR, AI summarization, and full-text search.**
.NET 10 backend · Blazor + Angular + React frontends · NUKE + xUnit v3 + Testcontainers.

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
├── PaperlessUI.Blazor/                 # Frontend variant — Blazor Web App (Server)
│   └── PaperlessUI.Blazor.csproj
├── PaperlessUI.Angular/                # Frontend variant — Angular 20 + pnpm
│   └── PaperlessUI.Angular.esproj
├── PaperlessUI.React/                  # Frontend variant — Vite + React + TypeScript
│   └── PaperlessUI.React.esproj
├── Pipeline/                           # NUKE build (`./build.sh <Target>`)
├── docker/                             # nginx + container infra
├── sample-data/                        # XML batch + PDF fixtures
└── compose.yaml                        # Local stack (postgres, minio, rabbitmq, elastic)
```

Three frontends sharing one backend let the grader compare the same use-cases across stacks.

## Quick start

```bash
docker compose up -d                    # postgres, rabbitmq, minio, elasticsearch
./build.sh Compile                      # NUKE: builds the slnx end-to-end
./build.sh UnitTests                    # MTP v2 + xUnit v3
./build.sh IntegrationTests             # Testcontainers
./build.sh Coverage                     # Cobertura via MTP CodeCoverage
./build.sh ReportCoverage --coverage-min-line 30 --coverage-min-branch 0
```

### Run individual UIs

```bash
# Blazor (Server interactivity)
dotnet run --project PaperlessUI.Blazor

# Angular
cd PaperlessUI.Angular && pnpm install && pnpm start

# React (Vite)
cd PaperlessUI.React   && pnpm install && pnpm dev
```

## Architecture

```mermaid
%%{init: {'theme':'dark'}}%%
flowchart LR
    subgraph Clients
      Blazor[PaperlessUI.Blazor]
      Angular[PaperlessUI.Angular]
      React[PaperlessUI.React]
    end
    Blazor & Angular & React -->|HTTPS / SSE| REST[PaperlessREST<br/>ASP.NET Core]
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

`Build & Test` (gate): backend unit + integration + coverage gate + Codecov upload.
Two non-gating jobs build the Angular and React apps via `pnpm`.

Coverage uploads to https://codecov.io/gh/ANcpLua/Paperless via tokenless OIDC.
`codecov.yml` ignores host entry points, EF migrations, and the build pipeline so the score reflects production surface only.

## Rating-Matrix mapping

The course rubric in [`docs/99_Reference/Rating-Matrix/`](docs/99_Reference/Rating-Matrix) maps to:

| Category | Where it lives |
|---|---|
| **Use Cases / REST API** | `PaperlessREST/Features/DocumentManagement/Presentation/Endpoints/DocumentEndpoints.cs` |
| **Web Frontend** | three implementations under `PaperlessUI.*/` |
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
| .NET 10, ASP.NET Core, EF Core 10, Mapster, ErrorOr, Hangfire 1.8.23, Polly | Blazor Web App (Server), Angular 20 + pnpm, React 19 + Vite + TypeScript | PostgreSQL 17, RabbitMQ 4, MinIO, Elasticsearch 9, nginx |
| xUnit v3, MTP v2, Testcontainers, AwesomeAssertions, Moq | – | OrbStack / Docker Compose |
