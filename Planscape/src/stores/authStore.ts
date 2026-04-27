import { create } from 'zustand';

interface AuthState {
  token: string | null;
  userId: string | null;
  tenantId: string | null;
  // Phase 142 — store display fields so screens can match assignees by name
  // for legacy issues that pre-date the AssigneeUserId migration without an
  // extra round-trip to /me.
  email: string | null;
  displayName: string | null;
  setAuth: (
    token: string,
    userId: string,
    tenantId: string,
    email?: string | null,
    displayName?: string | null,
  ) => void;
  clear: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  token: null,
  userId: null,
  tenantId: null,
  email: null,
  displayName: null,
  setAuth: (token, userId, tenantId, email = null, displayName = null) =>
    set({ token, userId, tenantId, email, displayName }),
  clear: () =>
    set({ token: null, userId: null, tenantId: null, email: null, displayName: null }),
}));
