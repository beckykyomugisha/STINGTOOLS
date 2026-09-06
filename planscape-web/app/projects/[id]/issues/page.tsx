'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { Badge, Button, DataGrid, MenuItem, PageHeader, Select, toneForStatus, type Column } from '@/components/ui';
import { listIssues, updateIssue } from '@/lib/data';
import type { BimIssue, IssuePriority, IssueStatus } from '@/lib/types';

const STATUSES: IssueStatus[] = ['OPEN', 'IN_PROGRESS', 'RESOLVED', 'CLOSED'];
const PRIORITIES: IssuePriority[] = ['CRITICAL', 'HIGH', 'MEDIUM', 'LOW'];

/**
 * U4 — Issues grid.
 *
 * This route did not exist: the project overview doubled as the issues list, so
 * `/projects/{id}/issues` 404'd. The rail links there, and an overview that is
 * really a list is a worse overview, so Issues gets its own page and the
 * overview goes back to being a summary.
 *
 * Editable columns are exactly what `UpdateIssueRequest` accepts — status,
 * priority, assignee. Title and description are edited on the detail page,
 * where there is room for them.
 */
export default function IssuesPage() {
  const { id: projectId } = useParams<{ id: string }>();
  const router = useRouter();
  const [issues, setIssues] = useState<BimIssue[] | null>(null);
  const [status, setStatus] = useState<string>('ALL');
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    setIssues(null);
    setError(null);
    listIssues(projectId, status === 'ALL' ? undefined : status)
      .then(setIssues)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load issues'));
  }, [projectId, status]);

  useEffect(load, [load]);

  const columns: Column<BimIssue>[] = [
    {
      key: 'code',
      header: 'Code',
      className: 'w-28 font-mono text-xs',
      render: (i) => i.code || i.id.slice(0, 8),
    },
    { key: 'title', header: 'Title', className: 'min-w-[16rem]' },
    {
      key: 'status',
      header: 'Status',
      className: 'w-36',
      render: (i) => <Badge tone={toneForStatus(i.status)}>{i.status}</Badge>,
      edit: { options: STATUSES, save: (i, v) => updateIssue(projectId, i.id, { status: v as IssueStatus }) },
    },
    {
      key: 'priority',
      header: 'Priority',
      className: 'w-32',
      render: (i) => <Badge tone={toneForStatus(i.priority)}>{i.priority}</Badge>,
      edit: { options: PRIORITIES, save: (i, v) => updateIssue(projectId, i.id, { priority: v as IssuePriority }) },
    },
    {
      key: 'assignee',
      header: 'Assignee',
      className: 'w-44',
      // The API returns assigneeEmail alongside the name and nothing showed it,
      // so two people called "D. Mayanja" were indistinguishable. It rides in
      // this cell rather than a column of its own — it identifies the assignee,
      // it isn't a separate fact to sort by.
      render: (i) =>
        i.assignee ? (
          <span className="flex flex-col leading-tight">
            <span>{i.assignee}</span>
            {i.assigneeEmail && <span className="text-2xs text-fg-subtle">{i.assigneeEmail}</span>}
          </span>
        ) : (
          <span className="text-fg-subtle">Unassigned</span>
        ),
      edit: { save: (i, v) => updateIssue(projectId, i.id, { assignee: v }) },
    },
    { key: 'type', header: 'Type', className: 'w-28' },
    { key: 'discipline', header: 'Discipline', className: 'w-28' },
    {
      key: 'dueDate',
      header: 'Due',
      className: 'w-28',
      render: (i) => (i.dueDate ? new Date(i.dueDate).toLocaleDateString() : <span className="text-fg-subtle">—</span>),
    },
    {
      key: 'createdAt',
      header: 'Raised',
      className: 'w-28',
      render: (i) =>
        i.createdAt ? new Date(i.createdAt).toLocaleDateString() : <span className="text-fg-subtle">—</span>,
    },
  ];

  return (
    <AppShell>
      <PageHeader
        title="Issues"
        description="Click a cell to edit status, priority or assignee inline."
        actions={
          <Button asChild variant="primary">
            <Link href={`/projects/${projectId}/issues/new`}>New issue</Link>
          </Button>
        }
      />
      <DataGrid<BimIssue>
        rows={issues}
        columns={columns}
        rowId={(i) => i.id}
        loading={!issues && !error}
        error={error}
        selectable
        onRowClick={(i) => router.push(`/projects/${projectId}/issues/${i.id}`)}
        rowMenu={(i, close) => (
          <MenuItem
            onClick={() => {
              close();
              router.push(`/projects/${projectId}/issues/${i.id}`);
            }}
          >
            Open issue
          </MenuItem>
        )}
        emptyTitle="No issues"
        emptyDescription="Raise one from the model viewer or with New issue."
        toolbar={() => (
          <Select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            aria-label="Filter by status"
            className="h-7 w-36"
          >
            <option value="ALL">All statuses</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </Select>
        )}
      />
    </AppShell>
  );
}
