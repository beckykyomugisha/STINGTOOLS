'use client';

import { useMemo, useRef, useState, useEffect } from 'react';
import type { PortalPhoto } from './types';

interface Props {
  photos: PortalPhoto[];
  buildFileUrl: (photoId: string) => string;
}

interface PairGroup {
  key: string;
  before: PortalPhoto;
  after: PortalPhoto;
}

export default function PhotoBeforeAfter({ photos, buildFileUrl }: Props) {
  const pairs = useMemo<PairGroup[]>(() => {
    const buckets = new Map<string, PortalPhoto[]>();
    for (const p of photos) {
      if (!p.pairKey) continue;
      const arr = buckets.get(p.pairKey);
      if (arr) arr.push(p);
      else buckets.set(p.pairKey, [p]);
    }
    const out: PairGroup[] = [];
    for (const [key, group] of buckets) {
      if (group.length < 2) continue;
      const sorted = [...group].sort(
        (a, b) =>
          new Date(a.capturedAt).getTime() -
          new Date(b.capturedAt).getTime(),
      );
      out.push({
        key,
        before: sorted[0],
        after: sorted[sorted.length - 1],
      });
    }
    // Newest "after" first
    out.sort(
      (a, b) =>
        new Date(b.after.capturedAt).getTime() -
        new Date(a.after.capturedAt).getTime(),
    );
    return out;
  }, [photos]);

  if (pairs.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-slate-300 bg-white p-12 text-center">
        <p className="text-base font-medium text-slate-700">
          No before / after pairs available
        </p>
        <p className="mt-1 text-sm text-slate-500">
          Before / after sliders appear when two or more photos share the
          same location and tag.
        </p>
      </div>
    );
  }

  return (
    <ul role="list" className="space-y-8">
      {pairs.map((pair) => (
        <li
          key={pair.key}
          className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm"
        >
          <BeforeAfterSlider
            beforeUrl={buildFileUrl(pair.before.id)}
            afterUrl={buildFileUrl(pair.after.id)}
            beforeLabel={`Before · ${new Date(
              pair.before.capturedAt,
            ).toLocaleDateString()}`}
            afterLabel={`After · ${new Date(
              pair.after.capturedAt,
            ).toLocaleDateString()}`}
          />
          <div className="border-t border-slate-200 px-4 py-3">
            <p className="text-sm font-medium text-slate-800">
              {pair.after.caption ||
                pair.before.caption ||
                pair.after.reason ||
                'Progress comparison'}
            </p>
            <p className="mt-1 flex flex-wrap gap-x-3 text-xs text-slate-500">
              {pair.after.levelCode && (
                <span>Level {pair.after.levelCode}</span>
              )}
              {pair.after.zoneCode && <span>Zone {pair.after.zoneCode}</span>}
              <span>Pair: {pair.key}</span>
            </p>
          </div>
        </li>
      ))}
    </ul>
  );
}

interface SliderProps {
  beforeUrl: string;
  afterUrl: string;
  beforeLabel: string;
  afterLabel: string;
}

function BeforeAfterSlider({
  beforeUrl,
  afterUrl,
  beforeLabel,
  afterLabel,
}: SliderProps) {
  const containerRef = useRef<HTMLDivElement | null>(null);
  const [pct, setPct] = useState(50);
  const draggingRef = useRef(false);

  function setFromClientX(clientX: number) {
    const el = containerRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    const raw = ((clientX - rect.left) / rect.width) * 100;
    setPct(Math.max(0, Math.min(100, raw)));
  }

  useEffect(() => {
    function onMove(e: MouseEvent | TouchEvent) {
      if (!draggingRef.current) return;
      const clientX =
        'touches' in e ? e.touches[0].clientX : (e as MouseEvent).clientX;
      setFromClientX(clientX);
    }
    function onUp() {
      draggingRef.current = false;
    }
    window.addEventListener('mousemove', onMove);
    window.addEventListener('touchmove', onMove, { passive: true });
    window.addEventListener('mouseup', onUp);
    window.addEventListener('touchend', onUp);
    return () => {
      window.removeEventListener('mousemove', onMove);
      window.removeEventListener('touchmove', onMove);
      window.removeEventListener('mouseup', onUp);
      window.removeEventListener('touchend', onUp);
    };
  }, []);

  function onKey(e: React.KeyboardEvent<HTMLDivElement>) {
    if (e.key === 'ArrowLeft') setPct((p) => Math.max(0, p - 2));
    else if (e.key === 'ArrowRight') setPct((p) => Math.min(100, p + 2));
  }

  return (
    <div
      ref={containerRef}
      className="relative aspect-video w-full select-none overflow-hidden bg-slate-100"
      onMouseDown={(e) => {
        draggingRef.current = true;
        setFromClientX(e.clientX);
      }}
      onTouchStart={(e) => {
        draggingRef.current = true;
        setFromClientX(e.touches[0].clientX);
      }}
    >
      {/* AFTER (full width, base layer) */}
      <img
        src={afterUrl}
        alt={afterLabel}
        loading="lazy"
        className="absolute inset-0 h-full w-full object-cover"
        draggable={false}
      />

      {/* BEFORE clipped from the right */}
      <div
        className="absolute inset-y-0 left-0 overflow-hidden"
        style={{ width: `${pct}%` }}
      >
        <img
          src={beforeUrl}
          alt={beforeLabel}
          loading="lazy"
          className="h-full w-full object-cover"
          style={{
            // Keep the BEFORE image at container size so the visible slice
            // aligns with the AFTER image rather than scaling.
            width: containerRef.current
              ? `${containerRef.current.clientWidth}px`
              : '100%',
            maxWidth: 'none',
          }}
          draggable={false}
        />
      </div>

      {/* Labels */}
      <span className="pointer-events-none absolute left-3 top-3 rounded bg-black/55 px-2 py-1 text-xs font-medium text-white">
        {beforeLabel}
      </span>
      <span className="pointer-events-none absolute right-3 top-3 rounded bg-black/55 px-2 py-1 text-xs font-medium text-white">
        {afterLabel}
      </span>

      {/* Divider + handle */}
      <div
        role="slider"
        aria-label="Before / after compare"
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={Math.round(pct)}
        tabIndex={0}
        onKeyDown={onKey}
        className="absolute inset-y-0 -ml-px w-0.5 cursor-ew-resize bg-white shadow-[0_0_0_1px_rgba(0,0,0,0.15)] focus:outline-none"
        style={{ left: `${pct}%` }}
      >
        <div className="absolute left-1/2 top-1/2 flex h-9 w-9 -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full bg-white text-orange shadow-md ring-1 ring-slate-300">
          <svg
            width="18"
            height="18"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="2.4"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
          >
            <polyline points="15 18 9 12 15 6" />
            <polyline points="9 18 15 12 9 6" transform="translate(6,0)" />
          </svg>
        </div>
      </div>
    </div>
  );
}
