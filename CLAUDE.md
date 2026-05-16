# Paperless — repo guide for Claude

Document management with OCR, AI summarization, and full-text search. .NET 10 backend, three interchangeable frontends (Blazor / Angular / React).

This file is the on-disk source of truth for working in this repo. Read it before touching anything.

---

## Layout

```
Paperless.slnx                       # modern slnx; flat — NUKE 10 doesn't traverse <Folder> wrappers
├── PaperlessREST/                   # ASP.NET Core API (REST + SSE)
├── PaperlessServices/               # BackgroundService worker (OCR + GenAI)
├── PaperlessREST.Tests/             # xUnit v3 + Testcontainers
├── PaperlessServices.Tests/         # xUnit v3 + Testcontainers
├── PaperlessUI.Blazor/              # Blazor Web App (Server interactivity) — PaperlessUI.Blazor.csproj
├── PaperlessUI.Angular/             # Angular 21 + pnpm                    — PaperlessUI.Angular.esproj
├── PaperlessUI.React/               # Vite + React 19 + TS                 — PaperlessUI.React.esproj
├── Pipeline/                        # NUKE build (Build.csproj)
├── docker/, sample-data/, compose.yaml
└── docs/99_Reference/Rating-Matrix/ # course grading rubric (PDF + xlsx)
```

Three frontends share one backend so the same use-cases can be graded across stacks.

## Build & test

NUKE-based, single entry point. Targets compose via `Pipeline/Components/*.cs` (`ICompile`, `ITest`, `ICoverage`, `IReportCoverage`).

```bash
./build.sh Compile                          # full slnx build
./build.sh UnitTests                        # MTP v2 + xUnit v3, no containers
./build.sh IntegrationTests                 # spins Testcontainers (Postgres, MinIO, RabbitMQ, ES)
./build.sh Coverage                         # Cobertura via the MTP CodeCoverage extension
./build.sh ReportCoverage --coverage-min-line 30 --coverage-min-branch 0
```

UI projects build via their respective toolchains, never via NUKE:

```bash
dotnet run --project PaperlessUI.Blazor
cd PaperlessUI.Angular && pnpm install && pnpm start
cd PaperlessUI.React   && pnpm install && pnpm dev
```

## CI

`.github/workflows/ci.yml` runs three parallel jobs on every push/PR:

