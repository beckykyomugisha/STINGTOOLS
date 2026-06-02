/** Matches Planscape.Core entity shapes */

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  // Server returns camelCase via ASP.NET Core JSON serialiser
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  userName: string;
  role: string;
  tier: string;
  mimEnabled: boolean;
}

export interface UserProfile {
  id: string;
  tenantId: string;
  email: string;
  displayName: string;
  role: string;
  iso19650Role: string;
  tier: string;
  mimEnabled?: boolean;
  lastLoginAt?: string;
  // Sent by the server profile endpoint (AuthController: `tenantName = u.Tenant!.Name`).
  tenantName?: string;
}

export interface Project {
  id: string;
  code: string;
  name: string;
  description: string;
  createdAt: string;
}

export interface ComplianceSnapshot {
  id: string;
  projectId: string;
  compliancePercent: number;
  strictPercent: number;
  totalElements: number;
  taggedElements: number;
  staleCount: number;
  warningCount: number;
  placeholderCount: number;
  timestamp: string;
  byDiscipline: Record<string, DisciplineCompliance>;
}

export interface DisciplineCompliance {
  total: number;
  tagged: number;
  compliancePct: number;
}

export interface BimIssue {
  id: string;
  projectId: string;
  issueCode: string;
  title: string;
  description: string;
  type: string;
  priority: 'CRITICAL' | 'HIGH' | 'MEDIUM' | 'LOW';
  status: 'OPEN' | 'IN_PROGRESS' | 'RESOLVED' | 'CLOSED';
  assignee: string;
  assigneeEmail?: string;
  assigneeUserId?: string;
  discipline: string;
  revision: string;
  elementIds: string;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  dueDate?: string;
  resolvedAt?: string;
  isOverdue?: boolean;
  daysOpen?: number;
  latitude?: number;
  longitude?: number;
  locationAccuracy?: number;
  deviceId?: string;
  source?: 'mobile' | 'plugin' | 'web' | 'mobile-bridge';
  attachmentCount?: number;
  // MODEL-VIEWER — 3D anchor. Populated when the issue was raised from the
  // viewer's "create issue here" action.
  modelId?: string | null;
  modelElementGuid?: string | null;
  modelX?: number | null;
  modelY?: number | null;
  modelZ?: number | null;
  // WATCHERS — array of AppUser ids who get push notifications on every
  // status change, comment, and attachment upload, in addition to the
  // assignee. Server stores it as a JSON-encoded string and emits it as
  // `string` in GetIssues responses; on the wire we accept both shapes.
  watcherUserIds?: string[] | string | null;
  // CO-ASSIGNEES — additional users jointly responsible for resolving the
  // issue. All receive the same assignment push as the primary assignee.
  coAssigneeUserIds?: string[] | string | null;
}

/** NEW-INFO-06/07 — Activity timeline entries surfaced from AuditLog. */
export interface IssueActivityEntry {
  id: string;
  action: 'CREATE' | 'UPDATE' | 'DELETE' | string;
  entityType: string;
  entityId: string;
  userName?: string;
  timestamp: string;
  details?: Record<string, unknown>;
}

export interface ProjectMember {
  userId: string;
  email: string;
  displayName: string;
  projectRole: string;
  iso19650Role: string;
}

export interface IssueAttachment {
  id: string;
  issueId: string;
  documentId: string;
  fileName: string;
  contentType: string;
  thumbnailUrl?: string;
  /** Phase 94 — authenticated URL for the raw attachment binary (preferred) or
   *  full-size variant when the server exposes it. Consumed by the mobile
   *  photo gallery in app/(tabs)/issue-detail.tsx. */
  url?: string;
  uploadedAt: string;
}

export interface DocumentRecord {
  id: string;
  projectId: string;
  fileName: string;
  documentType: string;
  description: string;
  cdeStatus: 'WIP' | 'SHARED' | 'PUBLISHED' | 'ARCHIVE';
  suitabilityCode: string;
  revision: string;
  originator: string;
  createdAt: string;
  updatedAt: string;
}

