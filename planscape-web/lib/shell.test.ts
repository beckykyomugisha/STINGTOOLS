import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { crumbsFor, GLOBAL_NAV, PROJECT_NAV } from '@/components/shell/nav';
import { setToken, getToken } from './api';

/**
 * U2 — the parts of the shell that are logic rather than pixels.
 *
 * Breadcrumb derivation and tenant switching are where a shell actually breaks:
 * a raw GUID in the breadcrumb, or a tenant switch that leaves the old token in
 * place and silently keeps showing the previous firm's data. Neither is visible
 * in a build, so they're asserted here. Layout itself is PENDING-HUMAN-VERIFY.
 */

describe('breadcrumbs', () => {
  it('labels known segments in human words', () => {
    expect(crumbsFor('/projects').map((c) => c.label)).toEqual(['Projects']);
    expect(crumbsFor('/settings/tokens').map((c) => c.label)).toEqual(['Settings', 'Access tokens']);
  });

  it('shortens GUIDs instead of printing a 36-character wall', () => {
    const crumbs = crumbsFor('/projects/3fa85f64-5717-4562-b3fc-2c963f66afa6/issues');
    expect(crumbs.map((c) => c.label)).toEqual(['Projects', '#3fa85f64', 'Issues']);
  });

  it('uses the project name when the caller knows it', () => {
    const crumbs = crumbsFor('/projects/3fa85f64-5717-4562-b3fc-2c963f66afa6/clashes', 'Riverside Tower');
    expect(crumbs.map((c) => c.label)).toEqual(['Projects', 'Riverside Tower', 'Clashes']);
  });

  it('never links the last crumb — you are already there', () => {
    const crumbs = crumbsFor('/projects/abc/issues');
    expect(crumbs[crumbs.length - 1].href).toBeUndefined();
    expect(crumbs[0].href).toBe('/projects');
  });

  it('builds cumulative hrefs for the ancestors', () => {
    const crumbs = crumbsFor('/projects/3fa85f64-5717-4562-b3fc-2c963f66afa6/meetings/new');
    expect(crumbs.map((c) => c.href)).toEqual([
      '/projects',
      '/projects/3fa85f64-5717-4562-b3fc-2c963f66afa6',
      '/projects/3fa85f64-5717-4562-b3fc-2c963f66afa6/meetings',
      undefined,
    ]);
  });

  it('handles the root without inventing a crumb', () => {
    expect(crumbsFor('/')).toEqual([]);
  });
});

describe('nav model', () => {
  it('gives every item a label and an icon path', () => {
    for (const item of [...GLOBAL_NAV, ...PROJECT_NAV]) {
      expect(item.label.length, JSON.stringify(item)).toBeGreaterThan(0);
      expect(item.icon.startsWith('M'), `${item.label} icon must be an SVG path`).toBe(true);
    }
  });

  it('has exactly one project-overview entry (the empty segment)', () => {
    expect(PROJECT_NAV.filter((i) => i.segment === '')).toHaveLength(1);
  });

  it('has no duplicate segments, which would render two identical rail rows', () => {
    const segs = PROJECT_NAV.map((i) => i.segment);
    expect(new Set(segs).size).toBe(segs.length);
  });

  it('points global nav at absolute paths', () => {
    for (const item of GLOBAL_NAV) expect(item.segment.startsWith('/')).toBe(true);
  });
});

describe('tenant switching', () => {
  let f: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    setToken('old-token');
    f = vi.fn(async () => new Response(JSON.stringify({ accessToken: 'new-token', refreshToken: 'new-refresh' }), { status: 200 }));
    vi.stubGlobal('fetch', f);
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    setToken(null);
  });

  it('stores the NEW token, so the next request is scoped to the new tenant', async () => {
    const { switchTenant } = await import('./tenants');
    await switchTenant('3fa85f64-5717-4562-b3fc-2c963f66afa6');
    // The whole point: leaving the old token in place would keep serving the
    // previous firm's data under the new firm's name.
    expect(getToken()).toBe('new-token');
    expect(window.localStorage.getItem('planscape_refresh')).toBe('new-refresh');
  });

  it('sends the tenant id to the switch endpoint', async () => {
    const { switchTenant } = await import('./tenants');
    await switchTenant('abc-123');
    const [url, init] = f.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toContain('/api/auth/switch-tenant');
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ tenantId: 'abc-123' });
  });

  it('throws and keeps the old token when the server returns no token', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({}), { status: 200 })));
    const { switchTenant } = await import('./tenants');
    await expect(switchTenant('abc')).rejects.toThrow(/did not return a token/i);
    expect(getToken()).toBe('old-token');
  });
});
