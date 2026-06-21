'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { getClash, updateClash, promoteClashToIssue } from '@/lib/data';
import type { ClashRecord, ClashStatus } from '@/lib/types';

const STATUSES: ClashStatus[] = ['NEW', 'ACKNOWLEDGED', 'RESOLVED', 'CLOSED'];

function Field({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wide text-slate-400">{label}</dt>
      <dd className="text-sm">{value || '—'}</dd>
    </div>
  );
}

export default function ClashDetailPage() {
  const params = useParams<{ id: string; clashId: string }>();
  const projectId = params.id;
  const clashId = params.clashId;
  const router = useRouter();

  const [clash, setClash] = useState<ClashRecord | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    getClash(projectId, clashId)
      .then(setClash)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load clash'));
  }, [projectId, clashId]);

  async function changeStatus(s: ClashStatus) {
    if (!clash || clash.status === s) return;
    const prev = clash.status;
    setClash({ ...clash, status: s });
    try {
      await updateClash(projectId, clashId, { status: s });
    } catch {
      setClash({ ...clash, status: prev });
      setError('Failed to update status');
    }
  }

  async function onPromote() {
    try {
      const r = await promoteClashToIssue(projectId, clashId);
      if (r.issueId) {
        router.push(`/projects/${projectId}/issues/${r.issueId}`);
      } else {
        setNotice('Promoted to an issue.');
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to promote');
    }
  }

  return (
    <AppShell>
      <Link href={`/projects/${projectId}/clashes`} className="text-sm text-slate-400 hover:underline">
        ← Back to clashes
      </Link>

      {error && <p className="my-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {notice && <p className="my-3 rounded bg-green-50 px-3 py-2 text-sm text-green-700">{notice}</p>}
      {!clash && !error && <p className="mt-3 text-slate-400">Loading…</p>}

      {clash && (
        <>
          <h1 className="mt-1 text-xl font-semibold">
            {(clash.elementAType || 'Element A')} ↔ {(clash.elementBType || 'Element B')}
          </h1>
          <div className="mt-1 text-xs text-slate-400">
            {clash.severity} · {clash.status}
            {clash.discipline ? ` · ${clash.discipline}` : ''}
          </div>

          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <div className="rounded-lg bg-white p-4 ring-1 ring-slate-200">
              <h2 className="mb-2 text-sm font-semibold">Geometry</h2>
              <dl className="space-y-2">
                <Field label="Overlap volume" value={typeof clash.overlapVolumeMm3 === 'number' ? `${Math.round(clash.overlapVolumeMm3).toLocaleString()} mm³` : '—'} />
                <Field label="Penetration / distance" value={typeof clash.distanceMm === 'number' ? `${clash.distanceMm.toFixed(1)} mm` : '—'} />
                <Field label="Centre (x, y, z)" value={`${clash.centreX?.toFixed?.(2)}, ${clash.centreY?.toFixed?.(2)}, ${clash.centreZ?.toFixed?.(2)}`} />
              </dl>
            </div>
            <div className="rounded-lg bg-white p-4 ring-1 ring-slate-200">
              <h2 className="mb-2 text-sm font-semibold">Elements</h2>
              <dl className="space-y-2">
                <Field label="A" value={`${clash.elementAName || clash.elementAType || 'A'} · ${clash.elementAGuid}`} />
                <Field label="B" value={`${clash.elementBName || clash.elementBType || 'B'} · ${clash.elementBGuid}`} />
                {clash.resolutionNote && <Field label="Resolution" value={clash.resolutionNote} />}
              </dl>
            </div>
          </div>

          <div className="mt-4">
            <span className="mb-1 block text-xs font-medium uppercase tracking-wide text-slate-400">Status</span>
            <div className="flex flex-wrap gap-2">
              {STATUSES.map((s) => (
                <button
                  key={s}
                  onClick={() => changeStatus(s)}
                  className={`rounded-full px-3 py-1 text-xs ${
                    clash.status === s ? 'bg-blue-600 text-white' : 'bg-white text-slate-600 ring-1 ring-slate-200'
                  }`}
                >
                  {s}
                </button>
              ))}
            </div>
          </div>

          <div className="mt-5 flex flex-wrap gap-2">
            <Link
              href={`/projects/${projectId}/viewer?guid=${encodeURIComponent(clash.elementAGuid)}&x=${clash.centreX}&y=${clash.centreY}&z=${clash.centreZ}`}
              className="rounded bg-slate-800 px-3 py-2 text-sm font-medium text-white hover:bg-slate-900"
            >
              View in model
            </Link>
            {clash.issueId ? (
              <Link
                href={`/projects/${projectId}/issues/${clash.issueId}`}
                className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
              >
                Open linked issue
              </Link>
            ) : (
              <button onClick={onPromote} className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50">
                Promote to issue
              </button>
            )}
          </div>
        </>
      )}
    </AppShell>
  );
}
