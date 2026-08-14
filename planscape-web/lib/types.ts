/**
 * Mirrors both project payloads, which are NOT the same shape:
 *  - `GET /api/projects` (list) omits `description`, `configJson` and the tag
 *    format fields;
 *  - `GET /api/projects/{id}` (detail) returns the full entity.
 * Everything the list can leave out is optional here, so a grid column that
 * silently renders `undefined` is a compile-time question rather than a
 * runtime blank.
 */
export interface Project {
  id: string;
  code: string;
  name: string;
  /** Detail payload only — the list projection does not include it. */
  description?: string;
  createdAt: string;
  phase?: string;
  status?: string; // Active | Archived | Completed
  compliancePercent?: number;
  ragStatus?: string;
  openIssueCount?: number;
  memberCount?: number;
  totalElements?: number;
  taggedElements?: number;
  lastSyncAt?: string | null;
  isPinned?: boolean;
  /**
   * Document sync — "Auto-sync this project". On by default. Off means a linked
   * Planscape Companion stops syncing automatically but still syncs on an
   * explicit "Sync now"; it does not unlink anything.
   */
  documentSyncAutoEnabled?: boolean;
  city?: string | null;
  country?: string | null;
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
  /**
   * FK to the assigned AppUser — what the server actually enforces against
   * project membership. `assignee` is the legacy display-name twin and is kept
   * because plugin/mobile callers still send it; prefer this when writing, and
   * use it to preselect a member picker on read.
   */
  assigneeUserId?: string | null;
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

// ── Documents (CDE) ──
export interface ProjectDocument {
  id: string;
  fileName: string;
  description?: string | null;
  documentType?: string;
  cdeStatus: string; // WIP | SHARED | PUBLISHED | ARCHIVE | SUPERSEDED | WITHDRAWN | OBSOLETE
  suitabilityCode?: string; // S0–S7 | CR | AB
  revision?: string | null;
  discipline?: string | null;
  originator?: string | null;
  fileSizeBytes?: number;
  uploadedBy?: string;
  uploadedAt?: string;
  scanStatus?: string; // PENDING | CLEAN | INFECTED | SKIPPED
}

export interface DocumentListResponse {
  items: ProjectDocument[];
  total: number;
  page: number;
  pageSize: number;
}

// ── Project members ──
export interface ProjectMember {
  id: string; // member row id
  userId: string;
  email: string;
  displayName: string;
  projectRole: string; // Viewer | Contributor | Coordinator | Manager | Owner | Admin
  iso19650Role?: string;
  joinedAt?: string | null;
  invitedBy?: string | null;
}

export interface Iso19650Role {
  code: string;
  label: string;
}

// ── Tenant (firm-wide) administration ──
// Mirrors TenantAdminController, which is [Authorize(Roles = "Owner,Admin")] in
// its entirety — every call here 403s for anyone else, and that is correct.

/** A user account in the firm — distinct from ProjectMember, which is a seat on one project. */
export interface TenantUser {
  id: string;
  email: string;
  displayName?: string | null;
  role?: string; // Owner | Admin | Coordinator | Contributor | Viewer
  iso19650Role?: string | null;
  lastLoginAt?: string | null;
  /** false until an invitee sets a password — an invite plants an inactive row. */
  isActive?: boolean;
}

export interface TenantQuotaAxis {
  current: number;
  max: number;
}

export interface TenantDashboard {
  tenant: {
    id: string;
    name: string;
    slug?: string;
    contactEmail?: string | null;
    plan?: string;
    currency?: string;
    billingCycle?: string;
    trialExpiresAt?: string | null;
    isActive?: boolean;
    createdAt?: string;
  };
  usage: {
    authors: TenantQuotaAxis;
    coordinators: TenantQuotaAxis;
    projects: TenantQuotaAxis;
    storage: { currentMb: number; maxMb: number };
    memberSeats?: number;
  };
  users: TenantUser[];
}

/** Shape of the 402 body from a quota refusal. */
export interface QuotaExceeded {
  error: 'quota_exceeded';
  axis?: string;
  current?: number;
  max?: number;
  reason?: string;
  upgrade_url?: string;
}

// ── Cross-project search ──
export interface SearchResult {
  type: 'tag' | 'issue' | 'document' | 'meeting';
  id: string;
  label: string;
  detail: string;
  projectId: string;
  projectName: string;
}

export interface SearchResponse {
  query: string;
  count: number;
  results: SearchResult[];
}

// ── Transmittals ──
export interface Transmittal {
  id: string;
  transmittalCode: string;
  recipient: string;
  status: string; // DRAFT | SENT | ACKNOWLEDGED | RESPONDED
  notes?: string | null;
  createdBy?: string;
  createdAt?: string;
  sentAt?: string | null;
  acknowledgedAt?: string | null;
  respondedAt?: string | null;
  responseNotes?: string | null;
}

// ── Site photos ──
export interface SitePhoto {
  id: string;
  projectId: string;
  reason?: string;
  audience?: string; // Internal | PendingReview | Approved | ClientPortal | Withdrawn
  blurStatus?: string;
  watermarkApplied?: boolean;
  caption?: string | null;
  capturedAt?: string;
  capturedByName?: string | null;
  levelCode?: string | null;
  zoneCode?: string | null;
  discipline?: string | null;
  approvedAt?: string | null;
  rejectedAt?: string | null;
  rejectedReason?: string | null;
}

// ── Personal access tokens (StingBridge / headless credentials) ──
export interface AccessToken {
  id: string;
  name: string;
  /**
   * A random display slug — deliberately NOT a slice of the secret. The server
   * used to use the token's own first characters here, which leaked 7 of its 43
   * characters to anyone who could read the token list. Do not present this as
   * something the user can match against their stored token; it will not match.
   */
  prefix: string;
  createdAt: string;
  lastUsedAt?: string | null;
  expiresAt?: string | null;
}

/**
 * Response to minting. `token` is the ONLY time the plaintext secret exists
 * outside the caller's hands — the server stores a hash and cannot re-issue it.
 */
export interface MintedAccessToken extends AccessToken {
  token: string;
}
