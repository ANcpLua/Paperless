import { expect, Page, test } from '@playwright/test';
import { readFileSync } from 'node:fs';

// Resolved against the project root (Playwright cwd) — works in both ESM and CJS.
const samplePdf = readFileSync('e2e/fixtures/sample.pdf');

function doc(over: Partial<Record<string, unknown>> = {}) {
  return {
    id: over.id ?? `id-${Math.random().toString(36).slice(2)}`,
    fileName: over.fileName ?? 'mock.pdf',
    status: over.status ?? 'Completed',
    createdAt: over.createdAt ?? '2026-06-24T10:00:00Z',
    processedAt: over.processedAt ?? null,
    content: over.content ?? null,
    summary: over.summary ?? null,
    summaryGeneratedAt: over.summaryGeneratedAt ?? null,
  };
}

/** Stub the list endpoint so card-rendering is deterministic without backend processing. */
async function mockList(page: Page, items: unknown[]): Promise<void> {
  await page.route('**/api/v1/documents?*', (route) =>
    route.fulfill({ json: { items, nextCursor: null, hasMore: false, count: items.length } }),
  );
}

async function uploadPdf(page: Page, name: string): Promise<void> {
  await page.getByTestId('file-input').setInputFiles({
    name,
    mimeType: 'application/pdf',
    buffer: samplePdf,
  });
}

// ───────────────────────────── live-stack flows ─────────────────────────────
test.describe('Paperless Angular UI — live stack', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: /Paperless OCR System/ })).toBeVisible();
  });

  test('renders shell: title, upload zone, search, refresh, theme toggle, doc container', async ({ page }) => {
    await expect(page.getByTestId('upload-zone')).toBeVisible();
    await expect(page.getByTestId('search-input')).toBeVisible();
    await expect(page.getByTestId('search-btn')).toBeVisible();
    await expect(page.getByTestId('refresh-btn')).toBeVisible();
    await expect(page.getByTestId('theme-toggle')).toBeVisible();
    await expect(page.getByTestId('documents-container')).toBeVisible();
  });

  test('uploads a PDF: card appears, Pending badge, "Uploaded" toast', async ({ page }) => {
    const name = `e2e-upload-${Date.now()}.pdf`;
    await uploadPdf(page, name);

    await expect(page.getByTestId('notifications')).toContainText(`Uploaded ${name}`);
    const card = page.getByTestId('document-card').filter({ hasText: name });
    await expect(card).toBeVisible();
    await expect(card.getByTestId('doc-status')).toContainText('Pending');
  });

  test('rejects a non-PDF with a warning toast and adds no card', async ({ page }) => {
    const before = await page.getByTestId('document-card').count();
    await page.getByTestId('file-input').setInputFiles({
      name: 'notes.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('not a pdf'),
    });
    await expect(page.getByTestId('notifications')).toContainText('notes.txt is not a PDF');
    await expect(page.getByTestId('document-card')).toHaveCount(before);
  });

  test('toggles theme and swaps the sun/moon icon', async ({ page }) => {
    const html = page.locator('html');
    const initial = (await html.getAttribute('data-bs-theme')) ?? 'light';
    const icon = page.getByTestId('theme-toggle').locator('i');

    await page.getByTestId('theme-toggle').click();
    await expect(html).not.toHaveAttribute('data-bs-theme', initial);
    await expect(icon).toHaveClass(initial === 'light' ? /bi-sun/ : /bi-moon/);

    await page.getByTestId('theme-toggle').click();
    await expect(html).toHaveAttribute('data-bs-theme', initial);
  });

  test('deletes a document after accepting confirm', async ({ page }) => {
    const name = `e2e-delete-${Date.now()}.pdf`;
    await uploadPdf(page, name);
    const card = page.getByTestId('document-card').filter({ hasText: name });
    await expect(card).toBeVisible();

    page.once('dialog', (d) => d.accept());
    await card.getByTestId('doc-delete').click();
    await expect(card).toHaveCount(0);
    await expect(page.getByTestId('notifications')).toContainText('Document deleted');
  });

  test('cancelling the confirm dialog does NOT delete', async ({ page }) => {
    const name = `e2e-cancel-${Date.now()}.pdf`;
    await uploadPdf(page, name);
    const card = page.getByTestId('document-card').filter({ hasText: name });
    await expect(card).toBeVisible();

    page.once('dialog', (d) => d.dismiss());
    await card.getByTestId('doc-delete').click();
    // Card must still be present after a cancelled delete.
    await expect(card).toBeVisible();
  });
});

