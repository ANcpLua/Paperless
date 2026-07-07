import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';

import { DocumentSummary } from '../data/document.models';

@Component({
  selector: 'app-document-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  templateUrl: './document-card.component.html',
})
export class DocumentCardComponent {
  readonly doc = input.required<DocumentSummary>();
  readonly delete = output<string>();

  protected readonly statusColor = computed(() => {
    const status = this.doc().status;
    if (status === 'Pending') return 'warning';
    if (status === 'Completed') return 'success';
    return 'danger';
  });

  /** OCR preview: only for Completed docs with content, capped at 500 chars (1:1 with the SPA). */
  protected readonly ocrPreview = computed(() => {
    const doc = this.doc();
    if (doc.status !== 'Completed' || !doc.content) return null;
    return doc.content.length > 500 ? `${doc.content.slice(0, 500)}…` : doc.content;
  });
}
