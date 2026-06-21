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
