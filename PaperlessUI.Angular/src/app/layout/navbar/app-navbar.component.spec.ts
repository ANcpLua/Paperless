import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { DocumentsService } from '../../features/documents/data/documents.service';
import { ThemeService } from '../../core/theme/theme.service';
import { AppNavbarComponent } from './app-navbar.component';

describe('AppNavbarComponent', () => {
  let theme: { theme: ReturnType<typeof signal<'light' | 'dark'>>; toggle: ReturnType<typeof vi.fn> };
  let docs: { liveDisconnected: ReturnType<typeof signal<boolean>> };

  beforeEach(async () => {
    theme = { theme: signal<'light' | 'dark'>('light'), toggle: vi.fn() };
    docs = { liveDisconnected: signal(false) };
    await TestBed.configureTestingModule({
      imports: [AppNavbarComponent],
      providers: [
        { provide: ThemeService, useValue: theme },
        { provide: DocumentsService, useValue: docs },
      ],
    }).compileComponents();
  });
  afterEach(() => vi.restoreAllMocks());

  it('shows the moon icon in light mode and toggles theme on click', () => {
    const f = TestBed.createComponent(AppNavbarComponent);
    f.detectChanges();
    expect(f.nativeElement.querySelector('i').classList).toContain('bi-moon');
    (f.nativeElement.querySelector('[data-testid="theme-toggle"]') as HTMLButtonElement).click();
    expect(theme.toggle).toHaveBeenCalled();
  });

  it('shows the sun icon in dark mode', () => {
    theme.theme.set('dark');
    const f = TestBed.createComponent(AppNavbarComponent);
    f.detectChanges();
    expect(f.nativeElement.querySelector('i').classList).toContain('bi-sun');
  });

  it('hides the disconnect badge when live updates are connected', () => {
    const f = TestBed.createComponent(AppNavbarComponent);
    f.detectChanges();
    expect(f.nativeElement.querySelector('[data-testid="sse-status"]')).toBeNull();
  });

  it('shows the disconnect badge when live updates drop', () => {
    docs.liveDisconnected.set(true);
    const f = TestBed.createComponent(AppNavbarComponent);
    f.detectChanges();
    expect(f.nativeElement.querySelector('[data-testid="sse-status"]')).not.toBeNull();
  });
});
