'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth';

export default function DashboardPage() {
  const { ready, user, logout } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (ready && !user) router.replace('/login');
  }, [ready, user, router]);

  if (!ready || !user) {
    return <main className="grid min-h-screen place-items-center text-slate-400">Loading…</main>;
  }

  return (
    <div className="min-h-screen">
      <header className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
        <span className="font-semibold">Planscape</span>
        <div className="flex items-center gap-3 text-sm">
          <span className="text-slate-500">{user.email}</span>
          <button
            onClick={() => {
              logout();
              router.replace('/login');
            }}
            className="rounded border border-slate-300 px-3 py-1 transition hover:bg-slate-50"
          >
            Sign out
          </button>
        </div>
      </header>

      <main className="mx-auto max-w-5xl p-6">
        <h1 className="text-xl font-semibold">Welcome{user.name ? `, ${user.name}` : ''}</h1>
        <p className="mt-2 max-w-prose text-slate-500">
          You&apos;re signed in to the Planscape web app. This is slice&nbsp;1 (foundation + auth).
          Projects &amp; issues, clashes &amp; the 3D viewer, and real-time updates arrive in the
          next slices.
        </p>

        <ul className="mt-6 space-y-2 text-sm text-slate-600">
          <li className="rounded-lg bg-white px-4 py-3 ring-1 ring-slate-200">✅ Sign in / session</li>
          <li className="rounded-lg bg-white px-4 py-3 ring-1 ring-slate-200 opacity-60">▫️ Projects &amp; Issues — next</li>
          <li className="rounded-lg bg-white px-4 py-3 ring-1 ring-slate-200 opacity-60">▫️ Clashes &amp; 3D viewer</li>
          <li className="rounded-lg bg-white px-4 py-3 ring-1 ring-slate-200 opacity-60">▫️ Real-time (SignalR)</li>
        </ul>
      </main>
    </div>
  );
}
