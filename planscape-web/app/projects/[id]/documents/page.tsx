'use client';

import { describeFailure } from '@/lib/api';
import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import {
  Badge,
  Button,
  DataGrid,
  Input,
  Modal,
  PageHeader,
  Select,
  useToast,
  type Column,
} from '@/components/ui';
import { documentDownloadUrl, listDocuments, transitionDocument, uploadDocument } from '@/lib/data';
import type { ProjectDocument } from '@/lib/types';

export const dynamic = 'force-dynamic';

const CDE = ['ALL', 'WIP', 'SHARED', 'PUBLISHED', 'ARCHIVE'] as const;
const PAGE_SIZE = 50;

/**
 * U4 — Documents.
 *
 * Like transmittals, and per the grid contract, this is NOT an editable grid:
 * `PUT …/documents/{id}/state` is a CDE state transition the server validates
 * against the full ISO 19650 matrix. Offering a free status dropdown would
 * advertise transitions that get rejected, so the row exposes only the single
 * legal forward move.
 */
const NEXT_STATE: Record<string, { to: string; suitability: string; label: string } | undefined> = {
  WIP: { to: 'SHARED', suitability: 'S2', label: 'Share' },
  SHARED: { to: 'PUBLISHED', suitability: 'S4', label: 'Publish' },
  PUBLISHED: { to: 'ARCHIVE', suitability: 'S7', label: 'Archive' },
};

function cdeTone(s: string): 'neutral' | 'warning' | 'success' | 'danger' {
  if (s === 'PUBLISHED') return 'success';
  if (s === 'SHARED') return 'warning';
  if (s === 'SUPERSEDED' || s === 'WITHDRAWN' || s === 'OBSOLETE') return 'danger';
  return 'neutral';
}

function fmtSize(b?: number): string {
  if (!b) return '';
  if (b < 1024) return `${b} B`;
  if (b < 1024 * 1024) return `${(b / 1024).toFixed(0)} KB`;
  return `${(b / 1024 / 1024).toFixed(1)} MB`;
}

