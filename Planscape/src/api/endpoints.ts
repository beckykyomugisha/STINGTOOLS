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

/**
 * Phase 164 — accepts an optional filter object so callers (e.g. the
 * sibling-pin loader in issue-detail.tsx) can fetch only the issues they
 * care about and let the server's ProjectId/ModelId index do the work.
 *
 * Also unwraps the paginated `{ items, total, page, pageSize }` envelope
 * the controller returns. Pre-Phase-164 this helper was typed
 * `Promise<BimIssue[]>` but actually returned the envelope, so consumers
 * calling `.filter()`/`.map()` on the result silently failed via their
 * try/catch wrappers. Backwards-compatible: when the server returns a
 * flat array we still pass it through unchanged.
 */
export interface ListIssuesOptions {
  modelId?: string;
  status?: string;
  type?: string;
  page?: number;
  pageSize?: number;
}

export async function listIssues(
  projectId: string,
  opts?: ListIssuesOptions
): Promise<BimIssue[]> {
  const params = new URLSearchParams();
  if (opts?.modelId)  params.set('modelId',  opts.modelId);
  if (opts?.status)   params.set('status',   opts.status);
  if (opts?.type)     params.set('type',     opts.type);
  if (opts?.page)     params.set('page',     String(opts.page));
  if (opts?.pageSize) params.set('pageSize', String(opts.pageSize));
  const qs = params.toString();
  const path = `/api/projects/${projectId}/issues${qs ? `?${qs}` : ''}`;
  const raw = await apiFetch<unknown>(path);
  // Unwrap envelope when present; pass-through when caller / older server
  // returns a flat array.
  if (raw && typeof raw === 'object' && Array.isArray((raw as { items?: unknown }).items)) {
    return (raw as { items: BimIssue[] }).items;
  }
  return Array.isArray(raw) ? (raw as BimIssue[]) : [];
}

// ── Viewer XKT availability (Phase 164, caveat 4) ─────────────────────
// Module-level cache of filenames returned by GET /api/viewer/models.
// XKT files are operator-managed (ViewerController serves *.xkt verbatim
// from {Storage:Path}/xkt/), so per-model fullscreen routing only loads
// when the operator's pipeline names files by GUID. This helper lets the
// caller decide which XKT to point WebBrowser at without round-tripping
// for every issue.
let _xktNameCache: Set<string> | null = null;

export async function listAvailableXkts(): Promise<Set<string>> {
  if (_xktNameCache) return _xktNameCache;
  try {
    const list = await apiFetch<string[]>(`/api/viewer/models`);
    _xktNameCache = new Set(Array.isArray(list) ? list : []);
  } catch (err) {
    // Network failure / 401 / unknown shape — treat as "unknown availability"
    // and let the caller decide its fallback.
    console.warn('[endpoints.listAvailableXkts] failed', err);
    _xktNameCache = new Set();
  }
  return _xktNameCache;
}

