import { api, API_BASE, getToken, setToken, ApiError } from './api';
import type {
  Project,
  BimIssue,
  IssueComment,
  ClashRecord,
  ClashListResponse,
  ClashDetectionResult,
  ProjectModel,
  SceneManifest,
  Meeting,
  MeetingAttendee,
  MeetingAgendaItem,
  MeetingActionItem,
  StartLiveSession,
  LiveKitToken,
  ProjectDocument,
  DocumentListResponse,
  ProjectMember,
  Iso19650Role,
  SearchResponse,
  Transmittal,
  SitePhoto,
  AccessToken,
  MintedAccessToken,
  TenantDashboard,
} from './types';

// ── Projects ──
export function listProjects(): Promise<Project[]> {
  return api<Project[]>('/api/projects');
}

export function getProject(id: string): Promise<Project> {
  return api<Project>(`/api/projects/${id}`);
}

export function createProject(body: {
  name: string;
  code?: string;
  description?: string;
  phase?: string;
}): Promise<Project> {
  return api<Project>('/api/projects', { method: 'POST', body: JSON.stringify(body) });
}

/**
 * Update project settings. The body is deliberately narrower than
 * `UpdateProjectRequest`: that record also accepts `status`, `tagSeparator`,
 * `seqNumPad`, `tagPrefix`, `tagSuffix` and `configJson`, none of which the
 * grid edits.
 *
 * `documentSyncAutoEnabled` is in the list because it genuinely is a project
 * setting this route accepts — the per-project "Auto-sync this project" toggle
 * from the document-sync design.
 *
 * `status` in particular stays out on purpose — writing it here would be a
 * second, unconfirmed route to `Archived`, bypassing the confirm-code gate that
 * `archiveProject` exists to honour. Note it is NOT accepting `code`: the server
 * has no write for it, and it is the archive confirmation token.
 *
 * Null-valued fields are "leave unchanged" server-side, so a partial body is safe.
 */
export function updateProject(
  id: string,
  body: { name?: string; description?: string; phase?: string; documentSyncAutoEnabled?: boolean },
): Promise<Project> {
  return api<Project>(`/api/projects/${id}`, { method: 'PUT', body: JSON.stringify(body) });
}

/**
 * Archive a project — a SOFT delete. `Status` flips to `Archived`; every row is
 * kept and the project stays visible under the archived filter. There is no hard
 * delete on this route by design; a true purge is separate admin tooling.
 *
 * The server double-gates it (`ProjectsController.ArchiveProject`):
 *  - **403** unless the caller is the project author (`CreatedById`) or a tenant
 *    admin. That is correct behaviour, not a bug — surface it as "you don't have
 *    permission", not as a generic failure.
 *  - **400** unless `confirmCode` equals the project's own `Code`
 *    (case-insensitive), with `{ message, expectedField, expectedValue }`.
 *
 * The client asks for the code too — see `ArchiveProjectDialog`. The 400 is the
 * server's backstop, not the UI's confirmation step.
 */
export function archiveProject(id: string, confirmCode: string): Promise<void> {
  return api<void>(`/api/projects/${id}?confirmCode=${encodeURIComponent(confirmCode)}`, {
    method: 'DELETE',
  });
}

/**
 * Toggle a project's pinned flag. The server has had `PATCH /{id}/pin` since
 * Phase 169 (pinned projects sort first) but nothing in the web app called it,
 * so the flag could only ever be set from another client.
 *
 * Server-side toggle, not a set: it flips whatever is stored rather than taking
 * a value, so callers can't push a stale local guess back.
 */
