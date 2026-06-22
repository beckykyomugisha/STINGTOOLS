'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { listMeetings } from '@/lib/data';
import type { Meeting } from '@/lib/types';

export const dynamic = 'force-dynamic';

function when(m: Meeting): string {
  try {
    return new Date(m.scheduledAt).toLocaleString([], {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return m.scheduledAt;
  }
}

const statusClass: Record<string, string> = {
  SCHEDULED: 'bg-blue-100 text-blue-700',
  IN_PROGRESS: 'bg-green-100 text-green-700',
  COMPLETED: 'bg-slate-100 text-slate-600',
  CANCELLED: 'bg-red-100 text-red-700',
};

export default function MeetingsPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const [meetings, setMeetings] = useState<Meeting[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    listMeetings(projectId)
      .then(setMeetings)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load meetings'));
  }, [projectId]);

  const now = Date.now();
  const sorted = (meetings ?? []).slice().sort((a, b) => +new Date(a.scheduledAt) - +new Date(b.scheduledAt));
  const upcoming = sorted.filter((m) => m.status !== 'COMPLETED' && +new Date(m.scheduledAt) >= now - 36e5);
  const past = sorted.filter((m) => !upcoming.includes(m)).reverse();

  function row(m: Meeting) {
    return (
      <li key={m.id}>
        <Link
          href={`/projects/${projectId}/meetings/${m.id}`}
          className="block rounded-lg bg-white p-3 ring-1 ring-slate-200 transition hover:ring-blue-300"
        >
          <div className="flex items-center justify-between gap-3">
            <span className="font-medium">{m.title}</span>
            <div className="flex shrink-0 items-center gap-2">
              {m.liveSessionId && (
                <span className="inline-flex items-center gap-1 text-xs font-medium text-green-600">
                  <span className="h-1.5 w-1.5 rounded-full bg-green-500" /> Live
                </span>
              )}
              <span className={`rounded px-2 py-0.5 text-xs ${statusClass[m.status] ?? 'bg-slate-100 text-slate-600'}`}>
                {m.status.replace('_', ' ')}
              </span>
            </div>
          </div>
          <div className="mt-1 text-xs text-slate-400">
            {when(m)}
            {m.meetingType ? ` · ${m.meetingType}` : ''}
            {m.location ? ` · ${m.location}` : ''}
            {typeof m.actionItemCount === 'number' ? ` · ${m.actionItemCount} action(s)` : ''}
          </div>
        </Link>
      </li>
    );
  }

  return (
    <AppShell>
      <div className="mb-4 flex items-center justify-between gap-3">
        <div>
          <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
            ← Project
          </Link>
          <h1 className="text-xl font-semibold">Meetings</h1>
        </div>
        <Link
          href={`/projects/${projectId}/meetings/new`}
          className="rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700"
        >
          Schedule
        </Link>
      </div>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {!meetings && !error && <p className="text-slate-400">Loading…</p>}
      {meetings && meetings.length === 0 && <p className="text-slate-500">No meetings yet.</p>}

      {upcoming.length > 0 && (
        <>
          <h2 className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">Upcoming</h2>
          <ul className="mb-6 space-y-2">{upcoming.map(row)}</ul>
        </>
      )}
      {past.length > 0 && (
        <>
          <h2 className="mb-2 text-xs font-medium uppercase tracking-wide text-slate-400">Past</h2>
          <ul className="space-y-2">{past.map(row)}</ul>
        </>
      )}
    </AppShell>
  );
}
