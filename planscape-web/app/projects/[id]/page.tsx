'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { ArchiveProjectDialog } from '@/components/ArchiveProjectDialog';
import { RagBadge } from '@/components/RagBadge';
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorNote,
  PageHeader,
  Skeleton,
  toneForStatus,
  useToast,
} from '@/components/ui';
import { getProject, listClashes, listIssues, updateProject } from '@/lib/data';
import { useProjectRealtime } from '@/lib/realtime';
import type { BimIssue, ClashRecord, Project } from '@/lib/types';

/**
 * U4 — project overview.
 *
 * Was a full issues list with status filters, which meant `/projects/{id}` and
 * the (missing) `/projects/{id}/issues` were the same screen. Issues now has its
 * own grid, so this goes back to being a summary: counts, the handful of items
 * that actually need attention, and a way in. Section navigation moved to the
 * rail, so the row of section buttons is gone too.
 */
export default function ProjectPage() {
  const { id: projectId } = useParams<{ id: string }>();
  const router = useRouter();
  const [project, setProject] = useState<Project | null>(null);
  const [archiveOpen, setArchiveOpen] = useState(false);
  const [issues, setIssues] = useState<BimIssue[] | null>(null);
  const [clashes, setClashes] = useState<ClashRecord[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [live, setLive] = useState(false);

  useEffect(() => {
    getProject(projectId)
      .then(setProject)
      .catch(() => {});
  }, [projectId]);

  const loadIssues = useCallback(() => {
    listIssues(projectId, 'OPEN')
      .then(setIssues)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load issues'));
  }, [projectId]);

  useEffect(() => {
    loadIssues();
    // Clash counts are a summary signal; a failure here must not blank the page.
    listClashes(projectId, { status: 'NEW' })
      .then((r) => setClashes(r.items ?? []))
      .catch(() => setClashes([]));
  }, [projectId, loadIssues]);

  useProjectRealtime(projectId, (event) => {
    setLive(true);
    if (event.startsWith('Issue')) loadIssues();
  });

  const critical = issues?.filter((i) => i.priority === 'CRITICAL' || i.priority === 'HIGH') ?? [];

  return (
    <AppShell>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            {project?.name ?? 'Project'}
            {project && <RagBadge rag={project.ragStatus} percent={project.compliancePercent} />}
            {live && (
              <span className="inline-flex items-center gap-1 text-xs font-normal text-success">
                <span className="h-1.5 w-1.5 rounded-full bg-success" /> Live
              </span>
            )}
          </span>
        }
        description={project?.code}
        actions={
          <>
            <Button asChild>
              <Link href={`/projects/${projectId}/viewer`}>3D model</Link>
            </Button>
            <Button asChild variant="primary">
              <Link href={`/projects/${projectId}/issues/new`}>New issue</Link>
            </Button>
            {/* Shown to everyone: the server decides who may actually archive
                (author or tenant admin) and the dialog reports its 403 plainly.
                Hiding it client-side would need a permission the API doesn't
                currently tell us on this payload. */}
            {project?.status !== 'Archived' && (
              <Button variant="ghost" onClick={() => setArchiveOpen(true)}>
                Archive
              </Button>
            )}
          </>
        }
      />

      {project && (
        <ArchiveProjectDialog
          open={archiveOpen}
          onOpenChange={setArchiveOpen}
          projectId={projectId}
          projectCode={project.code}
          projectName={project.name}
          onArchived={() => router.push('/projects')}
        />
      )}

      {error && (
        <div className="mb-4">
          <ErrorNote>{error}</ErrorNote>
        </div>
      )}

      <div className="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        <Stat label="Open issues" value={issues?.length} href={`/projects/${projectId}/issues`} />
        <Stat
          label="High / critical"
          value={issues ? critical.length : undefined}
          tone="danger"
          href={`/projects/${projectId}/issues`}
        />
        <Stat label="New clashes" value={clashes?.length} href={`/projects/${projectId}/clashes`} />
        <Stat label="Compliance" value={project?.compliancePercent} suffix="%" />
      </div>

      {project && (
        <div className="mb-4">
          <AutoSyncToggle
            project={project}
            onChanged={(v) => setProject((p) => (p ? { ...p, documentSyncAutoEnabled: v } : p))}
          />
        </div>
      )}

      <Card>
        <div className="mb-3 flex items-center justify-between">
          <h2 className="text-sm font-semibold text-fg">Needs attention</h2>
          <Link href={`/projects/${projectId}/issues`} className="text-xs text-accent hover:underline">
            All issues →
          </Link>
        </div>

        {!issues && <Skeleton className="h-24 w-full" />}
        {issues && critical.length === 0 && (
          <EmptyState title="Nothing critical open" description="High and critical issues will appear here." />
        )}
        {critical.length > 0 && (
          <ul className="flex flex-col gap-1.5">
            {critical.slice(0, 8).map((i) => (
              <li key={i.id}>
                <Link
                  href={`/projects/${projectId}/issues/${i.id}`}
                  className="flex items-center justify-between gap-3 rounded border border-border px-3 py-2 transition hover:bg-surface-3"
                >
                  <span className="min-w-0 truncate text-sm font-medium">{i.title}</span>
                  <span className="flex shrink-0 items-center gap-1.5">
                    <Badge tone={toneForStatus(i.priority)}>{i.priority}</Badge>
                    <Badge tone={toneForStatus(i.status)}>{i.status.replace('_', ' ')}</Badge>
                  </span>
                </Link>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </AppShell>
  );
}

/**
 * The per-project "Auto-sync this project" toggle from the document-sync design.
 *
 * It lives on the overview rather than behind a project-settings page because no
 * such page exists, and inventing one to hold a single checkbox would be a worse
 * answer than putting the checkbox where the project already is.
 *
 * Optimistic, with rollback — the same contract the grids use. The copy spells
 * out what OFF actually does, because "auto-sync off" reads like "sync off" and
 * it very deliberately is not: linked machines keep the project and keep syncing
 * on an explicit Sync now.
 */
function AutoSyncToggle({
  project,
  onChanged,
}: {
  project: Project;
  onChanged: (value: boolean) => void;
}) {
  const { toast } = useToast();
  const [busy, setBusy] = useState(false);
  // The server defaults it to true; an older payload without the field must read
  // as on, never off.
  const enabled = project.documentSyncAutoEnabled !== false;

  async function toggle() {
    const next = !enabled;
    setBusy(true);
    onChanged(next);
    try {
      await updateProject(project.id, { documentSyncAutoEnabled: next });
      toast(next ? 'Auto-sync enabled for this project.' : 'Auto-sync paused for this project.', 'success');
    } catch (e) {
      onChanged(enabled); // roll back — never leave a switch showing a state the server rejected
      toast(e instanceof Error ? e.message : 'Could not change auto-sync', 'error');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold text-fg">Document sync</h2>
          <p className="mt-0.5 text-sm text-fg-muted">
            {enabled
              ? 'Published and shared documents reach linked machines automatically.'
              : 'Paused. Linked machines keep this project and still sync when someone chooses “Sync now”.'}
          </p>
        </div>
        <label className="flex shrink-0 cursor-pointer items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={enabled}
            disabled={busy}
            onChange={() => void toggle()}
            className="h-4 w-4 accent-accent"
            aria-label="Auto-sync this project"
          />
          <span className={enabled ? 'text-fg' : 'text-fg-muted'}>
            {busy ? 'Saving…' : enabled ? 'Auto-sync on' : 'Auto-sync off'}
          </span>
        </label>
      </div>
    </Card>
  );
}

function Stat({
  label,
  value,
  suffix,
  tone,
  href,
}: {
  label: string;
  value?: number;
  suffix?: string;
  tone?: 'danger';
  href?: string;
}) {
  const body = (
    <Card className={href ? 'transition hover:border-border-strong' : undefined}>
      <div className="text-xs text-fg-muted">{label}</div>
      {value === undefined ? (
        <Skeleton className="mt-1 h-7 w-12" />
      ) : (
        <div className={`mt-0.5 text-2xl font-semibold ${tone === 'danger' && value > 0 ? 'text-danger' : 'text-fg'}`}>
          {value}
          {suffix}
        </div>
      )}
    </Card>
  );
  return href ? <Link href={href}>{body}</Link> : body;
}
