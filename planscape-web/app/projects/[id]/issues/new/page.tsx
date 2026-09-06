'use client';

import { useState, type FormEvent } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { MemberPicker } from '@/components/MemberPicker';
import { createIssue } from '@/lib/data';
import type { IssuePriority } from '@/lib/types';

const PRIORITIES: IssuePriority[] = ['CRITICAL', 'HIGH', 'MEDIUM', 'LOW'];
const TYPES = ['CLASH', 'RFI', 'DEFECT', 'QUERY', 'OTHER'];

export default function NewIssuePage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const router = useRouter();

  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [type, setType] = useState('CLASH');
  const [priority, setPriority] = useState<IssuePriority>('MEDIUM');
  const [discipline, setDiscipline] = useState('');
  const [assigneeUserId, setAssigneeUserId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setBusy(true);
    try {
      const issue = await createIssue(projectId, {
        title: title.trim(),
        description: description.trim(),
        type,
        priority,
        discipline: discipline.trim(),
        // Optional at creation — an issue raised before anyone owns it is a
        // normal state, so this stays unset rather than defaulting to self.
        ...(assigneeUserId ? { assigneeUserId } : {}),
      });
      router.replace(`/projects/${projectId}/issues/${issue.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create issue');
      setBusy(false);
    }
  }

  return (
    <AppShell>
      <Link href={`/projects/${projectId}`} className="text-sm text-fg-subtle hover:underline">
        ← Back
      </Link>
      <h1 className="mb-4 mt-1 text-xl font-semibold">New issue</h1>

      <form onSubmit={onSubmit} className="max-w-xl space-y-4 rounded-lg bg-surface p-5 ring-1 ring-border">
        <label className="block">
          <span className="mb-1 block text-sm font-medium">Title</span>
          <input
            required
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            className="w-full rounded border border-border-strong px-3 py-2 outline-none focus:border-accent"
          />
        </label>

        <label className="block">
          <span className="mb-1 block text-sm font-medium">Description</span>
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={4}
            className="w-full rounded border border-border-strong px-3 py-2 outline-none focus:border-accent"
          />
        </label>

        <div className="grid grid-cols-2 gap-4">
          <label className="block">
            <span className="mb-1 block text-sm font-medium">Type</span>
            <select value={type} onChange={(e) => setType(e.target.value)} className="w-full rounded border border-border-strong px-3 py-2">
              {TYPES.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          </label>

          <label className="block">
            <span className="mb-1 block text-sm font-medium">Priority</span>
            <select
              value={priority}
              onChange={(e) => setPriority(e.target.value as IssuePriority)}
              className="w-full rounded border border-border-strong px-3 py-2"
            >
              {PRIORITIES.map((p) => (
                <option key={p} value={p}>{p}</option>
              ))}
            </select>
          </label>
        </div>

        <label className="block">
          <span className="mb-1 block text-sm font-medium">Discipline</span>
          <input
            value={discipline}
            onChange={(e) => setDiscipline(e.target.value)}
            placeholder="e.g. M, E, P, S, A"
            className="w-full rounded border border-border-strong px-3 py-2 outline-none focus:border-accent"
          />
        </label>

        <div className="block">
          <span className="mb-1 block text-sm font-medium">Assignee (optional)</span>
          <MemberPicker
            projectId={projectId}
            value={assigneeUserId}
            onChange={(v) => setAssigneeUserId(v as string | null)}
          />
        </div>

        {error && <p className="rounded bg-danger-subtle px-3 py-2 text-sm text-danger">{error}</p>}

        <button
          type="submit"
          disabled={busy || !title.trim()}
          className="rounded bg-accent px-4 py-2 font-medium text-fg-on-accent hover:bg-accent-hover disabled:opacity-60"
        >
          {busy ? 'Creating…' : 'Create issue'}
        </button>
      </form>
    </AppShell>
  );
}
