'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { createProject } from '@/lib/data';
import { describeFailure } from '@/lib/api';

export const dynamic = 'force-dynamic';

export default function NewProjectPage() {
  const router = useRouter();
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<ReturnType<typeof describeFailure> | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    setBusy(true);
    setFailure(null);
    try {
      const p = await createProject({
        name: name.trim(),
        code: code.trim() || undefined,
        description: description.trim() || undefined,
      });
      router.push(`/projects/${p.id}`);
    } catch (err) {
      // #670 — a 402 is a PLAN limit, not a failure and not a refusal. Rendering
      // `error` showed the owner the literal token `quota_exceeded`, which reads
      // as an expired account. describeFailure surfaces the server's own
      // sentence plus the way out.
      setFailure(
        describeFailure(err, {
          forbidden: 'Only an Owner or Admin can create projects in this firm.',
          fallback: 'Failed to create project',
        }),
      );
      setBusy(false);
    }
  }

  return (
    <AppShell>
      <div className="mb-4">
        <Link href="/projects" className="text-sm text-fg-subtle hover:underline">
          ← Projects
        </Link>
        <h1 className="text-xl font-semibold">New project</h1>
      </div>

      {failure && (
        <p
          className={
            'mb-3 rounded px-3 py-2 text-sm ' +
            (failure.tone === 'quota'
              ? 'bg-warning-subtle text-warning'
              : failure.tone === 'forbidden'
                ? 'bg-warning-subtle text-warning'
                : 'bg-danger-subtle text-danger')
          }
        >
          {failure.tone === 'quota' && <span aria-hidden="true">📈 </span>}
          {failure.tone === 'forbidden' && <span aria-hidden="true">🔒 </span>}
          {failure.message}
          {failure.actionHref && (
            <>
              {' '}
              <a href={failure.actionHref} className="underline">
                Upgrade your plan
              </a>
              .
            </>
          )}
        </p>
      )}

      <form onSubmit={submit} className="max-w-lg space-y-3 rounded-lg border border-border bg-surface p-4">
        <label className="block">
          <span className="text-sm text-fg-muted">Name</span>
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            className="mt-1 w-full rounded border border-border-strong px-2 py-1.5 text-sm"
          />
        </label>
        <label className="block">
          <span className="text-sm text-fg-muted">Code (optional — auto-derived if blank)</span>
          <input
            value={code}
            onChange={(e) => setCode(e.target.value)}
            placeholder="e.g. KLA-OFFICE"
            className="mt-1 w-full rounded border border-border-strong px-2 py-1.5 text-sm"
          />
        </label>
        <label className="block">
          <span className="text-sm text-fg-muted">Description (optional)</span>
          <textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={3}
            className="mt-1 w-full rounded border border-border-strong px-2 py-1.5 text-sm"
          />
        </label>
        <button
          type="submit"
          disabled={busy || !name.trim()}
          className="rounded bg-accent px-3 py-2 text-sm font-medium text-fg-on-accent hover:bg-accent-hover disabled:opacity-50"
        >
          {busy ? 'Creating…' : 'Create project'}
        </button>
      </form>
    </AppShell>
  );
}
