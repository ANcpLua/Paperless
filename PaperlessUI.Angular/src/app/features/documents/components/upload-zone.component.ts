import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  output,
  signal,
  viewChild,
} from '@angular/core';

/** Drag-and-drop + click-to-browse upload zone (1:1 with the SPA initializeUpload). */
@Component({
  selector: 'app-upload-zone',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './upload-zone.component.html',
})
export class UploadZoneComponent {
  readonly filesSelected = output<File[]>();

  protected readonly dragging = signal(false);
  private readonly fileInput = viewChild.required<ElementRef<HTMLInputElement>>('fileInput');

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDragLeave(): void {
    this.dragging.set(false);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.emit(event.dataTransfer?.files);
  }

  protected onChange(event: Event): void {
    this.emit((event.target as HTMLInputElement).files);
  }

  protected browse(): void {
    this.fileInput().nativeElement.click();
  }

  private emit(files: FileList | null | undefined): void {
    if (files && files.length > 0) {
      this.filesSelected.emit(Array.from(files));
    }
  }
}
