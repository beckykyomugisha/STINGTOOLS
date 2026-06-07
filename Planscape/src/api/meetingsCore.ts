// meetingsCore.ts — the mobile twin of wwwroot/js/meetings-core.js (SAME contract + role
// matrix + helpers), bound to the mobile's authed apiFetch. Metro can't import the served
// wwwroot UMD across project roots, so the two surfaces keep one module EACH with an
// identical contract — meeting logic lives in one named module per surface, not scattered.
// (A future shared package/build step could collapse these to one physical file.)
import { apiFetch } from "./client";
import type { Meeting } from "../types/api";
import type { MeetingLiveArtifacts, ProjectRecording, MeetingAttendee, MeetingAgendaItem, MeetingActionItem } from "./endpoints";

// ── role → capability matrix (mirror of meetings-core.js CAPS — keep in lockstep) ──
const CAPS: Record<string, string[]> = {
  host: ["schedule","start","record","present","editAgenda","editMinutes","assignActions","manageAttendees","completeOwnActions","join","view","watchRecordings","readMinutes"],
  bimcoordinator: ["schedule","start","record","present","editAgenda","editMinutes","assignActions","manageAttendees","completeOwnActions","join","view","watchRecordings","readMinutes"],
  manager: ["schedule","start","record","present","editAgenda","editMinutes","assignActions","manageAttendees","completeOwnActions","join","view","watchRecordings","readMinutes"],
  chair: ["editAgenda","editMinutes","assignActions","completeOwnActions","join","view","watchRecordings","readMinutes"],
  secretary: ["editAgenda","editMinutes","assignActions","completeOwnActions","join","view","watchRecordings","readMinutes"],
  attendee: ["completeOwnActions","join","view","watchRecordings","readMinutes"],
  "discipline-lead": ["completeOwnActions","join","view","watchRecordings","readMinutes"],
  client: ["join","view","watchRecordings","readMinutes"],
  viewer: ["view","watchRecordings","readMinutes"],
  field: ["completeOwnActions","join","view","readMinutes"],
};
const normRole = (r?: string) => String(r || "viewer").toLowerCase().replace(/\s+/g, "-").replace(/_/g, "-");
export const roleCaps = (role?: string): string[] => CAPS[normRole(role)] || CAPS.viewer;
export const can = (role: string | undefined, capability: string): boolean => roleCaps(role).indexOf(capability) !== -1;

// ── pure helpers (mirror) ──
export const fmtDuration = (s?: number | null): string => { if (!s || s <= 0) return "—"; const m = Math.floor(s / 60), ss = Math.round(s % 60); return `${m}:${String(ss).padStart(2, "0")}`; };
export const fmtSize = (b?: number | null): string => { if (!b || b <= 0) return "—"; return b >= 1048576 ? `${(b / 1048576).toFixed(1)} MB` : `${(b / 1024).toFixed(0)} KB`; };
export const isAudioKind = (k: string): boolean => k === "audio-only" || k === "audio";
export const recordingIsPlayable = (r: { status?: string; downloadUrl?: string | null }): boolean => !!r && r.status === "COMPLETE" && !!r.downloadUrl;

export interface NormalizedMeetingEvent { type: string; meetingId: string | null; sessionId?: string | null; title: string; body: string; deepLink: string | null; }
export function parseNotificationEvent(event: string, payload: any): NormalizedMeetingEvent | null {
  payload = payload || {}; const data = payload.data || payload;
  const mid = data.meetingId || data.MeetingId || payload.meetingId || null;
  if (event === "Notification") {
    if (data.type === "meeting_live" || data.Type === "meeting_live")
      return { type: "live-start", meetingId: mid, sessionId: data.meetingSessionId || null, title: payload.title || "Meeting started", body: payload.message || payload.body || "Join the live meeting", deepLink: data.deepLink || (mid ? `?meeting=${mid}` : null) };
    return null;
  }
  if (event === "MeetingScheduled") return { type: "scheduled", meetingId: payload.id || mid, title: "Meeting scheduled", body: payload.title || "", deepLink: null };
  if (event === "MeetingCreated" || event === "MeetingUpdated") return { type: event === "MeetingCreated" ? "created" : "updated", meetingId: payload.id || mid, title: event, body: payload.title || "", deepLink: null };
  return null;
}
export const MEETING_EVENTS = ["Notification", "MeetingScheduled", "MeetingCreated", "MeetingUpdated"];

