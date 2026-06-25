import { ChangeDetectionStrategy, Component, inject, OnDestroy, OnInit, signal } from '@angular/core';

import { DocumentCardComponent } from '../components/document-card.component';
import { UploadZoneComponent } from '../components/upload-zone.component';
import { DocumentsService } from '../data/documents.service';

@Component({
  selector: 'app-documents-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [UploadZoneComponent, DocumentCardComponent],
  templateUrl: './documents-page.component.html',
  styleUrl: './documents-page.component.css',
})
export class DocumentsPageComponent implements OnInit, OnDestroy {
  protected readonly docs = inject(DocumentsService);
  protected readonly searchTerm = signal('');

  ngOnInit(): void {
    this.docs.connectStreams();
    this.docs.load();
  }

  ngOnDestroy(): void {
    this.docs.disconnectStreams();
  }

  protected async onFiles(files: File[]): Promise<void> {
    for (const file of files) {
      await this.docs.upload(file);
    }
  }

  protected onSearch(): void {
    this.docs.search(this.searchTerm());
  }

  protected onClear(): void {
    this.searchTerm.set('');
    this.docs.clearSearch();
  }
}
