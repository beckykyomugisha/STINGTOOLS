'use client';

import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react';

/**
 * U2 — light/dark, class-based (U1 set `darkMode: 'class'`).
 *
 * `system` is a real third state, not a synonym for one of the other two: it
 * tracks the OS live, so a user on auto-dark gets dark at sunset without
 * revisiting a setting.
 */
export type Theme = 'light' | 'dark' | 'system';

const KEY = 'planscape_theme';

interface ThemeState {
  theme: Theme;
  /** What is actually painted right now — `system` resolved against the OS. */
  resolved: 'light' | 'dark';
  setTheme: (t: Theme) => void;
}

const ThemeContext = createContext<ThemeState | null>(null);

function systemPrefersDark(): boolean {
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: dark)').matches;
}

function apply(theme: Theme): 'light' | 'dark' {
  const dark = theme === 'dark' || (theme === 'system' && systemPrefersDark());
  const root = document.documentElement;
  root.classList.toggle('dark', dark);
  root.classList.toggle('light', !dark);
  return dark ? 'dark' : 'light';
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>('system');
  const [resolved, setResolved] = useState<'light' | 'dark'>('light');

  // Read the stored choice on mount, not during render — localStorage doesn't
  // exist on the server, and reading it in render would desync hydration.
  useEffect(() => {
    const stored = (typeof window !== 'undefined' && window.localStorage.getItem(KEY)) as Theme | null;
    const initial: Theme = stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
    setThemeState(initial);
    setResolved(apply(initial));
  }, []);

  // Only follow the OS while the choice IS `system`; an explicit light/dark
  // must survive the OS flipping at sunset.
  useEffect(() => {
    if (theme !== 'system' || typeof window === 'undefined') return;
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => setResolved(apply('system'));
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, [theme]);

  const setTheme = useCallback((t: Theme) => {
    setThemeState(t);
    setResolved(apply(t));
    try {
      window.localStorage.setItem(KEY, t);
    } catch {
      /* private mode — the choice just won't persist */
    }
  }, []);

  return <ThemeContext.Provider value={{ theme, resolved, setTheme }}>{children}</ThemeContext.Provider>;
}

export function useTheme(): ThemeState {
  const ctx = useContext(ThemeContext);
  // A no-op fallback keeps a component usable outside the provider (tests,
  // storybook-ish harnesses) instead of throwing on render.
  return ctx ?? { theme: 'system', resolved: 'light', setTheme: () => {} };
}

/**
 * Blocking inline script for <head>. Without it the first paint is always light
 * and a dark-mode user sees a white flash on every hard navigation. Kept tiny and
 * dependency-free because it runs before React exists.
 */
export const themeInitScript = `(function(){try{var t=localStorage.getItem('${KEY}')||'system';var d=t==='dark'||(t==='system'&&window.matchMedia('(prefers-color-scheme: dark)').matches);var c=document.documentElement.classList;c.toggle('dark',d);c.toggle('light',!d);}catch(e){}})();`;
