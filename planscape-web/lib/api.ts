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
  /**
   * The sentence the SERVER actually sent, when it sent one. Undefined when the
   * response had no usable body and `message` is our own generic placeholder.
   *
   * WHY THE DISTINCTION MATTERS (#558). ASP.NET `Forbid()` returns an EMPTY
   * body, so on a 403 `message` is the literal string
   * `"Request failed (HTTP 403)"` — which four call sites were showing to users
   * verbatim. A forbidden state has to know whether it has a real reason to
   * show or should fall back to naming the capability itself. Recording it as a
   * separate field is the alternative to matching `"Request failed (HTTP "` out
   * of the message, which is the string-sniffing #624 was filed for.
   */
  serverMessage?: string;
  constructor(status: number, message: string, body?: unknown, serverMessage?: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
    this.serverMessage = serverMessage;
  }
}

/**
 * Is this thrown value the server REFUSING a legitimate request, as opposed to
 * the request failing? (#558)
 *
 * Every 403 site in this app used to retype `e instanceof ApiError && e.status
 * === 403`, and each one decided independently whether to treat it as an error.
 * One predicate, so the answer is the same everywhere.
 *
 * Note what this is NOT: a substring test on the message. `ApiError.message` is
 * the response BODY, so `message.includes('HTTP 403')` is an empty-body test and
 * never a status test — the defect #624 was filed for. The status is a number
 * and is read as one.
 *
 * Anything that is not an ApiError — a TypeError from a dropped fetch, an abort
 * — is deliberately NOT forbidden. A transport failure says nothing about
 * permissions, and rendering it as one would tell a user with every right that
 * they lack a role.
 */
// Returns a plain boolean, deliberately not an `e is ApiError` type predicate:
// narrowing an already-ApiError value with that predicate makes the ELSE branch
// `never`, which broke inviteMessage()'s final `return e.message`.
export function isForbidden(e: unknown): boolean {
  return e instanceof ApiError && e.status === 403;
}

/**
 * Turn a caught value into the sentence to show and the treatment to show it
 * in — so every call site stops deciding independently whether a refusal is an
 * error (#558).
 *
 * @param forbidden What to say when the server refused and sent no reason of
 *   its own. Name the capability or the role — "Only a project manager can…" —
 *   never the status. Four sites were showing users the literal string
 *   "Request failed (HTTP 403)" because ASP.NET `Forbid()` sends an empty body.
 *   When the server DOES send a sentence (the CDE transition gates do), that
 *   sentence wins: the server owns the rule, and a client copy would drift.
 * @param fallback What to say when the request genuinely failed and there is no
 *   server message either.
 */
export function describeFailure(
  e: unknown,
  { forbidden, fallback }: { forbidden: string; fallback: string },
): { message: string; tone: 'error' | 'forbidden' } {
  if (e instanceof ApiError && e.status === 403) {
    return { message: e.serverMessage?.trim() || forbidden, tone: 'forbidden' };
  }
  return { message: e instanceof Error ? e.message : fallback, tone: 'error' };
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
    const generic = `Request failed (HTTP ${res.status})`;
    let message = generic;
    let serverMessage: string | undefined;
    let parsed: unknown;
    try {
      const body = await res.json();
      parsed = body;
      serverMessage = body.message || body.error || undefined;
      message = serverMessage || generic;
    } catch {
      /* non-JSON error body — keep the generic message */
    }
    throw new ApiError(res.status, message, parsed, serverMessage);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}