| job | gate | runs |
|---|---|---|
| `Build & Test (backend)` | **required** | NUKE Compile → UnitTests → IntegrationTests → Coverage → DotCov gate → Codecov upload |
| `Build (PaperlessUI.Angular)` | non-blocking | `pnpm install && pnpm run build` (ng's default config is production — do NOT pass `--configuration production` after `--`; pnpm 10 passes `--` literally to scripts) |
| `Build (PaperlessUI.React)` | non-blocking | `pnpm install && pnpm run build` |

Coverage uploads to https://codecov.io/gh/ANcpLua/Paperless via tokenless OIDC. `codecov.yml` ignores host entry points, EF migrations, the pipeline, and test projects so the score reflects production surface only.

## Coverage tooling — dotcov

The DotCov gate runs `dotcov check` from `dotcov.tool` (NuGet, owner `ANcpLua/dotcov`). Two flags matter on this repo:

- `--exclude-generated` strips generators, migrations, designer files, state-machine classes, `Program.cs`. With this flag, dotcov's totals match Codecov's view byte-for-byte (1146/1465 = 78.23%).
- `--keep <comma-separated patterns>` re-includes paths after exclusion. Use `--exclude-generated --keep Program.cs` if you ever genuinely need to measure `Program.cs` (e.g., a single-binary CLI tool).

Per-file numbers also match Codecov exactly post-`fix(parser): merge <class> blocks and dedupe lines by source-line number` (dotcov PR #3) — records, error factories, and async-state-machine-heavy files all produce identical results.

## NUKE Cohesion (build code quality bar)

Keep build logic in pure C# fluent NUKE. Reject Bash / PS1 / Make / raw CLI in `Pipeline/`. The reusable patterns:

1. **OCP via interfaces.** `interface ICompile : INukeBuild { Target Compile => _ => _.Executes(...); }`. Compose with `.TryDependsOn<ICompile>()` so components stay decoupled.
2. **SRP per target.** One concern per target (Compile vs Pack vs Deploy). Group changes by reason.
3. **Typed inputs.** `[Parameter]` for inputs, `[Secret]` for keys, `[Solution(GenerateProjects=true)]` for project access. No magic strings.
4. **Loose dependencies.** `.TryDependsOn<T>()` lets a component opt in; `.DependsOn<T>()` is for the "knowing" side.
5. **Fluent builders.** `DotNetBuild(_ => _.SetConfiguration(c).SetVerbosity(v))` — never `SetArgument(...)`.
6. **Failure handling.** `.AssuredAfterFailure()` for cleanup, `.ProceedAfterFailure()` to continue on soft failure.

Anti-patterns that fail the self-check:

- [ ] Any inline shell in `Pipeline/`.
- [ ] Concrete class targets instead of interfaces — breaks OCP.
- [ ] Hardcoded project names instead of `Solution.GetProject("...")` — and remember `Solution.GetProject` does NOT recurse into `<Folder>` wrappers in NUKE 10's slnx parsing.

## Gotchas hit in this session — don't repeat

- **NUKE 10 + slnx + `<Folder>` wrappers**: `Solution.GetProject(name)` returns null. Keep Paperless.slnx flat (which it already is).
- **pnpm 10 `--` separator**: `pnpm run X -- --flag` passes `-- --flag` literally to the script. Drop the `--`; pass flags by editing `package.json` scripts or by setting framework-native defaults.
- **`secrets.CODECOV_TOKEN` empty for tokenless upload**: passing `token: ${{ secrets.CODECOV_TOKEN }}` when no secret exists expands to an empty string the Codecov CLI rejects as `Got unexpected extra arguments (***)`. Omit the line entirely for public repos.
- **BackgroundService race in tests**: `BackgroundService.StartAsync` returns before `ExecuteAsync` runs. Don't wait on a log predicate that's already true for an empty snapshot (`_ => true`). Signal via `TaskCompletionSource` from a mock's `DisposeAsync` or `AckAsync`, then await that.
- **Hangfire NU1107**: Hangfire + Hangfire.AspNetCore must move together. Renovate split them once and broke restore on `main` for days.
- **Gemini placeholder key**: `.env.test` ships `GEMINI__APIKEY=test-gemini-key-placeholder`. The integration test must mock `ITextSummarizer` (`FakeTextSummarizer` in `PaperlessServices.Tests/Integration/`), not hit the real API.

## Rating-Matrix mapping (course grading)

[`docs/99_Reference/Rating-Matrix/`](docs/99_Reference/Rating-Matrix) is the rubric. The repo maps to it:

| Category | Where |
|---|---|
| Use Cases / REST API | `PaperlessREST/Features/DocumentManagement/Presentation/Endpoints/` |
| Web Frontend | three implementations under `PaperlessUI.*/` |
| Queues | `SWEN3.Paperless.RabbitMq` consumed by REST + Services |
| Logging | `Microsoft.Extensions.Logging`; `FakeLogger` in tests |
| Validation | Mapster + DataAnnotations + FluentValidation at the boundary |
| Stability | `Microsoft.Extensions.Http.Resilience` (Polly v8) on Gemini |
| Unit Tests | `*.Tests/Unit/**` with `MockBehavior.Strict` repositories |
| Integration Tests | `*.Tests/Integration/**` on Testcontainers |
| Clean-Code | SOLID, ErrorOr result types, vertical-slice Feature folders |
| Packaging | `compose.yaml` + per-project `Dockerfile` |
| Loose Coupling | every cross-layer call is interface-mediated |
| Mapper | Mapster (`MapsterExtensions.Generator`) |
| DI | `IServiceCollection` extension methods per feature |
| DAL | EF Core 10 + repository pattern (`IDocumentRepository`) |
| BL | `DocumentService`, `OcrProcessor`, GenAI worker |
| GitFlow / CI / Docs | `.github/workflows/ci.yml`, branch-protected `main`, this file |

## Stack pins

| | version |
|---|---|
| .NET | 10.0 |
| EF Core | 10.0.0 |
| Hangfire | 1.8.23 (with Hangfire.AspNetCore 1.8.23 + Hangfire.PostgreSql 1.21.1) |
| xUnit | v3.2.1 + MTP v2 |
| SWEN3.Paperless.RabbitMq | 2.3.1 |
| React | 19.2 (Vite 7) |
| Angular | 21 (pnpm 10) |
| Node | 22 LTS in CI |
| Postgres / RabbitMQ / MinIO / Elasticsearch | 17 / 4 / latest / 9.x |

## House rules

- Branch protection on `main` requires `Build & Test (backend)` green. No admin bypass.
- No commits to `main` directly — PR everything.
- No "AI slop" in PR titles/bodies. Write what the change does and why, no meta narration.
- Don't rewrite a test to pass; rewrite the production code (or the timing) to make the assertion truthful. See `OcrWorker.EmptyStream_CompletesGracefully` for the canonical fix.
