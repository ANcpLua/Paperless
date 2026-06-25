import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { DocumentsService } from '../../features/documents/data/documents.service';
import { AppShellComponent } from './app-shell.component';

describe('AppShellComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([]),
        { provide: DocumentsService, useValue: { liveDisconnected: signal(false) } },
      ],
    }).compileComponents();
  });
  afterEach(() => vi.restoreAllMocks());

  it('renders the navbar and a router outlet for routed content', () => {
    const f = TestBed.createComponent(AppShellComponent);
    f.detectChanges();
    expect(f.nativeElement.querySelector('app-navbar')).not.toBeNull();
    // router-outlet must be present — without it routing silently breaks.
    expect(f.nativeElement.querySelector('router-outlet')).not.toBeNull();
  });
});
