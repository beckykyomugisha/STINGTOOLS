import { create } from 'zustand'

export interface User {
  id: string
  name: string
  email: string
  role: string
  tenantId: string
  tenantName: string
}

interface AuthState {
  user: User | null
  token: string | null
  isLoading: boolean
  error: string | null
  login: (email: string, password: string) => Promise<boolean>
  logout: () => Promise<void>
  restoreSession: () => Promise<void>
}

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  token: null,
  isLoading: false,
  error: null,

  login: async (email, password) => {
    set({ isLoading: true, error: null })
    try {
      const serverUrl = await window.electron.store.get<string>('serverUrl') ?? 'http://localhost:5000'
      const res = await fetch(`${serverUrl}/api/auth/login`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'X-Client-Type': 'desktop' },
        body: JSON.stringify({ email, password })
      })
      if (!res.ok) {
        const body = await res.json().catch(() => ({}))
        set({ isLoading: false, error: body.message ?? 'Login failed' })
        return false
      }
      const data = await res.json()
      await window.electron.store.set('accessToken', data.accessToken)
      await window.electron.store.set('refreshToken', data.refreshToken)
      set({ token: data.accessToken, user: data.user, isLoading: false })
      return true
    } catch (err) {
      set({ isLoading: false, error: err instanceof Error ? err.message : 'Network error' })
      return false
    }
  },

  logout: async () => {
    await window.electron.store.set('accessToken', '')
    await window.electron.store.set('refreshToken', '')
    set({ user: null, token: null })
  },

  restoreSession: async () => {
    const token = await window.electron.store.get<string>('accessToken')
    if (!token) return

    const serverUrl = await window.electron.store.get<string>('serverUrl') ?? 'http://localhost:5000'
    try {
      const res = await fetch(`${serverUrl}/api/auth/me`, {
        headers: { 'Authorization': `Bearer ${token}`, 'X-Client-Type': 'desktop' }
      })
      if (res.ok) {
        const user = await res.json()
        set({ token, user })
      } else {
        await window.electron.store.set('accessToken', '')
      }
    } catch { /* offline — keep token, will retry */ }
  }
}))
