'use client';

import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { API_BASE, getToken, setToken } from './api';

export interface AuthUser {
  email: string;
  name?: string;
  tenantId?: string;
}

interface AuthState {
  /** True once the initial token restore from localStorage has run. */
  ready: boolean;
  user: AuthUser | null;
  login: (email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AuthContext = createContext<AuthState | null>(null);

/** Decode the (unverified) JWT payload for display only — never for trust. */
function decodeJwt(token: string): AuthUser | null {
  try {
    const part = token.split('.')[1];
    if (!part) return null;
    const json = atob(part.replace(/-/g, '+').replace(/_/g, '/'));
    const p = JSON.parse(json) as Record<string, string>;
    return {
      email: p.email || p.sub || p.unique_name || 'user',
      name: p.name || p.given_name,
      tenantId: p.tenant || p.tenantId || p.tid,
    };
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(false);
  const [user, setUser] = useState<AuthUser | null>(null);

  useEffect(() => {
    const t = getToken();
    if (t) setUser(decodeJwt(t));
    setReady(true);
  }, []);

  async function login(email: string, password: string): Promise<void> {
    const res = await fetch(`${API_BASE}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    });
    if (!res.ok) {
      let message = 'Sign-in failed.';
      try {
        const body = await res.json();
        message = body.message || body.error || message;
      } catch {
        /* keep generic */
      }
      throw new Error(message);
    }
    const data = (await res.json()) as { accessToken?: string; token?: string };
    const token = data.accessToken || data.token;
    if (!token) throw new Error('Sign-in succeeded but no token was returned.');
    setToken(token);
    setUser(decodeJwt(token));
  }

  function logout(): void {
    setToken(null);
    setUser(null);
  }

  return <AuthContext.Provider value={{ ready, user, login, logout }}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider');
  return ctx;
}
