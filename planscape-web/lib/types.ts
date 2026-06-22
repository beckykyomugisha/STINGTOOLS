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
