'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { createProject } from '@/lib/data';

export const dynamic = 'force-dynamic';

export default function NewProjectPage() {
  const router = useRouter();
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [description, setDescription] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    setBusy(true);
    setError(null);
    try {
      const p = await createProject({
        name: name.trim(),
        code: code.trim() || undefined,
        description: description.trim() || undefined,
      });
      router.push(`/projects/${p.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create project');
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

      {error && <p className="mb-3 rounded bg-danger-subtle px-3 py-2 text-sm text-danger">{error}</p>}

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
