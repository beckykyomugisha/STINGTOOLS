import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api, ApiError, getToken, setToken } from './api';

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

  it('clears the token and throws on 401', async () => {
    setToken('tok');
    const f = mockFetch(401, {}, false);
    vi.stubGlobal('fetch', f);
    await expect(api('/api/secure')).rejects.toMatchObject({ status: 401 });
    expect(getToken()).toBeNull();
  });
});
