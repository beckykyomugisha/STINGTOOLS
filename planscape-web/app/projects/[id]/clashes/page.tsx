'use client';

import { useEffect, useState, useCallback } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { listClashes, runClashDetection } from '@/lib/data';
import type { ClashRecord, ClashStatus } from '@/lib/types';

const FILTERS: (ClashStatus | 'ALL')[] = ['ALL', 'NEW', 'ACKNOWLEDGED', 'RESOLVED', 'CLOSED'];

const severityClass: Record<string, string> = {
  CRITICAL: 'bg-red-100 text-red-700',
  MAJOR: 'bg-orange-100 text-orange-700',
  MINOR: 'bg-slate-100 text-slate-600',
};

export default function ClashesPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const [clashes, setClashes] = useState<ClashRecord[] | null>(null);
  const [filter, setFilter] = useState<ClashStatus | 'ALL'>('NEW');
  const [error, setError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(() => {
    setClashes(null);
    setError(null);
    listClashes(projectId, filter === 'ALL' ? {} : { status: filter })
      .then((r) => setClashes(r.items ?? []))
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load clashes'));
  }, [projectId, filter]);

  useEffect(() => {
    load();
  }, [load]);

  async function onRun() {
    setRunning(true);
    setNotice(null);
    try {
      const r = await runClashDetection(projectId);
      setNotice(`Detection complete — ${r.found ?? 0} found, ${r.created ?? 0} new, ${r.critical ?? 0} critical.`);
      load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Clash detection failed');
    } finally {
      setRunning(false);
    }
  }

  return (
    <AppShell>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
            ← Project
          </Link>
          <h1 className="text-xl font-semibold">Clashes</h1>
        </div>
        <button
          onClick={onRun}
          disabled={running}
          className="shrink-0 rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-60"
        >
          {running ? 'Running…' : 'Run detection'}
        </button>
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
            {s}
          </button>
        ))}
      </div>

      {notice && <p className="mb-3 rounded bg-green-50 px-3 py-2 text-sm text-green-700">{notice}</p>}
      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {!clashes && !error && <p className="text-slate-400">Loading…</p>}
      {clashes && clashes.length === 0 && <p className="text-slate-500">No clashes.</p>}

      <ul className="space-y-2">
        {clashes?.map((c) => (
          <li key={c.id}>
            <Link
              href={`/projects/${projectId}/clashes/${c.id}`}
              className="block rounded-lg bg-white p-3 ring-1 ring-slate-200 transition hover:ring-blue-300"
            >
              <div className="flex items-center justify-between gap-3">
                <span className="font-medium">
                  {(c.elementAType || 'Element A')} ↔ {(c.elementBType || 'Element B')}
                </span>
                <span className={`shrink-0 rounded px-2 py-0.5 text-xs ${severityClass[c.severity] ?? 'bg-slate-100 text-slate-600'}`}>
                  {c.severity}
                </span>
              </div>
              <div className="mt-1 text-xs text-slate-400">
                {c.status}
                {c.discipline ? ` · ${c.discipline}` : ''}
                {typeof c.overlapVolumeMm3 === 'number' ? ` · ${Math.round(c.overlapVolumeMm3).toLocaleString()} mm³` : ''}
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </AppShell>
  );
}
