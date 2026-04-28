import { useColorScheme } from 'react-native';
import { useEffect, useState } from 'react';
import AsyncStorage from '@react-native-async-storage/async-storage';

export interface Theme {
  bg: string;
  surface: string;
  surfaceElevated: string;
  text: string;
  textMuted: string;
  accent: string;
  border: string;
  success: string;
  warning: string;
  error: string;
}

const light: Theme = {
  bg: '#FFFFFF',
  surface: '#F5F7FA',
  surfaceElevated: '#FFFFFF',
  text: '#1A1A1A',
  textMuted: '#6B7280',
  accent: '#1F3864',
  border: '#E5E7EB',
  success: '#15803D',
  warning: '#B45309',
  error: '#B91C1C',
};

const dark: Theme = {
  bg: '#0F1115',
  surface: '#1A1D23',
  surfaceElevated: '#22272E',
  text: '#F3F4F6',
  textMuted: '#9CA3AF',
  accent: '#2E75B6',
  border: '#2A2E36',
  success: '#22C55E',
  warning: '#F59E0B',
  error: '#EF4444',
};

export type ThemeMode = 'light' | 'dark' | 'system';
const PREF_KEY = 'planscape.theme.mode';

let _mode: ThemeMode = 'system';
const _listeners = new Set<(m: ThemeMode) => void>();

function notify() {
  for (const cb of _listeners) {
    try { cb(_mode); } catch { /* swallow listener crashes */ }
  }
}

/** Phase 165 (MOB-11) — load persisted preference once at app boot. */
export async function loadThemePref(): Promise<ThemeMode> {
  try {
    const v = await AsyncStorage.getItem(PREF_KEY);
    if (v === 'light' || v === 'dark' || v === 'system') {
      _mode = v;
    }
  } catch { /* fall through to default */ }
  notify();
  return _mode;
}

export async function setThemePref(mode: ThemeMode): Promise<void> {
  _mode = mode;
  try { await AsyncStorage.setItem(PREF_KEY, mode); } catch { /* persistence is best-effort */ }
  notify();
}

export function getThemePref(): ThemeMode {
  return _mode;
}

/**
 * Resolves the active Theme. Honours user preference (light/dark) or falls
 * back to the OS color scheme when the preference is 'system'.
 */
export function useTheme(): Theme {
  const scheme = useColorScheme();
  const [mode, setMode] = useState<ThemeMode>(_mode);

  useEffect(() => {
    const cb = (m: ThemeMode) => setMode(m);
    _listeners.add(cb);
    return () => { _listeners.delete(cb); };
  }, []);

  const effective: 'light' | 'dark' =
    mode === 'system' ? (scheme === 'dark' ? 'dark' : 'light') : mode;

  return effective === 'dark' ? dark : light;
}
