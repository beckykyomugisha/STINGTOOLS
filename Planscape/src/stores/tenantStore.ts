// TENANT-SWITCH — Zustand store for the user's tenant memberships.
//
// When the user belongs to exactly one tenant (the common case) the UI hides
// the switcher entirely. When the count is 2–5 we show an inline picker; 6+
// gets a search box. Decision 3.1 = (c) adaptive.

import { create } from "zustand";

export interface TenantMembership {
  userId: string;
  tenantId: string;
  tenantName: string;
  tenantSlug: string;
  tenantTier: string;
  mimEnabled: boolean;
  role: string;
  isActiveTenant: boolean;
}

interface TenantState {
  memberships: TenantMembership[];
  currentTenantId: string | null;

  setMemberships: (m: TenantMembership[]) => void;
  setCurrentTenant: (tenantId: string) => void;
  clear: () => void;

  /** Derived — how many organisations the user belongs to. */
  count: () => number;

  /** Derived — how the UI should present the switcher. */
  presentation: () => "hidden" | "list" | "search";
}

export const useTenantStore = create<TenantState>((set, get) => ({
  memberships: [],
  currentTenantId: null,

  setMemberships: (memberships) => {
    const active = memberships.find((m) => m.isActiveTenant);
    set({
      memberships,
      currentTenantId: active?.tenantId ?? memberships[0]?.tenantId ?? null,
    });
  },

  setCurrentTenant: (tenantId) => {
    const memberships = get().memberships.map((m) => ({
      ...m,
      isActiveTenant: m.tenantId === tenantId,
    }));
    set({ memberships, currentTenantId: tenantId });
  },

  clear: () => set({ memberships: [], currentTenantId: null }),

  count: () => get().memberships.length,

  presentation: () => {
    const n = get().memberships.length;
    if (n <= 1) return "hidden";
    if (n <= 5) return "list";
    return "search";
  },
}));
