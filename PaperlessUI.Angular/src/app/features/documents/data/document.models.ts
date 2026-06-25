// App-facing document contract, layered over the generated PaperlessREST DTOs in
// ../../../core/api/generated/api-types.ts (mirrors the C# records — the source of truth).
// This file adds the richer `DocumentStatus` union over the wire's bare `string`, the
// normalized `DocumentSummary` view model the UI binds to, and the `toSummary` normalizer.

import type {
  CreateDocumentResponse,
  DocumentDto,
  DocumentSearchResultDto,
  PaginatedDocumentsResponse as ApiPaginatedDocumentsResponse,
  SummaryDto,
} from '../../../core/api/generated/api-types';

export type DocumentStatus = 'Pending' | 'Completed' | 'Failed' | (string & {});

/** POST /api/v1/documents — 202 Accepted body (CreateDocumentResponse, status narrowed). */
export type UploadedDocument = Omit<CreateDocumentResponse, 'status'> & { status: DocumentStatus };

/** GET /api/v1/documents/search — bare array of DocumentSearchResultDto (status narrowed). */
export type DocumentSearchResult = Omit<DocumentSearchResultDto, 'status'> & {
  status: DocumentStatus;
};

/** GET /api/v1/documents/{id}/summary — SummaryDto. */
export type DocumentSummaryResponse = SummaryDto;

/**
 * Normalized view model the UI binds to: every key is present (nullable values coalesced
 * to null by {@link toSummary} / list load). Mirrors the generated {@link DocumentDto},
 * with `status` narrowed to {@link DocumentStatus}.
 */
export interface DocumentSummary {
  id: string;
  fileName: string;
  status: DocumentStatus;
  createdAt: string;
  processedAt: string | null;
  content: string | null;
  summary: string | null;
  summaryGeneratedAt: string | null;
}

/** GET /api/v1/documents — cursor-paginated envelope (items as the view model). */
export interface PaginatedDocumentsResponse {
  items: DocumentSummary[];
  nextCursor: string | null;
  hasMore: boolean;
  count: number;
}

// Compile-time conformance: keep the app types a faithful narrowing of the generated DTOs.
// If a generated field changes shape, one of these resolves to `false` and the build fails.
// The failure branch MUST be `false`, not `never`: `never extends true` is satisfied, so a
// `never` branch would silently pass the Assert constraint and never catch drift.
type Assert<T extends true> = T;
type _SummaryMatchesDto = Assert<DocumentSummary extends DocumentDto ? true : false>;
type _PaginatedMatchesEnvelope = Assert<
  PaginatedDocumentsResponse extends Omit<ApiPaginatedDocumentsResponse, 'items'> & {
    items: DocumentSummary[];
  }
    ? true
    : false
>;
export type { _SummaryMatchesDto, _PaginatedMatchesEnvelope };

/**
 * Tolerant input for {@link toSummary}: the real callers pass {@link UploadedDocument} or
 * {@link DocumentSearchResult}, but the normalizer also defends against absent/null fields so
 * a malformed response degrades gracefully instead of throwing.
 */
type NormalizableDocument = Pick<DocumentSummary, 'id' | 'fileName'> & Partial<DocumentSummary>;

export function toSummary(doc: NormalizableDocument): DocumentSummary {
  return {
    id: doc.id,
    fileName: doc.fileName,
    status: doc.status ?? 'Completed',
    createdAt: 'createdAt' in doc && doc.createdAt ? doc.createdAt : '',
    processedAt: 'processedAt' in doc ? (doc.processedAt ?? null) : null,
    content: 'content' in doc ? (doc.content ?? null) : null,
    summary: 'summary' in doc ? (doc.summary ?? null) : null,
    summaryGeneratedAt: 'summaryGeneratedAt' in doc ? (doc.summaryGeneratedAt ?? null) : null,
  };
}
