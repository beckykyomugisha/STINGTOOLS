// TENANT-SWITCH — API + SecureStore helpers for per-tenant tokens.
//
// Each tenant gets its own token + refresh token under a keyed SecureStore
// entry: `planscape_token:{tenantId}`. This lets the user switch tenants
// instantly without re-login (decision 3.2 = b) while keeping the bearer
// tokens isolated — a leaked tenant-A token can't talk to tenant-B endpoints.

import * as SecureStore from "expo-secure-store";
import { apiFetch, setTokens } from "./client";
import type { TenantMembership } from "@/stores/tenantStore";

const TOKEN_PREFIX = "planscape_token:";
const REFRESH_PREFIX = "planscape_refresh:";
const ACTIVE_TENANT_KEY = "planscape_active_tenant";

export async function fetchMemberships(): Promise<TenantMembership[]> {
  return apiFetch<TenantMembership[]>("/api/auth/tenants");
}

export async function switchTenant(tenantId: string): Promise<{ token: string; refresh: string }> {
  const response = await apiFetch<{ accessToken: string; refreshToken: string }>(
    "/api/auth/switch-tenant",
    {
      method: "POST",
      body: JSON.stringify({ tenantId }),
    }
  );
  // Persist under the per-tenant key AND as the active tokens (client.ts reads
  // those). Caller is expected to update zustand + flush/rehydrate the
  // tenant-scoped stores (offline queue, saved filters) after this returns.
  await persistTenantTokens(tenantId, response.accessToken, response.refreshToken);
  await setTokens(response.accessToken, response.refreshToken);
  await SecureStore.setItemAsync(ACTIVE_TENANT_KEY, tenantId);
  return { token: response.accessToken, refresh: response.refreshToken };
}

export async function persistTenantTokens(tenantId: string, token: string, refresh: string) {
  await SecureStore.setItemAsync(TOKEN_PREFIX + tenantId, token);
  await SecureStore.setItemAsync(REFRESH_PREFIX + tenantId, refresh);
}

export async function loadTenantTokens(tenantId: string): Promise<{ token: string | null; refresh: string | null }> {
  const token   = await SecureStore.getItemAsync(TOKEN_PREFIX + tenantId);
  const refresh = await SecureStore.getItemAsync(REFRESH_PREFIX + tenantId);
  return { token, refresh };
}

export async function getActiveTenantId(): Promise<string | null> {
  return SecureStore.getItemAsync(ACTIVE_TENANT_KEY);
}

export async function setActiveTenantId(tenantId: string): Promise<void> {
  await SecureStore.setItemAsync(ACTIVE_TENANT_KEY, tenantId);
}

/** Clear tenant-scoped tokens (e.g. after sign-out-of-all-tenants). */
export async function clearAllTenantTokens(): Promise<void> {
  // SecureStore has no "list keys" API — tenant IDs must be passed in. Callers
  // should iterate `useTenantStore.memberships` before calling this.
  await SecureStore.deleteItemAsync(ACTIVE_TENANT_KEY);
}
