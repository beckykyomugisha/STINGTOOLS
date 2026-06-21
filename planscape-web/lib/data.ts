import { api } from './api';
import type { Project, BimIssue, IssueComment } from './types';

export function listProjects(): Promise<Project[]> {
  return api<Project[]>('/api/projects');
}

export function getProject(id: string): Promise<Project> {
  return api<Project>(`/api/projects/${id}`);
}

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
  return api<BimIssue>(`/api/projects/${projectId}/issues`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function updateIssue(projectId: string, issueId: string, body: Partial<BimIssue>): Promise<BimIssue> {
  return api<BimIssue>(`/api/projects/${projectId}/issues/${issueId}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
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
