import { api, setToken } from './api';

/**
 * U2 — tenant switching. Both endpoints already exist server-side
 * (`AuthController.GetMemberships` / `SwitchTenant`); nothing on the web app has
 * ever called them, so a consultant who belongs to two firms had to log out and
 * back in. The shell's tenant switcher is the first consumer.
 */

/** One row of `GET /api/auth/tenants` — the same email across several tenants. */
export interface TenantMembership {
  userId: string;
  tenantId: string;
  tenantName: string;
  tenantSlug?: string;
  tenantTier?: string;
  mimEnabled?: boolean;
  role?: string;
  /** True for the tenant the current JWT is scoped to. */
  isActiveTenant: boolean;
}

export function listTenants(): Promise<TenantMembership[]> {
  return api<TenantMembership[]>('/api/auth/tenants');
}

/**
 * Swap the session to another tenant. The server issues a WHOLE NEW JWT (and
 * burns the old refresh token), so the new access token must be stored before
 * anything else is fetched — every subsequent request carries the new tenant.
 *
 * The caller is expected to hard-navigate afterwards rather than re-render:
 * every list in memory belongs to the previous tenant, and a soft transition
 * would briefly paint one firm's data under another firm's name.
 */
export async function switchTenant(tenantId: string): Promise<void> {
  const res = await api<{ accessToken?: string; refreshToken?: string }>('/api/auth/switch-tenant', {
    method: 'POST',
    body: JSON.stringify({ tenantId }),
  });
  if (!res?.accessToken) throw new Error('Tenant switch did not return a token');
  setToken(res.accessToken);
  if (res.refreshToken && typeof window !== 'undefined') {
    window.localStorage.setItem('planscape_refresh', res.refreshToken);
  }
}
