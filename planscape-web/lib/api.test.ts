import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, describeFailure, getToken, isForbidden, setToken } from './api';

function mockFetch(status: number, body: unknown, ok = status < 400) {
  return vi.fn().mockResolvedValue({
    ok,
    status,
    json: async () => body,
  } as Response);
}

describe('token storage', () => {
  beforeEach(() => window.localStorage.clear());

  it('round-trips and clears the token', () => {
    expect(getToken()).toBeNull();
    setToken('abc');
    expect(getToken()).toBe('abc');
    setToken(null);
    expect(getToken()).toBeNull();
  });
});

describe('api()', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    window.localStorage.clear();
  });

  it('attaches the bearer token and parses JSON', async () => {
    setToken('tok123');
    const f = mockFetch(200, { hello: 'world' });
    vi.stubGlobal('fetch', f);

    const out = await api<{ hello: string }>('/api/thing');
    expect(out).toEqual({ hello: 'world' });

    const [url, init] = f.mock.calls[0];
    expect(url).toContain('/api/thing');
    expect(new Headers(init.headers).get('Authorization')).toBe('Bearer tok123');
  });

  it('sets JSON Content-Type when a body is present', async () => {
    const f = mockFetch(200, {});
    vi.stubGlobal('fetch', f);
    await api('/api/x', { method: 'POST', body: JSON.stringify({ a: 1 }) });
    const init = f.mock.calls[0][1];
    expect(new Headers(init.headers).get('Content-Type')).toBe('application/json');
  });

  it('returns undefined for 204', async () => {
    const f = mockFetch(204, null);
    vi.stubGlobal('fetch', f);
    const out = await api('/api/empty', { method: 'DELETE' });
    expect(out).toBeUndefined();
  });

  it('throws ApiError with the server message on failure', async () => {
    const f = mockFetch(400, { message: 'bad input' });
    vi.stubGlobal('fetch', f);
    await expect(api('/api/x')).rejects.toMatchObject({ status: 400, message: 'bad input' });
    await expect(api('/api/x')).rejects.toBeInstanceOf(ApiError);
  });

  it('keeps the parsed error body on the ApiError', async () => {
    // The 402 quota refusal carries its useful sentence in `reason`; `message`
    // resolves to the machine string "quota_exceeded", which is not something to
    // show a person. Callers need the body to do better.
    const f = mockFetch(402, { error: 'quota_exceeded', axis: 'Authors', reason: 'Authors cap reached (5 of 5).' });
    vi.stubGlobal('fetch', f);
    await expect(api('/api/tenant/invite')).rejects.toMatchObject({
      status: 402,
      message: 'quota_exceeded',
      body: { reason: 'Authors cap reached (5 of 5).' },
    });
  });

  it('clears the token and throws on 401', async () => {
    setToken('tok');
    const f = mockFetch(401, {}, false);
    vi.stubGlobal('fetch', f);
    await expect(api('/api/secure')).rejects.toMatchObject({ status: 401 });
    expect(getToken()).toBeNull();
  });
});

// ─────────────────────────────────────────────────────────────────────────
// #558 — a refusal is not a failure.
//
// Every 403 site in this app used to retype the status check and decide for
// itself whether to render a permission answer in the error treatment. These
// pin the one predicate and the one describe helper they now share, plus the
// distinction that makes them useful: whether the server sent a reason at all.
// ─────────────────────────────────────────────────────────────────────────

describe('isForbidden()', () => {
  it('is true only for a 403 ApiError', () => {
    expect(isForbidden(new ApiError(403, 'nope'))).toBe(true);
    expect(isForbidden(new ApiError(500, 'boom'))).toBe(false);
    expect(isForbidden(new ApiError(404, 'gone'))).toBe(false);
  });

  it('is false for a transport failure, which says nothing about permissions', () => {
    // A dropped fetch rejects with a TypeError. Treating it as forbidden would
    // tell a user with every right that they lack a role.
    expect(isForbidden(new TypeError('Failed to fetch'))).toBe(false);
    expect(isForbidden(undefined)).toBe(false);
  });

  it('does not read the status out of the message', () => {
    // ApiError.message is the response BODY. A 200-shaped error whose body
    // happens to mention 403 is not a refusal (#624).
    expect(isForbidden(new ApiError(400, 'the string HTTP 403 appears here'))).toBe(false);
  });
});

describe('ApiError.serverMessage', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    window.localStorage.clear();
  });

  it('is undefined when the response body carried nothing', async () => {
    // ASP.NET Forbid() sends an empty body, so `message` falls back to our own
    // placeholder. Four call sites were showing that placeholder to users.
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: false,
        status: 403,
        json: async () => {
          throw new Error('no body');
        },
      } as unknown as Response),
    );

    const e = (await api('/api/thing').catch((x) => x)) as ApiError;
    expect(e).toBeInstanceOf(ApiError);
    expect(e.serverMessage).toBeUndefined();
    expect(e.message).toBe('Request failed (HTTP 403)');
  });

  it('carries the sentence when the server did send one', async () => {
    vi.stubGlobal('fetch', mockFetch(403, { message: 'Insufficient role for WIP->SHARED transition.' }, false));

    const e = (await api('/api/thing').catch((x) => x)) as ApiError;
    expect(e.serverMessage).toBe('Insufficient role for WIP->SHARED transition.');
  });
});

describe('describeFailure()', () => {
  it('prefers the server sentence over the caller copy — the server owns the rule', () => {
    const e = new ApiError(403, 'Insufficient role.', undefined, 'Insufficient role.');
    expect(describeFailure(e, { forbidden: 'client copy', fallback: 'failed' })).toEqual({
      message: 'Insufficient role.',
      tone: 'forbidden',
    });
  });

  it('names the capability when the server sent an empty body', () => {
    const e = new ApiError(403, 'Request failed (HTTP 403)');
    expect(describeFailure(e, { forbidden: 'Only an Admin can do that.', fallback: 'failed' })).toEqual({
      message: 'Only an Admin can do that.',
      tone: 'forbidden',
    });
  });

  it('never shows the raw placeholder for a refusal', () => {
    const e = new ApiError(403, 'Request failed (HTTP 403)');
    expect(describeFailure(e, { forbidden: 'Only an Admin can do that.', fallback: 'failed' }).message).not.toContain(
      'HTTP 403',
    );
  });

  it('keeps the error tone for everything that is not a 403', () => {
    expect(describeFailure(new ApiError(500, 'boom'), { forbidden: 'f', fallback: 'failed' }).tone).toBe('error');
    expect(describeFailure(new TypeError('Failed to fetch'), { forbidden: 'f', fallback: 'failed' }).tone).toBe(
      'error',
    );
  });
});
