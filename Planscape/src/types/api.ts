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
  type: 'CREATE_ISSUE' | 'UPDATE_ISSUE' | 'TRANSITION_CDE';
  payload: Record<string, unknown>;
  createdAt: string;
  synced: boolean;
}
