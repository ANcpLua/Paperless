import { toSummary } from './document.models';

describe('toSummary', () => {
  it('maps an UploadedDocument and defaults all nullable fields', () => {
    const s = toSummary({ id: '1', fileName: 'a.pdf', status: 'Pending', createdAt: '2026-01-01T00:00:00Z' });
    expect(s).toEqual({
      id: '1',
      fileName: 'a.pdf',
      status: 'Pending',
      createdAt: '2026-01-01T00:00:00Z',
      processedAt: null,
      content: null,
      summary: null,
      summaryGeneratedAt: null,
    });
  });

  it('defaults status to Completed and createdAt to empty when absent', () => {
    const s = toSummary({ id: '2', fileName: 'b.pdf' });
    expect(s.status).toBe('Completed');
    expect(s.createdAt).toBe('');
    expect(s.content).toBeNull();
    expect(s.processedAt).toBeNull();
    expect(s.summary).toBeNull();
  });

  it('treats a present-but-empty createdAt as empty', () => {
    const s = toSummary({ id: '4', fileName: 'd.pdf', createdAt: '' });
    expect(s.createdAt).toBe('');
  });

  it('carries search-result optional fields when present', () => {
    const s = toSummary({
      id: '3',
      fileName: 'c.pdf',
      status: 'Completed',
      content: 'text',
      processedAt: '2026-02-02T00:00:00Z',
      summary: 'sum',
      summaryGeneratedAt: '2026-02-03T00:00:00Z',
      createdAt: '2026-02-01T00:00:00Z',
    });
    expect(s.content).toBe('text');
    expect(s.processedAt).toBe('2026-02-02T00:00:00Z');
    expect(s.summary).toBe('sum');
    expect(s.summaryGeneratedAt).toBe('2026-02-03T00:00:00Z');
  });

  it('coalesces present-but-null optional fields to null', () => {
    const s = toSummary({
      id: '5',
      fileName: 'e.pdf',
      status: 'Failed',
      content: null,
      processedAt: null,
      summary: null,
      summaryGeneratedAt: null,
      createdAt: '2026-03-01T00:00:00Z',
    });
    expect(s.status).toBe('Failed');
    expect(s.content).toBeNull();
    expect(s.processedAt).toBeNull();
    expect(s.summary).toBeNull();
    expect(s.summaryGeneratedAt).toBeNull();
  });
});
