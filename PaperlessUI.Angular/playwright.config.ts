import { defineConfig } from '@playwright/test';

/**
 * End-to-end tests for the Paperless Angular UI. Drives the running dev server
 * (which proxies /api to the PaperlessREST API) using the system Chrome — no
 * browser binary download. Run the backend stack first: `docker compose up -d`
 * plus the API on :5057, then `pnpm e2e`.
 */
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: 'list',
  timeout: 30_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: 'http://localhost:4200',
    channel: 'chrome',
    headless: true,
    actionTimeout: 10_000,
    trace: 'retain-on-failure',
  },
  webServer: {
    command: 'pnpm start',
    url: 'http://localhost:4200',
    reuseExistingServer: true,
    timeout: 120_000,
  },
});
