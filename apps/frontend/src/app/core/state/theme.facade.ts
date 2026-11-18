import { Injectable, computed, signal } from '@angular/core';

type Theme = 'light' | 'dark';
const STORAGE_KEY = 'kindred.theme';

/** Light/dark toggle. Default follows the OS; an explicit choice is saved to localStorage. */
@Injectable({ providedIn: 'root' })
export class ThemeFacade {
  private readonly _theme = signal<Theme>(initialTheme());

  readonly theme = this._theme.asReadonly();
  readonly isDark = computed(() => this._theme() === 'dark');

  toggle(): void {
    const next: Theme = this.isDark() ? 'light' : 'dark';
    this._theme.set(next);
    document.documentElement.setAttribute('data-theme', next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // ignore: theme just won't persist
    }
  }
}

function initialTheme(): Theme {
  try {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved === 'light' || saved === 'dark') return saved;
  } catch {
    // fall through to OS preference
  }
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}
