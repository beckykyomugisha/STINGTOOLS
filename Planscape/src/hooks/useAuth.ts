import { useState, useCallback } from 'react';
import { login as apiLogin, getMe } from '@/api/endpoints';
import { setTokens, setBaseUrl, clearTokens, getToken } from '@/api/client';
import type { UserProfile } from '@/types/api';

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
    await clearTokens();
    setUser(null);
  }, []);

  const restoreSession = useCallback(async () => {
    const token = await getToken();
    if (!token) return false;
    try {
      const profile = await getMe();
      setUser(profile);
      return true;
    } catch {
      await clearTokens();
      return false;
    }
  }, []);

  return { user, loading, error, login, logout, restoreSession };
}
