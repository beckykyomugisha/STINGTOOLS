import { useEffect, useState } from 'react';
import { Stack, useRouter, useSegments } from 'expo-router';
import { StatusBar } from 'expo-status-bar';
import { getToken, onSessionExpired } from '@/api/client';
import { crashReporter } from '@/services/crashReporter';
import { notificationTapRouter } from '@/services/notificationTapRouter';

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
      setIsAuthenticated(false);
      router.replace('/login');
    });
    checkAuth();
    return () => { unsubNotif(); unsubSession(); };
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
    const token = await getToken();
    setIsAuthenticated(!!token);
    setIsReady(true);
  }

  if (!isReady) return null;

  return (
    <>
      <StatusBar style="light" />
      <Stack screenOptions={{ headerShown: false }}>
        <Stack.Screen name="login" />
        <Stack.Screen name="(tabs)" />
      </Stack>
    </>
  );
}
