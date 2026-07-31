'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { ArchiveProjectDialog } from '@/components/ArchiveProjectDialog';
import { RagBadge } from '@/components/RagBadge';
import {
  Badge,
  Button,
  DataGrid,
  MenuItem,
  MenuSeparator,
  PageHeader,
  toneForStatus,
  type Column,
} from '@/components/ui';
import { listProjects, updateProject } from '@/lib/data';
import type { Project } from '@/lib/types';

/**
 * The last list in the app still rendering card tiles. Now a DataGrid like
 * Issues / Clashes / Members, per `docs/ACC_UI_SHELL_GRID_CONTRACT.md`.
 *
 * Editability was checked against `ProjectsController` rather than assumed:
 *  - `name`, `phase` — `PUT /api/projects/{id}` (`UpdateProjectRequest`) accepts
 *    both, so both edit inline.
 *  - `code` — no write anywhere in the controller, AND it is the token the
 *    archive endpoint demands as proof of intent. Read-only.
 *  - `status` — the PUT *does* accept it, and it is still read-only here. Making
 *    it an editable cell would be a second route to `Archived` that skips the
 *    confirm-code gate the server put there deliberately. Archiving goes through
 *    the row action.
 *  - RAG / compliance %, open issues, members, elements — server-computed, no
 *    write endpoint. Read-only.
 */
const PHASES = ['', 'Concept', 'Design', 'Technical', 'Construction', 'Handover', 'Operation'];

export default function ProjectsPage() {
  const router = useRouter();
  const [projects, setProjects] = useState<Project[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [archiving, setArchiving] = useState<Project | null>(null);

  const load = useCallback(() => {
    listProjects()
      .then(setProjects)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load projects'));
  }, []);

  useEffect(load, [load]);

  const columns: Column<Project>[] = [
    {
      key: 'name',
      header: 'Project',
      className: 'min-w-[14rem] font-medium',
      edit: { save: (p, v) => updateProject(p.id, { name: v }) },
    },
    { key: 'code', header: 'Code', className: 'w-32 font-mono text-xs' },
    {
      key: 'compliancePercent',
      header: 'Compliance',
      className: 'w-32',
      value: (p) => p.compliancePercent ?? -1,
      render: (p) =>
        p.compliancePercent == null && !p.ragStatus ? (
          <span className="text-fg-subtle">—</span>
        ) : (
          <RagBadge rag={p.ragStatus} percent={p.compliancePercent} />
        ),
    },
    {
      key: 'openIssueCount',
      header: 'Open issues',
      className: 'w-28',
      value: (p) => p.openIssueCount ?? 0,
      render: (p) =>
        p.openIssueCount ? (
          <Link
            href={`/projects/${p.id}/issues`}
            onClick={(e) => e.stopPropagation()}
            className="text-accent hover:underline"
          >
            {p.openIssueCount}
          </Link>
        ) : (
          <span className="text-fg-subtle">0</span>
        ),
    },
    {
      key: 'phase',
      header: 'Phase',
      className: 'w-36',
      edit: { options: PHASES, save: (p, v) => updateProject(p.id, { phase: v }) },
    },
    {
      key: 'status',
      header: 'Status',
      className: 'w-28',
      render: (p) =>
        p.status ? <Badge tone={toneForStatus(p.status)}>{p.status}</Badge> : <span className="text-fg-subtle">—</span>,
    },
    { key: 'memberCount', header: 'Members', className: 'w-24' },
    {
      key: 'lastSyncAt',
      header: 'Last sync',
      className: 'w-28',
      render: (p) =>
        p.lastSyncAt ? new Date(p.lastSyncAt).toLocaleDateString() : <span className="text-fg-subtle">Never</span>,
    },
    {
      key: 'actions',
      header: '',
      className: 'w-24',
      sortable: false,
      render: (p) =>
        p.status === 'Archived' ? null : (
          <Button
            size="sm"
            variant="ghost"
            onClick={(e) => {
              e.stopPropagation();
              setArchiving(p);
            }}
          >
            Archive
          </Button>
        ),
    },
  ];

  return (
    <AppShell>
      <PageHeader
        title="Projects"
        description="Name and phase are editable inline."
        actions={
          <Button asChild variant="primary">
            <Link href="/projects/new">New project</Link>
          </Button>
        }
      />

      <DataGrid<Project>
        rows={projects}
        columns={columns}
        rowId={(p) => p.id}
        loading={!projects && !error}
        error={error}
        onRowClick={(p) => router.push(`/projects/${p.id}`)}
        rowMenu={(p, close) => (
          <>
            <MenuItem
              onClick={() => {
                close();
                router.push(`/projects/${p.id}`);
              }}
            >
              Open project
            </MenuItem>
            <MenuItem
              onClick={() => {
                close();
                router.push(`/projects/${p.id}/issues`);
              }}
            >
              Open issues
            </MenuItem>
            <MenuSeparator />
            <MenuItem
              disabled={p.status === 'Archived'}
              onClick={() => {
                close();
                setArchiving(p);
              }}
            >
              Archive…
            </MenuItem>
          </>
        )}
        emptyTitle="No projects yet"
        emptyDescription="Create one to start syncing models, issues and documents."
      />

      {archiving && (
        <ArchiveProjectDialog
          open={!!archiving}
          onOpenChange={(o) => !o && setArchiving(null)}
          projectId={archiving.id}
          projectCode={archiving.code}
          projectName={archiving.name}
          onArchived={() => {
            setArchiving(null);
            load();
          }}
        />
      )}
    </AppShell>
  );
}
