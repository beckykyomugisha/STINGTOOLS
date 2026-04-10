import { apiFetch } from './client';
import type {
  LoginRequest,
  LoginResponse,
  UserProfile,
  Project,
  ComplianceSnapshot,
  BimIssue,
  DocumentRecord,
  DashboardData,
} from '../types/api';

// ── Auth ──

export function login(req: LoginRequest): Promise<LoginResponse> {
  return apiFetch('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify(req),
  });
}

export function getMe(): Promise<UserProfile> {
  return apiFetch('/api/auth/me');
}

// ── Projects ──

export function listProjects(): Promise<Project[]> {
  return apiFetch('/api/projects');
}

export function getProjectDashboard(projectId: string): Promise<DashboardData> {
  return apiFetch(`/api/projects/${projectId}/dashboard`);
}

// ── Compliance ──

export function getLatestCompliance(projectId: string): Promise<ComplianceSnapshot> {
  return apiFetch(`/api/projects/${projectId}/compliance`);
}

export function getComplianceTrend(projectId: string): Promise<ComplianceSnapshot[]> {
  return apiFetch(`/api/projects/${projectId}/compliance/trend`);
}

// ── Issues ──

export function listIssues(projectId: string): Promise<BimIssue[]> {
  return apiFetch(`/api/projects/${projectId}/issues`);
}

export function createIssue(
  projectId: string,
  issue: Partial<BimIssue>
): Promise<BimIssue> {
  return apiFetch(`/api/projects/${projectId}/issues`, {
    method: 'POST',
    body: JSON.stringify(issue),
  });
}

export function updateIssue(
  projectId: string,
  issueId: string,
  updates: Partial<BimIssue>
): Promise<BimIssue> {
  return apiFetch(`/api/projects/${projectId}/issues/${issueId}`, {
    method: 'PUT',
    body: JSON.stringify(updates),
  });
}

// ── Documents ──

export function listDocuments(projectId: string): Promise<DocumentRecord[]> {
  return apiFetch(`/api/projects/${projectId}/documents`);
}

export function transitionCDE(
  projectId: string,
  docId: string,
  newStatus: string
): Promise<DocumentRecord> {
  return apiFetch(`/api/projects/${projectId}/documents/${docId}/transition`, {
    method: 'POST',
    body: JSON.stringify({ newStatus }),
  });
}