/** Drop the cached XKT list (e.g. after re-auth or on logout). */
export function _resetXktCache(): void {
  _xktNameCache = null;
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

/**
 * T3-20 — richer row shape returned by the GET /members endpoint.
 * Server returns memberId (`Id`), userId, ACL CSV columns, and joinedAt;
 * the lean `ProjectMember` type doesn't capture those fields, so we expose
 * a richer alias here for screens that need them (project-settings ACL
 * editor). Existing callers using `listProjectMembers` are unaffected —
 * the extra fields are dropped at the type boundary, not at runtime.
 */
export interface ProjectMemberRow {
  id: string;
  userId: string;
  email: string;
  displayName: string;
  projectRole: string;
  iso19650Role: string;
  joinedAt?: string;
  invitedBy?: string | null;
  /** Server stores allow-lists as comma-separated strings; null = "all". */
  allowedCdeStates: string | null;
  allowedDisciplines: string | null;
  allowedSuitabilities: string | null;
}

export function listProjectMembers(projectId: string): Promise<ProjectMember[]> {
  return apiFetch(`/api/projects/${projectId}/members`);
}

/** Same call, typed for screens that need the rich payload. */
export function listProjectMembersFull(projectId: string): Promise<ProjectMemberRow[]> {
  return apiFetch(`/api/projects/${projectId}/members`);
}

/** T3-20 — update a project member's role + per-folder ACLs.
 *  Server route is by memberId (ProjectMember.Id), NOT by userId. */
export interface UpdateProjectMemberArgs {
  projectRole?: string;
  iso19650Role?: string;
  allowedCdeStates?: string[];
  allowedDisciplines?: string[];
  allowedSuitabilities?: string[];
  accessProfileId?: string;
}
export function updateProjectMember(
  projectId: string,
  memberId: string,
  body: UpdateProjectMemberArgs,
): Promise<{ id: string; projectRole: string; iso19650Role: string }> {
  return apiFetch(`/api/projects/${projectId}/members/${memberId}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

/** T3-20 — remove a member from the project (server hard-deletes). */
export function removeProjectMember(projectId: string, memberId: string): Promise<void> {
  return apiFetch(`/api/projects/${projectId}/members/${memberId}`, { method: 'DELETE' });
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
 *
 * M9 — built-in retry with exponential backoff (250ms / 750ms / 2s).
 * A single 3G timeout used to push the whole photo into the offline
 * queue, where it might wait minutes before the next drain. With 3
 * fast retries on the foreground call, transient flakiness is
 * absorbed without involving the queue at all. Idempotency key (set
 * by the caller) ensures a partial successful upload isn't double-
 * stored on retry.
 */
const UPLOAD_RETRY_DELAYS_MS = [250, 750, 2000];

export async function uploadIssueAttachment(
  args: UploadAttachmentArgs
): Promise<IssueAttachment> {
  const base = await getBaseUrl();
  const token = await getToken();

  let lastErr: unknown;
  for (let attempt = 0; attempt <= UPLOAD_RETRY_DELAYS_MS.length; attempt++) {
    try {
      // FormData / Blob references are single-use in React Native — we
      // rebuild the form on every attempt so a retried upload doesn't
      // resend a consumed stream.
      const form = new FormData();
      form.append('file', {
        uri: args.uri,
        name: args.fileName,
        type: args.contentType,
      } as unknown as Blob);

      const headers: Record<string, string> = {};
      if (token) headers['Authorization'] = `Bearer ${token}`;
      if (args.latitude !== undefined) headers['X-Latitude'] = String(args.latitude);
      if (args.longitude !== undefined) headers['X-Longitude'] = String(args.longitude);
      if (args.idempotencyKey) headers['X-Idempotency-Key'] = args.idempotencyKey;

      const res = await fetch(
        `${base}/api/projects/${args.projectId}/issues/${args.issueId}/attachments`,
        { method: 'POST', headers, body: form }
      );

      // 4xx is fatal — retrying won't fix a malformed body or revoked auth.
      if (res.status >= 400 && res.status < 500) {
        const text = await res.text();
        throw new Error(text || `Upload failed: HTTP ${res.status}`);
      }
      if (!res.ok) {
        // 5xx / network — fall through to retry
        const text = await res.text();
        lastErr = new Error(text || `Upload failed: HTTP ${res.status}`);
      } else {
        return await res.json();
      }
    } catch (err) {
      lastErr = err;
    }
    if (attempt < UPLOAD_RETRY_DELAYS_MS.length) {
      await new Promise((r) => setTimeout(r, UPLOAD_RETRY_DELAYS_MS[attempt]));
    }
  }
  throw lastErr instanceof Error ? lastErr : new Error('Upload failed after retries');
}

// ── Audio notes (T3-7 voice-to-text dictation) ─────────────────────────
//
// Uploads an audio recording captured by expo-av to the server, where it
// will be transcribed and the transcript appended to the linked issue's
// description / notes thread. The mobile bundle deliberately does NOT carry
// any STT model — transcription is a server concern (Whisper, Azure Speech,
// or Google Cloud Speech-to-Text behind a feature flag).
//
// TODO-SERVER: endpoint /api/projects/{pid}/issues/{iid}/audio-notes does not
//   exist yet. Server work needed:
//     1. Multipart receiver matching this contract (file + durationSec + idempotencyKey).
//     2. Background job that pulls bytes through the configured STT provider.
//     3. SignalR notification ("audioNoteTranscribed") so the issue detail
//        screen can refresh once the transcript lands.
//   Until then, replays of `ATTACH_AUDIO` will 404 and move to the failed
//   side-queue after MAX_RETRIES_PER_ACTION attempts (graceful degradation).

export interface UploadAudioNoteArgs {
  projectId: string;
  issueId: string;
  uri: string;
  fileName: string;
  contentType: string;
  durationSec?: number;
  idempotencyKey?: string;
}

const AUDIO_UPLOAD_RETRY_DELAYS_MS = [250, 750, 2000];

export async function uploadAudioNote(args: UploadAudioNoteArgs): Promise<{ id: string; status: string }> {
  const base = await getBaseUrl();
  const token = await getToken();

  let lastErr: unknown;
  for (let attempt = 0; attempt <= AUDIO_UPLOAD_RETRY_DELAYS_MS.length; attempt++) {
    try {
      const form = new FormData();
      form.append('file', {
        uri: args.uri,
        name: args.fileName,
        type: args.contentType,
      } as unknown as Blob);
      if (args.durationSec !== undefined) {
        form.append('durationSec', String(args.durationSec));
      }

      const headers: Record<string, string> = {};
      if (token) headers['Authorization'] = `Bearer ${token}`;
      if (args.idempotencyKey) headers['X-Idempotency-Key'] = args.idempotencyKey;
      headers['X-Client-Type'] = 'mobile';

      const res = await fetch(
        `${base}/api/projects/${args.projectId}/issues/${args.issueId}/audio-notes`,
        { method: 'POST', headers, body: form },
      );

      if (res.status >= 400 && res.status < 500) {
        const text = await res.text();
        throw new Error(text || `Audio upload failed: HTTP ${res.status}`);
      }
      if (!res.ok) {
        const text = await res.text();
        lastErr = new Error(text || `Audio upload failed: HTTP ${res.status}`);
      } else {
        return (await res.json()) as { id: string; status: string };
      }
    } catch (err) {
      lastErr = err;
    }
    if (attempt < AUDIO_UPLOAD_RETRY_DELAYS_MS.length) {
      await new Promise((r) => setTimeout(r, AUDIO_UPLOAD_RETRY_DELAYS_MS[attempt]));
    }
  }
  throw lastErr instanceof Error ? lastErr : new Error('Audio upload failed after retries');
}

// ── 3D model markup (S6.2 — referenced by ATTACH_MARKUP queue replay) ──
//
// TODO-SERVER: endpoint /api/projects/{pid}/models/{mid}/markups does not
//   yet exist. Stub kept here so the ATTACH_MARKUP replay path doesn't
//   throw `Cannot find module` at import time. When the server endpoint
//   lands, swap the body for a real multipart/JSON POST.

export interface UploadModelMarkupArgs {
  projectId: string;
  modelId: string;
  polylines: Array<{ points: number[][]; color: string; thickness: number }>;
  label?: string;
  idempotencyKey?: string;
}

export async function uploadModelMarkup(args: UploadModelMarkupArgs): Promise<{ id: string }> {
  return apiFetch(`/api/projects/${args.projectId}/models/${args.modelId}/markups`, {
    method: 'POST',
    body: JSON.stringify({
      polylines: args.polylines,
      label: args.label,
      idempotencyKey: args.idempotencyKey,
    }),
  });
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

// Phase 144 — update project settings. Admin booleans (e.g.
// enforceIso19650Naming) write through to first-class Project columns;
// any other key is treated as a soft preference and stored in ConfigJson.
// Caller must hold the K (BIM Manager) or C (Coordinator) project role.
export function updateProjectSettings(
  projectId: string,
  overrides: Record<string, unknown>,
): Promise<void> {
  return apiFetch(`/api/projects/${projectId}/settings`, {
    method: 'PUT',
    body: JSON.stringify(overrides),
  });
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

// T3-15 — 2D document markup (PDFs / sheets). The DocumentMarkup entity
// already exists server-side (Planscape.Core/Entities/DocumentMarkup.cs);
// we surface a thin typed wrapper here so the mobile sheet viewer can
// list / create / update / delete vector overlays. Shapes are stored as
// a JSON array on `shapesJson`; the renderer + the eventual web viewer
// both speak the same shape catalogue.
export interface DocumentMarkup {
  id: string;
  documentId: string;
  shapesJson: string;
  pageNumber: number;
  summary?: string | null;
  createdByUserId?: string | null;
  createdByName: string;
  createdAt: string;
  updatedAt?: string | null;
  previousMarkupId?: string | null;
}
export interface MarkupShape {
  /** Discriminated union: pen / arrow / text / circle. v1 keeps the catalogue
   *  small so the renderer can stay declarative; new tools extend by adding
   *  a new `kind` here, never by reusing an existing kind. */
  kind: 'pen' | 'arrow' | 'text' | 'circle';
  color: string;
  strokeWidth?: number;
  /** `pen` — array of normalised x/y points (0..1 within the page bounds). */
  points?: { x: number; y: number }[];
  /** `arrow` — start + end. */
  from?: { x: number; y: number };
  to?:   { x: number; y: number };
  /** `text` — anchor + body. */
  at?:   { x: number; y: number };
  text?: string;
  fontSize?: number;
  /** `circle` — centre + radius. */
  centre?: { x: number; y: number };
  radius?: number;
}
export function listDocumentMarkups(projectId: string, documentId: string): Promise<DocumentMarkup[]> {
  return apiFetch(`/api/projects/${projectId}/documents/${documentId}/markups`);
}
export function createDocumentMarkup(
  projectId: string, documentId: string,
  body: { shapesJson: string; pageNumber?: number; summary?: string; previousMarkupId?: string }
): Promise<DocumentMarkup> {
  return apiFetch(`/api/projects/${projectId}/documents/${documentId}/markups`, {
    method: 'POST', body: JSON.stringify(body),
  });
}
export function updateDocumentMarkup(
  projectId: string, documentId: string, markupId: string,
  body: { shapesJson?: string; pageNumber?: number; summary?: string }
): Promise<DocumentMarkup> {
  return apiFetch(`/api/projects/${projectId}/documents/${documentId}/markups/${markupId}`, {
    method: 'PUT', body: JSON.stringify(body),
  });
}
export function deleteDocumentMarkup(projectId: string, documentId: string, markupId: string): Promise<void> {
  return apiFetch(`/api/projects/${projectId}/documents/${documentId}/markups/${markupId}`, {
    method: 'DELETE',
  });
}

// Document approval (NEW-INT-15)
export function requestDocumentApproval(projectId: string, documentId: string, targetState: string): Promise<unknown> {
  return apiFetch(`/api/projects/${projectId}/documents/${documentId}/approvals`, {
    method: 'POST', body: JSON.stringify({ targetState }),
  });
}
export function decideDocumentApproval(projectId: string, documentId: string, approvalId: string, decision: 'APPROVED' | 'REJECTED', comment?: string): Promise<unknown> {
  // Phase 177 — server route is `/approval/{id}` (singular) per
  // DocumentsController.DecideApproval, not /approvals/{id}.
  return apiFetch(`/api/projects/${projectId}/documents/${documentId}/approval/${approvalId}`, {
    method: 'PUT', body: JSON.stringify({ decision, comments: comment }),
  });
}

// Phase 177 — per-folder ACL slice for the active user on a project. Used
// by the documents tab + project-settings UI to hide CDE-state filters,
// disciplines and suitability codes the user has no access to.
export interface MyProjectAccess {
  projectId: string;
  userId: string;
  bypassesAcl: boolean;
  projectRole: string | null;
  iso19650Role: string | null;
  allowedCdeStates: string[];
  allowedDisciplines: string[];
  allowedSuitabilities: string[];
}
export function getMyProjectAccess(projectId: string): Promise<MyProjectAccess> {
  return apiFetch(`/api/projects/${projectId}/members/me`);
}

// Phase 177-D — tenant-scoped named ACL presets. PMs pick a profile from a
// dropdown when inviting / updating members; the server folds the preset
// allow-lists onto the member row in one call.
export interface AccessProfile {
  id: string;
  name: string;
  description: string | null;
  allowedCdeStates: string[];
  allowedDisciplines: string[];
  allowedSuitabilities: string[];
  defaultProjectRole: string;
  defaultIso19650Role: string;
  createdAt: string;
  createdBy: string | null;
}
export function listAccessProfiles(): Promise<AccessProfile[]> {
  return apiFetch(`/api/access-profiles`);
}
export function createAccessProfile(body: Partial<AccessProfile>): Promise<AccessProfile> {
  return apiFetch(`/api/access-profiles`, { method: 'POST', body: JSON.stringify(body) });
}
export function updateAccessProfile(id: string, body: Partial<AccessProfile>): Promise<AccessProfile> {
  return apiFetch(`/api/access-profiles/${id}`, { method: 'PUT', body: JSON.stringify(body) });
}
export function deleteAccessProfile(id: string): Promise<void> {
  return apiFetch(`/api/access-profiles/${id}`, { method: 'DELETE' });
}

// ── My Actions (Phase 142) ──────────────────────────────────────────────
// Aggregator endpoint for the BIM/Construction Manager's morning inbox view.
// Returns counts + the first N rows from each bucket (issues / meeting actions /
// document approvals / SLA-breached issues). Backed by MyActionsController.
export interface MyActionsPayload {
  generatedAt: string;
  counts: {
    issues: number;
    actions: number;
    approvals: number;
    slaBreached: number;
    total: number;
  };
  issues: Array<{
    id: string; issueCode: string; type: string; title: string;
    priority: 'CRITICAL' | 'HIGH' | 'MEDIUM' | 'LOW';
    status: string; dueDate?: string | null; createdAt: string;
    discipline?: string | null;
    latitude?: number | null; longitude?: number | null;
    attachmentCount: number;
  }>;
  actions: Array<{
    id: string; meetingId: string; description: string;
    assignee?: string | null; dueDate?: string | null; status: string;
    linkedIssueId?: string | null;
    meetingTitle: string; meetingType: string;
  }>;
  approvals: Array<{
    id: string; documentId: string; transition: string;
    requestedBy: string; requestedAt: string;
    comments?: string | null; fileName: string; discipline?: string | null;
  }>;
  slaBreached: Array<{
    id: string; issueCode: string; type: string; title: string;
    priority: string; status: string;
    dueDate: string; assignee?: string | null; assigneeUserId?: string | null;
    breachHours: number;
  }>;
}

export function getMyActions(projectId: string, limit = 25): Promise<MyActionsPayload> {
  return apiFetch(`/api/projects/${projectId}/myactions?limit=${limit}`);
}

// ── Site Diary (Phase 142) ──────────────────────────────────────────────
// Daily site report — weather, manpower, equipment, narrative, photos.
// Backed by SiteDiariesController. Diary is keyed by (project, date,
// author) so multiple supervisors on the same site can post their own.
export interface SiteDiarySummary {
  id: string;
  diaryDate: string;
  authorName: string;
  authorRole: string;
  status: 'DRAFT' | 'SUBMITTED' | 'ACKNOWLEDGED' | 'ARCHIVED';
  weather?: string | null;
  temperatureCelsius?: number | null;
  manpowerCount: number;
  submittedAt?: string | null;
  acknowledgedAt?: string | null;
  createdAt: string;
  attachmentCount: number;
}

export interface SiteDiaryDetail extends SiteDiarySummary {
  projectId: string;
  authorUserId?: string | null;
  windSpeedKph?: number | null;
  rainfallMm?: number | null;
  manpowerByTradeJson?: string | null;
  equipmentJson?: string | null;
  deliveriesJson?: string | null;
  checklistJson?: string | null;
  narrative?: string | null;
  visitorsLog?: string | null;
  safetyIncidents?: string | null;
  delaysAndDisruption?: string | null;
  acknowledgedBy?: string | null;
  latitude?: number | null;
  longitude?: number | null;
  attachments: Array<{
    id: string; documentId: string; attachedBy: string;
    attachedAt: string; caption?: string | null;
    fileName?: string; filePath?: string; contentHash?: string;
  }>;
}

export interface CreateSiteDiaryRequest {
  diaryDate: string;          // ISO date — server normalises to .Date
  authorRole?: string | null;
  weather?: string | null;
  temperatureCelsius?: number | null;
  windSpeedKph?: number | null;
  rainfallMm?: number | null;
  manpowerCount: number;
  manpowerByTradeJson?: string | null;
  equipmentJson?: string | null;
  deliveriesJson?: string | null;
  narrative?: string | null;
  checklistJson?: string | null;
  visitorsLog?: string | null;
  safetyIncidents?: string | null;
  delaysAndDisruption?: string | null;
  latitude?: number | null;
  longitude?: number | null;
}

export function listSiteDiaries(
  projectId: string,
  args: { from?: string; to?: string; status?: string; page?: number; pageSize?: number } = {},
): Promise<{ total: number; page: number; pageSize: number; rows: SiteDiarySummary[] }> {
  const q = new URLSearchParams();
  if (args.from) q.set('from', args.from);
  if (args.to) q.set('to', args.to);
  if (args.status) q.set('status', args.status);
  if (args.page) q.set('page', String(args.page));
  if (args.pageSize) q.set('pageSize', String(args.pageSize));
  const suffix = q.toString();
  return apiFetch(`/api/projects/${projectId}/sitediaries${suffix ? `?${suffix}` : ''}`);
}

export function getSiteDiary(projectId: string, diaryId: string): Promise<SiteDiaryDetail> {
  return apiFetch(`/api/projects/${projectId}/sitediaries/${diaryId}`);
}

export function createSiteDiary(
  projectId: string, body: CreateSiteDiaryRequest,
): Promise<{ id: string; status: string; updated?: boolean }> {
  return apiFetch(`/api/projects/${projectId}/sitediaries`, {
    method: 'POST', body: JSON.stringify(body),
  });
}

export function updateSiteDiary(
  projectId: string, diaryId: string, body: CreateSiteDiaryRequest,
): Promise<{ id: string; status: string }> {
  return apiFetch(`/api/projects/${projectId}/sitediaries/${diaryId}`, {
    method: 'PUT', body: JSON.stringify(body),
  });
}

export function submitSiteDiary(projectId: string, diaryId: string): Promise<{ id: string; status: string }> {
  return apiFetch(`/api/projects/${projectId}/sitediaries/${diaryId}/submit`, { method: 'POST' });
}

export function acknowledgeSiteDiary(projectId: string, diaryId: string): Promise<{ id: string; status: string }> {
  return apiFetch(`/api/projects/${projectId}/sitediaries/${diaryId}/acknowledge`, { method: 'POST' });
}

export function linkSiteDiaryAttachment(
  projectId: string, diaryId: string, documentId: string, caption?: string,
): Promise<{ id?: string; linked?: boolean }> {
  return apiFetch(`/api/projects/${projectId}/sitediaries/${diaryId}/attachments/link`, {
    method: 'POST', body: JSON.stringify({ documentId, caption }),
  });
}

// ── Sync Conflicts (Phase 143) ──────────────────────────────────────────
// BIM Manager review surface for tag-sync conflicts written by
// TagSyncController. Pre-Phase-143 there was no UI — rows accumulated
// and managers had no way to triage stale-update collisions.

export interface SyncConflictSummary {
  id: string;
  elementId: string;
  conflictType: string;             // STALE_UPDATE | CONCURRENT_EDIT | DELETED_ON_SERVER
  resolution: 'PENDING' | 'SERVER_WINS' | 'CLIENT_WINS' | 'MERGED';
  serverTimestamp?: string | null;
  clientTimestamp?: string | null;
  clientUserName?: string | null;
  detectedAt: string;
  hasLinkedElement: boolean;
}

export interface SyncConflictDetail extends SyncConflictSummary {
  projectId: string;
  element?: {
    id: string;
    revitElementId: number;
    tag1: string;
    disc: string;
    sys: string;
    loc: string;
    zone: string;
    lvl: string;
    lastModifiedUtc?: string | null;
    version: number;
  } | null;
}

export interface SyncConflictsListResponse {
  total: number;
  page: number;
  pageSize: number;
  summary: { pending: number; recentServerWins: number };
  rows: SyncConflictSummary[];
}

export function listSyncConflicts(
  projectId: string,
  args: { resolution?: string; page?: number; pageSize?: number } = {},
): Promise<SyncConflictsListResponse> {
  const q = new URLSearchParams();
  if (args.resolution) q.set('resolution', args.resolution);
  if (args.page) q.set('page', String(args.page));
  if (args.pageSize) q.set('pageSize', String(args.pageSize));
  const suffix = q.toString();
  return apiFetch(`/api/projects/${projectId}/syncconflicts${suffix ? `?${suffix}` : ''}`);
}

export function getSyncConflict(projectId: string, conflictId: string): Promise<SyncConflictDetail> {
  return apiFetch(`/api/projects/${projectId}/syncconflicts/${conflictId}`);
}

export function resolveSyncConflict(
  projectId: string,
  conflictId: string,
  resolution: 'ACCEPT_SERVER' | 'ACCEPT_CLIENT' | 'MERGED',
  note?: string,
): Promise<{ resolved: number; resolution: string }> {
  return apiFetch(`/api/projects/${projectId}/syncconflicts/${conflictId}/resolve`, {
    method: 'POST', body: JSON.stringify({ resolution, note }),
  });
}

export function bulkResolveSyncConflicts(
  projectId: string,
  conflictIds: string[],
  resolution: 'ACCEPT_SERVER' | 'MERGED',
  note?: string,
): Promise<{ resolved: number; resolution: string; ids: string[] }> {
  return apiFetch(`/api/projects/${projectId}/syncconflicts/bulk-resolve`, {
    method: 'POST', body: JSON.stringify({ conflictIds, resolution, note }),
  });
}

// Phase 144 — bulk ACCEPT_CLIENT with per-conflict field maps. Each item
// carries the field values to re-apply for that specific conflict's
// linked TaggedElement. Server caps at 250 per call.
export interface ConflictFieldOverrides {
  tag1?: string | null;
  disc?: string | null;
  sys?: string | null;
  func?: string | null;
  prod?: string | null;
  loc?: string | null;
  zone?: string | null;
  lvl?: string | null;
  seq?: string | null;
  status?: string | null;
  rev?: string | null;
}
export function bulkResolveSyncConflictsWithFields(
  projectId: string,
  items: Array<{ conflictId: string; fields: ConflictFieldOverrides }>,
  note?: string,
): Promise<{ resolved: number; ids: string[]; skipped: Array<{ conflictId: string; reason: string }> }> {
  return apiFetch(`/api/projects/${projectId}/syncconflicts/bulk-resolve-with-fields`, {
    method: 'POST', body: JSON.stringify({ items, note }),
  });
}

// ── Federation Status (Phase 143) ───────────────────────────────────────
// BIM Coordinator's "are all disciplines up-to-date?" view. Aggregates the
// latest published model per discipline + counts + RAG.

export interface FederationStatus {
  projectId: string;
  generatedAt: string;
  staleDays: number;
  totals: {
    models: number;
    disciplines: number;
    staleModels: number;
    disciplinesWithStale: number;
  };
  rag: 'GREEN' | 'AMBER' | 'RED';
  disciplines: Array<{
    discipline: string;
    modelCount: number;
    latest: {
      id: string;
      name: string;
      fileName: string;
      revision?: string | null;
      uploadedAt: string;
      uploadedBy: string;
      elementCount?: number | null;
      fileSizeBytes: number;
    };
    daysSinceUpload: number;
    stale: boolean;
  }>;
}

export function getFederationStatus(projectId: string, staleDays = 14): Promise<FederationStatus> {
  return apiFetch(`/api/projects/${projectId}/federation-status?staleDays=${staleDays}`);
}

// ── Tag completeness heatmap (Phase 144) ────────────────────────────────
// Cross-discipline × per-token completeness matrix. Each cell is the
// percent of elements in that discipline whose token is non-empty.

export type TagToken =
  | 'DISC' | 'LOC' | 'ZONE' | 'LVL' | 'SYS' | 'FUNC' | 'PROD' | 'SEQ' | 'STATUS' | 'REV';

export interface TagHeatmap {
  projectId: string;
  generatedAt: string;
  totalElements: number;
  tokens: TagToken[];
  disciplines: Array<{
    discipline: string;
    elementCount: number;
    cells: Record<TagToken, number>;
  }>;
}

export function getTagHeatmap(projectId: string): Promise<TagHeatmap> {
  return apiFetch(`/api/projects/${projectId}/compliance/tag-heatmap`);
}

// ── Stage Gates + MIDP Deliverables (Phase 144) ─────────────────────────

export interface StageGateSummary {
  id: string;
  stageCode: string;
  stageName: string;
  sortOrder: number;
  plannedDate?: string | null;
  actualDate?: string | null;
  status: 'NOT_STARTED' | 'IN_PROGRESS' | 'PASSED' | 'FAILED' | 'WAIVED';
  decidedBy?: string | null;
  decidedAt?: string | null;
  deliverables: {
    total: number;
    pending: number;
    inProgress: number;
    submitted: number;
    accepted: number;
    rejected: number;
    overdue: number;
  };
}

export function listStageGates(projectId: string): Promise<StageGateSummary[]> {
  return apiFetch(`/api/projects/${projectId}/stagegates`);
}

export function seedRibaStages(projectId: string): Promise<{ added: number; totalNow: number }> {
  return apiFetch(`/api/projects/${projectId}/stagegates/seed-riba`, { method: 'POST' });
}

export function decideStageGate(
  projectId: string,
  gateId: string,
  status: 'PASSED' | 'FAILED' | 'WAIVED',
  actualDate?: string,
): Promise<StageGateSummary> {
  return apiFetch(`/api/projects/${projectId}/stagegates/${gateId}/decide`, {
    method: 'POST',
    body: JSON.stringify({ status, actualDate }),
  });
}

export interface DeliverableSummary {
  id: string;
  code: string;
  title: string;
  type: string;
  ownerRole: string;
  discipline?: string | null;
  suitabilityTarget?: string | null;
  dueDate: string;
  status: 'PENDING' | 'IN_PROGRESS' | 'SUBMITTED' | 'ACCEPTED' | 'REJECTED' | 'WAIVED';
  submittedAt?: string | null;
  submittedBy?: string | null;
  acceptedAt?: string | null;
  acceptedBy?: string | null;
  stageGateId?: string | null;
  documentId?: string | null;
  isOverdue: boolean;
}

export function listDeliverables(
  projectId: string,
  args: { stageGateId?: string; status?: string; discipline?: string; overdueOnly?: boolean; pageSize?: number } = {},
): Promise<{ total: number; page: number; pageSize: number; rows: DeliverableSummary[] }> {
  const q = new URLSearchParams();
  if (args.stageGateId) q.set('stageGateId', args.stageGateId);
  if (args.status) q.set('status', args.status);
  if (args.discipline) q.set('discipline', args.discipline);
  if (args.overdueOnly) q.set('overdueOnly', 'true');
  if (args.pageSize) q.set('pageSize', String(args.pageSize));
  const suffix = q.toString();
  return apiFetch(`/api/projects/${projectId}/deliverables${suffix ? `?${suffix}` : ''}`);
}

export function transitionDeliverable(
  projectId: string,
  deliverableId: string,
  newStatus: string,
  args: { documentId?: string; reason?: string } = {},
): Promise<DeliverableSummary> {
  return apiFetch(`/api/projects/${projectId}/deliverables/${deliverableId}/transition`, {
    method: 'POST',
    body: JSON.stringify({ newStatus, documentId: args.documentId, reason: args.reason }),
  });
}

// T3-17 — mobile deliverable CRUD. Server already exposes Get/Create/Update;
// only `listDeliverables` was wired previously.

export function getDeliverable(
  projectId: string,
  deliverableId: string,
): Promise<DeliverableSummary> {
  return apiFetch(`/api/projects/${projectId}/deliverables/${deliverableId}`);
}

export interface DeliverableUpsertArgs {
  code?: string;
  title?: string;
  description?: string;
  type?: string;
  ownerRole?: string;
  discipline?: string | null;
  suitabilityTarget?: string | null;
  dueDate?: string;
  stageGateId?: string | null;
}

export function createDeliverable(
  projectId: string,
  body: DeliverableUpsertArgs,
): Promise<DeliverableSummary> {
  return apiFetch(`/api/projects/${projectId}/deliverables`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function updateDeliverable(
  projectId: string,
  deliverableId: string,
  body: DeliverableUpsertArgs,
): Promise<DeliverableSummary> {
  return apiFetch(`/api/projects/${projectId}/deliverables/${deliverableId}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

// Phase 145 — resolved deliverable state machine for the active project.
// Returns the canonical ISO 19650 flow unless the project has overridden
// it via Project.CustomDeliverableStateMachineJson. Mobile uses the
// `transitions` array to render contextual buttons that match what the
// server will actually accept.
export interface DeliverableStateMachine {
  name: string;
  isCustom: boolean;
  jsonProvidedButFellBack: boolean;
  states: string[];
  initial?: string | null;
  terminal: string[];
  /**
   * Phase 146 — per-state semantic role (initial / working / submitting /
   * accepting / rejecting / terminal / none). Drives server-side metadata
   * side-effects on transition; the mobile UI uses it to colour buttons.
   */
  roles?: Record<string, string>;
  /**
   * Phase 149 — tenant-supplied substring keywords mapped to canonical
   * roles, e.g. `{ "working": ["WAITING_ON_X", "PARKED"] }`. Anything in
   * this map fires before the server's built-in vocabulary, so a project
   * can override the canonical inference (e.g. LOCKED is "working" instead
   * of "terminal" when the tenant uses LOCKED to mean "engineer is
   * editing").
   */
  customKeywords?: Record<string, string[]>;
  transitions: Array<{ from: string; to: string }>;
}

export function getDeliverableStateMachine(projectId: string): Promise<DeliverableStateMachine> {
  return apiFetch(`/api/projects/${projectId}/deliverables/state-machine`);
}

// ── Stage gate criterion sign-off (Phase 145) ───────────────────────────

export interface StageCriterion {
  key: string;
  label: string;
  description?: string | null;
  met: boolean;
  evidenceDocId?: string | null;
  signedBy?: string | null;
  signedAt?: string | null;
  comment?: string | null;
}

export interface StageCriteriaResponse {
  gateId: string;
  stageCode: string;
  criteria: StageCriterion[];
  summary: { total: number; met: number; outstanding: number };
}

export function listStageCriteria(projectId: string, gateId: string): Promise<StageCriteriaResponse> {
  return apiFetch(`/api/projects/${projectId}/stagegates/${gateId}/criteria`);
}

export function replaceStageCriteria(
  projectId: string,
  gateId: string,
  criteria: StageCriterion[],
): Promise<{ gateId: string; criteria: StageCriterion[] }> {
  return apiFetch(`/api/projects/${projectId}/stagegates/${gateId}/criteria`, {
    method: 'PUT', body: JSON.stringify(criteria),
  });
}

export function signOffStageCriterion(
  projectId: string,
  gateId: string,
  key: string,
  met: boolean,
  args: { comment?: string; evidenceDocId?: string } = {},
): Promise<StageCriteriaResponse> {
  return apiFetch(`/api/projects/${projectId}/stagegates/${gateId}/criteria/${encodeURIComponent(key)}/signoff`, {
    method: 'POST',
    body: JSON.stringify({ met, comment: args.comment, evidenceDocId: args.evidenceDocId }),
  });
}

// ── ISO 19650 naming validator (Phase 143) ───────────────────────────────
// Dry-run check before upload. Useful from the document upload modal so
// users see "your file name needs a Role code in segment 6" inline.
export interface NameValidationResult {
  fileName: string;
  isValid: boolean;
  pattern: string;
  issues: string[];
}

export function validateDocumentName(
  projectId: string,
  fileName: string,
): Promise<NameValidationResult> {
  return apiFetch(
    `/api/projects/${projectId}/documents/validate-name?fileName=${encodeURIComponent(fileName)}`,
  );
}


// ── Site Photos (Phase 178, slice 3) ────────────────────────────────────
// Six-Reason taxonomy + 5-state Audience machine. Server contract lives in
// Planscape.Server/src/Planscape.API/Controllers/SitePhotosController.cs.

import type {
  SitePhoto,
  SitePhotoListResponse,
  SitePhotoListFilters,
  SitePhotoDigestPreview,
  SitePhotoCaptureMeta,
} from '../types/api';

export interface CaptureSitePhotoArgs {
  projectId: string;
  uri: string;
  fileName: string;
  contentType: string;
  meta: SitePhotoCaptureMeta;
}

/**
 * POST /api/projects/{pid}/photos/capture as multipart/form-data. Mirrors
 * the controller's `CaptureForm` (file + reason + optional metadata).
 *
 * Performs in-foreground retries (250ms / 750ms / 2s) for 5xx and network
 * failures so a single 3G timeout doesn't dump the photo into the offline
 * queue when one more attempt would have succeeded. 4xx is treated as
 * fatal — the caller can decide whether to enqueue or surface to the user.
 */
const PHOTO_UPLOAD_RETRY_DELAYS_MS = [250, 750, 2000];

export async function captureSitePhoto(args: CaptureSitePhotoArgs): Promise<SitePhoto> {
  const base = await getBaseUrl();
  const token = await getToken();

  let lastErr: unknown;
  for (let attempt = 0; attempt <= PHOTO_UPLOAD_RETRY_DELAYS_MS.length; attempt++) {
    try {
      const form = new FormData();
      form.append('file', {
        uri: args.uri,
        name: args.fileName,
        type: args.contentType,
      } as unknown as Blob);

      form.append('reason', args.meta.reason);
      if (args.meta.caption !== undefined) form.append('caption', args.meta.caption);
      if (args.meta.levelCode !== undefined) form.append('levelCode', args.meta.levelCode);
      if (args.meta.zoneCode !== undefined) form.append('zoneCode', args.meta.zoneCode);
      if (args.meta.latitude !== undefined) form.append('latitude', String(args.meta.latitude));
      if (args.meta.longitude !== undefined) form.append('longitude', String(args.meta.longitude));
      if (args.meta.accuracyM !== undefined) form.append('accuracyM', String(args.meta.accuracyM));
      if (args.meta.pairKey !== undefined) form.append('pairKey', args.meta.pairKey);
      if (args.meta.classifierConfidence !== undefined)
        form.append('classifierConfidence', String(args.meta.classifierConfidence));
      if (args.meta.classifierSignals !== undefined)
        form.append('classifierSignals', JSON.stringify(args.meta.classifierSignals));
      if (args.meta.capturedAt !== undefined) form.append('capturedAt', args.meta.capturedAt);
      if (args.meta.deviceId !== undefined) form.append('deviceId', args.meta.deviceId);
      if (args.meta.source !== undefined) form.append('source', args.meta.source);
      if (args.meta.queuedClient !== undefined)
        form.append('queuedClient', String(args.meta.queuedClient));
      if (args.meta.anchorIssueId !== undefined) form.append('anchorIssueId', args.meta.anchorIssueId);
      if (args.meta.anchorElementGuid !== undefined)
        form.append('anchorElementGuid', args.meta.anchorElementGuid);
      if (args.meta.modelId !== undefined) form.append('modelId', args.meta.modelId);
      if (args.meta.modelX !== undefined) form.append('modelX', String(args.meta.modelX));
      if (args.meta.modelY !== undefined) form.append('modelY', String(args.meta.modelY));
      if (args.meta.modelZ !== undefined) form.append('modelZ', String(args.meta.modelZ));

      const headers: Record<string, string> = {};
      if (token) headers['Authorization'] = `Bearer ${token}`;
      if (args.meta.deviceId) headers['X-Device-Id'] = args.meta.deviceId;
      headers['X-Client-Type'] = 'mobile';

      const res = await fetch(
        `${base}/api/projects/${args.projectId}/photos/capture`,
        { method: 'POST', headers, body: form },
      );

      if (res.status >= 400 && res.status < 500) {
        const text = await res.text();
        throw new Error(text || `Photo capture failed: HTTP ${res.status}`);
      }
      if (!res.ok) {
        const text = await res.text();
        lastErr = new Error(text || `Photo capture failed: HTTP ${res.status}`);
      } else {
        return (await res.json()) as SitePhoto;
      }
    } catch (err) {
      lastErr = err;
    }
    if (attempt < PHOTO_UPLOAD_RETRY_DELAYS_MS.length) {
      await new Promise((r) => setTimeout(r, PHOTO_UPLOAD_RETRY_DELAYS_MS[attempt]));
    }
  }
  throw lastErr instanceof Error ? lastErr : new Error('Photo capture failed after retries');
}

export function listSitePhotos(
  projectId: string,
  filters: SitePhotoListFilters = {},
): Promise<SitePhotoListResponse> {
  const q = new URLSearchParams();
  if (filters.reason) q.set('reason', filters.reason);
  if (filters.audience) q.set('audience', filters.audience);
  if (filters.levelCode) q.set('levelCode', filters.levelCode);
  if (filters.zoneCode) q.set('zoneCode', filters.zoneCode);
  if (filters.anchorElementGuid) q.set('anchorElementGuid', filters.anchorElementGuid);
  if (filters.from) q.set('from', filters.from);
  if (filters.to) q.set('to', filters.to);
  if (filters.page) q.set('page', String(filters.page));
  if (filters.pageSize) q.set('pageSize', String(filters.pageSize));
  const suffix = q.toString();
  return apiFetch(`/api/projects/${projectId}/photos${suffix ? `?${suffix}` : ''}`);
}

export function getSitePhoto(projectId: string, photoId: string): Promise<SitePhoto> {
  return apiFetch(`/api/projects/${projectId}/photos/${photoId}`);
}

/** Build an authenticated URL for the photo bytes. The image consumer must
 *  supply the `Authorization` header itself (RN <Image source={{uri,headers}}>
 *  pattern), so this returns a plain URL string rather than fetching the blob. */
export async function getSitePhotoFile(
  projectId: string,
  photoId: string,
): Promise<{ url: string; headers: Record<string, string> }> {
  const base = await getBaseUrl();
  const token = await getToken();
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  return {
    url: `${base}/api/projects/${projectId}/photos/${photoId}/file`,
    headers,
  };
}

export function setSitePhotoAudience(
  projectId: string,
  photoId: string,
  audience: 'Internal' | 'PendingReview',
): Promise<SitePhoto> {
  return apiFetch(`/api/projects/${projectId}/photos/${photoId}/audience`, {
    method: 'PUT',
    body: JSON.stringify({ audience }),
  });
}

export function approveSitePhoto(
  projectId: string,
  photoId: string,
  caption: string,
): Promise<SitePhoto> {
  return apiFetch(`/api/projects/${projectId}/photos/${photoId}/approve`, {
    method: 'POST',
    body: JSON.stringify({ caption }),
  });
}

export function rejectSitePhoto(
  projectId: string,
  photoId: string,
  reason: string,
): Promise<SitePhoto> {
  return apiFetch(`/api/projects/${projectId}/photos/${photoId}/reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export function withdrawSitePhoto(projectId: string, photoId: string): Promise<SitePhoto> {
  return apiFetch(`/api/projects/${projectId}/photos/${photoId}/withdraw`, {
    method: 'POST',
  });
}

export interface BulkApproveSitePhotosResult {
  approved: number;
  skipped: number;
  skippedDetail: Array<{ id: string; reason: string; current?: string }>;
}

export function bulkApproveSitePhotos(
  projectId: string,
  photoIds: string[],
  caption: string,
): Promise<BulkApproveSitePhotosResult> {
  return apiFetch(`/api/projects/${projectId}/photos/bulk-approve`, {
    method: 'POST',
    body: JSON.stringify({ photoIds, caption }),
  });
}

export function getSitePhotoDigestPreview(projectId: string): Promise<SitePhotoDigestPreview> {
  return apiFetch(`/api/projects/${projectId}/photos/digest-preview`);
}

// ──────────────────────────────────────────────────────────────────────────────
//  Phase 179 — site-photo workflow enhancements
// ──────────────────────────────────────────────────────────────────────────────

export type PhotoAlbum = {
  id: string;
  projectId: string;
  name: string;
  description?: string;
  visibility: 'Internal' | 'Members' | 'Client' | 'Distribution';
  kind?: string;
  distributionGroupId?: string;
  distributionGroupName?: string;
  coverPhotoId?: string;
  isLocked: boolean;
  lockedAt?: string;
  autoArchiveAfterDays?: number;
  createdAt: string;
  photoCount: number;
};

export type PhotoAlbumDetail = {
  album: PhotoAlbum;
  photos: { photoId: string; sortOrder: number; addedAt: string }[];
};

export function listPhotoAlbums(projectId: string, kind?: string, visibility?: string): Promise<PhotoAlbum[]> {
  const qs = new URLSearchParams();
  if (kind) qs.set('kind', kind);
  if (visibility) qs.set('visibility', visibility);
  const suffix = qs.toString() ? `?${qs}` : '';
  return apiFetch(`/api/projects/${projectId}/photo-albums${suffix}`);
}

export function getPhotoAlbum(projectId: string, albumId: string): Promise<PhotoAlbumDetail> {
  return apiFetch(`/api/projects/${projectId}/photo-albums/${albumId}`);
}

export function createPhotoAlbum(
  projectId: string,
  body: {
    name: string;
    description?: string;
    visibility?: 'Internal' | 'Members' | 'Client' | 'Distribution';
    distributionGroupId?: string;
    kind?: string;
    autoArchiveAfterDays?: number;
  },
): Promise<PhotoAlbum> {
  return apiFetch(`/api/projects/${projectId}/photo-albums`, {
    method: 'POST',
    body: JSON.stringify(body),
    headers: { 'Content-Type': 'application/json' },
  });
}

export function addPhotosToAlbum(projectId: string, albumId: string, photoIds: string[]): Promise<{ added: number; total: number }> {
  return apiFetch(`/api/projects/${projectId}/photo-albums/${albumId}/photos`, {
    method: 'POST',
    body: JSON.stringify({ photoIds }),
    headers: { 'Content-Type': 'application/json' },
  });
}

export function removePhotoFromAlbum(projectId: string, albumId: string, photoId: string): Promise<void> {
  return apiFetch(`/api/projects/${projectId}/photo-albums/${albumId}/photos/${photoId}`, { method: 'DELETE' });
}

export function lockPhotoAlbum(projectId: string, albumId: string, locked: boolean): Promise<PhotoAlbum> {
  return apiFetch(`/api/projects/${projectId}/photo-albums/${albumId}/${locked ? 'lock' : 'unlock'}`, {
    method: 'POST',
  });
}

export type DistributionGroup = {
  id: string;
  projectId: string;
  name: string;
  description?: string;
  kind: 'Client' | 'Internal' | 'Mixed';
  includeInDailyDigest: boolean;
  forceRedacted: boolean;
  memberCount: number;
  createdAt: string;
};

export function listDistributionGroups(projectId: string): Promise<DistributionGroup[]> {
  return apiFetch(`/api/projects/${projectId}/distribution-groups`);
}

export function createDistributionGroup(
  projectId: string,
  body: {
    name: string;
    description?: string;
    kind?: 'Client' | 'Internal' | 'Mixed';
    includeInDailyDigest?: boolean;
    forceRedacted?: boolean;
  },
): Promise<DistributionGroup> {
  return apiFetch(`/api/projects/${projectId}/distribution-groups`, {
    method: 'POST',
    body: JSON.stringify(body),
    headers: { 'Content-Type': 'application/json' },
  });
}

export type PhotoChecklist = {
  id: string;
  projectId: string;
  name: string;
  description?: string;
  kind?: string;
  status: 'Draft' | 'Active' | 'Closed' | 'Archived';
  levelCode?: string;
  zoneCode?: string;
  workPackageId?: string;
  dueAt?: string;
  createdAt: string;
  closedAt?: string;
  total: number;
  done: number;
};

export type PhotoChecklistItem = {
  id: string;
  checklistId: string;
  title: string;
  description?: string;
  sortOrder: number;
  defaultReason: string;
  isRequired: boolean;
  isWaived: boolean;
  waivedReason?: string;
  fulfilledByPhotoId?: string;
  fulfilledAt?: string;
  fulfilledByUserId?: string;
};

export type PhotoChecklistDetail = {
  checklist: PhotoChecklist;
  items: PhotoChecklistItem[];
};

export function listPhotoChecklists(projectId: string, status?: string): Promise<PhotoChecklist[]> {
  const suffix = status ? `?status=${encodeURIComponent(status)}` : '';
  return apiFetch(`/api/projects/${projectId}/photo-checklists${suffix}`);
}

export function getPhotoChecklist(projectId: string, checklistId: string): Promise<PhotoChecklistDetail> {
  return apiFetch(`/api/projects/${projectId}/photo-checklists/${checklistId}`);
}

export function fulfilChecklistItem(
  projectId: string, checklistId: string, itemId: string, photoId: string,
): Promise<PhotoChecklistItem> {
  return apiFetch(`/api/projects/${projectId}/photo-checklists/${checklistId}/items/${itemId}/fulfil`, {
    method: 'POST',
    body: JSON.stringify({ photoId }),
    headers: { 'Content-Type': 'application/json' },
  });
}

export type PhotoAnnotation = {
  id: string;
  photoId: string;
  shapesJson: string;
  summary?: string;
  createdAt: string;
  createdByName?: string;
};

export function listPhotoAnnotations(projectId: string, photoId: string): Promise<PhotoAnnotation[]> {
  return apiFetch(`/api/projects/${projectId}/photos/${photoId}/annotations`);
}

export function createPhotoAnnotation(
  projectId: string, photoId: string, shapesJson: string, summary?: string,
): Promise<PhotoAnnotation> {
  return apiFetch(`/api/projects/${projectId}/photos/${photoId}/annotations`, {
    method: 'POST',
    body: JSON.stringify({ shapesJson, summary }),
    headers: { 'Content-Type': 'application/json' },
  });
}

export type PhotoVoiceNote = {
  id: string;
  photoId: string;
  documentId: string;
  transcriptText?: string;
  durationSeconds: number;
  createdAt: string;
  createdBy?: string;
};

export async function uploadPhotoVoiceNote(
  projectId: string,
  photoId: string,
  args: {
    fileUri: string;
    fileName?: string;
    transcript?: string;
    durationSeconds?: number;
    language?: string;
  },
): Promise<PhotoVoiceNote> {
  const fd = new FormData();
  // RN typing for FormData file is awkward — cast to any to side-step.
  fd.append('file', {
    uri: args.fileUri,
    name: args.fileName ?? `voice-${Date.now()}.m4a`,
    type: 'audio/m4a',
  } as unknown as Blob);
  if (args.transcript) fd.append('transcript', args.transcript);
  if (args.durationSeconds != null) fd.append('durationSeconds', String(args.durationSeconds));
  if (args.language) fd.append('language', args.language);
  return apiFetch(`/api/projects/${projectId}/photos/${photoId}/voice-notes`, {
    method: 'POST',
    body: fd as unknown as BodyInit,
  });
}

export type PhotoShareLink = {
  id: string;
  projectId: string;
  photoId?: string;
  albumId?: string;
  token: string;
  label?: string;
  expiresAt?: string;
  forceRedacted: boolean;
  maxFetches?: number;
  fetchCount: number;
  createdAt: string;
  revokedAt?: string;
};

export function createPhotoShareLink(
  projectId: string,
  body: {
    photoId?: string;
    albumId?: string;
    label?: string;
    expiresAt?: string;
    forceRedacted?: boolean;
    maxFetches?: number;
  },
): Promise<PhotoShareLink> {
  return apiFetch(`/api/projects/${projectId}/photo-share-links`, {
    method: 'POST',
    body: JSON.stringify(body),
    headers: { 'Content-Type': 'application/json' },
  });
}

// ── Phase 180 — Photo policy ─────────────────────────────────────────

export type PhotoPolicy = {
  id: string;
  projectId: string;
  allowedReasonsJson?: string;
  defaultAudienceByReasonJson?: string;
  watermarkLogoPath?: string;
  watermarkFooterTemplate?: string;
  watermarkRequired: boolean;
  faceBlurRequired: boolean;
  plateBlurRequired: boolean;
  retentionDays?: number;
  autoArchiveAfterHandover: boolean;
  geofenceWkt?: string;
  offsiteAudience?: string;
  digestHourLocal: number;
  digestDistributionGroupId?: string;
  approvalChain: string;
  enforceChecklistOnShiftEnd: boolean;
  defaultAlbumByReasonJson?: string;
  ndaText?: string;
  updatedAt: string;
};

export function getPhotoPolicy(projectId: string): Promise<PhotoPolicy> {
  return apiFetch(`/api/projects/${projectId}/photo-policy`);
}

// ── Phase 179.2 — NDA acceptance ─────────────────────────────────────

export type PhotoNdaAcceptance = {
  photoId: string;
  userId: string;
  acceptedAt: string;
  ipAddress?: string;
  userAgent?: string;
  acceptedTextSha256?: string;
};

/** Idempotent — re-posting returns the existing acceptance row. */
export function acceptPhotoNda(
  projectId: string, photoId: string, acceptedTextSha256?: string,
): Promise<PhotoNdaAcceptance> {
  return apiFetch(`/api/projects/${projectId}/photos/${photoId}/accept-nda`, {
    method: 'POST',
    body: JSON.stringify({ acceptedTextSha256 }),
    headers: { 'Content-Type': 'application/json' },
  });
}

// ── Healthcare Pack H-22 ──

export type HealthcarePressureLog = {
  id?: string;
  roomBimId: string;
  roomName: string;
  roomClass: string;
  designRegime: 'NEG' | 'POS' | 'NEUTRAL';
  designDeltaPa: number;
  liveDeltaPa: number;
  inBand: boolean;
  capturedAt?: string;
  capturedBy?: string;
  source?: 'BACNET' | 'OPC-UA' | 'MANUAL';
};

export type HealthcareMgasVerification = {
  id?: string;
  zone: string;
  gasCode: string;
  verifierName: string;
  verifierAsse6030Id?: string;
  certReference?: string;
  capturedAt?: string;
  overallPass: boolean;
  passCount: number;
  failCount: number;
  checkResultsJson: string;
  notes?: string;
};

export type HealthcareAntiLigatureAudit = {
  id?: string;
  roomBimId: string;
  roomName: string;
  fittingType: string;
  pass: boolean;
  notes?: string;
  photoBlobId?: string;
  gpsLat?: number;
  gpsLon?: number;
  capturedAt?: string;
  capturedBy?: string;
};

export type HealthcareDashboard = {
  pressure: { totalLast7d: number; breachLast7d: number; rag: 'R' | 'A' | 'G' };
  mgas: { latest: string | null; pass: boolean; rag: 'R' | 'A' | 'G' };
  antiLigature: { totalAudits: number; failed: number; rag: 'R' | 'A' | 'G' };
  rdsCount: number;
};

export function getHealthcareDashboard(projectId: string): Promise<HealthcareDashboard> {
  return apiFetch(`/api/projects/${projectId}/healthcare/dashboard`);
}

export function postPressureLog(projectId: string, body: HealthcarePressureLog): Promise<HealthcarePressureLog> {
  return apiFetch(`/api/projects/${projectId}/healthcare/pressure-log`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function listPressureLog(projectId: string, since?: string, roomBimId?: string): Promise<HealthcarePressureLog[]> {
  const q: string[] = [];
  if (since) q.push(`since=${encodeURIComponent(since)}`);
  if (roomBimId) q.push(`roomBimId=${encodeURIComponent(roomBimId)}`);
  const qs = q.length ? `?${q.join('&')}` : '';
  return apiFetch(`/api/projects/${projectId}/healthcare/pressure-log${qs}`);
}

export function postMgasVerification(projectId: string, body: HealthcareMgasVerification): Promise<HealthcareMgasVerification> {
  return apiFetch(`/api/projects/${projectId}/healthcare/mgas-verification`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function postAntiLigatureAudit(projectId: string, body: HealthcareAntiLigatureAudit): Promise<HealthcareAntiLigatureAudit> {
  return apiFetch(`/api/projects/${projectId}/healthcare/anti-ligature-audit`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function getRdsSnapshot(projectId: string, roomBimId: string): Promise<unknown> {
  return apiFetch(`/api/projects/${projectId}/healthcare/rds/${encodeURIComponent(roomBimId)}`);
}

// ── Phase 178f — penetration commissioning sign-off (FRP / damper / acoustic seal). ──

export interface PenetrationSignoff {
  penetrationControlNumber: string;
  pfvUuid?: string;
  hostType?: string;          // FLOOR / WALL / BEAM / CEILING / ROOF
  fireRating?: string;        // FR30 / FR60 / FR90 / FR120 / ''
  certification?: string;     // UL system / EN 1366-3 reference
  productKind?: string;       // FIRESTOP / FIRE_DAMPER / ACOUSTIC_SEAL / SLEEVE_GENERIC
  installerName?: string;
  installerCompany?: string;
  installedAt?: string;       // ISO 8601
  inspectorName?: string;
  inspectedAt?: string;
  status?: string;            // DRAFT / INSTALLED / INSPECTED / SIGNED-OFF / REWORK
  notes?: string;
  photoBlobId?: string;
  gpsLat?: number;
  gpsLon?: number;
}

export function putPenetrationSignoff(projectId: string, controlNumber: string,
    body: PenetrationSignoff): Promise<PenetrationSignoff> {
  return apiFetch(`/api/projects/${projectId}/penetrations/${encodeURIComponent(controlNumber)}/signoff`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export function getPenetrationSignoff(projectId: string, controlNumber: string): Promise<PenetrationSignoff> {
  return apiFetch(`/api/projects/${projectId}/penetrations/${encodeURIComponent(controlNumber)}/signoff`);
}

export function listPenetrationSignoffs(projectId: string,
    filters?: { status?: string; hostType?: string }): Promise<PenetrationSignoff[]> {
  const params = new URLSearchParams();
  if (filters?.status) params.append('status', filters.status);
  if (filters?.hostType) params.append('hostType', filters.hostType);
  const qs = params.toString();
  return apiFetch(`/api/projects/${projectId}/penetrations${qs ? '?' + qs : ''}`);
}

export function getPenetrationDashboard(projectId: string): Promise<{
  byStatus: { status: string; count: number }[];
  byHost:   { hostType: string; count: number }[];
}> {
  return apiFetch(`/api/projects/${projectId}/penetrations/dashboard`);
}
