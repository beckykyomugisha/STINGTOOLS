'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { listModels, uploadModel } from '@/lib/data';
import type { ProjectModel } from '@/lib/types';

export const dynamic = 'force-dynamic';

const ACCEPT = '.glb,.gltf';

export default function ModelsPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;

  const [models, setModels] = useState<ProjectModel[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const fileRef = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [name, setName] = useState('');
  const [discipline, setDiscipline] = useState('');

  function refresh() {
    listModels(projectId)
      .then(setModels)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load models'));
  }

  useEffect(refresh, [projectId]);

  async function onUpload(e: React.FormEvent) {
    e.preventDefault();
    if (!file) return;
    setBusy(true);
    setError(null);
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
      setError(err instanceof Error ? err.message : 'Upload failed');
    } finally {
      setBusy(false);
    }
  }

  return (
    <AppShell>
      <div className="mb-4 flex items-center justify-between gap-3">
        <div>
          <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
            ← Project
          </Link>
          <h1 className="text-xl font-semibold">Models</h1>
        </div>
        <Link
          href={`/projects/${projectId}/viewer`}
          className="rounded border border-slate-300 px-3 py-2 text-sm hover:bg-slate-50"
        >
          3D model
        </Link>
      </div>

      {/* Upload */}
      <form onSubmit={onUpload} className="mb-6 rounded-lg border border-slate-200 bg-white p-4">
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
            className="rounded border border-slate-300 px-2 py-1 text-sm"
          />
          <input
            value={discipline}
            onChange={(e) => setDiscipline(e.target.value)}
            placeholder="Discipline (e.g. M)"
            className="w-32 rounded border border-slate-300 px-2 py-1 text-sm"
          />
          <button
            type="submit"
            disabled={!file || busy}
            className="rounded bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {busy ? 'Uploading…' : 'Upload'}
          </button>
        </div>
        <p className="mt-2 text-xs text-slate-400">
          GLB/glTF render directly. Export from Revit (BIM tab → Publish Model), or convert IFC to GLB first.
        </p>
      </form>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {notice && <p className="mb-3 rounded bg-green-50 px-3 py-2 text-sm text-green-700">{notice}</p>}

      {/* List */}
      {models.length === 0 ? (
        <p className="text-slate-500">No models published yet.</p>
      ) : (
        <ul className="divide-y divide-slate-100 rounded-lg border border-slate-200 bg-white">
          {models.map((m) => (
            <li key={m.id} className="flex items-center justify-between px-4 py-3">
              <div>
                <span className="text-sm font-medium">{m.name}</span>
                <span className="ml-2 text-xs text-slate-400">
                  {m.discipline ? `${m.discipline} · ` : ''}
                  {m.format ?? ''}
                  {m.revision ? ` · ${m.revision}` : ''}
                </span>
              </div>
              <Link
                href={`/projects/${projectId}/viewer?model=${m.id}`}
                className="text-sm text-blue-600 hover:underline"
              >
                View
              </Link>
            </li>
          ))}
        </ul>
      )}
    </AppShell>
  );
}
