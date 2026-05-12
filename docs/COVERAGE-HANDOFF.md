# Coverage push handoff

Paste the prompt below into a fresh chat opened in the repository root. It is
self-contained: a cold session can pick up the work without re-reading prior
conversation. Everything inside the fenced block is the prompt — the framing
above is just for humans who land here.

The handoff assumes the preflight cleanup in PR #7 (transport DTOs, host
wiring, and `Program.cs` excluded from coverage) and the NUKE `Verify` target
in PR #8 are already on `main`.

---

```text
Push backend coverage to 95% line / 75% branch on PaperlessREST + PaperlessServices (excluding generated code). Baseline is 86.1% line (815/947) after the preflight in PR #7 — transport DTOs, host wiring, and Program.cs are already excluded, so anything still showing up represents real testable behaviour.

## Inner loop
One command, end-to-end:

    ./build.sh Verify --coverage-min-line 95 --coverage-min-branch 75 \
                      --coverage-format markdown --coverage-exclude-generated-param true

For tighter iteration without re-running tests:

    dotcov check Artifacts/coverage --min-line 95 --min-branch 75 --exclude-generated

To pull the per-file gap list:

    ./build.sh ReportCoverage --coverage-min-line 30 --coverage-min-branch 0 \
                              --coverage-format markdown --coverage-exclude-generated-param true

## Known gaps — one PR per cluster
1. `PaperlessREST/Host/Extensions/RichProblemDetailsFactory.cs` (0/41) and `ContractViolationException.cs` (0/8) — RFC 7807 / contract-violation logic
2. `PaperlessREST/Features/EventProcessing/Presentation/GenAiResultListener.cs` (36/61) and `OcrResultListener.cs` (28/37)
3. `PaperlessServices/Features/OcrProcessing/Infrastructure/Search/SearchIndexService.cs` (45/55)
4. `PaperlessREST/Host/Extensions/TypedErrorOrAsyncExtensions.cs` (24/35) and `ServiceCollectionExtensions.cs` (33/46)
5. Last edges in `DocumentService.cs` (78/84), `ReportProcessor.cs` (79/85), and `ReportErrors.cs` (3/6)

## Test conventions (CLAUDE.md has the rest)
- xUnit v3 + MTP v2; AwesomeAssertions (NOT FluentAssertions)
- Moq with `MockBehavior.Strict` — match the existing pattern
- Use `TestContext.Current.CancellationToken` in tests, not arbitrary `CancellationToken` values; don't grow the existing ~30 xUnit1051 warnings
- Integration tests: Testcontainers on OrbStack — already wired, real Postgres 17 / RabbitMQ 4 / MinIO / Elasticsearch 9
- `FakeTextSummarizer` is the ONLY allowed test double for a real service (Gemini is third-party). Do not introduce other fakes.
- Don't rewrite tests to make them pass — fix the production code or the timing (see CLAUDE.md `OcrWorker.EmptyStream_CompletesGracefully` story)

## Process
One branch + PR per cluster off `main`. After each cluster:
1. `./build.sh Verify --coverage-min-line 30 --coverage-min-branch 0 --coverage-format markdown --coverage-exclude-generated-param true` green locally
2. Commit, push, open PR, then `gh pr merge --squash --delete-branch --auto`
3. Wait for merge, `git pull --ff-only`, start the next cluster

Branch protection on `main` requires `Build & Test (backend)` green and forbids direct commits. No force-push, no `--no-verify`, no admin bypass.

After all clusters land, open a final PR ratcheting `.github/workflows/ci.yml` from `--coverage-min-line 30 --coverage-min-branch 0` to `--coverage-min-line 95 --coverage-min-branch 75`. That locks the floor in.

Report what's left if anything proves genuinely untestable (record records, unreachable catch branches, etc.). Don't fake coverage with throwaway tests.
```

---

## Syntax notes (v0.1.1, what's installed today)

- NUKE wrapper parameter: `--coverage-exclude-generated-param true` (the `-param`
  suffix is intentional and required at this version; see `./build.sh ReportCoverage --help`).
- Standalone CLI: `dotcov check <path> --min-line N [--min-branch N] [--exclude-generated]`
  (`--exclude-generated` is a switch, no value).
- Coverage Cobertura artifacts land in `Artifacts/coverage/<TestProject>/coverage.cobertura.xml`,
  not the dotcov-README default of `TestResults/**`. `Build.cs` already points
  `ICoverageReport.CoverageSearchDirectory` at that location.

If the `dotcov.tool` / `DotCov.Nuke` packages get bumped past 0.1.1, re-check
parameter names — newer versions may drop the `-param` suffix.
