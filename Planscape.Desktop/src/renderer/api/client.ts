// Desktop HTTP client — mirrors Planscape mobile client.ts
// Uses electron-store (via IPC) instead of expo-secure-store

const DEFAULT_SERVER = 'http://localhost:5000'

async function getStoredValue<T>(key: string): Promise<T | null> {
  try {
    return await window.electron.store.get<T>(key)
  } catch {
    return null
  }
}

async function setStoredValue(key: string, value: unknown): Promise<void> {
  await window.electron.store.set(key, value)
}

export interface ApiError {
  status: number
  message: string
}

async function refreshAccessToken(): Promise<string | null> {
  const serverUrl = await getStoredValue<string>('serverUrl') ?? DEFAULT_SERVER
  const refreshToken = await getStoredValue<string>('refreshToken')
  if (!refreshToken) return null

  try {
    const res = await fetch(`${serverUrl}/api/auth/refresh`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Client-Type': 'desktop' },
      body: JSON.stringify({ refreshToken })
    })
    if (!res.ok) return null
    const data = await res.json()
    await setStoredValue('accessToken', data.accessToken)
    if (data.refreshToken) await setStoredValue('refreshToken', data.refreshToken)
    return data.accessToken as string
  } catch {
    return null
  }
}

export async function apiFetch<T = unknown>(
  path: string,
  options: RequestInit & { skipAuth?: boolean } = {}
): Promise<T> {
  const serverUrl = await getStoredValue<string>('serverUrl') ?? DEFAULT_SERVER
  let accessToken = await getStoredValue<string>('accessToken')

  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
    'X-Client-Type': 'desktop',
    ...(options.headers as Record<string, string> ?? {})
  }

  if (!options.skipAuth && accessToken) {
    headers['Authorization'] = `Bearer ${accessToken}`
  }

  let response = await fetch(`${serverUrl}${path}`, {
    ...options,
    headers
  })

  // Try token refresh on 401
  if (response.status === 401 && !options.skipAuth) {
    const newToken = await refreshAccessToken()
    if (newToken) {
      headers['Authorization'] = `Bearer ${newToken}`
      response = await fetch(`${serverUrl}${path}`, { ...options, headers })
    }
  }

  if (!response.ok) {
    let message = response.statusText
    try {
      const body = await response.json()
      message = body.message ?? body.title ?? message
    } catch { /* ignore */ }
    const err: ApiError = { status: response.status, message }
    throw err
  }

  // 204 No Content
  if (response.status === 204) return undefined as T

  return response.json() as Promise<T>
}
