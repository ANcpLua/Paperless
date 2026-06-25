/**
 * PaperlessREST DTO contract — the "better implementation" source of truth.
 *
 * These types mirror the C# transport records in
 *   PaperlessREST/Contracts/DocumentManagement/DocumentDtos.cs
 *   PaperlessREST/Features/DocumentManagement/Presentation/Dto/DTOs.cs
 * field-for-field (Guid → string<uuid>, DateTimeOffset → string<date-time>,
 * `required`/non-nullable → present, nullable reference/`Nullable<T>` → `?: T | null`).
 *
 * Shaped like `openapi-typescript` output (`components["schemas"][...]`) so it is a
 * drop-in once the codegen pipeline is wired. NOTE: `dotnet build PaperlessREST.csproj`
 * does NOT currently emit `openapi/paperless.json` (no Microsoft.Extensions.ApiDescription.Server),
 * so `pnpm run generate:openapi` cannot produce the JSON yet — these types are derived by
 * hand from the C# records until build-time OpenAPI emit is added. Regenerate from there.
 */

export interface components {
  schemas: {
    /** Query parameters for paginated document listing (cursor-based, GUIDv7 ids). */
    PaginationQuery: {
      /** 1..100; handler applies the server default when omitted. */
      pageSize?: number | null;
      /** Last document id from the previous page (uuid). */
      cursor?: string | null;
    };

    /** Query parameters for full-text document search. */
    SearchQuery: {
      /** 1..100 chars. */
      query: string;
      /** 1..100. Non-nullable C# `int` (server-defaulted to DefaultResultLimit). */
      limit: number;
    };

    /** Document metadata returned by the API (GET /documents, GET /documents/{id}). */
    DocumentDto: {
      /** uuid */
      id: string;
      fileName: string;
      /** Pending | Completed | Failed */
      status: string;
      /** date-time (UTC) */
      createdAt: string;
      /** date-time (UTC) */
      processedAt?: string | null;
      /** OCR extracted text content. */
      content?: string | null;
      /** AI-generated summary. */
      summary?: string | null;
      /** date-time (UTC) */
      summaryGeneratedAt?: string | null;
    };

    /** 202 Accepted body returned after a successful upload (POST /documents). */
    CreateDocumentResponse: {
      /** uuid */
      id: string;
      fileName: string;
      status: string;
      /** date-time (UTC) */
      createdAt: string;
    };

    /** A single full-text search hit (GET /documents/search). */
    DocumentSearchResultDto: {
      /** uuid */
      id: string;
      fileName: string;
      content?: string | null;
      summary?: string | null;
      /** date-time (UTC) */
      createdAt: string;
      status: string;
    };

    /** AI summary envelope (GET /documents/{id}/summary). */
    SummaryDto: {
      summary?: string | null;
    };

    /** Cursor-paginated document listing (GET /documents). */
    PaginatedDocumentsResponse: {
      items: components['schemas']['DocumentDto'][];
      /** uuid; null when there are no more pages. */
      nextCursor?: string | null;
      hasMore: boolean;
      /** Always equals items.length (computed server-side). */
      count: number;
    };

    /**
     * Multipart upload input (POST /documents). Carries an IFormFile server-side;
     * the SPA posts it as multipart/form-data under the field name `File` — ASP.NET Core
     * form binding is case-insensitive, so it binds to the endpoint's `file` parameter.
     * Typed as `Blob` for browser ergonomics (a DOM `File` is a `Blob`); a real
     * openapi-typescript run would emit `string` (format binary) for the IFormFile instead.
     */
    UploadDocumentRequest: {
      file: Blob;
    };
  };
}

export type PaginationQuery = components['schemas']['PaginationQuery'];
export type SearchQuery = components['schemas']['SearchQuery'];
export type DocumentDto = components['schemas']['DocumentDto'];
export type CreateDocumentResponse = components['schemas']['CreateDocumentResponse'];
export type DocumentSearchResultDto = components['schemas']['DocumentSearchResultDto'];
export type SummaryDto = components['schemas']['SummaryDto'];
export type PaginatedDocumentsResponse = components['schemas']['PaginatedDocumentsResponse'];
export type UploadDocumentRequest = components['schemas']['UploadDocumentRequest'];
