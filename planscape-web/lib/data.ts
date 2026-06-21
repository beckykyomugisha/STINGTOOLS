import { api, API_BASE, getToken } from './api';
import type {
  Project,
  BimIssue,
  IssueComment,
  ClashRecord,
  ClashListResponse,
  ClashDetectionResult,
  ProjectModel,
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
