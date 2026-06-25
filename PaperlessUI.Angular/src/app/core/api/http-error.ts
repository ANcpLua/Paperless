import { HttpErrorResponse } from '@angular/common/http';

/**
 * True when an error is a transport/network failure (Angular's HttpClient reports
 * `status: 0`) rather than a server HTTP error response. Narrows with `instanceof`
 * so a non-HTTP throwable (e.g. a TypeError) is correctly treated as not-network.
 */
export function isNetworkError(err: unknown): boolean {
  return err instanceof HttpErrorResponse && err.status === 0;
}

/** The RFC-9457 problem `detail` from an HTTP error, or `fallback` for network / non-HTTP errors. */
export function problemDetail(err: unknown, fallback: string): string {
  return (err instanceof HttpErrorResponse ? err.error?.detail : null) ?? fallback;
}