export function toggleProjectPin(id: string): Promise<void> {
  return api<void>(`/api/projects/${id}/pin`, { method: 'PATCH' });
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
export function listModels(
  projectId: string,
  opts: { deleted?: boolean } = {},
): Promise<ProjectModel[]> {
  const q = opts.deleted ? '?deleted=true' : '';
  return api<ProjectModel[]>(`/api/projects/${projectId}/models${q}`);
}

/** Soft-delete. The bytes survive for 30 days (ModelPurgeJob), so this is undoable
 *  via restoreModel until then — say so wherever it is offered, because a Delete the
 *  user believes is permanent gets avoided, and one they believe is reversible when it
 *  is not gets trusted. */
export function deleteModel(projectId: string, modelId: string): Promise<void> {
  return api<void>(`/api/projects/${projectId}/models/${modelId}`, { method: 'DELETE' });
}

/** Undo a delete inside the 30-day window. 404 means the model is already purged. */
export function restoreModel(projectId: string, modelId: string): Promise<void> {
  return api<void>(`/api/projects/${projectId}/models/${modelId}/restore`, { method: 'POST' });
}

/** Authenticated GLB URL — the token rides as a query param because the viewer
 *  iframe fetches the geometry itself and can't set an Authorization header. */
export function modelFileUrl(projectId: string, modelId: string): string {
  const token = getToken();
  const base = `${API_BASE}/api/projects/${projectId}/models/${modelId}/file`;
  return token ? `${base}?access_token=${encodeURIComponent(token)}` : base;
}

// ── Federation ──
/** Multi-discipline scene manifest. Returns null when the project has no scene
 *  chunks yet (404) so callers can fall back to the single-model path. */
export async function getSceneManifest(
  projectId: string,
  disciplines?: string[],
): Promise<SceneManifest | null> {
  const q = disciplines && disciplines.length ? `?disciplines=${disciplines.join(',')}` : '';
  try {
    return await api<SceneManifest>(`/api/projects/${projectId}/scene${q}`);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null; // no chunks published
    throw e;
  }
}

/** Authenticated absolute URL for a scene chunk. The manifest gives a relative
 *  path (/api/v1/scene-nodes/{id}/file); the iframe needs the token in the query. */
export function chunkFileUrl(relativeUrl: string): string {
  const token = getToken();
  const base = `${API_BASE}${relativeUrl}`;
  return token ? `${base}${base.includes('?') ? '&' : '?'}access_token=${encodeURIComponent(token)}` : base;
}

export interface UploadModelResult {
  id?: string;
  duplicate?: boolean;
  converting?: boolean;
  message?: string;
}

/** Upload a model (GLB/glTF) as multipart/form-data. Uses fetch directly rather
 *  than the JSON api() wrapper so the browser sets the multipart boundary, and
 *  bearer auth rides in the header (not the iframe ?access_token= trick). The
 *  endpoint is role-gated (Admin/Owner/Coordinator) — a 403 surfaces as ApiError. */
export async function uploadModel(
  projectId: string,
  file: File,
  opts: { name?: string; discipline?: string; description?: string } = {},
): Promise<UploadModelResult> {
  const form = new FormData();
  form.append('File', file, file.name);
  if (opts.name) form.append('Name', opts.name);
  if (opts.discipline) form.append('Discipline', opts.discipline);
  if (opts.description) form.append('Description', opts.description);

  const token = getToken();
  const headers = new Headers();
  if (token) headers.set('Authorization', `Bearer ${token}`); // no Content-Type — browser sets the boundary

  const res = await fetch(`${API_BASE}/api/projects/${projectId}/models`, {
    method: 'POST',
    headers,
    body: form,
  });

  if (res.status === 401) {
    setToken(null);
    if (typeof window !== 'undefined' && window.location.pathname !== '/login') window.location.href = '/login';
    throw new ApiError(401, 'Session expired — please sign in again.');
  }
  if (!res.ok) {
    // `serverMessage` stays undefined when the body carried nothing, so a
    // forbidden state can tell "the server explained why" from "the server said
    // nothing and this is our placeholder". See ApiError in lib/api.ts.
    const generic = `Upload failed (HTTP ${res.status})`;
    let serverMessage: string | undefined;
    try {
      const b = await res.json();
      serverMessage = b.message || b.error || undefined;
    } catch {
      /* non-JSON */
    }
    throw new ApiError(res.status, serverMessage || generic, undefined, serverMessage);
  }
  if (res.status === 204) return {};
  return (await res.json()) as UploadModelResult;
}

// ── Meetings ──
const mBase = (projectId: string) => `/api/projects/${projectId}/meetings`;

export async function listMeetings(projectId: string, status?: string): Promise<Meeting[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  // MeetingsController.GetMeetings returns { items, total, page, pageSize }, not a flat
  // array — same envelope as listIssues. This was silently mismatched: the type annotation
  // said Meeting[], api<T>() just casts the JSON to it, and nothing checks at runtime. The
  // crash only shows up once real data flows through .slice()/.filter() downstream, which
  // is why it took a live browser load (not tsc, not a build, not a vitest run) to surface.
  const raw = await api<Meeting[] | { items: Meeting[] }>(`${mBase(projectId)}${qs}`);
  return Array.isArray(raw) ? raw : (raw.items ?? []);
}

export function getMeeting(projectId: string, meetingId: string): Promise<Meeting> {
  return api<Meeting>(`${mBase(projectId)}/${meetingId}`);
}

/** Mirrors the server's AttendeeDto. `userId` is what makes an attendee real —
 *  it is the FK the meeting-invite push and the ICS export both key off. */
export interface MeetingAttendeeInput {
  userId?: string;
  name?: string;
  email?: string;
  company?: string;
  discipline?: string;
  role?: string;
}

export interface CreateMeetingBody {
  title: string;
  meetingType?: string;
  scheduledAt: string;
  durationMinutes?: number;
  location?: string;
  meetingUrl?: string;
  /** Persisted by MeetingsController.CreateMeeting into MeetingAttendee rows. */
  attendees?: MeetingAttendeeInput[];
}

export function createMeeting(projectId: string, body: CreateMeetingBody): Promise<Meeting> {
  return api<Meeting>(mBase(projectId), { method: 'POST', body: JSON.stringify(body) });
}

export function updateMeeting(projectId: string, meetingId: string, body: Partial<Meeting>): Promise<Meeting> {
  return api<Meeting>(`${mBase(projectId)}/${meetingId}`, { method: 'PUT', body: JSON.stringify(body) });
}

export function logMeetingMinutes(
  projectId: string,
  meetingId: string,
  minutes: string,
  status?: string,
): Promise<Meeting> {
  return api<Meeting>(`${mBase(projectId)}/${meetingId}/minutes`, {
    method: 'PUT',
    body: JSON.stringify({ minutes, status }),
  });
}

export function listAttendees(projectId: string, meetingId: string): Promise<MeetingAttendee[]> {
  return api<MeetingAttendee[]>(`${mBase(projectId)}/${meetingId}/attendees`);
}

export function addAgendaItem(
  projectId: string,
  meetingId: string,
  body: { title: string; description?: string; durationMinutes?: number; presenter?: string },
): Promise<MeetingAgendaItem> {
  return api<MeetingAgendaItem>(`${mBase(projectId)}/${meetingId}/agenda`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function updateAgendaItem(
  projectId: string,
  meetingId: string,
  itemId: string,
  body: Partial<MeetingAgendaItem>,
): Promise<MeetingAgendaItem> {
  return api<MeetingAgendaItem>(`${mBase(projectId)}/${meetingId}/agenda/${itemId}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export function addAction(
  projectId: string,
  meetingId: string,
  body: { description: string; assignee?: string; dueDate?: string; priority?: string; notes?: string },
): Promise<MeetingActionItem> {
  return api<MeetingActionItem>(`${mBase(projectId)}/${meetingId}/actions`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function updateAction(
  projectId: string,
  meetingId: string,
  actionId: string,
  body: Partial<MeetingActionItem>,
): Promise<MeetingActionItem> {
  return api<MeetingActionItem>(`${mBase(projectId)}/${meetingId}/actions/${actionId}`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

/** Agenda + actions aren't returned by getMeeting — fetch them from the detail
 *  endpoint payload. The server embeds them under the meeting; we expose typed
 *  getters that read the same GET /meetings/{id} response. */
export async function getMeetingDetail(
  projectId: string,
  meetingId: string,
): Promise<{ meeting: Meeting; agenda: MeetingAgendaItem[]; actions: MeetingActionItem[] }> {
  const raw = await api<
    Meeting & { agendaItems?: MeetingAgendaItem[]; agenda?: MeetingAgendaItem[]; actions?: MeetingActionItem[]; actionItems?: MeetingActionItem[] }
  >(`${mBase(projectId)}/${meetingId}`);
  return {
    meeting: raw,
    agenda: raw.agendaItems ?? raw.agenda ?? [],
    actions: raw.actionItems ?? raw.actions ?? [],
  };
}

// ── Live session + LiveKit ──
export function startLiveSession(
  projectId: string,
  meetingId: string,
  body: { modelId?: string; displayName?: string; surface?: string } = {},
): Promise<StartLiveSession> {
  return api<StartLiveSession>(`${mBase(projectId)}/${meetingId}/live-session`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

/** Mint a LiveKit access token + room URL for a live session. The server
 *  returns 501 when LiveKit isn't configured — the caller degrades to view-only. */
export function getLiveKitToken(
  projectId: string,
  sessionId: string,
  displayName?: string,
): Promise<LiveKitToken> {
  return api<LiveKitToken>(`/api/projects/${projectId}/meeting-sessions/${sessionId}/livekit-token`, {
    method: 'POST',
    body: JSON.stringify({ displayName }),
  });
}

// ── Documents (CDE) ──
export async function listDocuments(
  projectId: string,
  opts: { cdeStatus?: string; discipline?: string; documentType?: string; search?: string; page?: number; pageSize?: number } = {},
): Promise<ProjectDocument[]> {
  const params = new URLSearchParams();
  if (opts.cdeStatus) params.set('cdeStatus', opts.cdeStatus);
  if (opts.discipline) params.set('discipline', opts.discipline);
  if (opts.documentType) params.set('documentType', opts.documentType);
  if (opts.search) params.set('search', opts.search);
  if (opts.page) params.set('page', String(opts.page));
  if (opts.pageSize) params.set('pageSize', String(opts.pageSize));
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

// ── Tenant (firm-wide) administration ──
// Every route here lives on TenantAdminController, which is
// [Authorize(Roles = "Owner,Admin")] as a whole. A 403 from any of them means
// "you are not an Owner or Admin", not "something went wrong" — render it that
// way. There is no tenant id in the path: the tenant is resolved from the token
// and the global query filter, so an admin cannot even type the wrong one.

/** Plan, live usage vs limits, and the firm's user list — one payload. */
export function getTenantDashboard(): Promise<TenantDashboard> {
  return api<TenantDashboard>('/api/tenant/dashboard');
}

/**
 * Invite someone to the FIRM, not to a single project — the counterpart to
 * `inviteMember`, which adds a seat on one project. This is the path that was
 * server-only until now: `POST /api/tenant/invite` had no client code anywhere.
 *
 * `role` is `"Author"` or anything else, which the server maps to
 * `"Coordinator"` — those are the two axes the plan meters separately.
 *
 * Failures worth handling by hand rather than as a generic toast:
 *  - **402** `{ error: 'quota_exceeded', axis, current, max, reason }` — the
 *    plan's Author/Coordinator cap is full. `ApiError.body` carries the detail.
 *  - **409** the email already belongs to a user.
 *  - **403** the caller is not an Owner/Admin.
 *
 * The invited row is planted inactive with a stub password; the server's real
 * invite-email flow is still a TODO on its side, so treat "invited" as "seat
 * reserved", not "they got a link".
 */
export function inviteTenantMember(body: {
  email: string;
  displayName: string;
  role: string;
}): Promise<{ id: string; email: string; displayName: string; role: string }> {
  return api('/api/tenant/invite', { method: 'POST', body: JSON.stringify(body) });
}

// ── Cross-project search ──
export function search(q: string, types?: string[], limit = 25): Promise<SearchResponse> {
  const params = new URLSearchParams({ q, limit: String(limit) });
  if (types && types.length) params.set('type', types.join(','));
  return api<SearchResponse>(`/api/search?${params.toString()}`);
}

// ── Document upload + CDE transition ──
/** Multipart document upload (file + ISO 19650 metadata). Role-gated server-side. */
export async function uploadDocument(
  projectId: string,
  file: File,
  meta: { documentType?: string; discipline?: string; revision?: string; description?: string } = {},
): Promise<ProjectDocument> {
  const form = new FormData();
  form.append('file', file, file.name);
  if (meta.documentType) form.append('documentType', meta.documentType);
  if (meta.discipline) form.append('discipline', meta.discipline);
  if (meta.revision) form.append('revision', meta.revision);
  if (meta.description) form.append('description', meta.description);

  const token = getToken();
  const headers = new Headers();
  if (token) headers.set('Authorization', `Bearer ${token}`); // browser sets multipart boundary

  const res = await fetch(`${API_BASE}/api/projects/${projectId}/documents/upload`, {
    method: 'POST',
    headers,
    body: form,
  });
  if (res.status === 401) {
    setToken(null);
    if (typeof window !== 'undefined' && window.location.pathname !== '/login') window.location.href = '/login';
    throw new ApiError(401, 'Session expired — please sign in again.');
  }
  if (!res.ok) {
    // `serverMessage` stays undefined when the body carried nothing, so a
    // forbidden state can tell "the server explained why" from "the server said
    // nothing and this is our placeholder". See ApiError in lib/api.ts.
    const generic = `Upload failed (HTTP ${res.status})`;
    let serverMessage: string | undefined;
    try {
      const b = await res.json();
      serverMessage = b.message || b.error || undefined;
    } catch {
      /* non-JSON */
    }
    throw new ApiError(res.status, serverMessage || generic, undefined, serverMessage);
  }
  return (await res.json()) as ProjectDocument;
}

/** CDE state transition (WIP→SHARED→PUBLISHED→ARCHIVE…). Role/suitability/approval
 *  gated server-side — a 400/403 surfaces as ApiError with the reason. */
export function transitionDocument(
  projectId: string,
  docId: string,
  body: { newState: string; suitabilityCode?: string; revision?: string },
): Promise<ProjectDocument> {
  return api<ProjectDocument>(`/api/projects/${projectId}/documents/${docId}/state`, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

// ── Transmittals ──
export async function listTransmittals(projectId: string): Promise<Transmittal[]> {
  const raw = await api<{ transmittals?: Transmittal[] } | Transmittal[]>(
    `/api/projects/${projectId}/transmittals`,
  );
  if (Array.isArray(raw)) return raw;
  return raw.transmittals ?? [];
}

export function createTransmittal(
  projectId: string,
  body: { recipient: string; notes?: string; documentIds?: string[] },
): Promise<Transmittal> {
  return api<Transmittal>(`/api/projects/${projectId}/transmittals`, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

export function transmittalAction(
  projectId: string,
  txId: string,
  action: 'send' | 'acknowledge' | 'respond',
  body?: { responseNotes?: string },
): Promise<Transmittal> {
  return api<Transmittal>(`/api/projects/${projectId}/transmittals/${txId}/${action}`, {
    method: 'PUT',
    body: body ? JSON.stringify(body) : undefined,
  });
}

// ── Site photos ──
export async function listSitePhotos(
  projectId: string,
  opts: { reason?: string; audience?: string } = {},
): Promise<SitePhoto[]> {
  const params = new URLSearchParams();
  if (opts.reason) params.set('reason', opts.reason);
  if (opts.audience) params.set('audience', opts.audience);
  const qs = params.toString();
  const raw = await api<{ items?: SitePhoto[] } | SitePhoto[]>(
    `/api/projects/${projectId}/photos${qs ? `?${qs}` : ''}`,
  );
  if (Array.isArray(raw)) return raw;
  return raw.items ?? [];
}

/** Authenticated photo bytes URL (original or redacted), token via query. */
export function photoFileUrl(projectId: string, photoId: string): string {
  const token = getToken();
  const base = `${API_BASE}/api/projects/${projectId}/photos/${photoId}/file`;
  return token ? `${base}?access_token=${encodeURIComponent(token)}` : base;
}

export function approvePhoto(projectId: string, photoId: string, caption: string): Promise<unknown> {
  return api(`/api/projects/${projectId}/photos/${photoId}/approve`, {
    method: 'POST',
    body: JSON.stringify({ caption }),
  });
}

export function rejectPhoto(projectId: string, photoId: string, reason: string): Promise<unknown> {
  return api(`/api/projects/${projectId}/photos/${photoId}/reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

// ── Personal access tokens ──
// Long-lived headless credentials, used by StingBridge (STING_PLANSCAPE_TOKEN)
// and any other non-interactive client. A PAT is exchanged for a normal JWT at
// /api/auth/token/exchange; it is never accepted as a bearer token directly, so
// the API stays single-scheme.

export function listAccessTokens(): Promise<AccessToken[]> {
  return api<AccessToken[]>('/api/auth/tokens');
}

/**
 * Mint a token. The plaintext in the response is unrecoverable afterwards —
 * the server keeps only a hash — so the caller MUST surface it immediately.
 */
export function createAccessToken(body: {
  name: string;
  expiresInDays?: number;
}): Promise<MintedAccessToken> {
  return api<MintedAccessToken>('/api/auth/tokens', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

/** Soft-revoke, so the audit trail survives. Returns 204. */
export function revokeAccessToken(id: string): Promise<void> {
  return api<void>(`/api/auth/tokens/${id}`, { method: 'DELETE' });
}
