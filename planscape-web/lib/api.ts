// Minimal fetch wrapper for the Planscape API: attaches the bearer token,
// normalises errors, and bounces to /login on 401. Bearer-token auth (not
// cookies), so cross-origin calls need the API's CORS allow-list to include
// this app's origin (app.planscape.build is allow-listed; for localhost dev,
// add it to Cors__Origins on the server or run the API locally).

const TOKEN_KEY = 'planscape_token';

export const API_BASE = (process.env.NEXT_PUBLIC_API_BASE || 'https://api.planscape.build').replace(/\/$/, '');

export function getToken(): string | null {
  if (typeof window === 'undefined') return null;
  return window.localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string | null): void {
  if (typeof window === 'undefined') return;
  if (token) window.localStorage.setItem(TOKEN_KEY, token);
  else window.localStorage.removeItem(TOKEN_KEY);
}

export class ApiError extends Error {
  status: number;
  /**
   * The parsed error body, when there was one. `message` is still the field a
   * caller should show by default; this exists for the handful of responses that
   * carry structured detail worth rendering differently — e.g. the 402
   * `{ error: 'quota_exceeded', axis, current, max, reason, upgrade_url }`,
   * whose useful sentence is `reason`, not `error`.
   */
  body?: unknown;
  constructor(status: number, message: string, body?: unknown) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = getToken();
  const headers = new Headers(init.headers);
  if (token) headers.set('Authorization', `Bearer ${token}`);
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json');

  const res = await fetch(`${API_BASE}${path}`, { ...init, headers });

  if (res.status === 401) {
    setToken(null);
    if (typeof window !== 'undefined' && window.location.pathname !== '/login') {
      window.location.href = '/login';
    }
    throw new ApiError(401, 'Session expired — please sign in again.');
  }

  if (!res.ok) {
    let message = `Request failed (HTTP ${res.status})`;
    let parsed: unknown;
    try {
      const body = await res.json();
      parsed = body;
      message = body.message || body.error || message;
    } catch {
      /* non-JSON error body — keep the generic message */
    }
    throw new ApiError(res.status, message, parsed);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}