// ──────────────────────── mocked-contract rendering ────────────────────────
test.describe('Paperless Angular UI — mocked contract', () => {
  test('Completed doc renders green badge + OCR preview, capped at 500 chars', async ({ page }) => {
    const content = 'A'.repeat(500) + 'TAIL_MARKER_BEYOND_500';
    await mockList(page, [doc({ fileName: 'ocr.pdf', status: 'Completed', content })]);
    await page.goto('/');

    const card = page.getByTestId('document-card').filter({ hasText: 'ocr.pdf' });
    await expect(card.getByTestId('doc-status')).toHaveClass(/bg-success/);
    await expect(card).toContainText('OCR Text Preview');
    await expect(card).toContainText('…');
    await expect(card).not.toContainText('TAIL_MARKER_BEYOND_500');
  });

  test('Pending doc renders warning badge + spinner; Failed renders danger badge', async ({ page }) => {
    await mockList(page, [
      doc({ fileName: 'pending.pdf', status: 'Pending' }),
      doc({ fileName: 'failed.pdf', status: 'Failed' }),
    ]);
    await page.goto('/');

    const pending = page.getByTestId('document-card').filter({ hasText: 'pending.pdf' });
    await expect(pending.getByTestId('doc-status')).toHaveClass(/bg-warning/);
    await expect(pending.locator('.spinner-border')).toBeVisible();

    const failed = page.getByTestId('document-card').filter({ hasText: 'failed.pdf' });
    await expect(failed.getByTestId('doc-status')).toHaveClass(/bg-danger/);
  });

  test('doc with a summary renders the AI Summary section', async ({ page }) => {
    await mockList(page, [
      doc({ fileName: 'summed.pdf', status: 'Completed', content: 'text', summary: 'This is the AI summary.' }),
    ]);
    await page.goto('/');

    const card = page.getByTestId('document-card').filter({ hasText: 'summed.pdf' });
    await expect(card.getByTestId('doc-summary')).toBeVisible();
    await expect(card.getByTestId('doc-summary')).toContainText('This is the AI summary.');
  });

  test('OCR preview is hidden for a Pending doc even if content is present', async ({ page }) => {
    await mockList(page, [doc({ fileName: 'pendingwithtext.pdf', status: 'Pending', content: 'hidden ocr text' })]);
    await page.goto('/');
    const card = page.getByTestId('document-card').filter({ hasText: 'pendingwithtext.pdf' });
    await expect(card).not.toContainText('OCR Text Preview');
  });

  test('positive search shows "Found N result(s)" and renders the results', async ({ page }) => {
    await mockList(page, []); // initial load empty
    await page.route('**/api/v1/documents/search?*', (route) =>
      route.fulfill({
        json: [
          { id: 's1', fileName: 'invoice.pdf', status: 'Completed' },
          { id: 's2', fileName: 'invoice-2.pdf', status: 'Completed' },
        ],
      }),
    );
    await page.goto('/');
    await page.getByTestId('search-input').fill('invoice');
    await page.getByTestId('search-btn').click();

    await expect(page.getByTestId('notifications')).toContainText('Found 2 result(s)');
    await expect(page.getByTestId('document-card')).toHaveCount(2);
    await expect(page.getByTestId('document-card').filter({ hasText: 'invoice.pdf' })).toBeVisible();
  });

  test('load failure shows the "Failed to load documents" toast', async ({ page }) => {
    await page.route('**/api/v1/documents?*', (route) =>
      route.fulfill({ status: 500, json: { detail: 'boom' } }),
    );
    await page.goto('/');
    await expect(page.getByTestId('notifications')).toContainText('Failed to load documents');
  });

  test('search empty-state message appears for a no-match query', async ({ page }) => {
    await mockList(page, [doc({ fileName: 'x.pdf' })]);
    await page.route('**/api/v1/documents/search?*', (route) => route.fulfill({ json: [] }));
    await page.goto('/');
    await page.getByTestId('search-input').fill('nomatch');
    await page.getByTestId('search-btn').click();
    await expect(page.getByTestId('empty-state')).toContainText('No documents match your search');
  });

  test('upload network failure shows the file-named "Error uploading" toast', async ({ page }) => {
    await mockList(page, []);
    await page.goto('/');
    await page.route('**/api/v1/documents', (route) =>
      route.request().method() === 'POST' ? route.abort('failed') : route.continue(),
    );
    await uploadPdf(page, 'neterr.pdf');
    await expect(page.getByTestId('notifications')).toContainText('Error uploading neterr.pdf');
  });

  test('delete network failure shows the "Error deleting document" toast', async ({ page }) => {
    await mockList(page, [doc({ id: 'del1', fileName: 'delnet.pdf', status: 'Completed' })]);
    await page.goto('/');
    await page.route('**/api/v1/documents/del1', (route) => route.abort('failed'));
    const card = page.getByTestId('document-card').filter({ hasText: 'delnet.pdf' });
    await expect(card).toBeVisible();
    page.once('dialog', (d) => d.accept());
    await card.getByTestId('doc-delete').click();
    await expect(page.getByTestId('notifications')).toContainText('Error deleting document');
  });
});

// ──────────────────────────────── SSE health ────────────────────────────────
test.describe('Paperless Angular UI — live updates', () => {
  test('shows the disconnect badge when the OCR SSE stream fails', async ({ page }) => {
    await mockList(page, []);
    await page.route('**/api/v1/ocr-results', (route) => route.abort('failed'));
    await page.goto('/');
    await expect(page.getByTestId('sse-status')).toContainText('Live Updates Disconnected');
  });
});
