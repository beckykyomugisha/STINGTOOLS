/** Matches Planscape.Core entity shapes */

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  refreshToken: string;
  expiresAt: string;
  user: UserProfile;
}

export interface UserProfile {
  id: string;
  email: string;
  displayName: string;
  role: string;
  tenantId: string;
  tenantName: string;
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

export interface TaggedElement {
  id: string;
  projectId: string;
  uniqueId: string;
  assTag1: string;
  discipline: string;
  location: string;
  zone: string;
  level: string;
  systemType: string;
  function: string;
  productCode: string;
  sequenceNumber: string;
  status: string;
  revision: string;
  categoryName: string;
  familyName: string;
  typeName: string;
  roomName: string;
  gridRef: string;
  tag7Summary: string;
  syncedAt: string;
}

export interface OfflineAction {
  id: string;
  /** Phase 94 adds ATTACH_PHOTO for mobile photo uploads queued when offline. */
  type: 'CREATE_ISSUE' | 'UPDATE_ISSUE' | 'TRANSITION_CDE' | 'ATTACH_PHOTO';
  payload: Record<string, unknown>;
  createdAt: string;
  synced: boolean;
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
  type: string;
  scheduledAt: string;
  status: string;
  organiser: string;
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
