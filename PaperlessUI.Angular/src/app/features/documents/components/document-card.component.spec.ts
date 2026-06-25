import { ComponentFixture, TestBed } from '@angular/core/testing';

import { DocumentSummary } from '../data/document.models';
import { DocumentCardComponent } from './document-card.component';

function makeDoc(over: Partial<DocumentSummary> = {}): DocumentSummary {
  return {
    id: 'd1',
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

describe('DocumentCardComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DocumentCardComponent] }).compileComponents();
  });
  afterEach(() => vi.restoreAllMocks());

  function render(doc: DocumentSummary): ComponentFixture<DocumentCardComponent> {
    const f = TestBed.createComponent(DocumentCardComponent);
    f.componentRef.setInput('doc', doc);
    f.detectChanges();
    return f;
  }
  const badge = (f: ComponentFixture<DocumentCardComponent>) =>
    f.nativeElement.querySelector('[data-testid="doc-status"]') as HTMLElement;

  it('renders the filename and an upload date', () => {
    const f = render(makeDoc({ fileName: 'report.pdf' }));
    expect(f.nativeElement.querySelector('[data-testid="doc-filename"]').textContent).toContain('report.pdf');
    expect(f.nativeElement.textContent).toContain('Uploaded:');
  });

  it('shows "Unknown" when createdAt is empty', () => {
    const f = render(makeDoc({ createdAt: '' }));
    expect(f.nativeElement.textContent).toContain('Unknown');
  });

  it('renders the processed-date value when present', () => {
    const f = render(makeDoc({ processedAt: '2026-01-02T12:00:00Z' }));
    expect(f.nativeElement.textContent as string).toMatch(/Processed:.*2026/);
  });

  it('omits the processed line when processedAt is null', () => {
    const f = render(makeDoc({ processedAt: null }));
    expect(f.nativeElement.textContent).not.toContain('Processed:');
  });

  it('maps Pending -> warning badge with a spinner', () => {
    const f = render(makeDoc({ status: 'Pending' }));
    expect(badge(f).classList).toContain('bg-warning');
    expect(f.nativeElement.querySelector('.spinner-border')).not.toBeNull();
  });

  it('maps Completed -> success badge', () => {
    const f = render(makeDoc({ status: 'Completed' }));
    expect(badge(f).classList).toContain('bg-success');
  });

  it('maps any other status -> danger badge', () => {
    const f = render(makeDoc({ status: 'Failed' }));
    expect(badge(f).classList).toContain('bg-danger');
  });

  it('renders an OCR preview for Completed docs, capped at 500 chars with an ellipsis', () => {
    const f = render(makeDoc({ status: 'Completed', content: 'A'.repeat(500) + 'TAIL' }));
    const text = f.nativeElement.textContent as string;
    expect(text).toContain('OCR Text Preview');
    expect(text).toContain('…');
    expect(text).not.toContain('TAIL');
  });

  it('does not add an ellipsis to short OCR content', () => {
    const f = render(makeDoc({ status: 'Completed', content: 'short text' }));
    expect(f.nativeElement.textContent).toContain('short text');
    expect((f.nativeElement.textContent as string)).not.toContain('…');
  });

  it('hides the OCR preview when not Completed', () => {
    const f = render(makeDoc({ status: 'Pending', content: 'hidden' }));
    expect(f.nativeElement.textContent).not.toContain('OCR Text Preview');
  });

  it('hides the OCR preview when Completed but content is null', () => {
    const f = render(makeDoc({ status: 'Completed', content: null }));
    expect(f.nativeElement.textContent).not.toContain('OCR Text Preview');
  });

  it('renders the AI summary section when a summary is present', () => {
    const f = render(makeDoc({ summary: 'the summary' }));
    expect(f.nativeElement.querySelector('[data-testid="doc-summary"]').textContent).toContain('the summary');
  });

  it('emits delete with the document id on trash click', () => {
    const f = render(makeDoc({ id: 'del-1' }));
    let emitted: string | undefined;
    f.componentInstance.delete.subscribe((id: string) => (emitted = id));
    (f.nativeElement.querySelector('[data-testid="doc-delete"]') as HTMLButtonElement).click();
    expect(emitted).toBe('del-1');
  });
});
