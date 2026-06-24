'use client';

import { Suspense, useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useSearchParams, useRouter } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { search as runSearch } from '@/lib/data';
import type { SearchResult } from '@/lib/types';

export const dynamic = 'force-dynamic';

const typeBadge: Record<string, string> = {
  issue: 'bg-blue-100 text-blue-700',
  document: 'bg-amber-100 text-amber-700',
  meeting: 'bg-green-100 text-green-700',
  tag: 'bg-slate-100 text-slate-600',
};

function hrefFor(r: SearchResult): string {
  switch (r.type) {
    case 'issue':
      return `/projects/${r.projectId}/issues/${r.id}`;
    case 'meeting':
      return `/projects/${r.projectId}/meetings/${r.id}`;
    case 'document':
      return `/projects/${r.projectId}/documents`;
    default:
      return `/projects/${r.projectId}`;
  }
}

export default function SearchPage() {
  return (
    <Suspense fallback={<AppShell><p className="text-slate-400">Loading…</p></AppShell>}>
      <SearchInner />
    </Suspense>
  );
}

function SearchInner() {
  const params = useSearchParams();
  const router = useRouter();
  const q = params.get('q') || '';

  const [input, setInput] = useState(q);
  const [results, setResults] = useState<SearchResult[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const doSearch = useCallback((term: string) => {
    if (term.trim().length < 2) {
      setResults(null);
      return;
    }
    setLoading(true);
    setError(null);
    runSearch(term.trim())
      .then((r) => setResults(r.results))
      .catch((e) => setError(e instanceof Error ? e.message : 'Search failed'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    setInput(q);
    doSearch(q);
  }, [q, doSearch]);

  return (
    <AppShell>
      <h1 className="mb-3 text-xl font-semibold">Search</h1>

      <form
        onSubmit={(e) => {
          e.preventDefault();
          router.push(`/search?q=${encodeURIComponent(input.trim())}`);
        }}
        className="mb-4"
      >
        <input
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder="Search issues, documents, meetings, tags…"
          autoFocus
          className="w-full max-w-xl rounded border border-slate-300 px-3 py-2 text-sm"
        />
      </form>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {loading && <p className="text-slate-400">Searching…</p>}
      {!loading && q.trim().length >= 2 && results && results.length === 0 && (
        <p className="text-slate-500">No matches for “{q}”.</p>
      )}
      {q.trim().length > 0 && q.trim().length < 2 && (
        <p className="text-slate-400">Type at least 2 characters.</p>
      )}

      {results && results.length > 0 && (
        <ul className="space-y-2">
          {results.map((r) => (
            <li key={`${r.type}-${r.id}`}>
              <Link
                href={hrefFor(r)}
                className="flex items-center justify-between gap-3 rounded-lg bg-white p-3 ring-1 ring-slate-200 transition hover:ring-blue-300"
              >
                <div className="min-w-0">
                  <div className="truncate text-sm font-medium">{r.label}</div>
                  <div className="truncate text-xs text-slate-400">
                    {r.detail}
                    {r.projectName ? ` · ${r.projectName}` : ''}
                  </div>
                </div>
                <span className={`shrink-0 rounded px-2 py-0.5 text-[10px] uppercase ${typeBadge[r.type] ?? 'bg-slate-100 text-slate-600'}`}>
                  {r.type}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </AppShell>
  );
}
