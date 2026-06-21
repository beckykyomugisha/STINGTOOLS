'use client';

import { useState, type FormEvent } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
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
      });
      router.replace(`/projects/${projectId}/issues/${issue.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create issue');
      setBusy(false);
    }
  }

  return (
    <AppShell>
      <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
        ← Back
      </Link>
      <h1 className="mb-4 mt-1 text-xl font-semibold">New issue</h1>

      <form onSubmit={onSubmit} className="max-w-xl space-y-4 rounded-lg bg-white p-5 ring-1 ring-slate-200">
        <label className="block">
          <span className="mb-1 block text-sm font-medium">Title</span>
          <input
            required
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            className="w-full rounded border border-slate-300 px-3 py-2 outline-none focus:border-blue-500"
          />
        </label>

        <label className="block">
          <span className="mb-1 block text-sm font-medium">Description</span>
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={4}
            className="w-full rounded border border-slate-300 px-3 py-2 outline-none focus:border-blue-500"
          />
        </label>

        <div className="grid grid-cols-2 gap-4">
          <label className="block">
            <span className="mb-1 block text-sm font-medium">Type</span>
            <select value={type} onChange={(e) => setType(e.target.value)} className="w-full rounded border border-slate-300 px-3 py-2">
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
              className="w-full rounded border border-slate-300 px-3 py-2"
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
            className="w-full rounded border border-slate-300 px-3 py-2 outline-none focus:border-blue-500"
          />
        </label>

        {error && <p className="rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}

        <button
          type="submit"
          disabled={busy || !title.trim()}
          className="rounded bg-blue-600 px-4 py-2 font-medium text-white hover:bg-blue-700 disabled:opacity-60"
        >
          {busy ? 'Creating…' : 'Create issue'}
        </button>
      </form>
    </AppShell>
  );
}