export interface DashboardData {
  project: Project;
  compliance: ComplianceSnapshot | null;
  openIssueCount: number;
  documentCount: number;
  recentIssues: BimIssue[];
}

export type CDEStatus = 'WIP' | 'SHARED' | 'PUBLISHED' | 'ARCHIVE';

/**
 * A tagged BIM element as serialized by the server — the raw
 * `TaggedElement` entity
 * (`Planscape.Server/src/Planscape.Core/Entities/TaggedElement.cs`).
 * ASP.NET Core uses its default camelCase policy, so each field below
 * mirrors the entity's PascalCase property with a lowercased first
 * letter. This interface is the wire contract; do NOT invent verbose
 * aliases (an earlier version declared `assTag1`/`discipline`/`systemType`
 * /… which matched nothing on the wire, so every field deserialized as
 * `undefined`).
 *
 * Producers:
 *   - `lookupElement()` → GET /api/tagsync/elements/search (returns the
 *     raw entity, verified in TagSyncController.SearchElements).
 *   - `listIfcElements()` → GET /api/projects/{id}/tagged-elements
 *     (NOTE: that route does not currently exist server-side — flagged
 *     separately; the return type stays `TaggedElement[]` for whenever it
 *     lands).
 *
 * `lvl` vs `level`: the server emits BOTH. `lvl` is the ISO 19650 level
 * *code* (e.g. "L02"); `level` is the human-readable level *name*
 * (e.g. "Level 2"). They are distinct fields — do not conflate them.
 */
export interface TaggedElement {
  id: string;
  projectId: string;
  revitElementId: number;
  uniqueId: string;

  // 8 ISO 19650 source tokens
  disc: string;
  loc: string;
  zone: string;
  lvl: string; // level CODE — e.g. "L02"
  sys: string;
  func: string;
  prod: string;
  seq: string;

  // Assembled tags
  tag1: string; // full 8-segment tag, e.g. "M-BLD1-Z01-L02-HVAC-SUP-AHU-0042"
  tag7?: string | null; // rich descriptive narrative
  tag7A?: string | null; // Identity Header
  tag7B?: string | null; // System & Function
  tag7C?: string | null; // Spatial Context
  tag7D?: string | null; // Lifecycle & Status
  tag7E?: string | null; // Technical Specs
  tag7F?: string | null; // Classification

  // Context
  categoryName: string;
  familyName: string;
  typeName: string;
  status?: string | null; // NEW/EXISTING/DEMOLISHED/TEMPORARY
  rev?: string | null;
  gridRef?: string | null;
  roomName?: string | null;
  level?: string | null; // level NAME — distinct from `lvl` above

  // Compliance state
  isStale: boolean;
  isComplete: boolean;
  isFullyResolved: boolean;
  validationErrors?: string | null; // JSON array of errors

  // Audit / sync
  previousTag?: string | null;
  source?: string | null; // "archicad" | "ifc" | "revit" | null
  syncedAt: string;
  lastModifiedUtc?: string | null;
}

