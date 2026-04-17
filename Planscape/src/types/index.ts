export interface BimIssue {
  id: string;
  projectId: string;
  title: string;
  description?: string;
  status: string;
  priority: string;
  createdAt: string;
  lastModifiedUtc?: string;
  version?: number;
  latitude?: number;
  longitude?: number;
  locationAccuracy?: number;
  deviceId?: string;
  attachments?: IssueAttachment[];
}

export interface IssueAttachment {
  id: string;
  issueId: string;
  filePath: string;
  contentType: string;
  thumbnailUrl?: string;
}

export interface DocumentRecord {
  id: string;
  projectId: string;
  fileName: string;
  filePath: string;
  contentType: string;
  cdeState: string;
  versionNumber?: number;
  latitude?: number;
  longitude?: number;
}

export interface QueuedAction {
  id: string;
  type: 'CREATE_ISSUE' | 'UPDATE_ISSUE' | 'TRANSITION_CDE' | 'UPLOAD_ATTACHMENT';
  payload: unknown;
  createdAt: string;
  attempts: number;
  lastError?: string;
}

export interface SyncConflict {
  elementId: string;
  serverTimestamp?: string;
  clientTimestamp?: string;
  resolution: string;
}
