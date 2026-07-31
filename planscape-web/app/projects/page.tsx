'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { AppShell } from '@/components/AppShell';
import { LoadingBlock } from '@/components/ui';
import { RagBadge } from '@/components/RagBadge';
import { listProjects } from '@/lib/data';
import type { Project } from '@/lib/types';

export default function ProjectsPage() {
  const [projects, setProjects] = useState<Project[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    listProjects()
      .then(setProjects)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load projects'));
  }, []);

  return (
    <AppShell>
      <div className="mb-4 flex items-center justify-between">
        <h1 className="text-xl font-semibold">Projects</h1>
        <Link
          href="/projects/new"
          className="rounded bg-accent px-3 py-2 text-sm font-medium text-fg-on-accent hover:bg-accent-hover"
        >
          New project
        </Link>
      </div>

      {error && <p className="mb-3 rounded bg-danger-subtle px-3 py-2 text-sm text-danger">{error}</p>}
      {!projects && !error && <LoadingBlock />}
      {projects && projects.length === 0 && <p className="text-fg-muted">No projects yet.</p>}

      <div className="grid gap-3 sm:grid-cols-2">
        {projects?.map((p) => (
          <Link
            key={p.id}
            href={`/projects/${p.id}`}
            className="rounded-lg bg-surface p-4 ring-1 ring-border transition hover:ring-accent"
          >
            <div className="flex items-center justify-between gap-3">
              <span className="font-medium">{p.name}</span>
              <div className="flex shrink-0 items-center gap-2">
                <RagBadge rag={p.ragStatus} percent={p.compliancePercent} />
                <span className="text-xs text-fg-subtle">{p.code}</span>
              </div>
            </div>
            {p.description && <p className="mt-1 line-clamp-2 text-sm text-fg-muted">{p.description}</p>}
            {typeof p.openIssueCount === 'number' && (
              <p className="mt-1 text-xs text-fg-subtle">{p.openIssueCount} open issue(s)</p>
            )}
          </Link>
        ))}
      </div>
    </AppShell>
  );
}
