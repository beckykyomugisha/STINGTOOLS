import { apiFetch } from './client'

// ── Auth ──────────────────────────────────────────────────────────────────────

export interface LoginPayload { email: string; password: string }
export interface LoginResponse { accessToken: string; refreshToken: string; user: UserDto }
export interface UserDto {
  id: string; name: string; email: string; role: string; tenantId: string; tenantName: string
}

export const auth = {
  login:   (p: LoginPayload) => apiFetch<LoginResponse>('/api/auth/login', { method: 'POST', body: JSON.stringify(p), skipAuth: true }),
  me:      ()                => apiFetch<UserDto>('/api/auth/me'),
  refresh: (refreshToken: string) =>
    apiFetch<LoginResponse>('/api/auth/refresh', { method: 'POST', body: JSON.stringify({ refreshToken }), skipAuth: true }),
  logout:  ()                => apiFetch('/api/auth/logout', { method: 'POST' })
}

// ── Projects ──────────────────────────────────────────────────────────────────

export interface ProjectDto {
  id: string; name: string; projectCode: string; description?: string
  compliancePercent?: number; memberCount?: number; status: string; createdAt: string
}

export const projects = {
  list:       ()                      => apiFetch<ProjectDto[]>('/api/projects'),
  get:        (id: string)            => apiFetch<ProjectDto>(`/api/projects/${id}`),
  create:     (p: Partial<ProjectDto>) => apiFetch<ProjectDto>('/api/projects', { method: 'POST', body: JSON.stringify(p) }),
  dashboard:  (id: string)            => apiFetch<Record<string, unknown>>(`/api/projects/${id}/dashboard`)
}

// ── Documents ─────────────────────────────────────────────────────────────────

export interface DocumentDto {
  id: string; name: string; revisionCode: string; cdeState: string
  discipline: string; documentType: string; filePath?: string
  uploadedBy: string; uploadedAt: string; fileSize?: number; mimeType?: string
}

export const documents = {
  list:       (projectId: string)     => apiFetch<DocumentDto[]>(`/api/projects/${projectId}/documents`),
  transition: (projectId: string, docId: string, state: string) =>
    apiFetch(`/api/projects/${projectId}/documents/${docId}/transition`, {
      method: 'POST', body: JSON.stringify({ state })
    }),
  download:   (projectId: string, docId: string) =>
    apiFetch<{ downloadUrl: string }>(`/api/projects/${projectId}/documents/${docId}/download`)
}

// ── Issues ────────────────────────────────────────────────────────────────────

export interface IssueDto {
  id: string; title: string; description?: string; status: string
  priority: string; assignedTo?: string; dueDate?: string
  createdBy: string; createdAt: string; slaBreached?: boolean
}

export const issues = {
  list:   (projectId: string)           => apiFetch<IssueDto[]>(`/api/projects/${projectId}/issues`),
  get:    (projectId: string, id: string) => apiFetch<IssueDto>(`/api/projects/${projectId}/issues/${id}`),
  create: (projectId: string, p: Partial<IssueDto>) =>
    apiFetch<IssueDto>(`/api/projects/${projectId}/issues`, { method: 'POST', body: JSON.stringify(p) }),
  update: (projectId: string, id: string, p: Partial<IssueDto>) =>
    apiFetch<IssueDto>(`/api/projects/${projectId}/issues/${id}`, { method: 'PUT', body: JSON.stringify(p) })
}

// ── Compliance ────────────────────────────────────────────────────────────────

export interface ComplianceSnapshot {
  projectId: string; totalElements: number; taggedElements: number
  compliancePercent: number; strictPercent: number; capturedAt: string
}

export const compliance = {
  latest:  (projectId: string) => apiFetch<ComplianceSnapshot>(`/api/projects/${projectId}/compliance/latest`),
  history: (projectId: string) => apiFetch<ComplianceSnapshot[]>(`/api/projects/${projectId}/compliance`),
  trend:   (projectId: string) => apiFetch<Record<string, unknown>>(`/api/projects/${projectId}/compliance/trend`)
}

// ── Tag sync ──────────────────────────────────────────────────────────────────

export const tagSync = {
  sync: (projectId: string, elements: unknown[]) =>
    apiFetch(`/api/tagsync/sync`, { method: 'POST', body: JSON.stringify({ projectId, elements }) })
}

// ── Transmittals ──────────────────────────────────────────────────────────────

export interface TransmittalDto {
  id: string; transmittalNumber: string; subject: string; status: string
  sentBy: string; sentAt?: string; createdAt: string
}

export const transmittals = {
  list:   (projectId: string) => apiFetch<TransmittalDto[]>(`/api/projects/${projectId}/transmittals`),
  create: (projectId: string, p: Partial<TransmittalDto>) =>
    apiFetch<TransmittalDto>(`/api/projects/${projectId}/transmittals`, { method: 'POST', body: JSON.stringify(p) })
}