export default function DocumentsPage() {
  const { id: projectId } = useParams<{ id: string }>();
  const { toast } = useToast();
  const [docs, setDocs] = useState<ProjectDocument[] | null>(null);
  const [cde, setCde] = useState<(typeof CDE)[number]>('ALL');
  const [query, setQuery] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [hasMore, setHasMore] = useState(false);
  const [uploadOpen, setUploadOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [discipline, setDiscipline] = useState('');
  const [busy, setBusy] = useState(false);

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
      const more = await listDocuments(projectId, {
        cdeStatus: cde === 'ALL' ? undefined : cde,
        search: query || undefined,
        page,
        pageSize: PAGE_SIZE,
      });
      setDocs([...docs, ...more]);
      setHasMore(more.length === PAGE_SIZE);
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Failed to load more', 'error');
    }
  }

  async function onUpload() {
    if (!file) return;
    setBusy(true);
    try {
      await uploadDocument(projectId, file, { discipline: discipline.trim() || undefined });
      toast(`${file.name} uploaded (WIP)`, 'success');
      setFile(null);
      setDiscipline('');
      setUploadOpen(false);
      load();
    } catch (e) {
      toast(e instanceof Error ? e.message : 'Upload failed', 'error');
    } finally {
      setBusy(false);
    }
  }

  async function onTransition(d: ProjectDocument) {
    const n = NEXT_STATE[d.cdeStatus];
    if (!n) return;
    try {
      await transitionDocument(projectId, d.id, { newState: n.to, suitabilityCode: n.suitability });
      toast(`${d.fileName} → ${n.to}`, 'success');
      load();
    } catch (e) {
      // This gate DOES send a reason ("Insufficient role for WIP->SHARED…"),
      // so describeFailure shows the server's sentence rather than a client
      // copy of the rule. The fallback only fires if it ever stops.
      const d = describeFailure(e, {
        forbidden: 'You do not have the role required for this CDE transition.',
        fallback: 'Transition failed',
      });
      toast(d.message, d.tone);
    }
  }

  const columns: Column<ProjectDocument>[] = [
    { key: 'fileName', header: 'File', className: 'min-w-[18rem]' },
    {
      key: 'cdeStatus',
      header: 'CDE state',
      className: 'w-32',
      render: (d) => <Badge tone={cdeTone(d.cdeStatus)}>{d.cdeStatus}</Badge>,
    },
    { key: 'suitabilityCode', header: 'Suitability', className: 'w-28' },
    { key: 'revision', header: 'Rev', className: 'w-20' },
    { key: 'discipline', header: 'Discipline', className: 'w-28' },
    {
      key: 'fileSizeBytes',
      header: 'Size',
      className: 'w-24 text-right',
      value: (d) => d.fileSizeBytes ?? 0,
      render: (d) => fmtSize(d.fileSizeBytes) || <span className="text-fg-subtle">—</span>,
    },
    {
      key: 'uploadedAt',
      header: 'Uploaded',
      className: 'w-28',
      render: (d) =>
        d.uploadedAt ? new Date(d.uploadedAt).toLocaleDateString() : <span className="text-fg-subtle">—</span>,
    },
    {
      key: 'actions',
      header: '',
      className: 'w-44',
      sortable: false,
      render: (d) => {
        const n = NEXT_STATE[d.cdeStatus];
        return (
          <span className="flex gap-1">
            {n && (
              <Button size="sm" onClick={() => void onTransition(d)}>
                {n.label}
              </Button>
            )}
            <Button size="sm" variant="ghost" asChild>
              <a href={documentDownloadUrl(projectId, d.id)} target="_blank" rel="noreferrer">
                Download
              </a>
            </Button>
          </span>
        );
      },
    },
  ];

  return (
    <AppShell>
      <PageHeader
        title="Documents"
        description="CDE state advances through the ISO 19650 transitions, not by editing."
        actions={
          <Button variant="primary" onClick={() => setUploadOpen(true)}>
            Upload
          </Button>
        }
      />
      <DataGrid<ProjectDocument>
        rows={docs}
        columns={columns}
        rowId={(d) => d.id}
        loading={!docs && !error}
        error={error}
        // The list is server-filtered + paged, so the grid's own text filter
        // would only search the page in hand and quietly look broken.
        filterable={false}
        emptyTitle="No documents"
        emptyDescription="Upload a drawing, model or report to start the CDE workflow."
        toolbar={() => (
          <>
            <Select
              value={cde}
              onChange={(e) => setCde(e.target.value as (typeof CDE)[number])}
              aria-label="Filter by CDE state"
              className="h-7 w-36"
            >
              {CDE.map((c) => (
                <option key={c} value={c}>
                  {c === 'ALL' ? 'All states' : c}
                </option>
              ))}
            </Select>
            <form
              onSubmit={(e) => {
                e.preventDefault();
                const v = new FormData(e.currentTarget).get('q');
                setQuery(String(v || ''));
              }}
            >
              <Input name="q" placeholder="Search documents…" aria-label="Search documents" className="h-7 w-52" />
            </form>
          </>
        )}
      />
      {hasMore && (
        <div className="mt-3 flex justify-center">
          <Button onClick={() => void loadMore()}>Load more</Button>
        </div>
      )}

      <Modal
        open={uploadOpen}
        onOpenChange={setUploadOpen}
        title="Upload document"
        description="Uploads land in WIP; share and publish from the row actions."
        footer={
          <>
            <Button onClick={() => setUploadOpen(false)}>Cancel</Button>
            <Button variant="primary" onClick={() => void onUpload()} disabled={busy || !file}>
              {busy ? 'Uploading…' : 'Upload'}
            </Button>
          </>
        }
      >
        <div className="flex flex-col gap-3">
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-fg-muted">File</span>
            <input
              type="file"
              onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              className="text-sm text-fg file:mr-2 file:rounded file:border-0 file:bg-surface-3 file:px-2 file:py-1 file:text-sm file:text-fg"
            />
          </label>
          <label className="flex flex-col gap-1 text-sm">
            <span className="text-fg-muted">Discipline (optional)</span>
            <Input value={discipline} onChange={(e) => setDiscipline(e.target.value)} placeholder="e.g. Structural" />
          </label>
        </div>
      </Modal>
    </AppShell>
  );
}
