'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams, useSearchParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { listModels, modelFileUrl, getSceneManifest, chunkFileUrl } from '@/lib/data';
import { API_BASE, getToken } from '@/lib/api';
import { useAuth } from '@/lib/auth';
import type { ProjectModel, SceneManifest } from '@/lib/types';

// useSearchParams needs the page rendered dynamically (no static prerender).
export const dynamic = 'force-dynamic';

// The existing 3D viewer (Planscape/assets/viewer → API wwwroot). Override with
// NEXT_PUBLIC_VIEWER_URL if it's hosted elsewhere.
const VIEWER_BASE_URL = process.env.NEXT_PUBLIC_VIEWER_URL || `${API_BASE}/viewer.html`;

export default function ViewerPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const search = useSearchParams();
  const guid = search.get('guid') || undefined;
  const wantModel = search.get('model') || undefined;

  const iframeRef = useRef<HTMLIFrameElement>(null);
  const [ready, setReady] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // The 3D viewer + meetings (camera, screen share, markup, chat) all live in
  // a legacy static bundle served by the API (wwwroot/viewer.html), embedded
  // here as a cross-origin iframe. That origin's localStorage starts empty on
  // every load — it has never heard of this browser's planscape-web session —
  // so coordination-viewer.js / meeting-sync.js / livekit-av.js (which each
  // independently read 'planscape_token' etc. from localStorage) always saw
  // nothing and bounced to their own login. Pass the current session through
  // as URL params; viewer.html's own bootstrap script (added alongside this
  // change) writes them into its origin's localStorage before those scripts
  // run, then strips them from the address bar.
  const { user: authUser } = useAuth();
  const [viewerSrc, setViewerSrc] = useState<string | null>(null);
  useEffect(() => {
    const u = new URL(VIEWER_BASE_URL);
    // Without ?project=, coordination-viewer.js's own bootstrap() sees no
    // projectId in ITS url, shows the "Pick a project to get started" CTA,
    // and returns early — skipping its project-name/members/issues load
    // entirely. It doesn't know about the postMessage 'load' this page
    // sends once the iframe is ready, so the CTA never gets dismissed even
    // though the base viewer.html goes on to render the model behind it.
    u.searchParams.set('project', projectId);
    const token = getToken();
    if (token) {
      u.searchParams.set('token', token);
      if (authUser?.tenantId) u.searchParams.set('tenant', authUser.tenantId);
      if (authUser?.email) u.searchParams.set('user', authUser.email);
    }
    setViewerSrc(u.toString());
  }, [authUser, projectId]);

  // Federation (preferred): multi-discipline scene chunks.
  const [scene, setScene] = useState<SceneManifest | null>(null);
  const [hidden, setHidden] = useState<Set<string>>(new Set()); // disciplines toggled off

  // Single-model fallback (no scene chunks published yet).
  const [models, setModels] = useState<ProjectModel[]>([]);
  const [activeId, setActiveId] = useState<string | undefined>(wantModel);

  const [loaded, setLoaded] = useState(false); // resolved which mode we're in

  // Decide the mode: try the federation manifest first; fall back to the
  // single-model list when the project has no scene chunks.
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const manifest = await getSceneManifest(projectId);
        if (cancelled) return;
        if (manifest && manifest.chunks.length > 0) {
          setScene(manifest);
        } else {
          const ms = await listModels(projectId);
          if (cancelled) return;
          setModels(ms);
          setActiveId((cur) => cur ?? ms[0]?.id);
        }
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load the model');
      } finally {
        if (!cancelled) setLoaded(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [projectId]);

  function post(cmd: { type: string; payload?: unknown }) {
    iframeRef.current?.contentWindow?.postMessage(JSON.stringify(cmd), '*');
  }

  const disciplines = useMemo(
    () => (scene ? Array.from(new Set(scene.chunks.map((c) => c.discipline))).sort() : []),
    [scene],
  );

  // FEDERATION: once the iframe is ready, add every chunk as a model, frame the
  // whole scene, then deep-link to the clash element if requested.
  useEffect(() => {
    if (!ready || !scene) return;
    for (const c of scene.chunks) {
      post({ type: 'addModel', payload: { url: chunkFileUrl(c.url), modelId: c.id } });
    }
    const fit = setTimeout(() => post({ type: 'fit' }), 1500);
    const sel = guid ? setTimeout(() => post({ type: 'selectAndZoom', payload: { guid } }), 2500) : undefined;
    return () => {
      clearTimeout(fit);
      if (sel) clearTimeout(sel);
    };
  }, [ready, scene, guid]);

  // SINGLE-MODEL fallback.
  useEffect(() => {
    if (!ready || scene || !activeId) return;
    post({ type: 'load', payload: { url: modelFileUrl(projectId, activeId), modelId: activeId } });
    if (!guid) return;
    const t = setTimeout(() => post({ type: 'selectAndZoom', payload: { guid } }), 2500);
    return () => clearTimeout(t);
  }, [ready, scene, activeId, projectId, guid]);

  function toggleDiscipline(disc: string) {
    if (!scene) return;
    const willHide = !hidden.has(disc);
    const next = new Set(hidden);
    if (willHide) next.add(disc);
    else next.delete(disc);
    setHidden(next);
    for (const c of scene.chunks) {
      if (c.discipline === disc) {
        post({ type: 'setModelVisibleById', payload: { modelId: c.id, visible: !willHide } });
      }
    }
  }

  return (
    <AppShell>
      <div className="mb-3 flex flex-wrap items-center justify-between gap-3">
        <Link href={`/projects/${projectId}`} className="text-sm text-fg-subtle hover:underline">
          ← Project
        </Link>

        {/* Federation discipline toggles */}
        {scene && disciplines.length > 0 && (
          <div className="flex flex-wrap items-center gap-3">
            <span className="text-xs font-medium uppercase tracking-wide text-fg-subtle">Disciplines</span>
            {disciplines.map((d) => (
              <label key={d} className="flex items-center gap-1.5 text-sm text-fg-muted">
                <input
                  type="checkbox"
                  checked={!hidden.has(d)}
                  onChange={() => toggleDiscipline(d)}
                  className="h-4 w-4"
                />
                {d}
              </label>
            ))}
          </div>
        )}

        {/* Single-model selector (fallback) */}
        {!scene && models.length > 0 && (
          <select
            value={activeId}
            onChange={(e) => setActiveId(e.target.value)}
            className="rounded border border-border-strong px-2 py-1 text-sm"
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

      {error && <p className="mb-3 rounded bg-danger-subtle px-3 py-2 text-sm text-danger">{error}</p>}
      {loaded && !scene && models.length === 0 && !error && (
        <p className="text-fg-muted">No models published to this project yet.</p>
      )}

      {scene && (
        <p className="mb-2 text-xs text-fg-subtle">
          Federated model — {scene.chunks.length} chunk{scene.chunks.length === 1 ? '' : 's'} across{' '}
          {disciplines.length} discipline{disciplines.length === 1 ? '' : 's'}.
        </p>
      )}

      <div className="overflow-hidden rounded-lg ring-1 ring-border" style={{ height: '70vh' }}>
        {viewerSrc && (
          <iframe
            ref={iframeRef}
            src={viewerSrc}
            title="3D model"
            className="h-full w-full border-0"
            onLoad={() => setReady(true)}
            allow="fullscreen; camera; microphone; display-capture"
          />
        )}
      </div>

      {guid && <p className="mt-2 text-xs text-fg-subtle">Deep-linked to element {guid}.</p>}
    </AppShell>
  );
}
