'use client';

import { useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams, useSearchParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { listModels, modelFileUrl } from '@/lib/data';
import { API_BASE } from '@/lib/api';
import type { ProjectModel } from '@/lib/types';

// useSearchParams needs the page rendered dynamically (no static prerender).
export const dynamic = 'force-dynamic';

// The existing 3D viewer (Planscape/assets/viewer → API wwwroot). Override with
// NEXT_PUBLIC_VIEWER_URL if it's hosted elsewhere.
const VIEWER_URL = process.env.NEXT_PUBLIC_VIEWER_URL || `${API_BASE}/viewer.html`;

export default function ViewerPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const search = useSearchParams();
  const guid = search.get('guid') || undefined;
  const wantModel = search.get('model') || undefined;

  const iframeRef = useRef<HTMLIFrameElement>(null);
  const [models, setModels] = useState<ProjectModel[]>([]);
  const [activeId, setActiveId] = useState<string | undefined>(wantModel);
  const [error, setError] = useState<string | null>(null);
  const [ready, setReady] = useState(false);

  useEffect(() => {
    listModels(projectId)
      .then((ms) => {
        setModels(ms);
        setActiveId((cur) => cur ?? ms[0]?.id);
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load models'));
  }, [projectId]);

  function post(cmd: { type: string; payload?: unknown }) {
    iframeRef.current?.contentWindow?.postMessage(JSON.stringify(cmd), '*');
  }

  // Load the active model into the viewer once the iframe is ready, then
  // deep-link to the clash element (node names carry IFC GUIDs).
  useEffect(() => {
    if (!ready || !activeId) return;
    post({ type: 'load', payload: { url: modelFileUrl(projectId, activeId), modelId: activeId } });
    if (!guid) return;
    const t = setTimeout(() => post({ type: 'selectAndZoom', payload: { guid } }), 2500);
    return () => clearTimeout(t);
  }, [ready, activeId, projectId, guid]);

  return (
    <AppShell>
      <div className="mb-3 flex items-center justify-between gap-3">
        <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
          ← Project
        </Link>
        {models.length > 0 && (
          <select
            value={activeId}
            onChange={(e) => setActiveId(e.target.value)}
            className="rounded border border-slate-300 px-2 py-1 text-sm"
          >
            {models.map((m) => (
              <option key={m.id} value={m.id}>
                {m.name}
                {m.discipline ? ` (${m.discipline})` : ''}
              </option>
            ))}
          </select>
        )}
      </div>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {models.length === 0 && !error && (
        <p className="text-slate-500">No models published to this project yet.</p>
      )}

      <div className="overflow-hidden rounded-lg ring-1 ring-slate-200" style={{ height: '70vh' }}>
        <iframe
          ref={iframeRef}
          src={VIEWER_URL}
          title="3D model"
          className="h-full w-full border-0"
          onLoad={() => setReady(true)}
          allow="fullscreen"
        />
      </div>

      {guid && <p className="mt-2 text-xs text-slate-400">Deep-linked to element {guid}.</p>}
    </AppShell>
  );
}
