export interface Project {
  id: string;
  code: string;
  name: string;
  description: string;
  createdAt: string;
  phase?: string;
  status?: string;
  compliancePercent?: number;
  ragStatus?: string;
  openIssueCount?: number;
}

export type IssuePriority = 'CRITICAL' | 'HIGH' | 'MEDIUM' | 'LOW';
export type IssueStatus = 'OPEN' | 'IN_PROGRESS' | 'RESOLVED' | 'CLOSED';

export interface BimIssue {
  id: string;
  code?: string;
  title: string;
  description: string;
  type: string;
  priority: IssuePriority;
  status: IssueStatus;
  assignee: string;
  assigneeEmail?: string;
  discipline: string;
  createdAt: string;
  dueDate?: string;
}

export interface IssueComment {
  id: string;
  body: string;
  authorName?: string;
  authorUserId?: string;
  source?: string;
  createdAt: string;
}

// ── Clashes ──
export type ClashSeverity = 'CRITICAL' | 'MAJOR' | 'MINOR';
export type ClashStatus = 'NEW' | 'ACKNOWLEDGED' | 'RESOLVED' | 'CLOSED';

export interface ClashRecord {
  id: string;
  status: ClashStatus;
  severity: ClashSeverity;
  discipline?: string;
  kind?: string;
  elementAGuid: string;
  elementAName?: string;
  elementAType?: string;
  elementBGuid: string;
  elementBName?: string;
  elementBType?: string;
  centreX: number;
  centreY: number;
  centreZ: number;
  overlapVolumeMm3: number;
  distanceMm?: number;
  assignedTo?: string;
  resolutionNote?: string;
  issueId?: string;
  detectedAt?: string;
}

export interface ClashListResponse {
  total: number;
  aggregates?: {
    byStatus?: Array<{ status: string; count: number }>;
    bySeverity?: Array<{ severity: string; count: number }>;
  };
  items: ClashRecord[];
}

export interface ClashDetectionResult {
  scannedPairs?: number;
  found?: number;
  created?: number;
  critical?: number;
}

// ── Models ──
export interface ProjectModel {
  id: string;
  name: string;
  discipline?: string;
  format?: string;
  revision?: string;
  uploadedAt?: string;
}

// Federation — one Draco/GLB chunk per discipline (+level/system), produced by
// the converter sidecar and served by SceneNodesController
// (GET /api/projects/{id}/scene). The mobile chunked loader consumes the same shape.
export interface SceneChunk {
  id: string;
  discipline: string;
  levelCode?: string;
  systemCode?: string;
  url: string;          // relative, e.g. /api/v1/scene-nodes/{id}/file
  hash?: string;
  sizeBytes?: number;
  vertexCount?: number;
  compression?: string;
  minX: number; minY: number; minZ: number;
  maxX: number; maxY: number; maxZ: number;
}

export interface SceneManifest {
  projectId: string;
  generatedAt?: string;
  chunks: SceneChunk[];
  minX: number; minY: number; minZ: number;
  maxX: number; maxY: number; maxZ: number;
  disciplines: string[];
}

// ── Meetings ── (mirrors MeetingsController DTOs; see mobile meetingsCore.ts)
export interface Meeting {
  id: string;
  projectId: string;
  title: string;
  meetingType?: string;
  scheduledAt: string;
  durationMinutes?: number | null;
  location?: string | null;
  meetingUrl?: string | null;
  status: string; // SCHEDULED | IN_PROGRESS | COMPLETED | CANCELLED
  minutes?: string | null;
  organiser?: string;
  createdBy?: string;
  createdAt?: string;
  actionItemCount?: number;
  liveSessionId?: string | null;
}

export interface MeetingAttendee {
  id: string;
  meetingId: string;
  userId?: string | null;
  name: string;
  email?: string | null;
  company?: string | null;
  discipline?: string | null;
  role: string; // CHAIR | SECRETARY | ATTENDEE | NOTIFIED
  attendanceStatus: string; // INVITED | CONFIRMED | ATTENDED | ABSENT | APOLOGY
}

export interface MeetingAgendaItem {
  id: string;
  meetingId: string;
  orderIndex: number;
  title: string;
  description?: string | null;
  durationMinutes?: number | null;
  presenter?: string | null;
  outcome?: string | null;
  decision?: string | null;
  status: string; // PENDING | DISCUSSED | DEFERRED | RESOLVED
}

export interface MeetingActionItem {
  id: string;
  meetingId?: string;
  meetingTitle?: string;
  description: string;
  notes?: string | null;
  assignee?: string | null;
  assigneeUserId?: string | null;
  dueDate?: string | null;
  priority?: string; // CRITICAL | HIGH | MEDIUM | LOW
  status?: string; // OPEN | IN_PROGRESS | COMPLETE | ESCALATED | CLOSED
  linkedIssueId?: string | null;
  isOverdue?: boolean;
}

export interface StartLiveSession {
  sessionId: string;
  meetingId: string;
  isNew: boolean;
  status: string;
  modelId?: string | null;
  hostUserId?: string | null;
}

export interface LiveKitToken {
  token: string;
  url: string;
  identity: string;
  room: string;
  isPresenter: boolean;
}
