import AsyncStorage from '@react-native-async-storage/async-storage';
import { Platform } from 'react-native';
import * as Application from 'expo-application';
import * as Device from 'expo-device';
import { getBaseUrl, getToken } from '@/api/client';

const STORAGE_KEY = 'planscape_crash_log';
const MAX_ENTRIES = 100;

interface Breadcrumb {
  ts: string;
  level: 'info' | 'warn' | 'error';
  message: string;
  context?: Record<string, unknown>;
}

let breadcrumbs: Breadcrumb[] = [];

function pushBreadcrumb(b: Breadcrumb) {
  breadcrumbs.push(b);
  if (breadcrumbs.length > MAX_ENTRIES) breadcrumbs = breadcrumbs.slice(-MAX_ENTRIES);
}

export const crashReporter = {
  init() {
    const origHandler = (global as { ErrorUtils?: { getGlobalHandler: () => (e: Error, fatal?: boolean) => void; setGlobalHandler: (h: (e: Error, fatal?: boolean) => void) => void } }).ErrorUtils?.getGlobalHandler();
    (global as { ErrorUtils?: { setGlobalHandler: (h: (e: Error, fatal?: boolean) => void) => void } }).ErrorUtils?.setGlobalHandler((err: Error, fatal?: boolean) => {
      this.captureError(err, { fatal: !!fatal });
      origHandler?.(err, fatal);
    });
  },

  info(message: string, context?: Record<string, unknown>) {
    pushBreadcrumb({ ts: new Date().toISOString(), level: 'info', message, context });
  },

  warn(message: string, context?: Record<string, unknown>) {
    pushBreadcrumb({ ts: new Date().toISOString(), level: 'warn', message, context });
    console.warn(`[planscape] ${message}`, context ?? '');
  },

  async captureError(err: unknown, context?: Record<string, unknown>) {
    const e = err instanceof Error ? err : new Error(String(err));
    const entry = {
      ts: new Date().toISOString(),
      message: e.message,
      stack: e.stack,
      context: context ?? {},
      breadcrumbs: breadcrumbs.slice(),
      device: {
        platform: Platform.OS,
        appVersion: Application.nativeApplicationVersion,
        buildVersion: Application.nativeBuildVersion,
        model: Device.modelName,
        osVersion: Device.osVersion,
      },
    };
    pushBreadcrumb({ ts: entry.ts, level: 'error', message: e.message, context });

    try {
      const raw = await AsyncStorage.getItem(STORAGE_KEY);
      const list = raw ? JSON.parse(raw) : [];
      list.push(entry);
      await AsyncStorage.setItem(STORAGE_KEY, JSON.stringify(list.slice(-MAX_ENTRIES)));
    } catch {
      // Storage unavailable — drop the breadcrumb-only path
    }

    // Best-effort upload, don't await long
    this.flushOne(entry).catch(() => {});
  },

  async flushOne(entry: unknown) {
    try {
      const base = await getBaseUrl();
      const token = await getToken();
      const headers: Record<string, string> = { 'Content-Type': 'application/json' };
      if (token) headers['Authorization'] = `Bearer ${token}`;
      await fetch(`${base}/api/diagnostics/crash`, {
        method: 'POST', headers, body: JSON.stringify(entry),
      });
    } catch {
      // Server unreachable — entry stays in AsyncStorage for next session
    }
  },

  async flushPending(): Promise<number> {
    try {
      const raw = await AsyncStorage.getItem(STORAGE_KEY);
      if (!raw) return 0;
      const list: unknown[] = JSON.parse(raw);
      let sent = 0;
      for (const entry of list) {
        try { await this.flushOne(entry); sent++; } catch { break; }
      }
      if (sent === list.length) await AsyncStorage.removeItem(STORAGE_KEY);
      else if (sent > 0) await AsyncStorage.setItem(STORAGE_KEY, JSON.stringify(list.slice(sent)));
      return sent;
    } catch {
      return 0;
    }
  },
};
