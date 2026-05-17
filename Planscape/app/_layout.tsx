import { useEffect, useState } from 'react';
import { Stack, useRouter, useSegments } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { AppState } from 'react-native';
import { getToken, onSessionExpired } from '@/api/client';
import { useAuthStore } from '@/stores/authStore';
import { useProjectStore } from '@/stores/projectStore';
import { realtime } from '@/services/realtimeClient';
import { crashReporter } from '@/services/crashReporter';
import { notificationTapRouter } from '@/services/notificationTapRouter';
import { notificationService } from '@/services/notificationService';
import { initI18n } from '@/i18n';
import { markBackgrounded, challengeIfDue } from '@/services/biometricLock';
import { ErrorBoundary } from '@/components/ErrorBoundary';
import { loadThemePref } from '@/theme/theme';
import { reapOrphanQueuedPhotos } from '../src/utils/offlineQueue';

export default function RootLayout() {
  const [isReady, setIsReady] = useState(false);
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const segments = useSegments();
  const router = useRouter();

  useEffect(() => {
    crashReporter.init();
    crashReporter.flushPending().catch(() => {});
    reapOrphanQueuedPhotos().catch(console.error);
    const unsubNotif = notificationTapRouter.attach(router);
    // NEW-INT-04 — on refresh-token failure, apiFetch emits this event and we
    // force-navigate to the login screen so the user isn't stuck with silent 401s.
    const unsubSession = onSessionExpired(() => {
      // M7 — guard against re-fire while already on /login. The previous
      // implementation dispatched clearProjectsCache + store clear EVERY
      // time a 401 came back, even after the first redirect. On a flaky
      // network, that meant fresh state was being wiped during login
      // attempts. Returning early when we're already unauthenticated
      // makes the handler idempotent.
      if (segments[0] === 'login') return;
      // A6 — clear auth state before disconnecting SignalR on session expiry.
      useAuthStore.getState().clear();
      useProjectStore.getState().clear();
      realtime.disconnect().catch(() => {});
      // Phase 96 — drop any cached server data so the next user on the same
      // device sees fresh tenant-scoped content after re-login.
      import('@/api/endpoints').then(({ clearProjectsCache }) => clearProjectsCache());
      import('@/stores/notificationStore').then(({ useNotificationStore }) =>
        useNotificationStore.getState().clear());
      setIsAuthenticated(false);
      router.replace('/login');
    });
    // T3 — biometric/PIN re-auth on app resume. Silently passes when the lock
    // isn't armed or when the user backgrounded the app only briefly.
    const appStateSub = AppState.addEventListener('change', async (state) => {
      if (state === 'background') {
        try { await markBackgrounded(); } catch { /* ignore */ }
      } else if (state === 'active') {
        try {
          const r = await challengeIfDue();
          if (!r.passed) {
            setIsAuthenticated(false);
            router.replace('/login');
          }
        } catch { /* ignore — never lock the user out on an API failure */ }
      }
    });
    checkAuth().catch(() => {});
    return () => { unsubNotif(); unsubSession(); appStateSub.remove(); };
  }, [router]);

  useEffect(() => {
    if (!isReady) return;

    const inAuthGroup = segments[0] === 'login';

    if (!isAuthenticated && !inAuthGroup) {
      router.replace('/login');
    } else if (isAuthenticated && inAuthGroup) {
      router.replace('/(tabs)');
    }
  }, [isAuthenticated, segments, isReady, router]);

  async function checkAuth() {
    // FLEX-15 — load the user's preferred language before we render any screen.
    // initI18n is cheap (no network) and safe to call on every app start.
    try { await initI18n(); } catch { /* non-fatal */ }
    // Phase 165 (MOB-11) — restore the user's theme preference before first paint
    // so users with a fixed light or dark choice never flash the OS default.
    try { await loadThemePref(); } catch { /* non-fatal */ }
    const token = await getToken();
    setIsAuthenticated(!!token);
    setIsReady(true);
    if (token) {
      // A7 — restore the last active project from AsyncStorage so the dashboard
      // renders the correct project immediately on cold start without waiting for
      // listProjects to resolve.
      useProjectStore.getState().hydrate().catch(() => {});
      // B4 — cold-start push registration when a JWT is already cached.
      notificationService.register().catch((err) => {
        crashReporter.warn('_layout.checkAuth: push register failed', { err: String(err) });
      });
    }
  }

  if (!isReady) return null;

  return (
    <ErrorBoundary>
      <StatusBar style="light" />
      <Stack screenOptions={{ headerShown: false }}>
        <Stack.Screen name="login" />
        <Stack.Screen name="(tabs)" />
      </Stack>
    </ErrorBoundary>
  );
}
