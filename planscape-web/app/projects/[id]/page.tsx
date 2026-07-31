'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { ArchiveProjectDialog } from '@/components/ArchiveProjectDialog';
import { RagBadge } from '@/components/RagBadge';
import { Badge, Button, Card, EmptyState, ErrorNote, PageHeader, Skeleton, toneForStatus } from '@/components/ui';
import { getProject, listClashes, listIssues } from '@/lib/data';
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
