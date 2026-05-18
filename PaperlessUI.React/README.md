# PaperlessUI.React

Canonical Paperless frontend. Vite 8 + React 19 + TypeScript 6, consumes
`PaperlessREST` via `/api/*` (proxied to `http://localhost:5057` in dev,
served same-origin behind nginx in production via `compose.yaml`).

```bash
pnpm install --frozen-lockfile
pnpm dev            # Vite dev server
pnpm build          # tsc --noEmit && vite build
pnpm generate:types # regenerate src/types/api.ts from PaperlessREST/openapi/paperless.json
```

Override the API base for `pnpm dev` via `.env`: `VITE_API_URL=http://...`
(see `.env.example`). Production ignores the variable; nginx routes `/api/*`.

For repo-wide context (architecture, build commands, stack pins, CI):
see the root [`README.md`](../README.md) and [`CLAUDE.md`](../CLAUDE.md).
