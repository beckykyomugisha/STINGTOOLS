'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { listDocuments, documentDownloadUrl, uploadDocument, transitionDocument } from '@/lib/data';
import type { ProjectDocument } from '@/lib/types';

export const dynamic = 'force-dynamic';

const CDE = ['ALL', 'WIP', 'SHARED', 'PUBLISHED', 'ARCHIVE'] as const;

// First valid forward transition per CDE state + the suitability it implies
// (server enforces the full matrix; this is the common-path shortcut).
const NEXT_STATE: Record<string, { to: string; suitability: string; label: string } | undefined> = {
  WIP: { to: 'SHARED', suitability: 'S2', label: 'Share' },
  SHARED: { to: 'PUBLISHED', suitability: 'S4', label: 'Publish' },
  PUBLISHED: { to: 'ARCHIVE', suitability: 'S7', label: 'Archive' },
};

const cdeClass: Record<string, string> = {
  WIP: 'bg-slate-100 text-slate-600',
  SHARED: 'bg-amber-100 text-amber-700',
  PUBLISHED: 'bg-green-100 text-green-700',
  ARCHIVE: 'bg-slate-200 text-slate-500',
  SUPERSEDED: 'bg-red-50 text-red-600',
  WITHDRAWN: 'bg-red-50 text-red-600',
};

function fmtSize(b?: number): string {
  if (!b) return '';
  if (b < 1024) return `${b} B`;
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(0)} KB`;
  return `${(b / 1024 / 1024).toFixed(1)} MB`;
}

export default function DocumentsPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const [docs, setDocs] = useState<ProjectDocument[] | null>(null);
  const [cde, setCde] = useState<(typeof CDE)[number]>('ALL');
  const [search, setSearch] = useState('');
  const [query, setQuery] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [discipline, setDiscipline] = useState('');
  const [busy, setBusy] = useState(false);
  const [hasMore, setHasMore] = useState(false);

  const PAGE_SIZE = 50;
  const filters = () => ({
    cdeStatus: cde === 'ALL' ? undefined : cde,
    search: query || undefined,
  });

  const load = useCallback(() => {
    setDocs(null);
    setError(null);
    listDocuments(projectId, {
      cdeStatus: cde === 'ALL' ? undefined : cde,
      search: query || undefined,
      page: 1,
      pageSize: PAGE_SIZE,
    })
      .then((d) => {
        setDocs(d);
        setHasMore(d.length === PAGE_SIZE);
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load documents'));
  }, [projectId, cde, query]);

  useEffect(load, [load]);

  async function loadMore() {
    if (!docs) return;
    const page = Math.floor(docs.length / PAGE_SIZE) + 1;
    try {
      const more = await listDocuments(projectId, { ...filters(), page, pageSize: PAGE_SIZE });
      setDocs([...docs, ...more]);
      setHasMore(more.length === PAGE_SIZE);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load more');
    }
  }

  async function onUpload(e: React.FormEvent) {
    e.preventDefault();
    if (!file) return;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await uploadDocument(projectId, file, { discipline: discipline.trim() || undefined });
      setNotice('Document uploaded (WIP).');
      setFile(null);
      setDiscipline('');
      if (fileRef.current) fileRef.current.value = '';
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Upload failed');
    } finally {
      setBusy(false);
    }
  }

  async function onTransition(d: ProjectDocument) {
    const n = NEXT_STATE[d.cdeStatus];
    if (!n) return;
    setError(null);
    setNotice(null);
    try {
      await transitionDocument(projectId, d.id, { newState: n.to, suitabilityCode: n.suitability });
      setNotice(`${d.fileName} → ${n.to}.`);
      load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Transition failed');
    }
  }

  return (
    <AppShell>
      <div className="mb-4 flex items-center justify-between gap-3">
        <div>
          <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
            ← Project
          </Link>
          <h1 className="text-xl font-semibold">Documents</h1>
        </div>
      </div>

      <form onSubmit={onUpload} className="mb-3 flex flex-wrap items-end gap-2 rounded-lg border border-slate-200 bg-white p-3">
        <input
          ref={fileRef}
          type="file"
          onChange={(e) => setFile(e.target.files?.[0] ?? null)}
          className="text-sm"
        />
        <input
          value={discipline}
          onChange={(e) => setDiscipline(e.target.value)}
          placeholder="Discipline (e.g. A)"
          className="w-32 rounded border border-slate-300 px-2 py-1 text-sm"
        />
        <button
          type="submit"
          disabled={!file || busy}
          className="rounded bg-blue-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {busy ? 'Uploading…' : 'Upload (WIP)'}
        </button>
      </form>

      <div className="mb-3 flex flex-wrap items-center gap-2">
        {CDE.map((s) => (
          <button
            key={s}
            onClick={() => setCde(s)}
            className={`rounded-full px-3 py-1 text-xs ${
              cde === s ? 'bg-blue-600 text-white' : 'bg-white text-slate-600 ring-1 ring-slate-200'
            }`}
          >
            {s}
          </button>
        ))}
        <form
          onSubmit={(e) => {
            e.preventDefault();
            setQuery(search.trim());
          }}
          className="ml-auto"
        >
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search documents…"
            className="rounded border border-slate-300 px-2 py-1 text-sm"
          />
        </form>
      </div>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {notice && <p className="mb-3 rounded bg-green-50 px-3 py-2 text-sm text-green-700">{notice}</p>}
      {!docs && !error && <p className="text-slate-400">Loading…</p>}
      {docs && docs.length === 0 && <p className="text-slate-500">No documents.</p>}

      {docs && docs.length > 0 && (
        <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200 bg-white">
          {docs.map((d) => (
            <li key={d.id} className="flex items-center justify-between gap-3 px-4 py-3">
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <span className="truncate text-sm font-medium">{d.fileName}</span>
                  <span className={`shrink-0 rounded px-1.5 py-0.5 text-[10px] ${cdeClass[d.cdeStatus] ?? 'bg-slate-100 text-slate-600'}`}>
                    {d.cdeStatus}
                  </span>
                </div>
                <div className="mt-0.5 text-xs text-slate-400">
                  {d.documentType ? `${d.documentType} · ` : ''}
                  {d.suitabilityCode ? `${d.suitabilityCode} · ` : ''}
                  {d.revision ? `${d.revision} · ` : ''}
                  {d.discipline ? `${d.discipline} · ` : ''}
                  {fmtSize(d.fileSizeBytes)}
                  {d.scanStatus && d.scanStatus !== 'CLEAN' && d.scanStatus !== 'SKIPPED' ? ` · ${d.scanStatus}` : ''}
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-2">
                {NEXT_STATE[d.cdeStatus] && (
                  <button
                    onClick={() => onTransition(d)}
                    className="rounded border border-slate-300 px-2 py-1 text-xs hover:bg-slate-50"
                  >
                    {NEXT_STATE[d.cdeStatus]!.label}
                  </button>
                )}
                <a
                  href={documentDownloadUrl(projectId, d.id)}
                  className="text-sm text-blue-600 hover:underline"
                  target="_blank"
                  rel="noreferrer"
                >
                  Download
                </a>
              </div>
            </li>
          ))}
        </ul>
      )}

      {hasMore && (
        <div className="mt-3 text-center">
          <button
            onClick={loadMore}
            className="rounded border border-slate-300 px-4 py-2 text-sm hover:bg-slate-50"
          >
            Load more
          </button>
        </div>
      )}
    </AppShell>
  );
}
