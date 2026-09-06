'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { ErrorNote, ForbiddenNote } from '@/components/ui';
import { ApiError, describeFailure } from '@/lib/api';
import { deleteModel, listModels, restoreModel, uploadModel } from '@/lib/data';
import type { ProjectModel } from '@/lib/types';

export const dynamic = 'force-dynamic';

const ACCEPT = '.glb,.gltf';

/** Matches ModelPurgeJob.PurgeGrace. If that changes, this copy is a lie. */
const PURGE_GRACE_DAYS = 30;

/**
 * Days left before ModelPurgeJob removes the bytes for good.
 *
 * Returns null rather than guessing when the server sent no timestamp — an
 * invented countdown on a delete is worse than none, because it is the number a
 * user decides against.
 */
function daysLeft(deletedAt?: string): number | null {
  if (!deletedAt) return null;
  const t = Date.parse(deletedAt);
  if (Number.isNaN(t)) return null;
  const elapsedDays = (Date.now() - t) / 86_400_000;
  return Math.max(0, Math.ceil(PURGE_GRACE_DAYS - elapsedDays));
}

export default function ModelsPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;

  const [models, setModels] = useState<ProjectModel[]>([]);
  const [deletedModels, setDeletedModels] = useState<ProjectModel[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  // Which row is asking "are you sure". Inline rather than window.confirm: a native
  // modal cannot explain that this is reversible, and that is the whole point here.
  const [confirmId, setConfirmId] = useState<string | null>(null);
  const [rowBusyId, setRowBusyId] = useState<string | null>(null);

  const fileRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [name, setName] = useState('');
  const [discipline, setDiscipline] = useState('');

  const refresh = useCallback(() => {
    listModels(projectId)
      .then(setModels)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load models'));
    // Deleted models are fetched separately and failures are swallowed on purpose:
    // the restore list is an extra, and losing it must not blank the page that shows
    // the live models.
    listModels(projectId, { deleted: true })
      .then(setDeletedModels)
      .catch(() => setDeletedModels([]));
  }, [projectId]);

  useEffect(refresh, [refresh]);

  async function onUpload(e: React.FormEvent) {
    e.preventDefault();
    if (!file) return;
    setBusy(true);
    setError(null);
    setForbidden(false);
    setNotice(null);
    try {
      const r = await uploadModel(projectId, file, {
        name: name.trim() || undefined,
        discipline: discipline.trim() || undefined,
      });
      setNotice(
        r.converting
          ? 'Uploaded — a renderable GLB is being generated and will appear shortly.'
          : r.duplicate
            ? 'That model was already published (identical bytes).'
            : 'Model uploaded.',
      );
      setFile(null);
      setName('');
      setDiscipline('');
      if (fileRef.current) fileRef.current.value = '';
      refresh();
    } catch (err) {
      // POST models is [Authorize(Roles = "Admin,Owner,Coordinator")] and
      // Forbid() sends an EMPTY body, so this used to render the literal
      // "Request failed (HTTP 403)". Name the roles instead.
      const d = describeFailure(err, {
        forbidden: 'Only an Admin, Owner or Coordinator can upload a model to this project.',
        fallback: 'Upload failed',
      });
      setError(d.message);
      setForbidden(d.tone === 'forbidden');
    } finally {
      setBusy(false);
    }
  }

  async function onDelete(m: ProjectModel) {
    setRowBusyId(m.id);
    setError(null);
    setForbidden(false);
    setNotice(null);
    try {
      await deleteModel(projectId, m.id);
      setNotice(
        `"${m.name}" removed. It can be restored below for ${PURGE_GRACE_DAYS} days, ` +
          'after which the file is deleted permanently.',
      );
      setConfirmId(null);
      refresh();
    } catch (err) {
      // Same 403 shape as upload — an empty body would otherwise surface as a bare
      // status code, which tells the user nothing they can act on.
      const d = describeFailure(err, {
        forbidden: 'Only an Admin, Owner or Coordinator can remove a model from this project.',
        fallback: 'Could not remove the model',
      });
      setError(d.message);
      setForbidden(d.tone === 'forbidden');
    } finally {
      setRowBusyId(null);
    }
  }

  async function onRestore(m: ProjectModel) {
    setRowBusyId(m.id);
    setError(null);
    setForbidden(false);
    setNotice(null);
    try {
      await restoreModel(projectId, m.id);
      setNotice(`"${m.name}" restored.`);
      refresh();
    } catch (err) {
      // 404 means ModelPurgeJob already ran: the row and the bytes are gone. That is
      // not something a retry fixes, and describeFailure has no notFound case — it
      // takes { forbidden, fallback } only — so it is handled here rather than passed
      // as an option that would be silently ignored.
      if (err instanceof ApiError && err.status === 404) {
        setError(
          `"${m.name}" is past its ${PURGE_GRACE_DAYS}-day window and has been permanently ` +
            'deleted. Publish it again to bring it back.',
        );
        setForbidden(false);
      } else {
        const d = describeFailure(err, {
          forbidden: 'Only an Admin, Owner or Coordinator can restore a model.',
          fallback: 'Could not restore the model',
        });
        setError(d.message);
        setForbidden(d.tone === 'forbidden');
      }
      // Re-read either way: a 404 means our list is stale, and showing a Restore button
      // for something that no longer exists invites the same failure again.
      refresh();
    } finally {
      setRowBusyId(null);
    }
  }

  return (
    <AppShell>
      <div className="mb-4 flex items-center justify-between gap-3">
        <div>
          <Link href={`/projects/${projectId}`} className="text-sm text-fg-subtle hover:underline">
            ← Project
          </Link>
          <h1 className="text-xl font-semibold">Models</h1>
        </div>
        <Link
          href={`/projects/${projectId}/viewer`}
          className="rounded border border-border-strong px-3 py-2 text-sm hover:bg-surface-2"
        >
          3D model
        </Link>
      </div>

      {/* Upload */}
      <form onSubmit={onUpload} className="mb-6 rounded-lg border border-border bg-surface p-4">
        <h2 className="mb-3 text-sm font-medium">Publish a model</h2>
        <div className="flex flex-wrap items-end gap-3">
          <input
            ref={fileRef}
            type="file"
            accept={ACCEPT}
            onChange={(e) => setFile(e.target.files?.[0] ?? null)}
            className="text-sm"
          />
          <input
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Name (optional)"
            className="rounded border border-border-strong px-2 py-1 text-sm"
          />
          <input
            value={discipline}
            onChange={(e) => setDiscipline(e.target.value)}
            placeholder="Discipline (e.g. M)"
            className="w-32 rounded border border-border-strong px-2 py-1 text-sm"
          />
          <button
            type="submit"
            disabled={!file || busy}
            className="rounded bg-accent px-3 py-2 text-sm font-medium text-fg-on-accent hover:bg-accent-hover disabled:opacity-50"
          >
            {busy ? 'Uploading…' : 'Upload'}
          </button>
        </div>
        <p className="mt-2 text-xs text-fg-subtle">
          GLB/glTF render directly. Export from Revit (BIM tab → Publish Model), or convert IFC to GLB first.
        </p>
      </form>

      {error && (
        <div className="mb-3">
          {forbidden ? <ForbiddenNote>{error}</ForbiddenNote> : <ErrorNote>{error}</ErrorNote>}
        </div>
      )}
      {notice && <p className="mb-3 rounded bg-success-subtle px-3 py-2 text-sm text-success">{notice}</p>}

      {/* List */}
      {models.length === 0 ? (
        <p className="text-fg-muted">No models published yet.</p>
      ) : (
        <ul className="divide-y divide-border rounded-lg border border-border bg-surface">
          {models.map((m) => (
            <li key={m.id} className="flex items-center justify-between gap-3 px-4 py-3">
              <div className="min-w-0">
                <span className="text-sm font-medium">{m.name}</span>
                <span className="ml-2 text-xs text-fg-subtle">
                  {m.discipline ? `${m.discipline} · ` : ''}
                  {m.format ?? ''}
                  {m.revision ? ` · ${m.revision}` : ''}
                </span>
              </div>

              {confirmId === m.id ? (
                <div className="flex shrink-0 items-center gap-2">
                  <span className="text-xs text-fg-subtle">
                    Remove from this project? Restorable for {PURGE_GRACE_DAYS} days.
                  </span>
                  <button
                    onClick={() => onDelete(m)}
                    disabled={rowBusyId === m.id}
                    className="rounded bg-danger-subtle px-2 py-1 text-xs font-medium text-danger hover:opacity-80 disabled:opacity-50"
                  >
                    {rowBusyId === m.id ? 'Removing…' : 'Remove'}
                  </button>
                  <button
                    onClick={() => setConfirmId(null)}
                    className="rounded border border-border-strong px-2 py-1 text-xs hover:bg-surface-2"
                  >
                    Cancel
                  </button>
                </div>
              ) : (
                <div className="flex shrink-0 items-center gap-3">
                  <Link
                    href={`/projects/${projectId}/viewer?model=${m.id}`}
                    className="text-sm text-accent hover:underline"
                  >
                    View
                  </Link>
                  <button
                    onClick={() => setConfirmId(m.id)}
                    className="text-sm text-fg-subtle hover:text-danger hover:underline"
                  >
                    Remove
                  </button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {/* Recently deleted — the 30-day window made reachable. Hidden when empty so it
          costs nothing on the common path. */}
      {deletedModels.length > 0 && (
        <section className="mt-8">
          <h2 className="mb-2 text-sm font-medium">Recently removed</h2>
          <p className="mb-2 text-xs text-fg-subtle">
            Removed models are kept for {PURGE_GRACE_DAYS} days and can be restored. After that the
            file is deleted permanently.
          </p>
          <ul className="divide-y divide-border rounded-lg border border-border bg-surface">
            {deletedModels.map((m) => {
              const left = daysLeft(m.deletedAt);
              return (
                <li key={m.id} className="flex items-center justify-between gap-3 px-4 py-3">
                  <div className="min-w-0">
                    <span className="text-sm text-fg-muted">{m.name}</span>
                    <span className="ml-2 text-xs text-fg-subtle">
                      {m.discipline ? `${m.discipline} · ` : ''}
                      {left === null
                        ? 'removed'
                        : left === 0
                          ? 'deletes today'
                          : `${left} day${left === 1 ? '' : 's'} left`}
                    </span>
                  </div>
                  <button
                    onClick={() => onRestore(m)}
                    disabled={rowBusyId === m.id}
                    className="shrink-0 rounded border border-border-strong px-2 py-1 text-xs hover:bg-surface-2 disabled:opacity-50"
                  >
                    {rowBusyId === m.id ? 'Restoring…' : 'Restore'}
                  </button>
                </li>
              );
            })}
          </ul>
        </section>
      )}
    </AppShell>
  );
}
