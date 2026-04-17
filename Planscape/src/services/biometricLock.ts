// T3 — Biometric / device-PIN lock for app resume.
//
//   armLock(minutes)     — enable and set the idle window (5m default)
//   disarmLock()         — turn off
//   challengeIfDue()     — call on app resume; prompts for Face ID / Touch ID /
//                          Android biometric / device PIN if the app has been
//                          backgrounded longer than the configured window.
//
// No server round-trip. The JWT remains valid throughout; this is an
// on-device lock so a stolen phone can't open the app even if it's still
// signed in.

import * as LocalAuthentication from "expo-local-authentication";
import AsyncStorage from "@react-native-async-storage/async-storage";

const IDLE_WINDOW_KEY = "planscape.biometric.windowMinutes";
const LAST_BG_KEY     = "planscape.biometric.lastBackground";
const ENABLED_KEY     = "planscape.biometric.enabled";
const DEFAULT_WINDOW_MIN = 5;

export async function isSupported(): Promise<boolean> {
  try {
    const has = await LocalAuthentication.hasHardwareAsync();
    if (!has) return false;
    const enrolled = await LocalAuthentication.isEnrolledAsync();
    return enrolled;
  } catch { return false; }
}

export async function isArmed(): Promise<boolean> {
  return (await AsyncStorage.getItem(ENABLED_KEY)) === "1";
}

export async function armLock(windowMinutes: number = DEFAULT_WINDOW_MIN): Promise<void> {
  await AsyncStorage.multiSet([
    [ENABLED_KEY, "1"],
    [IDLE_WINDOW_KEY, String(Math.max(1, Math.round(windowMinutes)))],
  ]);
}

export async function disarmLock(): Promise<void> {
  await AsyncStorage.multiRemove([ENABLED_KEY, IDLE_WINDOW_KEY, LAST_BG_KEY]);
}

/** Call from AppState 'background' handler to stamp the idle start. */
export async function markBackgrounded(): Promise<void> {
  await AsyncStorage.setItem(LAST_BG_KEY, new Date().toISOString());
}

/**
 * Call from AppState 'active' handler. If the lock is armed AND the app has
 * been backgrounded longer than the window, prompt for biometric auth.
 * Returns true when the user passes (or when lock isn't armed).
 */
export async function challengeIfDue(): Promise<{ passed: boolean; reason?: string }> {
  const [enabled, windowStr, lastBgStr] = await Promise.all([
    AsyncStorage.getItem(ENABLED_KEY),
    AsyncStorage.getItem(IDLE_WINDOW_KEY),
    AsyncStorage.getItem(LAST_BG_KEY),
  ]);
  if (enabled !== "1") return { passed: true };

  const windowMs = Math.max(1, Number(windowStr ?? DEFAULT_WINDOW_MIN)) * 60 * 1000;
  if (lastBgStr) {
    const lastBg = new Date(lastBgStr).getTime();
    if (Date.now() - lastBg < windowMs) return { passed: true };
  }

  try {
    const result = await LocalAuthentication.authenticateAsync({
      promptMessage: "Unlock Planscape",
      cancelLabel: "Cancel",
      disableDeviceFallback: false,
    });
    if (result.success) {
      await AsyncStorage.removeItem(LAST_BG_KEY);
      return { passed: true };
    }
    return { passed: false, reason: "cancelled" };
  } catch (err) {
    return { passed: false, reason: err instanceof Error ? err.message : String(err) };
  }
}
