'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { listTransmittals, createTransmittal, transmittalAction } from '@/lib/data';
import type { Transmittal } from '@/lib/types';

export const dynamic = 'force-dynamic';

const statusClass: Record<string, string> = {
  DRAFT: 'bg-slate-100 text-slate-600',
  SENT: 'bg-blue-100 text-blue-700',
  ACKNOWLEDGED: 'bg-amber-100 text-amber-700',
  RESPONDED: 'bg-green-100 text-green-700',
};

// Allowed next action per status (DRAFT→send, SENT→acknowledge, ACKNOWLEDGED→respond).
const nextAction: Record<string, 'send' | 'acknowledge' | 'respond' | undefined> = {
  DRAFT: 'send',
  SENT: 'acknowledge',
  ACKNOWLEDGED: 'respond',
};
const actionLabel: Record<string, string> = { send: 'Send', acknowledge: 'Acknowledge', respond: 'Respond' };

export default function TransmittalsPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const [items, setItems] = useState<Transmittal[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [recipient, setRecipient] = useState('');
  const [notes, setNotes] = useState('');
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    listTransmittals(projectId)
      .then(setItems)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load transmittals'));
  }, [projectId]);

  useEffect(load, [load]);

  async function onCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!recipient.trim()) return;
    setBusy(true);
    setError(null);
    try {
      await createTransmittal(projectId, { recipient: recipient.trim(), notes: notes.trim() || undefined });
      setRecipient('');
      setNotes('');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create transmittal');
    } finally {
      setBusy(false);
    }
  }

  async function onAction(t: Transmittal) {
    const action = nextAction[t.status];
    if (!action) return;
    const body = action === 'respond' ? { responseNotes: prompt('Response notes (optional)') || undefined } : undefined;
    try {
      await transmittalAction(projectId, t.id, action, body);
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Action failed');
    }
  }

  return (
    <AppShell>
      <div className="mb-4">
        <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
          ← Project
        </Link>
        <h1 className="text-xl font-semibold">Transmittals</h1>
      </div>

      <form onSubmit={onCreate} className="mb-4 flex flex-wrap items-end gap-2 rounded-lg border border-slate-200 bg-white p-4">
        <label className="block">
          <span className="text-sm text-slate-600">Recipient</span>
          <input
            value={recipient}
            onChange={(e) => setRecipient(e.target.value)}
            placeholder="Name / organisation"
            className="mt-1 block w-56 rounded border border-slate-300 px-2 py-1.5 text-sm"
          />
        </label>
        <label className="block flex-1">
          <span className="text-sm text-slate-600">Notes (optional)</span>
          <input
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            className="mt-1 block w-full rounded border border-slate-300 px-2 py-1.5 text-sm"
          />
        </label>
        <button
          type="submit"
          disabled={busy || !recipient.trim()}
          className="rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {busy ? 'Creating…' : 'New transmittal'}
        </button>
      </form>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {!items && !error && <p className="text-slate-400">Loading…</p>}
      {items && items.length === 0 && <p className="text-slate-500">No transmittals.</p>}

      {items && items.length > 0 && (
        <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200 bg-white">
          {items.map((t) => (
            <li key={t.id} className="flex items-center justify-between gap-3 px-4 py-3">
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-sm font-medium">{t.transmittalCode}</span>
                  <span className={`rounded px-1.5 py-0.5 text-[10px] ${statusClass[t.status] ?? 'bg-slate-100 text-slate-600'}`}>
                    {t.status}
                  </span>
                </div>
                <div className="mt-0.5 truncate text-xs text-slate-400">
                  To {t.recipient}
                  {t.notes ? ` · ${t.notes}` : ''}
                </div>
              </div>
              {nextAction[t.status] && (
                <button
                  onClick={() => onAction(t)}
                  className="shrink-0 rounded border border-slate-300 px-3 py-1.5 text-sm hover:bg-slate-50"
                >
                  {actionLabel[nextAction[t.status]!]}
                </button>
              )}
            </li>
          ))}
        </ul>
      )}
    </AppShell>
  );
}
