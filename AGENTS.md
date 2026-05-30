# Paperless — repo guide for Claude

Document management with OCR, AI summarization, and full-text search. .NET 10 backend. Frontend story: React is the canonical/priority implementation, Angular is a parallel implementation kept for comparison, Blazor is currently a WIP scaffold. The production demo UI today is the vanilla SPA at `PaperlessREST/wwwroot/` which nginx serves from `compose.yaml`.

This file is the on-disk source of truth for working in this repo. Read it before touching anything.

> `AGENTS.md` is the canonical copy; `CLAUDE.md` is a symlink to it, so Claude Code and any `AGENTS.md`-aware tool read the same guide.

---

## Layout

```
Paperless.slnx                       # modern slnx; flat — NUKE 10 doesn't traverse <Folder> wrappers
├── PaperlessREST/                   # ASP.NET Core API (REST + SSE)
│   └── wwwroot/                     # Vanilla Bootstrap SPA — production demo UI mounted by nginx
├── PaperlessServices/               # BackgroundService worker (OCR + GenAI)
├── PaperlessREST.Tests/             # xUnit v3 + Testcontainers
├── PaperlessServices.Tests/         # xUnit v3 + Testcontainers
├── PaperlessUI.React/               # Vite + React 19 + TS  (canonical frontend)  — PaperlessUI.React.esproj
├── PaperlessUI.Angular/             # Angular 21 + pnpm     (parallel implementation) — PaperlessUI.Angular.esproj
├── PaperlessUI.Blazor/              # Blazor Web App scaffold — WIP, NOT in Paperless.slnx, NOT built by CI
├── Pipeline/                        # NUKE build (Build.csproj)
├── docker/, sample-data/, compose.yaml
└── docs/99_Reference/Rating-Matrix/ # course grading rubric (PDF + xlsx)
```

React + Angular share the backend so the same use-cases can be compared across stacks. Blazor is on-disk but not building until a Paperless page is implemented (add back to Paperless.slnx + ci.yml when it does).

## Build & test

NUKE-based, single entry point. Targets compose via `Pipeline/Components/*.cs` (`ICompile`, `ITest`, `ICoverage`, `IReportCoverage`). `build.sh` is the only bootstrapper kept at root; the Windows `build.cmd`/`build.ps1` were retired (regenerate via NUKE setup if Windows support is ever needed).

```bash
./build.sh Compile                          # full slnx build
./build.sh UnitTests                        # MTP v2 + xUnit v3, no containers
./build.sh IntegrationTests                 # spins Testcontainers (Postgres, MinIO, RabbitMQ, ES)
./build.sh Coverage                         # Cobertura via the MTP CodeCoverage extension
./build.sh ReportCoverage --coverage-min-line 0 --coverage-min-branch 0 --coverage-format markdown --coverage-exclude-generated-param true
```

The `ReportCoverage` gate is report-only (thresholds 0/0). CI publishes the markdown
summary and Codecov diff; regressions surface in PR review, not as a hard build fail.

UI projects build via their respective toolchains, never via NUKE:

```bash
cd PaperlessUI.React   && pnpm install --frozen-lockfile && pnpm dev       # canonical
cd PaperlessUI.Angular && pnpm install --frozen-lockfile && pnpm start     # parallel impl
# Blazor scaffold: dotnet run --project PaperlessUI.Blazor (WIP, not in slnx)
```

## CI

`.github/workflows/ci.yml` runs three parallel jobs on every push/PR:

