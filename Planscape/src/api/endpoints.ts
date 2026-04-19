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

/**
 * Phase 96 — 30-second in-memory cache. Previously every tab called
 * `listProjects()` on mount, producing 5× the necessary round-trips in a
 * single session. Cache is invalidated on session-expired via clearCaches().
 */
let _projectsCache: { projects: Project[]; fetchedAt: number } | null = null;
const PROJECTS_CACHE_MS = 30_000;

export async function listProjects(forceRefresh = false): Promise<Project[]> {
  if (!forceRefresh && _projectsCache && (Date.now() - _projectsCache.fetchedAt) < PROJECTS_CACHE_MS) {
    return _projectsCache.projects;
  }
  const projects = await apiFetch<Project[]>('/api/projects');
  _projectsCache = { projects, fetchedAt: Date.now() };
  return projects;
}

/** Clear the projects cache — call from logout / tenant switch. */
export function clearProjectsCache(): void {
  _projectsCache = null;
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
  /** Phase 96 — client-supplied idempotency key so replays dedup server-side. */
  idempotencyKey?: string;
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
  // Phase 96 — idempotency key lets the server dedup replays (e.g. the upload
  // socket dropped after a successful write). Server: if key matches an
  // existing attachment, return 200 with that record instead of 201+duplicate.
  if (args.idempotencyKey) headers['X-Idempotency-Key'] = args.idempotencyKey;

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
// Phase 96 — create meeting, log minutes, action items
export function createMeeting(projectId: string, body: {
  title: string; meetingType?: string; scheduledAt: string;
  agendaJson?: string; attendeesJson?: string;
}): Promise<Meeting> {
  return apiFetch(`/api/projects/${projectId}/meetings`, { method: 'POST', body: JSON.stringify(body) });
}
export function logMeetingMinutes(projectId: string, meetingId: string, minutes: string): Promise<Meeting> {
  return apiFetch(`/api/projects/${projectId}/meetings/${meetingId}/minutes`, {
    method: 'PUT', body: JSON.stringify({ minutes }),
  });
}
export interface MeetingActionItem {
  id: string;
  description: string;
  assignee?: string | null;
  dueDate?: string | null;
  status?: string;
  linkedIssueId?: string | null;
  /** Phase 96 — server projection now includes this so mobile can tick
   *  actions off without a parent-meeting lookup. */
  meetingId?: string;
  meetingTitle?: string;
  isOverdue?: boolean;
}
export function addMeetingAction(projectId: string, meetingId: string, body: {
  description: string; assignee?: string; dueDate?: string;
}): Promise<MeetingActionItem> {
  return apiFetch(`/api/projects/${projectId}/meetings/${meetingId}/actions`, {
    method: 'POST', body: JSON.stringify(body),
  });
}
export function updateMeetingAction(projectId: string, meetingId: string, actionId: string, body: {
  status?: string; assignee?: string; linkedIssueId?: string;
}): Promise<MeetingActionItem> {
  return apiFetch(`/api/projects/${projectId}/meetings/${meetingId}/actions/${actionId}`, {
    method: 'PUT', body: JSON.stringify(body),
  });
}
export function listOpenMeetingActions(projectId: string): Promise<MeetingActionItem[]> {
  return apiFetch(`/api/projects/${projectId}/meetings/actions/open`);
}

// Workflow runs
export function listWorkflowRuns(projectId: string): Promise<WorkflowRun[]> {
  return apiFetch(`/api/projects/${projectId}/workflows`);
}

// Warnings
export function listWarnings(projectId: string): Promise<WarningRecord[]> {
  return apiFetch(`/api/projects/${projectId}/warnings`);
}

// Issue comments (P2)
export interface IssueComment {
  id: string;
  body: string;
  authorName: string;
  authorUserId?: string | null;
  source?: string | null;
  mentionedUserId?: string | null;
  createdAt: string;
  editedAt?: string | null;
}
export function listIssueComments(projectId: string, issueId: string): Promise<IssueComment[]> {
  return apiFetch(`/api/projects/${projectId}/issues/${issueId}/comments`);
}
export function addIssueComment(
  projectId: string, issueId: string,
  body: string, mentionedUserId?: string,
): Promise<IssueComment> {
  return apiFetch(`/api/projects/${projectId}/issues/${issueId}/comments`, {
    method: 'POST',
    body: JSON.stringify({ body, source: 'mobile', mentionedUserId }),
  });
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
