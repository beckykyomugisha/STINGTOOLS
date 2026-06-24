'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { createMeeting } from '@/lib/data';

export const dynamic = 'force-dynamic';

const TYPES = ['BIM Coordination', 'Design Review', 'Progress', 'Client', 'Site', 'Other'];

export default function NewMeetingPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const router = useRouter();

  const [title, setTitle] = useState('');
  const [meetingType, setMeetingType] = useState(TYPES[0]);
  const [scheduledAt, setScheduledAt] = useState('');
  const [durationMinutes, setDurationMinutes] = useState('60');
  const [location, setLocation] = useState('');
  const [meetingUrl, setMeetingUrl] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!title.trim() || !scheduledAt) return;
    setBusy(true);
    setError(null);
    try {
      const m = await createMeeting(projectId, {
        title: title.trim(),
        meetingType,
        // datetime-local has no zone — treat as local and send ISO.
        scheduledAt: new Date(scheduledAt).toISOString(),
        durationMinutes: durationMinutes ? Number(durationMinutes) : undefined,
        location: location.trim() || undefined,
        meetingUrl: meetingUrl.trim() || undefined,
      });
      router.push(`/projects/${projectId}/meetings/${m.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create meeting');
      setBusy(false);
    }
  }

  return (
    <AppShell>
      <div className="mb-4">
        <Link href={`/projects/${projectId}/meetings`} className="text-sm text-slate-400 hover:underline">
          ← Meetings
        </Link>
        <h1 className="text-xl font-semibold">Schedule a meeting</h1>
      </div>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

      <form onSubmit={submit} className="max-w-lg space-y-3 rounded-lg border border-slate-200 bg-white p-4">
        <label className="block">
          <span className="text-sm text-slate-600">Title</span>
          <input
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            required
            className="mt-1 w-full rounded border border-slate-300 px-2 py-1.5 text-sm"
          />
        </label>
        <div className="flex gap-3">
          <label className="block flex-1">
            <span className="text-sm text-slate-600">Type</span>
            <select
              value={meetingType}
              onChange={(e) => setMeetingType(e.target.value)}
              className="mt-1 w-full rounded border border-slate-300 px-2 py-1.5 text-sm"
            >
              {TYPES.map((t) => (
                <option key={t}>{t}</option>
              ))}
            </select>
          </label>
          <label className="block w-32">
            <span className="text-sm text-slate-600">Duration (min)</span>
            <input
              type="number"
              min={15}
              step={15}
              value={durationMinutes}
              onChange={(e) => setDurationMinutes(e.target.value)}
              className="mt-1 w-full rounded border border-slate-300 px-2 py-1.5 text-sm"
            />
          </label>
        </div>
        <label className="block">
          <span className="text-sm text-slate-600">When</span>
          <input
            type="datetime-local"
            value={scheduledAt}
            onChange={(e) => setScheduledAt(e.target.value)}
            required
            className="mt-1 w-full rounded border border-slate-300 px-2 py-1.5 text-sm"
          />
        </label>
        <label className="block">
          <span className="text-sm text-slate-600">Location (optional)</span>
          <input
            value={location}
            onChange={(e) => setLocation(e.target.value)}
            className="mt-1 w-full rounded border border-slate-300 px-2 py-1.5 text-sm"
          />
        </label>
        <label className="block">
          <span className="text-sm text-slate-600">External link (Teams/Zoom, optional)</span>
          <input
            value={meetingUrl}
            onChange={(e) => setMeetingUrl(e.target.value)}
            className="mt-1 w-full rounded border border-slate-300 px-2 py-1.5 text-sm"
          />
        </label>
        <button
          type="submit"
          disabled={busy || !title.trim() || !scheduledAt}
          className="rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {busy ? 'Scheduling…' : 'Schedule meeting'}
        </button>
      </form>
    </AppShell>
  );
}
