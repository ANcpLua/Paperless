import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AppComponent } from './app.component';
import { NotificationService } from './core/notifications/notification.service';
import { DocumentsService } from './features/documents/data/documents.service';

describe('AppComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [
        provideRouter([]),
        { provide: DocumentsService, useValue: { liveDisconnected: signal(false) } },
      ],
    }).compileComponents();
  });
  afterEach(() => vi.restoreAllMocks());

  it('mounts the shell and renders pushed notifications through its host', () => {
    vi.useFakeTimers();
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-shell')).not.toBeNull();

    // The notifications host must actually render service output — not just exist.
    TestBed.inject(NotificationService).show('hello from the app shell', 'success');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('hello from the app shell');
    vi.useRealTimers();
  });
});
