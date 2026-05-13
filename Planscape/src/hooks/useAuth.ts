import { useState, useCallback } from 'react';
import { login as apiLogin, getMe } from '@/api/endpoints';
import { setTokens, setBaseUrl, clearTokens, getToken } from '@/api/client';
import type { UserProfile } from '@/types/api';
import { notificationService } from '@/services/notificationService';
import { crashReporter } from '@/services/crashReporter';
import { useAuthStore } from '@/stores/authStore';
import { realtime } from '@/services/realtimeClient';

export function useAuth() {
  const [user, setUser] = useState<UserProfile | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const login = useCallback(
    async (email: string, password: string, serverUrl?: string) => {
      setLoading(true);
      setError(null);
      try {
        if (serverUrl) {
          await setBaseUrl(serverUrl);
        }
        const res = await apiLogin({ email, password });
        await setTokens(res.token, res.refreshToken);
        setUser(res.user);
        // Phase 142 — populate the global authStore so screens can identify
        // "the current user" without a round-trip to /me. Was previously
        // declared but never set, leaving issue-detail / documents reading
        // null.
        useAuthStore.getState().setAuth(
          res.token, res.user.id, res.user.tenantId,
          res.user.email, res.user.displayName);
        // B4 — register the device push token with the server now that we
        // have a JWT. Fire-and-forget; permission may be denied or the
        // server may be unreachable, neither of which should block sign-in.
        notificationService.register().catch((err) => {
          crashReporter.warn('useAuth.login: push register failed', { err: String(err) });
        });
        return true;
      } catch (err: unknown) {
        const message =
          err instanceof Error ? err.message : 'Login failed';
        setError(message);
        return false;
      } finally {
        setLoading(false);
      }
    },
    [],
  );

  const logout = useCallback(async () => {
    // A6 — clear auth state FIRST so any reconnect attempt finds no token.
    useAuthStore.getState().clear();
    setUser(null);
    await clearTokens();
    realtime.disconnect().catch(() => {});
  }, []);

  const restoreSession = useCallback(async () => {
    const token = await getToken();
    if (!token) return false;
    try {
      const profile = await getMe();
      setUser(profile);
      // Phase 142 — refresh the authStore on session restore so consumers
      // (e.g. issues "Mine" filter) have the user identity immediately on
      // cold start.
      useAuthStore.getState().setAuth(
        token, profile.id, profile.tenantId,
        profile.email, profile.displayName);
      // B4 — re-register on session restore so a re-issued Expo token (after
      // app reinstall or token rotation) is pushed up at least once per cold
      // start.
      notificationService.register().catch((err) => {
        crashReporter.warn('useAuth.restoreSession: push register failed', { err: String(err) });
      });
      return true;
    } catch {
      await clearTokens();
      return false;
    }
  }, []);

  return { user, loading, error, login, logout, restoreSession };
}
