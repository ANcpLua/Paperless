# PaperlessUI.Angular

Parallel-implementation Paperless frontend. Angular 21 + pnpm 10
(via corepack). Consumes `PaperlessREST` via `/api/*` (proxied per
`proxy.conf.json` to `http://localhost:5057` in dev; nginx routes
same-origin in production via `compose.yaml`).

```bash
pnpm install --frozen-lockfile
pnpm start          # ng serve (http://localhost:4200)
pnpm run build      # ng build (production by default)
pnpm generate:openapi   # regenerate src/types/api.ts from the REST OpenAPI doc
```

Avoid the pnpm-10 `--` separator footgun: do NOT call `pnpm run X -- --flag`;
pnpm 10 passes the literal `--` to the script. Configure the production
build via `angular.json` instead.

For repo-wide context (architecture, build commands, stack pins, CI):
see the root [`README.md`](../README.md) and [`CLAUDE.md`](../CLAUDE.md).
