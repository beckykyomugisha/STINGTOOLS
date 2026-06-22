import { api, API_BASE, getToken } from './api';
import type {
  Project,
  BimIssue,
  IssueComment,
  ClashRecord,
  ClashListResponse,
  ClashDetectionResult,
  ProjectModel,
  ProjectDocument,
  DocumentListResponse,
  ProjectMember,
  Iso19650Role,
  SearchResponse,
} from './types';

// ── Projects ──
export function listProjects(): Promise<Project[]> {
  return api<Project[]>('/api/projects');
}

export function getProject(id: string): Promise<Project> {
  return api<Project>(`/api/projects/${id}`);
}

// ── Issues ──
export async function listIssues(projectId: string, status?: string): Promise<BimIssue[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  // The server may return a flat array or an { items } envelope — handle both.
  const raw = await api<BimIssue[] | { items: BimIssue[] }>(`/api/projects/${projectId}/issues${qs}`);
  if (Array.isArray(raw)) return raw;
  return raw.items ?? [];
}

export function getIssue(projectId: string, issueId: string): Promise<BimIssue> {
  return api<BimIssue>(`/api/projects/${projectId}/issues/${issueId}`);
}

export function createIssue(projectId: string, body: Partial<BimIssue>): Promise<BimIssue> {
  return api<BimIssue>(`/api/projects/${projectId}/issues`, { method: 'POST', body: JSON.stringify(body) });
}

export function updateIssue(projectId: string, issueId: string, body: Partial<BimIssue>): Promise<BimIssue> {
  return api<BimIssue>(`/api/projects/${projectId}/issues/${issueId}`, { method: 'PUT', body: JSON.stringify(body) });
}

export function listComments(projectId: string, issueId: string): Promise<IssueComment[]> {
  return api<IssueComment[]>(`/api/projects/${projectId}/issues/${issueId}/comments`);
}

export function addComment(projectId: string, issueId: string, body: string): Promise<IssueComment> {
  return api<IssueComment>(`/api/projects/${projectId}/issues/${issueId}/comments`, {
    method: 'POST',
    body: JSON.stringify({ body }),
  });
}

// ── Clashes ──
export function listClashes(
  projectId: string,
  opts: { status?: string; severity?: string } = {},
): Promise<ClashListResponse> {
  const params = new URLSearchParams();
  if (opts.status) params.set('status', opts.status);
  if (opts.severity) params.set('severity', opts.severity);
  const qs = params.toString();
  return api<ClashListResponse>(`/api/projects/${projectId}/clashes${qs ? `?${qs}` : ''}`);
}

export function getClash(projectId: string, clashId: string): Promise<ClashRecord> {
  return api<ClashRecord>(`/api/projects/${projectId}/clashes/${clashId}`);
}

export function updateClash(projectId: string, clashId: string, body: Partial<ClashRecord>): Promise<ClashRecord> {
  return api<ClashRecord>(`/api/projects/${projectId}/clashes/${clashId}`, {
    method: 'PATCH',
    body: JSON.stringify(body),
  });
}

export function runClashDetection(projectId: string): Promise<ClashDetectionResult> {
  return api<ClashDetectionResult>(`/api/projects/${projectId}/clashes/run`, { method: 'POST' });
}

export function promoteClashToIssue(projectId: string, clashId: string): Promise<{ issueId?: string }> {
  return api<{ issueId?: string }>(`/api/projects/${projectId}/clashes/${clashId}/promote-to-issue`, {
    method: 'POST',
  });
}

// ── Models / viewer ──
export function listModels(projectId: string): Promise<ProjectModel[]> {
  return api<ProjectModel[]>(`/api/projects/${projectId}/models`);
}

/** Authenticated GLB URL — the token rides as a query param because the viewer
 *  iframe fetches the geometry itself and can't set an Authorization header. */
export function modelFileUrl(projectId: string, modelId: string): string {
  const token = getToken();
  const base = `${API_BASE}/api/projects/${projectId}/models/${modelId}/file`;
  return token ? `${base}?access_token=${encodeURIComponent(token)}` : base;
}

// ── Documents (CDE) ──
export async function listDocuments(
  projectId: string,
  opts: { cdeStatus?: string; discipline?: string; documentType?: string; search?: string } = {},
): Promise<ProjectDocument[]> {
  const params = new URLSearchParams();
  if (opts.cdeStatus) params.set('cdeStatus', opts.cdeStatus);
  if (opts.discipline) params.set('discipline', opts.discipline);
  if (opts.documentType) params.set('documentType', opts.documentType);
  if (opts.search) params.set('search', opts.search);
  const qs = params.toString();
  const raw = await api<DocumentListResponse | ProjectDocument[]>(
    `/api/projects/${projectId}/documents${qs ? `?${qs}` : ''}`,
  );
  return Array.isArray(raw) ? raw : (raw.items ?? []);
}

/** Authenticated download URL — token as a query param (global ?access_token=
 *  auth), so a plain anchor download carries the bearer. */
export function documentDownloadUrl(projectId: string, docId: string): string {
  const token = getToken();
  const base = `${API_BASE}/api/projects/${projectId}/documents/${docId}/download`;
  return token ? `${base}?access_token=${encodeURIComponent(token)}` : base;
}

// ── Project members ──
export function listMembers(projectId: string): Promise<ProjectMember[]> {
  return api<ProjectMember[]>(`/api/projects/${projectId}/members`);
}

export function listProjectRoles(projectId: string): Promise<Iso19650Role[]> {
  return api<Iso19650Role[]>(`/api/projects/${projectId}/members/roles`);
}

export function inviteMember(
  projectId: string,
  body: { email: string; displayName?: string; projectRole?: string; iso19650Role?: string },
): Promise<{ message?: string; isPending?: boolean; emailSent?: boolean }> {
  return api(`/api/projects/${projectId}/members/invite`, { method: 'POST', body: JSON.stringify(body) });
}

export function updateMemberRole(
  projectId: string,
  memberId: string,
  body: { projectRole?: string; iso19650Role?: string },
): Promise<{ id: string; projectRole: string; iso19650Role?: string }> {
  return api(`/api/projects/${projectId}/members/${memberId}`, { method: 'PUT', body: JSON.stringify(body) });
}

export function removeMember(projectId: string, memberId: string): Promise<void> {
  return api(`/api/projects/${projectId}/members/${memberId}`, { method: 'DELETE' });
}

// ── Cross-project search ──
export function search(q: string, types?: string[], limit = 25): Promise<SearchResponse> {
  const params = new URLSearchParams({ q, limit: String(limit) });
  if (types && types.length) params.set('type', types.join(','));
  return api<SearchResponse>(`/api/search?${params.toString()}`);
}
