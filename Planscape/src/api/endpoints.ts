import { apiFetch, getBaseUrl, getToken } from './client';
// Re-export for components that need a raw base URL for thumbnails/downloads.
export { getBaseUrl as _getBaseUrl } from './client';
import type {
  LoginRequest,
  LoginResponse,
  UserProfile,
  Project,
  ComplianceSnapshot,
  BimIssue,
  DocumentRecord,
  DashboardData,
  TaggedElement,
  ProjectMember,
  IssueAttachment,
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

// ── Tag Sync / Element Lookup ──

export function lookupElement(
  projectId: string,
  query: string
): Promise<TaggedElement[]> {
  return apiFetch(
    `/api/tagsync/elements/search?projectId=${projectId}&q=${encodeURIComponent(query)}`
  );
}

// ── Project Members (NEW-MOB-13) ──

export function listProjectMembers(projectId: string): Promise<ProjectMember[]> {
  return apiFetch(`/api/projects/${projectId}/members`);
}

// ── Issue Attachments (NEW-MOB-15) ──

export function listIssueAttachments(
  projectId: string,
  issueId: string
): Promise<IssueAttachment[]> {
  return apiFetch(`/api/projects/${projectId}/issues/${issueId}/attachments`);
}

/** NEW-INFO-01 — Server exposes JPEG thumbnails at {150, 300, 600}px. */
export async function getAttachmentThumbnailUrl(
  projectId: string,
  issueId: string,
  attachmentId: string,
  size: 150 | 300 | 600 = 300,
): Promise<string> {
  const base = await getBaseUrl();
  return `${base}/api/projects/${projectId}/issues/${issueId}/attachments/${attachmentId}/thumbnail?size=${size}`;
}

export interface UploadAttachmentArgs {
  projectId: string;
  issueId: string;
  uri: string;
  fileName: string;
  contentType: string;
  latitude?: number;
  longitude?: number;
}

/**
 * Upload an issue attachment using multipart/form-data.
 * Sends X-Latitude/X-Longitude headers when GPS available so server
 * geofence + EXIF persistence can pick them up.
 */
export async function uploadIssueAttachment(
  args: UploadAttachmentArgs
): Promise<IssueAttachment> {
  const base = await getBaseUrl();
  const token = await getToken();
  const form = new FormData();
  // React Native FormData accepts { uri, name, type } objects
  form.append('file', {
    uri: args.uri,
    name: args.fileName,
    type: args.contentType,
  } as unknown as Blob);

  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  if (args.latitude !== undefined) headers['X-Latitude'] = String(args.latitude);
  if (args.longitude !== undefined) headers['X-Longitude'] = String(args.longitude);

  const res = await fetch(
    `${base}/api/projects/${args.projectId}/issues/${args.issueId}/attachments`,
    { method: 'POST', headers, body: form }
  );
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || `Upload failed: HTTP ${res.status}`);
  }
  return res.json();
}

// ── Push token registration (NEW-MOB-18) ──

export interface SubscribeArgs {
  token: string;
  platform: string;
  deviceId?: string;
  appVersion?: string;
  model?: string;
}

export function subscribePushToken(args: SubscribeArgs): Promise<void> {
  return apiFetch('/api/notifications/subscribe', {
    method: 'POST',
    body: JSON.stringify(args),
  });
}

// ── NEW-INT-01 mobile coverage for server endpoints ────────────────────

import type {
  Transmittal,
  Meeting,
  WorkflowRun,
  WarningRecord,
  ProjectSettings,
  NotificationPreferences,
  IssueActivityEntry,
} from '../types/api';

// Transmittals
export function listTransmittals(projectId: string): Promise<Transmittal[]> {
  return apiFetch(`/api/projects/${projectId}/transmittals`);
}
export function createTransmittal(projectId: string, body: Partial<Transmittal>): Promise<Transmittal> {
  return apiFetch(`/api/projects/${projectId}/transmittals`, { method: 'POST', body: JSON.stringify(body) });
}
export function sendTransmittal(projectId: string, id: string): Promise<Transmittal> {
  return apiFetch(`/api/projects/${projectId}/transmittals/${id}/send`, { method: 'POST' });
}

// Meetings
export function listMeetings(projectId: string): Promise<Meeting[]> {
  return apiFetch(`/api/projects/${projectId}/meetings`);
}

// Workflow runs
export function listWorkflowRuns(projectId: string): Promise<WorkflowRun[]> {
  return apiFetch(`/api/projects/${projectId}/workflows`);
}

// Warnings
export function listWarnings(projectId: string): Promise<WarningRecord[]> {
  return apiFetch(`/api/projects/${projectId}/warnings`);
}

// Global search (NEW-INT-12)
export interface SearchResults {
  tags: unknown[]; issues: unknown[]; documents: unknown[]; meetings: unknown[];
}
export function globalSearch(query: string): Promise<SearchResults> {
  return apiFetch(`/api/search?q=${encodeURIComponent(query)}`);
}

// Project settings (NEW-FLEX-02/05/08)
export function getProjectSettings(projectId: string): Promise<ProjectSettings> {
  return apiFetch(`/api/projects/${projectId}/settings`);
}

// Notification preferences (NEW-FLEX-12)
export function getNotificationPreferences(): Promise<NotificationPreferences> {
  return apiFetch('/api/me/notifications/preferences');
}
export function updateNotificationPreferences(prefs: Partial<NotificationPreferences>): Promise<NotificationPreferences> {
  return apiFetch('/api/me/notifications/preferences', { method: 'PUT', body: JSON.stringify(prefs) });
}

// Issue activity timeline (NEW-INFO-06/07)
export function getIssueActivity(projectId: string, issueId: string): Promise<IssueActivityEntry[]> {
  return apiFetch(`/api/projects/${projectId}/issues/${issueId}/activity`);
}

// Document approval (NEW-INT-15)
export function requestDocumentApproval(projectId: string, documentId: string, targetState: string): Promise<unknown> {
  return apiFetch(`/api/projects/${projectId}/documents/${documentId}/approvals`, {
    method: 'POST', body: JSON.stringify({ targetState }),
  });
}
export function decideDocumentApproval(projectId: string, documentId: string, approvalId: string, decision: 'APPROVED' | 'REJECTED', comment?: string): Promise<unknown> {
  return apiFetch(`/api/projects/${projectId}/documents/${documentId}/approvals/${approvalId}`, {
    method: 'PUT', body: JSON.stringify({ decision, comment }),
  });
}
