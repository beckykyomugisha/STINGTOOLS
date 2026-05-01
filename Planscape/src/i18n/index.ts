// FLEX-15 — Mobile translation helper. Zero runtime dependencies on purpose — swap
// the underlying store for i18next/react-intl/etc later without changing any caller.
//
// Usage:
//   import { t, setLanguage, useT } from "@/i18n";
//   t("common.save")                         → "Save"
//   t("issue.days_open", { n: 3 })           → "3 days open"
//   useT("tabs.home")                        → re-renders when setLanguage() runs
//
// Bundled locales ship with the app (see locales/*.json). At first start the
// wrapper falls back through:
//   AsyncStorage["planscape.language"]  →  device locale  →  "en"

import AsyncStorage from "@react-native-async-storage/async-storage";
import { NativeModules, Platform } from "react-native";
import { useEffect, useState } from "react";

import en from "./locales/en.json";
import de from "./locales/de.json";
import es from "./locales/es.json";
// S6.4 — Swahili (sw) for the Uganda / Kenya / Tanzania / Rwanda /
// DR Congo corridor. Auto-selected when device locale starts with 'sw'.
import sw from "./locales/sw.json";

type Bundle = Record<string, any>;

const BUNDLES: Record<string, Bundle> = { en, de, es, sw };
const FALLBACK = "en";
const STORAGE_KEY = "planscape.language";

let currentLanguage: string = FALLBACK;
const listeners = new Set<(lang: string) => void>();

// ── Public API ────────────────────────────────────────────────────────────

export async function initI18n(): Promise<string> {
  try {
    const stored = await AsyncStorage.getItem(STORAGE_KEY);
    if (stored && BUNDLES[stored]) {
      currentLanguage = stored;
      return stored;
    }
  } catch { /* AsyncStorage unavailable — fall through */ }

  const deviceLang = detectDeviceLanguage();
  if (BUNDLES[deviceLang]) {
    currentLanguage = deviceLang;
  }
  return currentLanguage;
}

export async function setLanguage(lang: string): Promise<void> {
  const normalised = normaliseLanguage(lang);
  if (!BUNDLES[normalised]) {
    console.warn(`[i18n] unsupported language "${lang}" — keeping "${currentLanguage}"`);
    return;
  }
  currentLanguage = normalised;
  try { await AsyncStorage.setItem(STORAGE_KEY, normalised); } catch { /* ignore */ }
  listeners.forEach(fn => fn(normalised));
}

export function getLanguage(): string { return currentLanguage; }

export function supportedLanguages(): string[] { return Object.keys(BUNDLES); }

/**
 * Translate a dotted key with optional parameter substitution.
 * Unknown keys return the literal key so missing translations are visible in dev.
 */
export function t(key: string, vars?: Record<string, string | number>): string {
  const bundle = BUNDLES[currentLanguage] ?? BUNDLES[FALLBACK];
  const fallbackBundle = BUNDLES[FALLBACK];

  const value = lookup(bundle, key) ?? lookup(fallbackBundle, key) ?? key;
  if (!vars) return value;

  return value.replace(/\{([A-Za-z0-9_]+)\}/g, (_, name) => {
    const v = vars[name];
    return v == null ? `{${name}}` : String(v);
  });
}

/** Hook variant — re-renders when the active language changes. */
export function useT(key?: string, vars?: Record<string, string | number>) {
  const [, tick] = useState(0);
  useEffect(() => {
    const fn = () => tick(n => n + 1);
    listeners.add(fn);
    return () => { listeners.delete(fn); };
  }, []);
  return key == null ? t : t(key, vars);
}

// ── Helpers ───────────────────────────────────────────────────────────────

function lookup(bundle: Bundle, key: string): string | null {
  let node: any = bundle;
  for (const segment of key.split(".")) {
    if (node == null || typeof node !== "object") return null;
    node = node[segment];
  }
  return typeof node === "string" ? node : null;
}

function normaliseLanguage(lang: string): string {
  if (!lang) return FALLBACK;
  const code = lang.toLowerCase().trim();
  if (BUNDLES[code]) return code;
  const dash = code.indexOf("-");
  if (dash > 0) {
    const prefix = code.substring(0, dash);
    if (BUNDLES[prefix]) return prefix;
  }
  return FALLBACK;
}

function detectDeviceLanguage(): string {
  try {
    const candidate: string | undefined =
      Platform.OS === "ios"
        ? NativeModules?.SettingsManager?.settings?.AppleLocale
          ?? NativeModules?.SettingsManager?.settings?.AppleLanguages?.[0]
        : NativeModules?.I18nManager?.localeIdentifier
          ?? NativeModules?.I18nManager?.language;
    if (!candidate) return FALLBACK;
    return normaliseLanguage(candidate);
  } catch {
    return FALLBACK;
  }
}
