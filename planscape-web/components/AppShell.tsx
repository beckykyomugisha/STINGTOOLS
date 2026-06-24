'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { useAuth } from '@/lib/auth';
import { NotificationBell } from '@/components/NotificationBell';

/** Protected app chrome: gates on auth (redirect to /login), renders the header + content. */
export function AppShell({ children }: { children: ReactNode }) {
  const { ready, user, logout } = useAuth();
  const router = useRouter();
  const [q, setQ] = useState('');

  useEffect(() => {
    if (ready && !user) router.replace('/login');
  }, [ready, user, router]);

  if (!ready || !user) {
    return <main className="grid min-h-screen place-items-center text-slate-400">Loading…</main>;
  }

  return (
    <div className="min-h-screen">
      <header className="flex items-center justify-between border-b border-slate-200 bg-white px-6 py-3">
        <Link href="/projects" className="font-semibold">
          Planscape
        </Link>
        <div className="flex items-center gap-3 text-sm">
          <form
            onSubmit={(e) => {
              e.preventDefault();
              if (q.trim().length >= 2) router.push(`/search?q=${encodeURIComponent(q.trim())}`);
            }}
          >
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Search…"
              aria-label="Search"
              className="w-40 rounded border border-slate-300 px-2 py-1 text-sm focus:w-56"
            />
          </form>
          <NotificationBell />
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
      <main className="mx-auto max-w-5xl p-6">{children}</main>
    </div>
  );
}
