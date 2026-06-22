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
    let message = `Upload failed (HTTP ${res.status})`;
    try {
      const b = await res.json();
      message = b.message || b.error || message;
    } catch {
      /* non-JSON */
    }
    throw new ApiError(res.status, message);
  }
  if (res.status === 204) return {};
  return (await res.json()) as UploadModelResult;
}

// ── Meetings ──
const mBase = (projectId: string) => `/api/projects/${projectId}/meetings`;

export function listMeetings(projectId: string, status?: string): Promise<Meeting[]> {
  const qs = status ? `?status=${encodeURIComponent(status)}` : '';
  return api<Meeting[]>(`${mBase(projectId)}${qs}`);
}

export function getMeeting(projectId: string, meetingId: string): Promise<Meeting> {
  return api<Meeting>(`${mBase(projectId)}/${meetingId}`);
}

export interface CreateMeetingBody {
  title: string;
  meetingType?: string;
  scheduledAt: string;
  durationMinutes?: number;
  location?: string;
  meetingUrl?: string;
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
