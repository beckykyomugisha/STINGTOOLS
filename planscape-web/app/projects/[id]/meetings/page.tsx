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
  SCHEDULED: 'bg-accent-subtle text-accent',
  IN_PROGRESS: 'bg-success-subtle text-success',
  COMPLETED: 'bg-surface-3 text-fg-muted',
  CANCELLED: 'bg-danger-subtle text-danger',
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
          className="block rounded-lg bg-surface p-3 ring-1 ring-border transition hover:ring-accent"
        >
          <div className="flex items-center justify-between gap-3">
            <span className="font-medium">{m.title}</span>
            <div className="flex shrink-0 items-center gap-2">
              {m.liveSessionId && (
                <span className="inline-flex items-center gap-1 text-xs font-medium text-success">
                  <span className="h-1.5 w-1.5 rounded-full bg-success" /> Live
                </span>
              )}
              <span className={`rounded px-2 py-0.5 text-xs ${statusClass[m.status] ?? 'bg-surface-3 text-fg-muted'}`}>
                {m.status.replace('_', ' ')}
              </span>
            </div>
          </div>
          <div className="mt-1 text-xs text-fg-subtle">
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
          <Link href={`/projects/${projectId}`} className="text-sm text-fg-subtle hover:underline">
            ← Project
          </Link>
          <h1 className="text-xl font-semibold">Meetings</h1>
        </div>
        <Link
          href={`/projects/${projectId}/meetings/new`}
          className="rounded bg-accent px-3 py-2 text-sm font-medium text-fg-on-accent hover:bg-accent-hover"
        >
          Schedule
        </Link>
      </div>

      {error && <p className="mb-3 rounded bg-danger-subtle px-3 py-2 text-sm text-danger">{error}</p>}
      {!meetings && !error && <p className="text-fg-subtle">Loading…</p>}
      {meetings && meetings.length === 0 && <p className="text-fg-muted">No meetings yet.</p>}

      {upcoming.length > 0 && (
        <>
          <h2 className="mb-2 text-xs font-medium uppercase tracking-wide text-fg-subtle">Upcoming</h2>
          <ul className="mb-6 space-y-2">{upcoming.map(row)}</ul>
        </>
      )}
      {past.length > 0 && (
        <>
          <h2 className="mb-2 text-xs font-medium uppercase tracking-wide text-fg-subtle">Past</h2>
          <ul className="space-y-2">{past.map(row)}</ul>
        </>
      )}
    </AppShell>
  );
}
