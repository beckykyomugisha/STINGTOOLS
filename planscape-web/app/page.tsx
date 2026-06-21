'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/lib/auth';

export default function Home() {
  const { ready, user } = useAuth();
  const router = useRouter();

  useEffect(() => {
    if (!ready) return;
    router.replace(user ? '/projects' : '/login');
  }, [ready, user, router]);

  return <main className="grid min-h-screen place-items-center text-slate-400">Loading…</main>;
}