export interface OfflineAction {
  id: string;
  /**
   * S3.7 (April 2026) — coverage extended from issue-only to every write
   * the mobile app makes. Every site-team action is now safely queueable
   * when network is patchy:
   *
   *   CREATE_ISSUE / UPDATE_ISSUE / TRANSITION_CDE / ATTACH_PHOTO
   *   POST_COMMENT       — issue comments
   *   ADD_MEETING_ACTION — meeting action items
   *   UPDATE_MEETING_ACTION
   *   DIARY_ENTRY        — daily site diary entries
   *   STAGE_SIGNOFF      — stage-gate criterion sign-off
   *   ATTACH_AUDIO       — voice notes (S6.1)
   *   ATTACH_MARKUP      — 3D markup polylines (S6.2)
   *
   * DEFERRED (see docs/MOBILE_DEFERRED_FEATURES.md): PIN_PLACE / PIN_DELETE
   * (3D issue pins) were removed — no pin CRUD endpoint exists yet, and no
   * screen enqueued them. Re-add when the pin endpoints land.
   */
  type:
    | 'CREATE_ISSUE'
    | 'UPDATE_ISSUE'
    | 'TRANSITION_CDE'
    | 'ATTACH_PHOTO'
    | 'POST_COMMENT'
    | 'ADD_MEETING_ACTION'
    | 'UPDATE_MEETING_ACTION'
    | 'DIARY_ENTRY'
    | 'STAGE_SIGNOFF'
    | 'ATTACH_AUDIO'
    | 'ATTACH_MARKUP'
    // Phase 178 — site-photo capture, queued when offline.
    | 'CAPTURE_SITE_PHOTO'
    // T3-17 — mobile deliverable CRUD + transition, queued when offline.
    | 'CREATE_DELIVERABLE'
    | 'UPDATE_DELIVERABLE'
    | 'TRANSITION_DELIVERABLE'
    // HC-11 — Healthcare Pack mobile screens: queued when network is absent.
    | 'HC_MGAS_VERIFICATION'
    | 'HC_PRESSURE_LOG'
    | 'HC_ANTI_LIGATURE_AUDIT';
  payload: Record<string, unknown>;
  createdAt: string;
  synced: boolean;
  /** Phase 96 — retry bookkeeping. Actions move to failed side-queue at 3. */
  retryCount?: number;
  lastError?: string;
  /** N-G17 — exponential-backoff gate: epoch ms when this action may retry. */
  nextRetryAt?: number;
}

// ── NEW-INT-01 — entities the mobile app can now list/read ────────────

export interface Transmittal {
  id: string;
  projectId: string;
  transmittalNumber: string;
  subject: string;
  issuedBy: string;
  issuedTo: string;
  status: 'DRAFT' | 'SENT' | 'ACKNOWLEDGED';
  createdAt: string;
  sentAt?: string;
  documentCount?: number;
}

export interface Meeting {
  id: string;
  projectId: string;
  title: string;
  /** Meeting type: BIM Coordination | Design Review | Client Review | etc. */
  meetingType: string;
  /** Legacy alias kept for backward compat */
  type?: string;
  scheduledAt: string;
  durationMinutes?: number | null;
  location?: string | null;
  meetingUrl?: string | null;
  /** SCHEDULED | IN_PROGRESS | COMPLETED | CANCELLED */
  status: string;
  minutes?: string | null;
  minutesDocumentId?: string | null;
  organiser?: string;
  createdBy?: string;
  createdAt?: string;
  notifiedUserIds?: string | null;
  recurrenceRule?: string | null;
  seriesId?: string | null;
  actionItemCount?: number;
}

export interface WorkflowRun {
  id: string;
  projectId: string;
  presetName: string;
  userName: string;
  stepsPassed: number;
  stepsFailed: number;
  stepsSkipped: number;
  durationMs: number;
  complianceBefore: number;
  complianceAfter: number;
  executedAt: string;
}

export interface WarningRecord {
  id: string;
  projectId: string;
  category: string;
  severity: string;
  description: string;
  elementId?: string;
  createdAt: string;
  // Per-warning detail fields read by app/warnings/index.tsx. These are
  // optional + guarded at the call site: the current server `GET /warnings`
  // route returns a compliance-snapshot TREND, not per-warning records, so
  // none of the detail fields are emitted yet (see FINDINGS — warnings
  // contract gap). Typed here to match what the screen consumes without
  // asserting the server sends them.
  elementCount?: number;
  autoFixStrategy?: string;
  firstSeen?: string;
  discipline?: string;
}

export interface ProjectSettings {
  issueTypes: string[];
  priorities: string[];
  disciplines: string[];
  cdeStates: string[];
  suitabilityCodes: string[];
  limits: { maxAttachmentMB: number; maxDocumentMB: number; maxPhotosPerIssue: number };
  slaHours: { critical: number; high: number; medium: number; low: number };
  geofence: { hasBoundary: boolean; requireBoundary: boolean };
  // Phase 144 — first-class admin booleans (stored on Project row, not ConfigJson)
  // Phase 145 — adds the custom deliverable state machine carrier.
  admin: {
    enforceIso19650Naming: boolean;
    hasCustomDeliverableStateMachine: boolean;
    customDeliverableStateMachineJson: string | null;
  };
}

