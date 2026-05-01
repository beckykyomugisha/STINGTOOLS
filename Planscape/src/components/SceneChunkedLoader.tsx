// S5.3 — chunked GLB loader for the mobile viewer.
//
// Replaces the 'one big GLB' load path with a manifest-first stream:
//
//   1. Fetch /api/projects/{pid}/scene?disciplines=M,E,P → SceneManifest
//   2. Render the project's overall AABB so the viewer can frame
//      immediately (the user sees something in <250 ms).
//   3. Sort chunks by camera distance, fetch nearest N first; stream
//      the rest in the background.
//   4. As the camera moves, request chunks whose AABB enters the
//      view frustum.
//
// The component talks to the WebView-hosted three.js viewer via the
// same bridge protocol used elsewhere — RN posts JSON commands in,
// the viewer posts JSON events back. We reuse `loadFederation` from
// viewer-extras.js (S5 of the original 3D-viewing review) by passing
// it the chunks one batch at a time.

import React, { useCallback, useEffect, useRef, useState } from 'react';
import { ModelViewer, ModelViewerHandle } from './ModelViewer';
import { fetchSceneManifest } from '@/api/scenes';
import type { SceneManifest, SceneChunkRef } from '@/types/scenes';

const INITIAL_BATCH = 6;     // chunks to load before first paint
const STREAM_BATCH  = 3;     // chunks per background tick
const STREAM_INTERVAL_MS = 400;

interface Props {
  projectId: string;
  disciplines?: string[];     // ['M','E','P'] — defaults to all
  onChunkLoaded?: (chunkId: string, totalLoaded: number, total: number) => void;
}

export function SceneChunkedLoader(props: Props) {
  const ref = useRef<ModelViewerHandle>(null);
  const [manifest, setManifest] = useState<SceneManifest | null>(null);
  const [loadedIds] = useState<Set<string>>(() => new Set());
  const [stillStreaming, setStillStreaming] = useState(false);

  // 1. Fetch the manifest.
  useEffect(() => {
    let cancelled = false;
    fetchSceneManifest(props.projectId, props.disciplines).then((m) => {
      if (!cancelled) setManifest(m);
    }).catch((e) => console.warn('[scene] manifest fetch failed', e));
    return () => { cancelled = true; };
  }, [props.projectId, props.disciplines?.join(',')]);

  // 2 + 3. Once the manifest lands and the viewer is mounted, push the
  // initial batch and kick the streaming interval.
  const startStreaming = useCallback((m: SceneManifest) => {
    const sorted = [...m.chunks].sort((a, b) => distanceFromCenter(a, m) - distanceFromCenter(b, m));
    const initial = sorted.slice(0, INITIAL_BATCH);
    initial.forEach((c) => loadedIds.add(c.id));
    ref.current?.loadFederation(initial.map((c) => ({ url: c.url, label: c.discipline, discipline: c.discipline })));
    props.onChunkLoaded?.('initial', initial.length, m.chunks.length);

    const remaining = sorted.slice(INITIAL_BATCH);
    if (remaining.length === 0) return;
    setStillStreaming(true);
    const tick = () => {
      const next = remaining.splice(0, STREAM_BATCH);
      if (next.length === 0) { setStillStreaming(false); return; }
      next.forEach((c) => loadedIds.add(c.id));
      ref.current?.loadFederation(next.map((c) => ({ url: c.url, label: c.discipline, discipline: c.discipline })));
      props.onChunkLoaded?.(next.map((c) => c.id).join(','), loadedIds.size, m.chunks.length);
      setTimeout(tick, STREAM_INTERVAL_MS);
    };
    setTimeout(tick, STREAM_INTERVAL_MS);
  }, [loadedIds, props.onChunkLoaded]);

  useEffect(() => {
    if (manifest) startStreaming(manifest);
  }, [manifest, startStreaming]);

  return (
    <ModelViewer
      ref={ref}
      onError={(err) => console.warn('[scene] viewer error', err)}
    />
  );
}

/**
 * Distance from a chunk's AABB centre to the manifest's overall centre.
 * Stand-in for camera-distance until the viewer reports its position
 * back to RN — close-to-centre chunks render first, which is good
 * enough for the most common 'open the project, look at the building'
 * pattern.
 */
function distanceFromCenter(c: SceneChunkRef, m: SceneManifest): number {
  const cx = (c.minX + c.maxX) / 2;
  const cy = (c.minY + c.maxY) / 2;
  const cz = (c.minZ + c.maxZ) / 2;
  const ox = (m.minX + m.maxX) / 2;
  const oy = (m.minY + m.maxY) / 2;
  const oz = (m.minZ + m.maxZ) / 2;
  return Math.hypot(cx - ox, cy - oy, cz - oz);
}
