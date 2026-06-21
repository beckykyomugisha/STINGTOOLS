'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';

// The post-login landing is now /projects. Keep /dashboard as a redirect so any
// existing links/bookmarks still resolve.
export default function DashboardRedirect() {
  const router = useRouter();
  useEffect(() => {
    router.replace('/projects');
  }, [router]);
  return <main className="grid min-h-screen place-items-center text-slate-400">Loading…</main>;
}