export interface NotificationPreferences {
  id: string;
  userId: string;
  tenantId: string;
  issuesEnabled: boolean;
  complianceEnabled: boolean;
  revisionsEnabled: boolean;
  meetingsEnabled: boolean;
  slaBreachesEnabled: boolean;
  channel: 'push' | 'email' | 'signalr' | 'all';
  quietHoursStart?: string;
  quietHoursEnd?: string;
  timeZone?: string;
  updatedAt: string;
}

// ── Site Photos (Phase 178, slice 3) ───────────────────────────────────
//
// Mirrors `SitePhotoDto` in Planscape.Server/.../SitePhotosController.cs.
// Six-Reason taxonomy: Progress | Issue | Defect | Safety | AsBuilt | Reference.
// 5-state Audience machine: Internal → PendingReview → Approved → ClientPortal → Withdrawn.

export type SitePhotoReason =
  | 'Progress'
  | 'Issue'
  | 'Defect'
  | 'Safety'
  | 'AsBuilt'
  | 'Reference';

export type SitePhotoAudience =
  | 'Internal'
  | 'PendingReview'
  | 'Approved'
  | 'ClientPortal'
  | 'Withdrawn';

export interface SitePhoto {
  id: string;
  projectId: string;
  documentId: string;
  reason: SitePhotoReason;
  audience: SitePhotoAudience;
  blurStatus: string;
  watermarkApplied: boolean;
  caption: string | null;
  capturedAt: string;
  capturedByUserId: string | null;
  levelCode: string | null;
  zoneCode: string | null;
  anchorIssueId: string | null;
  anchorElementGuid: string | null;
  modelId: string | null;
  modelX: number | null;
  modelY: number | null;
  modelZ: number | null;
  pairKey: string | null;
  classifierConfidence: number;
  approvedAt: string | null;
  approvedByUserId: string | null;
  rejectedAt: string | null;
  rejectedReason: string | null;
  latitude: number | null;
  longitude: number | null;
  // Phase 178 FU1 — resolved at projection time (NOT stored on SitePhoto):
  //   capturedByName joins AppUser.DisplayName via capturedByUserId so
  //   rows can render "Captured by …" without a second round-trip.
  //   discipline is derived from the linked BimIssue.Discipline when
  //   anchorIssueId is set, else null.
  capturedByName?: string | null;
  discipline?: string | null;
}

export interface SitePhotoListResponse {
  items: SitePhoto[];
  total: number;
  page: number;
  pageSize: number;
  /** Phase 179.2 — ids of photos in `items` that the caller must
   *  accept the project NDA for before the photo bytes will load.
   *  Empty when the caller bypasses ACL (Admin / Owner / SecurityOfficer)
   *  or no listed photo carries an NDA-required PhotoAccessRule. */
  ndaRequiredIds?: string[];
}

export interface SitePhotoListFilters {
  reason?: SitePhotoReason;
  audience?: SitePhotoAudience;
  levelCode?: string;
  zoneCode?: string;
  anchorElementGuid?: string;
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}

export interface SitePhotoDigestPreview {
  projectId: string;
  windowStart: string;
  count: number;
  items: Array<{
    id: string;
    reason: SitePhotoReason;
    caption: string | null;
    levelCode: string | null;
    zoneCode: string | null;
    capturedAt: string;
    approvedAt: string | null;
  }>;
}

/** Optional capture metadata posted with the multipart body. */
export interface SitePhotoCaptureMeta {
  reason: SitePhotoReason;
  caption?: string;
  levelCode?: string;
  zoneCode?: string;
  latitude?: number;
  longitude?: number;
  accuracyM?: number;
  pairKey?: string;
  classifierConfidence?: number;
  classifierSignals?: Record<string, unknown>;
  capturedAt?: string;
  deviceId?: string;
  source?: string;
  queuedClient?: boolean;
  anchorIssueId?: string;
  anchorElementGuid?: string;
  modelId?: string;
  modelX?: number;
  modelY?: number;
  modelZ?: number;
}
