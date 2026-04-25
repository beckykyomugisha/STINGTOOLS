import { useEffect, useState } from 'react';
import { Stack, useRouter, useSegments } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { AppState } from 'react-native';
import { getToken, onSessionExpired } from '@/api/client';
import { crashReporter } from '@/services/crashReporter';
import { notificationTapRouter } from '@/services/notificationTapRouter';
import { initI18n } from '@/i18n';
import { markBackgrounded, challengeIfDue } from '@/services/biometricLock';
import { ErrorBoundary } from '@/components/ErrorBoundary';

export default function RootLayout() {
  const [isReady, setIsReady] = useState(false);
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const segments = useSegments();
  const router = useRouter();

  useEffect(() => {
    crashReporter.init();
    crashReporter.flushPending().catch(() => {});
    const unsubNotif = notificationTapRouter.attach(router);
    // NEW-INT-04 — on refresh-token failure, apiFetch emits this event and we
    // force-navigate to the login screen so the user isn't stuck with silent 401s.
    const unsubSession = onSessionExpired(() => {
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
    checkAuth();
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
  }, [isAuthenticated, segments, isReady]);

  async function checkAuth() {
    // FLEX-15 — load the user's preferred language before we render any screen.
    // initI18n is cheap (no network) and safe to call on every app start.
    try { await initI18n(); } catch { /* non-fatal */ }
    const token = await getToken();
    setIsAuthenticated(!!token);
    setIsReady(true);
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
