'use client';

import { useMemo, useState } from 'react';
import type { PortalPhoto } from './types';
import PhotoLightbox from './PhotoLightbox';

interface Props {
  photos: PortalPhoto[];
  buildFileUrl: (photoId: string) => string;
}

function dayKey(iso: string): string {
  // yyyy-mm-dd in user's local timezone
  const d = new Date(iso);
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function dayLabel(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, {
    weekday: 'long',
    year: 'numeric',
    month: 'long',
    day: 'numeric',
  });
}

export default function PhotoTimeline({ photos, buildFileUrl }: Props) {
  const [active, setActive] = useState<PortalPhoto | null>(null);

  const grouped = useMemo(() => {
    // newest first
    const sorted = [...photos].sort(
      (a, b) =>
        new Date(b.capturedAt).getTime() - new Date(a.capturedAt).getTime(),
    );
    const map = new Map<string, PortalPhoto[]>();
    for (const p of sorted) {
      const k = dayKey(p.capturedAt);
      const arr = map.get(k);
      if (arr) arr.push(p);
      else map.set(k, [p]);
    }
    return Array.from(map.entries());
  }, [photos]);

  if (photos.length === 0) {
    return (
      <div className="rounded-xl border border-dashed border-slate-300 bg-white p-12 text-center">
        <p className="text-base font-medium text-slate-700">
          No photos in this date range
        </p>
        <p className="mt-1 text-sm text-slate-500">
          Try clearing filters or selecting a wider date window.
        </p>
      </div>
    );
  }

  return (
    <>
      <div className="space-y-8">
        {grouped.map(([key, dayPhotos]) => (
          <section key={key} aria-labelledby={`timeline-${key}`}>
            <h3
              id={`timeline-${key}`}
              className="sticky top-0 z-10 -mx-1 mb-3 bg-slate-50/95 px-1 py-2 text-sm font-semibold text-navy backdrop-blur"
            >
              {dayLabel(dayPhotos[0].capturedAt)}
              <span className="ml-2 text-xs font-normal text-slate-500">
                ({dayPhotos.length}{' '}
                {dayPhotos.length === 1 ? 'photo' : 'photos'})
              </span>
            </h3>
            <ul role="list" className="space-y-3">
              {dayPhotos.map((p) => (
                <li
                  key={p.id}
                  className="flex gap-4 rounded-lg border border-slate-200 bg-white p-3 shadow-sm"
                >
                  <button
                    type="button"
                    onClick={() => setActive(p)}
                    className="block h-24 w-32 flex-shrink-0 overflow-hidden rounded-md bg-slate-100"
                    aria-label={`View photo${
                      p.caption ? `: ${p.caption}` : ''
                    }`}
                  >
                    <img
                      src={buildFileUrl(p.id)}
                      alt={p.caption ?? p.reason ?? 'Site photo'}
                      loading="lazy"
                      className="h-full w-full object-cover"
                    />
                  </button>
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-slate-800">
                      {p.caption || p.reason || 'Site photo'}
                    </p>
                    <p className="mt-1 flex flex-wrap gap-x-3 text-xs text-slate-500">
                      <span>
                        {new Date(p.capturedAt).toLocaleTimeString([], {
                          hour: '2-digit',
                          minute: '2-digit',
                        })}
                      </span>
                      {p.levelCode && <span>Level {p.levelCode}</span>}
                      {p.zoneCode && <span>Zone {p.zoneCode}</span>}
                    </p>
                  </div>
                </li>
              ))}
            </ul>
          </section>
        ))}
      </div>

      {active && (
        <PhotoLightbox
          photo={active}
          fileUrl={buildFileUrl(active.id)}
          onClose={() => setActive(null)}
        />
      )}
    </>
  );
}
