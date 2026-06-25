import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

import { ApiClientService } from '../../../core/api/api-client.service';
import { isNetworkError, problemDetail } from '../../../core/api/http-error';
import { API_BASE_URL } from '../../../core/config/api-base-url.token';
import { NotificationService } from '../../../core/notifications/notification.service';
import { EVENT_SOURCE_FACTORY } from '../../../core/sse/event-source.factory';
import {
  DocumentSearchResult,
  DocumentSummary,
  PaginatedDocumentsResponse,
  toSummary,
  UploadedDocument,
} from './document.models';

const PAGE_SIZE = 50;
const OCR_RECONNECT_MS = 5_000;
const SUMMARY_RECONNECT_MS = 10_000;

/**
 * Full transpilation of the wwwroot SPA's document state machine: load / upload /
 * delete / search plus the OCR + GenAI Server-Sent-Event live-update streams.
 */
@Injectable({ providedIn: 'root' })
export class DocumentsService {
  private readonly api = inject(ApiClientService);
  private readonly notify = inject(NotificationService);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly openEventSource = inject(EVENT_SOURCE_FACTORY);

  readonly documents = signal<readonly DocumentSummary[]>([]);
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly currentSearch = signal('');
  readonly liveDisconnected = signal(false);

  /** Visible list after the SPA's client-side fileName/content filter. */
  readonly visible = computed(() => {
    const search = this.currentSearch().toLowerCase();
    const docs = this.documents();
    if (!search) return docs;
    return docs.filter(
      (d) =>
        d.fileName.toLowerCase().includes(search) ||
        (d.content?.toLowerCase().includes(search) ?? false),
    );
  });

  private ocrStream: EventSource | null = null;
  private summaryStream: EventSource | null = null;
  private ocrTimer: ReturnType<typeof setTimeout> | null = null;
  private summaryTimer: ReturnType<typeof setTimeout> | null = null;

  load(): void {
    this.loading.set(true);
    this.failed.set(false);
    this.api
      .get<PaginatedDocumentsResponse>(`api/v1/documents?PageSize=${PAGE_SIZE}`)
      .subscribe({
        next: (res) => {
          this.documents.set(res.items);
          this.loading.set(false);
        },
        error: () => {
          this.failed.set(true);
          this.loading.set(false);
          this.notify.show('Failed to load documents', 'danger');
        },
      });
  }

  /** Awaitable so callers can upload multiple files sequentially (1:1 with the SPA's awaited loop). */
  async upload(file: File): Promise<void> {
    if (file.type !== 'application/pdf') {
      this.notify.show(`${file.name} is not a PDF`, 'warning');
      return;
    }
    const form = new FormData();
    form.append('File', file);
    try {
      const doc = await firstValueFrom(
        this.api.post<FormData, UploadedDocument>('api/v1/documents', form),
      );
      // SPA appends new uploads to the end of the store.
      this.documents.update((list) => [...list.filter((d) => d.id !== doc.id), toSummary(doc)]);
      this.notify.show(`Uploaded ${file.name}`, 'success');
    } catch (err) {
      this.notify.show(
        isNetworkError(err)
          ? `Error uploading ${file.name}`
          : `Failed: ${problemDetail(err, 'upload failed')}`,
        'danger',
      );
    }
  }

  async remove(id: string): Promise<void> {
    if (!confirm('Delete this document?')) return;
    try {
      await firstValueFrom(this.api.delete(`api/v1/documents/${id}`));
      this.documents.update((list) => list.filter((d) => d.id !== id));
      this.notify.show('Document deleted', 'success');
    } catch (err) {
      this.notify.show(isNetworkError(err) ? 'Error deleting document' : 'Delete failed', 'danger');
    }
  }

  search(rawQuery: string): void {
    const query = rawQuery.trim();
    if (!query) {
      this.clearSearch();
      return;
    }
    this.api
      .get<DocumentSearchResult[]>(
        `api/v1/documents/search?Query=${encodeURIComponent(query)}&Limit=${PAGE_SIZE}`,
      )
      .subscribe({
        next: (results) => {
          this.documents.set(results.map(toSummary));
          this.currentSearch.set(query);
          this.notify.show(`Found ${results.length} result(s)`, 'info');
        },
        error: () => this.notify.show('Search failed', 'danger'),
      });
  }

  clearSearch(): void {
    this.currentSearch.set('');
    this.load();
  }

  // ---- SSE live updates. Both streams report liveness via liveDisconnected. ----

  connectStreams(): void {
    this.connectOcr();
    this.connectSummary();
  }

  disconnectStreams(): void {
    this.ocrStream?.close();
    this.summaryStream?.close();
    if (this.ocrTimer) clearTimeout(this.ocrTimer);
    if (this.summaryTimer) clearTimeout(this.summaryTimer);
  }

  private streamUrl(path: string): string {
    return new URL(path, this.baseUrl).toString();
  }

  private connectOcr(): void {
    this.ocrStream?.close();
    const es = this.openEventSource(this.streamUrl('api/v1/ocr-results'));
    this.ocrStream = es;
    es.onopen = () => this.liveDisconnected.set(false);
    es.onerror = () => {
      this.liveDisconnected.set(true);
      es.close();
      this.ocrTimer = setTimeout(() => this.connectOcr(), OCR_RECONNECT_MS);
    };
    es.addEventListener('ocr-completed', () => {
      this.load();
      this.notify.show('OCR completed', 'success');
    });
    es.addEventListener('ocr-failed', () => {
      this.load();
      this.notify.show('OCR failed', 'danger');
    });
  }

  private connectSummary(): void {
    this.summaryStream?.close();
    const es = this.openEventSource(this.streamUrl('api/v1/events/genai'));
    this.summaryStream = es;
    es.onopen = () => this.liveDisconnected.set(false);
    es.onerror = () => {
      this.liveDisconnected.set(true);
      es.close();
      this.summaryTimer = setTimeout(() => this.connectSummary(), SUMMARY_RECONNECT_MS);
    };
    es.addEventListener('genai-completed', () => {
      this.load();
      this.notify.show('Summary generated', 'success');
    });
    es.addEventListener('genai-failed', () => {
      this.load();
      this.notify.show('Summary generation failed', 'danger');
    });
  }
}
