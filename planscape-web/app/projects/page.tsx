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
import { listProjects, updateProject, toggleProjectPin } from '@/lib/data';
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
 *
 * Columns beyond the core set are hidden by default and opt-in via the grid's
 * "Columns" picker (choice persisted per browser). Everything shown here already
 * comes back from GET /api/projects — no extra request, and no N+1: the fields
 * were being fetched and thrown away.
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

  // Optimistic pin: the star flips immediately and reverts if the server says
  // no. A full reload would re-sort the grid under the cursor (pinned rows sort
  // first), which reads as the row jumping away from the click.
  const pin = useCallback(async (p: Project) => {
    setProjects((cur) => cur?.map((x) => (x.id === p.id ? { ...x, isPinned: !x.isPinned } : x)) ?? cur);
    try {
      await toggleProjectPin(p.id);
    } catch {
      setProjects((cur) => cur?.map((x) => (x.id === p.id ? { ...x, isPinned: p.isPinned } : x)) ?? cur);
    }
  }, []);

  const columns: Column<Project>[] = [
    {
      key: 'rowNo',
      header: '#',
      className: 'w-10 text-fg-subtle tabular-nums',
      sortable: false,
      // Position in the CURRENT view, so it renumbers with sort/filter rather
      // than pretending to be a stable project number. The project's real
      // identifier is Code.
      render: (p) => <span>{(projects ?? []).indexOf(p) + 1}</span>,
    },
    {
      key: 'pin',
      header: '',
      className: 'w-8',
      sortable: false,
      render: (p) => (
        <button
          type="button"
          title={p.isPinned ? 'Unpin' : 'Pin to top'}
          aria-label={p.isPinned ? `Unpin ${p.name}` : `Pin ${p.name}`}
          onClick={(e) => {
            e.stopPropagation();
            void pin(p);
          }}
          className={p.isPinned ? 'text-warning' : 'text-fg-subtle hover:text-fg'}
        >
          {p.isPinned ? '★' : '☆'}
        </button>
      ),
    },
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
      key: 'elements',
      header: 'Elements',
      className: 'w-32 tabular-nums',
      value: (p) => p.totalElements ?? 0,
      render: (p) =>
        p.totalElements
          ? <span>{p.taggedElements ?? 0} / {p.totalElements}</span>
          : <span className="text-fg-subtle">—</span>,
    },
    {
      key: 'location',
      header: 'Location',
      className: 'w-40',
      value: (p) => [p.city, p.country].filter(Boolean).join(', '),
      render: (p) => {
        const where = [p.city, p.country].filter(Boolean).join(', ');
        return where ? <span>{where}</span> : <span className="text-fg-subtle">—</span>;
      },
    },
    {
      key: 'createdAt',
      header: 'Created',
      className: 'w-28',
      render: (p) =>
        p.createdAt ? new Date(p.createdAt).toLocaleDateString() : <span className="text-fg-subtle">—</span>,
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
        description="Click a row to open it. Double-click a name or phase to edit it inline."
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
        storageKey="projects"
        // Off by default so the default view stays the one people already know;
        // the picker is how you opt in, and the choice sticks per browser.
        defaultHiddenColumns={['elements', 'location', 'createdAt']}
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
