import { create } from 'zustand';

interface AuthState {
  token: string | null;
  userId: string | null;
  tenantId: string | null;
  setAuth: (token: string, userId: string, tenantId: string) => void;
  clear: () => void;
}

export const useAuthStore = create<AuthState>((set) => ({
  token: null,
  userId: null,
  tenantId: null,
  setAuth: (token, userId, tenantId) => set({ token, userId, tenantId }),
  clear: () => set({ token: null, userId: null, tenantId: null }),
}));
