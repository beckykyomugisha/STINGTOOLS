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

