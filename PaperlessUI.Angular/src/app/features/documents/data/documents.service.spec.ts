import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { NotificationService } from '../../../core/notifications/notification.service';
import { EVENT_SOURCE_FACTORY } from '../../../core/sse/event-source.factory';
import { DocumentSummary } from './document.models';
import { DocumentsService } from './documents.service';

class FakeEventSource {
  url: string;
  onopen: (() => void) | null = null;
  onerror: (() => void) | null = null;
  closed = false;
  private listeners: Record<string, ((e: { data: string }) => void)[]> = {};

  constructor(url: string) {
    this.url = url;
  }
  addEventListener(type: string, cb: (e: { data: string }) => void): void {
    (this.listeners[type] ||= []).push(cb);
  }
  emit(type: string, data: unknown = {}): void {
    (this.listeners[type] || []).forEach((cb) => cb({ data: JSON.stringify(data) }));
  }
  close(): void {
    this.closed = true;
  }
}

const baseUrl = 'http://localhost:4200/';
const LIST_URL = `${baseUrl}api/v1/documents?PageSize=50`;
const pdf = (name = 'a.pdf') => new File(['%PDF-1.4'], name, { type: 'application/pdf' });

function summary(over: Partial<DocumentSummary> = {}): DocumentSummary {
  return {
    id: 'id',
    fileName: 'f.pdf',
    status: 'Completed',
    createdAt: '2026-01-01T00:00:00Z',
    processedAt: null,
    content: null,
    summary: null,
    summaryGeneratedAt: null,
    ...over,
  };
}

