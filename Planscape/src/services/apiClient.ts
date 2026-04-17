import { secureStorage } from './secureStorage';

const BASE_URL = process.env.EXPO_PUBLIC_API_BASE ?? 'https://planscape.example/api';

async function authHeader(): Promise<Record<string, string>> {
  const token = await secureStorage.getToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = {
    'Content-Type': 'application/json',
    ...(await authHeader()),
    ...(init.headers as Record<string, string> | undefined),
  };
  const res = await fetch(`${BASE_URL}${path}`, { ...init, headers });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`HTTP ${res.status}: ${text}`);
  }
  return res.status === 204 ? (undefined as T) : res.json();
}

export const api = {
  // Issues
  listIssues: (projectId: string) =>
    request<unknown[]>(`/projects/${projectId}/issues`),
  createIssue: (projectId: string, body: unknown) =>
    request<unknown>(`/projects/${projectId}/issues`, {
      method: 'POST', body: JSON.stringify(body),
    }),
  updateIssue: (id: string, body: unknown) =>
    request<unknown>(`/issues/${id}`, {
      method: 'PUT', body: JSON.stringify(body),
    }),

  // Elements (for QR lookup)
  lookupElement: (elementId: string) =>
    request<unknown>(`/tagsync/elements/${elementId}`),

  // Documents
  listDocuments: (projectId: string) =>
    request<unknown[]>(`/projects/${projectId}/documents`),
  downloadDocument: (id: string) => `${BASE_URL}/documents/${id}/download`,

  // Compliance
  getCompliance: (projectId: string) =>
    request<unknown>(`/projects/${projectId}/compliance`),
};
