import { TestBed } from '@angular/core/testing';

import { ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-bs-theme');
  });
  afterEach(() => vi.restoreAllMocks());

  it('defaults to light when nothing is stored', () => {
    const svc = TestBed.inject(ThemeService);
    expect(svc.theme()).toBe('light');
    expect(document.documentElement.getAttribute('data-bs-theme')).toBe('light');
  });

  it('initializes from a stored dark preference', () => {
    localStorage.setItem('theme', 'dark');
    const svc = TestBed.inject(ThemeService);
    expect(svc.theme()).toBe('dark');
    expect(document.documentElement.getAttribute('data-bs-theme')).toBe('dark');
  });

  it('toggle flips theme, persists it, and updates the attribute', () => {
    const svc = TestBed.inject(ThemeService);
    svc.toggle();
    expect(svc.theme()).toBe('dark');
    expect(localStorage.getItem('theme')).toBe('dark');
    expect(document.documentElement.getAttribute('data-bs-theme')).toBe('dark');

    svc.toggle();
    expect(svc.theme()).toBe('light');
    expect(localStorage.getItem('theme')).toBe('light');
  });

  it('set applies a specific theme', () => {
    const svc = TestBed.inject(ThemeService);
    svc.set('dark');
    expect(svc.theme()).toBe('dark');
    expect(document.documentElement.getAttribute('data-bs-theme')).toBe('dark');
  });
});
