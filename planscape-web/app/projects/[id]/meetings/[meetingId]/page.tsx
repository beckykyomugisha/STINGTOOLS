'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import {
  getMeetingDetail,
  listAttendees,
  addAgendaItem,
  addAction,
  updateAction,
} from '@/lib/data';
import type { Meeting, MeetingAgendaItem, MeetingActionItem, MeetingAttendee } from '@/lib/types';

export const dynamic = 'force-dynamic';

const ACTION_NEXT: Record<string, string> = {
  OPEN: 'IN_PROGRESS',
  IN_PROGRESS: 'COMPLETE',
  COMPLETE: 'OPEN',
};

export default function MeetingDetailPage() {
  const params = useParams<{ id: string; meetingId: string }>();
  const projectId = params.id;
  const meetingId = params.meetingId;

  const [meeting, setMeeting] = useState<Meeting | null>(null);
  const [agenda, setAgenda] = useState<MeetingAgendaItem[]>([]);
  const [actions, setActions] = useState<MeetingActionItem[]>([]);
  const [attendees, setAttendees] = useState<MeetingAttendee[]>([]);
  const [error, setError] = useState<string | null>(null);

  const [agendaTitle, setAgendaTitle] = useState('');
  const [actionDesc, setActionDesc] = useState('');

  const load = useCallback(() => {
    getMeetingDetail(projectId, meetingId)
      .then((d) => {
        setMeeting(d.meeting);
        setAgenda(d.agenda);
        setActions(d.actions);
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load meeting'));
    listAttendees(projectId, meetingId)
      .then(setAttendees)
      .catch(() => {/* attendees are non-critical */});
  }, [projectId, meetingId]);

  useEffect(load, [load]);

  async function onAddAgenda(e: React.FormEvent) {
    e.preventDefault();
    if (!agendaTitle.trim()) return;
    try {
      await addAgendaItem(projectId, meetingId, { title: agendaTitle.trim() });
      setAgendaTitle('');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to add agenda item');
    }
  }

  async function onAddAction(e: React.FormEvent) {
    e.preventDefault();
    if (!actionDesc.trim()) return;
    try {
      await addAction(projectId, meetingId, { description: actionDesc.trim() });
      setActionDesc('');
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to add action');
    }
  }

  async function cycleStatus(a: MeetingActionItem) {
    const next = ACTION_NEXT[a.status ?? 'OPEN'] ?? 'OPEN';
    // optimistic
    setActions((cur) => cur.map((x) => (x.id === a.id ? { ...x, status: next } : x)));
    try {
      await updateAction(projectId, meetingId, a.id, { status: next });
    } catch {
      load();
    }
  }

  if (error && !meeting) {
    return (
      <AppShell>
        <Link href={`/projects/${projectId}/meetings`} className="text-sm text-slate-400 hover:underline">
          ← Meetings
        </Link>
        <p className="mt-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>
      </AppShell>
    );
  }

  return (
    <AppShell>
      <div className="mb-4 flex items-start justify-between gap-3">
        <div>
          <Link href={`/projects/${projectId}/meetings`} className="text-sm text-slate-400 hover:underline">
            ← Meetings
          </Link>
          <h1 className="text-xl font-semibold">{meeting?.title ?? 'Meeting'}</h1>
          {meeting && (
            <p className="text-xs text-slate-400">
              {new Date(meeting.scheduledAt).toLocaleString()}
              {meeting.meetingType ? ` · ${meeting.meetingType}` : ''}
              {meeting.location ? ` · ${meeting.location}` : ''}
              {` · ${meeting.status.replace('_', ' ')}`}
            </p>
          )}
        </div>
        <div className="flex shrink-0 items-center gap-2">
          {meeting?.meetingUrl && (
            <a
              href={meeting.meetingUrl}
              target="_blank"
              rel="noreferrer"
              className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
            >
              External link
            </a>
          )}
          <Link
            href={`/projects/${projectId}/meetings/${meetingId}/live`}
            className="rounded bg-green-600 px-3 py-2 text-sm font-medium text-white hover:bg-green-700"
          >
            {meeting?.liveSessionId ? 'Join live' : 'Start live'}
          </Link>
        </div>
      </div>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <div className="grid gap-6 md:grid-cols-2">
        {/* Agenda */}
        <section>
          <h2 className="mb-2 text-sm font-medium">Agenda</h2>
          <ul className="mb-2 space-y-1">
            {agenda.length === 0 && <li className="text-sm text-slate-400">No agenda items.</li>}
            {agenda.map((it) => (
              <li key={it.id} className="rounded border border-slate-200 bg-white px-3 py-2 text-sm">
                <div className="flex items-center justify-between">
                  <span>{it.title}</span>
                  <span className="text-xs text-slate-400">{it.status}</span>
                </div>
                {it.presenter && <p className="text-xs text-slate-400">Presenter: {it.presenter}</p>}
                {it.outcome && <p className="mt-1 text-xs text-slate-500">Outcome: {it.outcome}</p>}
              </li>
            ))}
          </ul>
          <form onSubmit={onAddAgenda} className="flex gap-2">
            <input
              value={agendaTitle}
              onChange={(e) => setAgendaTitle(e.target.value)}
              placeholder="Add agenda item"
              className="flex-1 rounded border border-slate-300 px-2 py-1 text-sm"
            />
            <button className="rounded bg-slate-700 px-3 py-1 text-sm text-white hover:bg-slate-800">Add</button>
          </form>
        </section>

        {/* Actions */}
        <section>
          <h2 className="mb-2 text-sm font-medium">Action items</h2>
          <ul className="mb-2 space-y-1">
            {actions.length === 0 && <li className="text-sm text-slate-400">No actions.</li>}
            {actions.map((a) => (
              <li key={a.id} className="rounded border border-slate-200 bg-white px-3 py-2 text-sm">
                <div className="flex items-center justify-between gap-2">
                  <span className={a.status === 'COMPLETE' || a.status === 'CLOSED' ? 'text-slate-400 line-through' : ''}>
                    {a.description}
                  </span>
                  <button
                    onClick={() => cycleStatus(a)}
                    className="shrink-0 rounded bg-slate-100 px-2 py-0.5 text-xs text-slate-600 hover:bg-slate-200"
                  >
                    {a.status ?? 'OPEN'}
                  </button>
                </div>
                <div className="mt-0.5 text-xs text-slate-400">
                  {a.assignee ? `${a.assignee}` : 'Unassigned'}
                  {a.dueDate ? ` · due ${new Date(a.dueDate).toLocaleDateString()}` : ''}
                  {a.isOverdue ? ' · overdue' : ''}
                </div>
              </li>
            ))}
          </ul>
          <form onSubmit={onAddAction} className="flex gap-2">
            <input
              value={actionDesc}
              onChange={(e) => setActionDesc(e.target.value)}
              placeholder="Add action item"
              className="flex-1 rounded border border-slate-300 px-2 py-1 text-sm"
            />
            <button className="rounded bg-slate-700 px-3 py-1 text-sm text-white hover:bg-slate-800">Add</button>
          </form>
        </section>
      </div>

      {/* Attendees */}
      <section className="mt-6">
        <h2 className="mb-2 text-sm font-medium">Attendees</h2>
        {attendees.length === 0 ? (
          <p className="text-sm text-slate-400">No attendees recorded.</p>
        ) : (
          <ul className="flex flex-wrap gap-2">
            {attendees.map((at) => (
              <li key={at.id} className="rounded-full bg-slate-100 px-3 py-1 text-xs text-slate-600">
                {at.name}
                {at.role ? ` · ${at.role}` : ''}
                {at.attendanceStatus ? ` · ${at.attendanceStatus}` : ''}
              </li>
            ))}
          </ul>
        )}
      </section>

      {/* Minutes */}
      {meeting?.minutes && (
        <section className="mt-6">
          <h2 className="mb-2 text-sm font-medium">Minutes</h2>
          <p className="whitespace-pre-wrap rounded border border-slate-200 bg-white p-3 text-sm text-slate-700">
            {meeting.minutes}
          </p>
        </section>
      )}
    </AppShell>
  );
}
