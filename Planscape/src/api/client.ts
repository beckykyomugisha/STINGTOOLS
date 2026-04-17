import * as SecureStore from 'expo-secure-store';

const TOKEN_KEY = 'planscape_token';
const REFRESH_KEY = 'planscape_refresh';
const BASE_URL_KEY = 'planscape_base_url';

const DEFAULT_BASE_URL = 'http://localhost:5000';

let cachedBaseUrl: string | null = null;

export async function getBaseUrl(): Promise<string> {
  if (cachedBaseUrl) return cachedBaseUrl;
  const stored = await SecureStore.getItemAsync(BASE_URL_KEY);
  cachedBaseUrl = stored || DEFAULT_BASE_URL;
  return cachedBaseUrl;
}

export async function setBaseUrl(url: string): Promise<void> {
  cachedBaseUrl = url;
  await SecureStore.setItemAsync(BASE_URL_KEY, url);
}

export async function getToken(): Promise<string | null> {
  return SecureStore.getItemAsync(TOKEN_KEY);
}

export async function setTokens(token: string, refresh: string): Promise<void> {
  await SecureStore.setItemAsync(TOKEN_KEY, token);
  await SecureStore.setItemAsync(REFRESH_KEY, refresh);
}

export async function clearTokens(): Promise<void> {
  await SecureStore.deleteItemAsync(TOKEN_KEY);
  await SecureStore.deleteItemAsync(REFRESH_KEY);
  cachedBaseUrl = null;
}

// ── NEW-INT-04 — session-expired event bus ──────────────────────────────
// Root layout subscribes and pushes the user to /login when emitted.
type Listener = () => void;
const sessionExpiredListeners = new Set<Listener>();
export function onSessionExpired(listener: Listener): () => void {
  sessionExpiredListeners.add(listener);
  return () => sessionExpiredListeners.delete(listener);
}
function emitSessionExpired() {
  sessionExpiredListeners.forEach(l => { try { l(); } catch { /* never throw from listener */ } });
}

// ── Refresh-token single-flight ─────────────────────────────────────────
// Multiple concurrent 401 responses would otherwise all try to refresh.
// Collapse into one in-flight call and share the result.
let refreshInFlight: Promise<string | null> | null = null;
async function refreshAccessToken(): Promise<string | null> {
  if (refreshInFlight) return refreshInFlight;
  refreshInFlight = (async () => {
    try {
      const refresh = await SecureStore.getItemAsync(REFRESH_KEY);
      if (!refresh) return null;

      const base = await getBaseUrl();
      const res = await fetch(`${base}/api/auth/refresh`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: refresh }),
      });

      if (!res.ok) return null;

      const data = await res.json();
      await setTokens(data.token, data.refreshToken);
      return data.token;
    } finally {
      // Drop the cached promise once complete so the next 401 can trigger a
      // fresh refresh later.
      setTimeout(() => { refreshInFlight = null; }, 0);
    }
  })();
  return refreshInFlight;
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}

export async function apiFetch<T>(
  path: string,
  options: RequestInit = {}
): Promise<T> {
  const base = await getBaseUrl();
  let token = await getToken();

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    ...(options.headers as Record<string, string>),
  };

  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  let res = await fetch(`${base}${path}`, { ...options, headers });

  // Auto-refresh on 401 — except for the login/refresh endpoints themselves,
  // which must fail cleanly without triggering another refresh attempt.
  const isAuthEndpoint = path.startsWith('/api/auth/login') || path.startsWith('/api/auth/refresh');
  if (res.status === 401 && token && !isAuthEndpoint) {
    const newToken = await refreshAccessToken();
    if (newToken) {
      headers['Authorization'] = `Bearer ${newToken}`;
      res = await fetch(`${base}${path}`, { ...options, headers });
    } else {
      // NEW-INT-04 — refresh-token path itself rejected us (revoked / expired).
      // Wipe local credentials and notify the app to navigate to /login.
      await clearTokens();
      emitSessionExpired();
    }
  }

  if (!res.ok) {
    const body = await res.text();
    throw new ApiError(res.status, body || `HTTP ${res.status}`);
  }

  const text = await res.text();
  return text ? JSON.parse(text) : ({} as T);
}
