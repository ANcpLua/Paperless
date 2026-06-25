import { HttpErrorResponse } from '@angular/common/http';

import { isNetworkError, problemDetail } from './http-error';

describe('isNetworkError', () => {
  it('is true for a status-0 HttpErrorResponse (transport failure)', () => {
    expect(isNetworkError(new HttpErrorResponse({ status: 0 }))).toBe(true);
  });

  it('is false for an HTTP error response', () => {
    expect(isNetworkError(new HttpErrorResponse({ status: 500 }))).toBe(false);
  });

  it('is false for a non-HTTP throwable', () => {
    expect(isNetworkError(new TypeError('boom'))).toBe(false);
  });
});

describe('problemDetail', () => {
  it('returns the RFC-9457 detail when present', () => {
    const err = new HttpErrorResponse({ error: { detail: 'too big' }, status: 413 });
    expect(problemDetail(err, 'fallback')).toBe('too big');
  });

  it('falls back when the error body has no detail', () => {
    const err = new HttpErrorResponse({ error: {}, status: 500 });
    expect(problemDetail(err, 'fallback')).toBe('fallback');
  });

  it('falls back for a network error with no body', () => {
    expect(problemDetail(new HttpErrorResponse({ status: 0 }), 'fallback')).toBe('fallback');
  });

  it('falls back for a non-HTTP throwable', () => {
    expect(problemDetail(new Error('x'), 'fallback')).toBe('fallback');
  });
});