| job | gate | runs |
|---|---|---|
| `Build & Test (backend)` | **required** | NUKE UnitTests → IntegrationTests → Coverage → markdown summary (no hard gate) → Codecov upload |
| `Build (PaperlessUI.Angular)` | non-blocking | `pnpm install --frozen-lockfile && pnpm run build` (ng's default config is production — do NOT pass `--configuration production` after `--`; pnpm 10 passes `--` literally to scripts) |
| `Build (PaperlessUI.React)` | non-blocking | `pnpm install --frozen-lockfile && pnpm run build` |

The workflow declares `concurrency:` (cancel-in-progress on PRs only) and least-privilege `permissions:` (read defaults; codecov/check annotations escalate as needed).

Coverage uploads to https://codecov.io/gh/ANcpLua/Paperless via tokenless OIDC. `codecov.yml` ignores host entry points, EF migrations, the pipeline, and test projects so the score reflects production surface only.

## Coverage tooling — dotcov

Coverage uses `DotCov.Nuke` (NuGet, owner `ANcpLua/dotcov`) as a NUKE component package — `Pipeline/Build.csproj:29` references it, `Pipeline/Build.cs:43` mixes the `ICoverageReport` interface into the build class. There is no `dotnet tool` CLI; the gate runs via `./build.sh ReportCoverage`. Two parameters matter on this repo:

- `--coverage-exclude-generated-param true` (the NUKE [Parameter] kebab-cased form of `--exclude-generated`) strips generators, migrations, designer files, async state-machine sequence points, `Program.cs`. With this flag, the gate metric is **99.8% line coverage (928/930)** — see the sequence-point note in Gotchas. Disable by passing `false` to see the raw numbers (currently ~73% with generated code mixed in).
- `--coverage-min-line` / `--coverage-min-branch` set the gate threshold. CI passes `0 / 0` (report-only mode); per-file numbers and the overall summary still publish to the workflow log as markdown. Codecov-side gating is configured in `codecov.yml` (project: auto, patch: 80%).

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
- **The "missing 2 lines" of coverage are sequence-point artifacts, not testable code**. Gate metric is 928/930 = 99.8%. The two unhit lines are the closing braces of try/catch blocks in `GenAiResultListener.cs` and `ReportProcessor.cs:120` — Roslyn emits a sequence point on the fall-through-after-catch path, but every code path inside those try blocks either `return`s early or unwinds via exception. No test can reach those sequence points without breaking the design intent. Leave them; do not chase 100% by restructuring around the coverage tool.
- **Custom SDK in Dockerfiles**: PaperlessREST.csproj uses `<Project Sdk="ANcpLua.NET.Sdk.Web">` (version pinned in `global.json` msbuild-sdks). For docker builds, `global.json` + `nuget.config` + `Directory.Packages.props` + `Version.props` must be COPYed into the build context BEFORE `dotnet restore`, otherwise the SDK resolver errors with "Could not resolve SDK". Both Dockerfiles do this; if you copy a Dockerfile for a new project, preserve those COPY lines.

## Rating-Matrix mapping (course grading)

[`docs/99_Reference/Rating-Matrix/`](docs/99_Reference/Rating-Matrix) is the rubric. The repo maps to it:

| Category | Where |
|---|---|
| Use Cases / REST API | `PaperlessREST/Features/DocumentManagement/Presentation/Endpoints/` |
| Web Frontend | React (`PaperlessUI.React/`, canonical) + Angular (`PaperlessUI.Angular/`, parallel impl) + the vanilla SPA at `PaperlessREST/wwwroot/` that nginx serves in production. Blazor (`PaperlessUI.Blazor/`) is a WIP scaffold, not currently built. |
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
| Trunk-based / CI / Docs | `.github/workflows/ci.yml`, branch-protected `main`, this file |

## Stack pins

Inline versions drift; treat `Version.props` + `Directory.Packages.props` as the source of truth and the table below as commentary on what matters.

| | version (see `Version.props` for exact) |
|---|---|
| .NET SDK | 10.0.300 (`global.json`, rollForward latestFeature) |
| EF Core | 10.0.x (currently 10.0.8 — `Version.props:MicrosoftEntityFrameworkCoreVersion`) |
| Hangfire | 1.8.23 (Hangfire + Hangfire.AspNetCore share `$(HangfireVersion)` MSBuild var; Hangfire.PostgreSql 1.21.1) |
| xUnit | v3.2.x + MTP v2 (`Version.props:XunitV3MtpV2Version`) |
| SWEN3.Paperless.RabbitMq | 2.3.1 |
| React | 19.2 + Vite 8 + TypeScript 6 |
| Angular | 21 (pnpm 10.30.2 via corepack) |
| Node | 22 LTS in CI |
| Postgres / RabbitMQ / MinIO / Elasticsearch | 17-alpine / 4.3.0-management / RELEASE-date-pinned / 9.1.3 |

## House rules

- Branch protection on `main` requires `Build & Test (backend)` green. No admin bypass.
- No commits to `main` directly — PR everything.
- No "AI slop" in PR titles/bodies. Write what the change does and why, no meta narration.
- Don't rewrite a test to pass; rewrite the production code (or the timing) to make the assertion truthful. See `OcrWorker.EmptyStream_CompletesGracefully` for the canonical fix.
