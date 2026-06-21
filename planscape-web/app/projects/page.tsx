'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { AppShell } from '@/components/AppShell';
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
      <h1 className="mb-4 text-xl font-semibold">Projects</h1>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {!projects && !error && <p className="text-slate-400">Loading…</p>}
      {projects && projects.length === 0 && <p className="text-slate-500">No projects yet.</p>}

      <div className="grid gap-3 sm:grid-cols-2">
        {projects?.map((p) => (
          <Link
            key={p.id}
            href={`/projects/${p.id}`}
            className="rounded-lg bg-white p-4 ring-1 ring-slate-200 transition hover:ring-blue-300"
          >
            <div className="flex items-center justify-between gap-3">
              <span className="font-medium">{p.name}</span>
              <span className="text-xs text-slate-400">{p.code}</span>
            </div>
            {p.description && <p className="mt-1 line-clamp-2 text-sm text-slate-500">{p.description}</p>}
          </Link>
        ))}
      </div>
    </AppShell>
  );
}
