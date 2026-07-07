import { Injectable, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

/** Mirrors the wwwroot SPA toggleTheme(): flips data-bs-theme and persists it. */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private static readonly STORAGE_KEY = 'theme';

  readonly theme = signal<Theme>(ThemeService.readInitial());

  constructor() {
    this.apply(this.theme());
  }

  toggle(): void {
    this.set(this.theme() === 'dark' ? 'light' : 'dark');
  }

  set(theme: Theme): void {
    this.theme.set(theme);
    localStorage.setItem(ThemeService.STORAGE_KEY, theme);
    this.apply(theme);
  }

  private apply(theme: Theme): void {
    document.documentElement.setAttribute('data-bs-theme', theme);
  }

  private static readInitial(): Theme {
    return localStorage.getItem(ThemeService.STORAGE_KEY) === 'dark' ? 'dark' : 'light';
  }
}
