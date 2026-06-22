'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { RagBadge } from '@/components/RagBadge';
import { getProject, listIssues } from '@/lib/data';
import { useProjectRealtime } from '@/lib/realtime';
import type { Project, BimIssue, IssueStatus } from '@/lib/types';

const FILTERS: (IssueStatus | 'ALL')[] = ['ALL', 'OPEN', 'IN_PROGRESS', 'RESOLVED', 'CLOSED'];

const priorityClass: Record<string, string> = {
  CRITICAL: 'bg-red-100 text-red-700',
  HIGH: 'bg-orange-100 text-orange-700',
  MEDIUM: 'bg-amber-100 text-amber-700',
  LOW: 'bg-slate-100 text-slate-600',
};

export default function ProjectPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const [project, setProject] = useState<Project | null>(null);
  const [issues, setIssues] = useState<BimIssue[] | null>(null);
  const [filter, setFilter] = useState<IssueStatus | 'ALL'>('OPEN');
  const [error, setError] = useState<string | null>(null);
  const [live, setLive] = useState(false);

  useEffect(() => {
    getProject(projectId).then(setProject).catch(() => {});
  }, [projectId]);

  const loadIssues = useCallback(() => {
    listIssues(projectId, filter === 'ALL' ? undefined : filter)
      .then(setIssues)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load issues'));
  }, [projectId, filter]);

  useEffect(() => {
    setIssues(null);
    setError(null);
    loadIssues();
  }, [loadIssues]);

  // Live updates: refresh the list when an issue changes elsewhere.
  useProjectRealtime(projectId, (event) => {
    setLive(true);
    if (event.startsWith('Issue')) loadIssues();
  });

  return (
    <AppShell>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <Link href="/projects" className="text-sm text-slate-400 hover:underline">
            ← Projects
          </Link>
          <div className="flex items-center gap-2">
            <h1 className="text-xl font-semibold">{project?.name ?? 'Project'}</h1>
            {project && <RagBadge rag={project.ragStatus} percent={project.compliancePercent} />}
          </div>
          {project?.code && <p className="text-xs text-slate-400">{project.code}</p>}
          {live && (
            <span className="mt-1 inline-flex items-center gap-1 text-xs text-green-600">
              <span className="h-1.5 w-1.5 rounded-full bg-green-500" /> Live
            </span>
          )}
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <Link
            href={`/projects/${projectId}/documents`}
            className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
          >
            Documents
          </Link>
          <Link
            href={`/projects/${projectId}/transmittals`}
            className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
          >
            Transmittals
          </Link>
          <Link
            href={`/projects/${projectId}/photos`}
            className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
          >
            Photos
          </Link>
          <Link
            href={`/projects/${projectId}/members`}
            className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
          >
            Members
          </Link>
          <Link
            href={`/projects/${projectId}/clashes`}
            className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
          >
            Clashes
          </Link>
          <Link
            href={`/projects/${projectId}/meetings`}
            className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
          >
            Meetings
          </Link>
          <Link
            href={`/projects/${projectId}/models`}
            className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
          >
            Models
          </Link>
          <Link
            href={`/projects/${projectId}/viewer`}
            className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
          >
            3D model
          </Link>
          <Link
            href={`/projects/${projectId}/issues/new`}
            className="rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            New issue
          </Link>
        </div>
      </div>

      <div className="mb-3 flex flex-wrap gap-2">
        {FILTERS.map((s) => (
          <button
            key={s}
            onClick={() => setFilter(s)}
            className={`rounded-full px-3 py-1 text-xs ${
              filter === s ? 'bg-blue-600 text-white' : 'bg-white text-slate-600 ring-1 ring-slate-200'
            }`}
          >
            {s.replace('_', ' ')}
          </button>
        ))}
      </div>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {!issues && !error && <p className="text-slate-400">Loading…</p>}
      {issues && issues.length === 0 && <p className="text-slate-500">No issues.</p>}

      <ul className="space-y-2">
        {issues?.map((i) => (
          <li key={i.id}>
            <Link
              href={`/projects/${projectId}/issues/${i.id}`}
              className="block rounded-lg bg-white p-3 ring-1 ring-slate-200 transition hover:ring-blue-300"
            >
              <div className="flex items-center justify-between gap-3">
                <span className="font-medium">{i.title}</span>
                <span className={`shrink-0 rounded px-2 py-0.5 text-xs ${priorityClass[i.priority] ?? 'bg-slate-100 text-slate-600'}`}>
                  {i.priority}
                </span>
              </div>
              <div className="mt-1 text-xs text-slate-400">
                {i.status.replace('_', ' ')}
                {i.discipline ? ` · ${i.discipline}` : ''}
                {i.assignee ? ` · ${i.assignee}` : ''}
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </AppShell>
  );
}
