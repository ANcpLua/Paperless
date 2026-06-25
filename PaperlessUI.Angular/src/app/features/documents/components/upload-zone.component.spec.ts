import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UploadZoneComponent } from './upload-zone.component';

describe('UploadZoneComponent', () => {
  let f: ComponentFixture<UploadZoneComponent>;
  let emitted: File[][];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [UploadZoneComponent] }).compileComponents();
    f = TestBed.createComponent(UploadZoneComponent);
    f.detectChanges();
    emitted = [];
    f.componentInstance.filesSelected.subscribe((files: File[]) => emitted.push(files));
  });
  afterEach(() => vi.restoreAllMocks());

  const zone = () => f.nativeElement.querySelector('[data-testid="upload-zone"]') as HTMLElement;
  const input = () => f.nativeElement.querySelector('[data-testid="file-input"]') as HTMLInputElement;
  const pdf = (name = 'a.pdf') => new File(['x'], name, { type: 'application/pdf' });

  function dropEvent(files: File[] | null): Event {
    const ev = new Event('drop', { bubbles: true });
    Object.defineProperty(ev, 'dataTransfer', { value: files === null ? null : { files } });
    return ev;
  }

  it('highlights on dragover and clears on dragleave', () => {
    zone().dispatchEvent(new Event('dragover'));
    f.detectChanges();
    expect(zone().classList).toContain('border-info');
    zone().dispatchEvent(new Event('dragleave'));
    f.detectChanges();
    expect(zone().classList).toContain('border-primary');
  });

  it('emits dropped files', () => {
    const file = pdf();
    zone().dispatchEvent(dropEvent([file]));
    expect(emitted).toEqual([[file]]);
  });

  it('ignores a drop with no files', () => {
    zone().dispatchEvent(dropEvent([]));
    expect(emitted).toHaveLength(0);
  });

  it('ignores a drop with no dataTransfer', () => {
    zone().dispatchEvent(dropEvent(null));
    expect(emitted).toHaveLength(0);
  });

  it('emits the files chosen via the file input', () => {
    const file = pdf('chosen.pdf');
    Object.defineProperty(input(), 'files', { value: [file], configurable: true });
    input().dispatchEvent(new Event('change'));
    expect(emitted).toEqual([[file]]);
  });

  it('opens the native dialog when "Choose Files" is clicked', () => {
    const clickSpy = vi.spyOn(input(), 'click').mockImplementation(() => {});
    (f.nativeElement.querySelector('button') as HTMLButtonElement).click();
    expect(clickSpy).toHaveBeenCalled();
  });
});