// ── effective meeting role (mirror of dashboard.js meetingRole) ──
// Pure base64url decode (no atob/Buffer dependency — safe in RN/Hermes + web).
function b64urlToStr(b64: string): string {
  b64 = b64.replace(/-/g, "+").replace(/_/g, "/");
  const chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  let out = "", bc = 0, bs = 0;
  for (let i = 0; i < b64.length; i++) {
    const c = b64.charAt(i); if (c === "=") break;
    const idx = chars.indexOf(c); if (idx < 0) continue;
    bs = (bs << 6) | idx; bc += 6;
    if (bc >= 8) { bc -= 8; out += String.fromCharCode((bs >> bc) & 0xff); }
  }
  return out;
}
export function jwtRole(token?: string | null): string {
  try { const p = (token || "").split("."); if (p.length < 2) return ""; const j = JSON.parse(b64urlToStr(p[1])); return j.iso_role || j.role || ""; } catch { return ""; }
}
function mapRoleKey(r?: string): string {
  r = (r || "").toString().toLowerCase();
  if (/host|manager|coordinator|\bbc\b|bim/.test(r)) return "host";
  if (/chair/.test(r)) return "chair";
  if (/secretary|minute/.test(r)) return "secretary";
  if (/client/.test(r)) return "client";
  if (/lead|author|attendee|discipline/.test(r)) return "attendee";
  return r;
}
// creator → host; else the user's attendee role; else fallbackRole (e.g. jwtRole); unknown → "attendee".
export function meetingRole(meeting: any, userId?: string | null, fallbackRole?: string): string {
  if (meeting && userId && (meeting.createdBy === userId || meeting.hostUserId === userId)) return "host";
  let r = "";
  if (meeting && meeting.attendees && userId) {
    const me = meeting.attendees.find((a: any) => a.userId === userId);
    if (me && me.role) r = me.role;
  }
  if (!r) r = fallbackRole || "";
  const k = mapRoleKey(r);
  return CAPS[k] ? k : "attendee";
}

// ── API (same method names as meetings-core.js create()) ──
const base = (pid: string) => `/api/projects/${pid}/meetings`;
export const meetingsCore = {
  listMeetings: (pid: string, status?: string): Promise<Meeting[]> => apiFetch(`${base(pid)}${status ? "?status=" + encodeURIComponent(status) : ""}`),
  getMeeting: (pid: string, mid: string): Promise<Meeting> => apiFetch(`${base(pid)}/${mid}`),
  createMeeting: (pid: string, body: any): Promise<Meeting> => apiFetch(base(pid), { method: "POST", body: JSON.stringify(body) }),
  updateMeeting: (pid: string, mid: string, body: any): Promise<Meeting> => apiFetch(`${base(pid)}/${mid}`, { method: "PUT", body: JSON.stringify(body) }),

  addAgendaItem: (pid: string, mid: string, body: any): Promise<MeetingAgendaItem> => apiFetch(`${base(pid)}/${mid}/agenda`, { method: "POST", body: JSON.stringify(body) }),
  updateAgendaItem: (pid: string, mid: string, itemId: string, body: any) => apiFetch(`${base(pid)}/${mid}/agenda/${itemId}`, { method: "PUT", body: JSON.stringify(body) }),
  deleteAgendaItem: (pid: string, mid: string, itemId: string) => apiFetch(`${base(pid)}/${mid}/agenda/${itemId}`, { method: "DELETE" }),

  addAction: (pid: string, mid: string, body: any): Promise<MeetingActionItem> => apiFetch(`${base(pid)}/${mid}/actions`, { method: "POST", body: JSON.stringify(body) }),
  updateAction: (pid: string, mid: string, actionId: string, body: any) => apiFetch(`${base(pid)}/${mid}/actions/${actionId}`, { method: "PUT", body: JSON.stringify(body) }),
  listOpenActions: (pid: string): Promise<MeetingActionItem[]> => apiFetch(`${base(pid)}/actions/open`),

  listAttendees: (pid: string, mid: string): Promise<MeetingAttendee[]> => apiFetch(`${base(pid)}/${mid}/attendees`),
  addAttendee: (pid: string, mid: string, body: any): Promise<MeetingAttendee> => apiFetch(`${base(pid)}/${mid}/attendees`, { method: "POST", body: JSON.stringify(body) }),
  updateAttendee: (pid: string, mid: string, attendeeId: string, body: any) => apiFetch(`${base(pid)}/${mid}/attendees/${attendeeId}`, { method: "PUT", body: JSON.stringify(body) }),
  deleteAttendee: (pid: string, mid: string, attendeeId: string) => apiFetch(`${base(pid)}/${mid}/attendees/${attendeeId}`, { method: "DELETE" }),

  logMinutes: (pid: string, mid: string, minutes: string, status?: string): Promise<Meeting> => apiFetch(`${base(pid)}/${mid}/minutes`, { method: "POST", body: JSON.stringify({ minutes, status }) }),
  generateMinutesDoc: (pid: string, mid: string): Promise<{ documentId: string }> => apiFetch(`${base(pid)}/${mid}/export/minutes`, { method: "POST" }),

  startLiveSession: (pid: string, mid: string, body: any = {}) => apiFetch(`${base(pid)}/${mid}/live-session`, { method: "POST", body: JSON.stringify(body) }),
  getLiveArtifacts: (pid: string, mid: string): Promise<MeetingLiveArtifacts> => apiFetch(`${base(pid)}/${mid}/live-artifacts`),

  listRecordings: (pid: string): Promise<ProjectRecording[]> =>
    apiFetch(`/api/projects/${pid}/recordings`).then((r: any) => (r && r.recordings) || []),
  recordingsByMeeting: (pid: string): Promise<{ all: ProjectRecording[]; byMeeting: Record<string, ProjectRecording[]> }> =>
    meetingsCore.listRecordings(pid).then((recs) => {
      const by: Record<string, ProjectRecording[]> = {};
      recs.forEach((r) => { if (r.meetingId) (by[r.meetingId] = by[r.meetingId] || []).push(r); });
      return { all: recs, byMeeting: by };
    }),
};