describe('DocumentsService', () => {
  let svc: DocumentsService;
  let httpTesting: HttpTestingController;
  let notify: { show: ReturnType<typeof vi.fn> };
  let streams: FakeEventSource[];

  beforeEach(() => {
    vi.useFakeTimers();
    notify = { show: vi.fn() };
    streams = [];
    const factory = (url: string): EventSource => {
      const es = new FakeEventSource(url);
      streams.push(es);
      return es as unknown as EventSource;
    };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: baseUrl },
        { provide: NotificationService, useValue: notify },
        { provide: EVENT_SOURCE_FACTORY, useValue: factory },
      ],
    });
    svc = TestBed.inject(DocumentsService);
    httpTesting = TestBed.inject(HttpTestingController);
  });
  afterEach(() => {
    httpTesting.verify();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  const flushList = (items: DocumentSummary[] = []) =>
    httpTesting
      .expectOne(LIST_URL)
      .flush({ items, nextCursor: null, hasMore: false, count: items.length });

  // ---- load ----
  it('load fetches the first page and stores the items', () => {
    svc.load();
    expect(svc.loading()).toBe(true);
    flushList([summary({ id: '1' })]);
    expect(svc.documents().map((d) => d.id)).toEqual(['1']);
    expect(svc.loading()).toBe(false);
    expect(svc.failed()).toBe(false);
  });

  it('load surfaces a failure', () => {
    svc.load();
    httpTesting.expectOne(LIST_URL).flush(null, { status: 500, statusText: 'err' });
    expect(svc.failed()).toBe(true);
    expect(svc.loading()).toBe(false);
    expect(notify.show).toHaveBeenCalledWith('Failed to load documents', 'danger');
  });

  // ---- visible computed ----
  it('visible returns every document when not searching', () => {
    svc.documents.set([summary({ id: '1' }), summary({ id: '2' })]);
    expect(svc.visible()).toHaveLength(2);
  });

  it('visible filters by filename or content (and excludes null content)', () => {
    svc.documents.set([
      summary({ id: '1', fileName: 'foo.pdf', content: null }),
      summary({ id: '2', fileName: 'bar.pdf', content: 'has foo inside' }),
      summary({ id: '3', fileName: 'baz.pdf', content: null }),
    ]);
    svc.currentSearch.set('foo');
    expect(svc.visible().map((d) => d.id)).toEqual(['1', '2']);
  });

  // ---- upload ----
  it('upload rejects a non-PDF without hitting the network', async () => {
    await svc.upload(new File(['x'], 'note.txt', { type: 'text/plain' }));
    httpTesting.expectNone(`${baseUrl}api/v1/documents`);
    expect(notify.show).toHaveBeenCalledWith('note.txt is not a PDF', 'warning');
  });

  it('upload posts FormData, appends the doc, and notifies success', async () => {
    svc.documents.set([summary({ id: 'existing' })]);
    const p = svc.upload(pdf('ok.pdf'));
    const req = httpTesting.expectOne(`${baseUrl}api/v1/documents`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);
    req.flush({ id: 'u1', fileName: 'ok.pdf', status: 'Pending', createdAt: '2026-01-01T00:00:00Z' });
    await p;
    expect(svc.documents().map((d) => d.id)).toEqual(['existing', 'u1']);
    expect(notify.show).toHaveBeenCalledWith('Uploaded ok.pdf', 'success');
  });

  it('upload reports an HTTP error with the problem detail', async () => {
    const p = svc.upload(pdf());
    httpTesting
      .expectOne(`${baseUrl}api/v1/documents`)
      .flush({ detail: 'too big' }, { status: 413, statusText: 'Payload Too Large' });
    await p;
    expect(notify.show).toHaveBeenCalledWith('Failed: too big', 'danger');
  });

  it('upload reports a network failure with the file name', async () => {
    const p = svc.upload(pdf('net.pdf'));
    httpTesting.expectOne(`${baseUrl}api/v1/documents`).error(new ProgressEvent('error'));
    await p;
    expect(notify.show).toHaveBeenCalledWith('Error uploading net.pdf', 'danger');
  });

  // ---- remove ----
  it('remove does nothing when the confirm dialog is cancelled', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(false);
    await svc.remove('x');
    httpTesting.expectNone(`${baseUrl}api/v1/documents/x`);
  });

  it('remove deletes the document and notifies on success', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
    svc.documents.set([summary({ id: 'r1' })]);
    const p = svc.remove('r1');
    httpTesting.expectOne(`${baseUrl}api/v1/documents/r1`).flush(null);
    await p;
    expect(svc.documents().some((d) => d.id === 'r1')).toBe(false);
    expect(notify.show).toHaveBeenCalledWith('Document deleted', 'success');
  });

  it('remove reports an HTTP delete failure', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
    const p = svc.remove('r2');
    httpTesting.expectOne(`${baseUrl}api/v1/documents/r2`).flush(null, { status: 500, statusText: 'err' });
    await p;
    expect(notify.show).toHaveBeenCalledWith('Delete failed', 'danger');
  });

  it('remove reports a network delete failure', async () => {
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
    const p = svc.remove('r3');
    httpTesting.expectOne(`${baseUrl}api/v1/documents/r3`).error(new ProgressEvent('error'));
    await p;
    expect(notify.show).toHaveBeenCalledWith('Error deleting document', 'danger');
  });

  // ---- search ----
  it('search with a blank query falls back to a reload', () => {
    svc.currentSearch.set('old');
    svc.search('   ');
    expect(svc.currentSearch()).toBe('');
    flushList();
  });

  it('search queries the endpoint and reports the count', () => {
    svc.search('inv');
    const req = httpTesting.expectOne(`${baseUrl}api/v1/documents/search?Query=inv&Limit=50`);
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 's1', fileName: 'inv.pdf', status: 'Completed' }]);
    expect(svc.documents().map((d) => d.id)).toEqual(['s1']);
    expect(svc.currentSearch()).toBe('inv');
    expect(notify.show).toHaveBeenCalledWith('Found 1 result(s)', 'info');
  });

  it('search reports a failure', () => {
    svc.search('x');
    httpTesting
      .expectOne(`${baseUrl}api/v1/documents/search?Query=x&Limit=50`)
      .flush(null, { status: 500, statusText: 'err' });
    expect(notify.show).toHaveBeenCalledWith('Search failed', 'danger');
  });

  it('clearSearch resets the term and reloads', () => {
    svc.currentSearch.set('term');
    svc.clearSearch();
    expect(svc.currentSearch()).toBe('');
    flushList();
  });

  // ---- SSE live updates (via the injected factory, no global stub) ----
  it('connectStreams opens the OCR and GenAI streams through the factory', () => {
    svc.connectStreams();
    expect(streams.map((s) => s.url)).toEqual([
      `${baseUrl}api/v1/ocr-results`,
      `${baseUrl}api/v1/events/genai`,
    ]);
  });

  it('either stream opening clears the disconnect flag', () => {
    svc.connectStreams();

    svc.liveDisconnected.set(true);
    streams[0].onopen?.(); // OCR stream reconnected
    expect(svc.liveDisconnected()).toBe(false);

    svc.liveDisconnected.set(true);
    streams[1].onopen?.(); // GenAI stream reconnected
    expect(svc.liveDisconnected()).toBe(false);
  });

  it('ocr-completed reloads and notifies success', () => {
    svc.connectStreams();
    streams[0].emit('ocr-completed');
    flushList();
    expect(notify.show).toHaveBeenCalledWith('OCR completed', 'success');
  });

  it('ocr-failed reloads and notifies danger', () => {
    svc.connectStreams();
    streams[0].emit('ocr-failed');
    flushList();
    expect(notify.show).toHaveBeenCalledWith('OCR failed', 'danger');
  });

  it('genai-completed reloads and notifies success', () => {
    svc.connectStreams();
    streams[1].emit('genai-completed');
    flushList();
    expect(notify.show).toHaveBeenCalledWith('Summary generated', 'success');
  });

  it('genai-failed reloads and notifies danger', () => {
    svc.connectStreams();
    streams[1].emit('genai-failed');
    flushList();
    expect(notify.show).toHaveBeenCalledWith('Summary generation failed', 'danger');
  });

  it('an OCR stream error flags disconnect, closes, and reconnects after 5s', () => {
    svc.connectStreams();
    streams[0].onerror?.();
    expect(svc.liveDisconnected()).toBe(true);
    expect(streams[0].closed).toBe(true);
    vi.advanceTimersByTime(5_000);
    expect(streams).toHaveLength(3);
    expect(streams[2].url).toBe(`${baseUrl}api/v1/ocr-results`);
  });

  it('a GenAI stream error also flags disconnect and reconnects after 10s', () => {
    svc.connectStreams();
    streams[1].onerror?.();
    expect(svc.liveDisconnected()).toBe(true);
    expect(streams[1].closed).toBe(true);
    vi.advanceTimersByTime(10_000);
    expect(streams).toHaveLength(3);
    expect(streams[2].url).toBe(`${baseUrl}api/v1/events/genai`);
  });

  it('disconnectStreams closes both streams when nothing is pending', () => {
    svc.connectStreams();
    svc.disconnectStreams();
    expect(streams[0].closed).toBe(true);
    expect(streams[1].closed).toBe(true);
  });

  it('disconnectStreams cancels pending reconnect timers', () => {
    svc.connectStreams();
    streams[0].onerror?.();
    streams[1].onerror?.();
    svc.disconnectStreams();
    vi.advanceTimersByTime(20_000);
    expect(streams).toHaveLength(2);
  });

  it('disconnectStreams is safe when never connected', () => {
    expect(() => svc.disconnectStreams()).not.toThrow();
  });
});
