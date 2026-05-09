'use client';

import { useState } from 'react';
import type { PortalPhoto } from './types';
import PhotoLightbox from './PhotoLightbox';

interface Props {
  photos: PortalPhoto[];
  buildFileUrl: (photoId: string) => string;
}

export default function PhotoGalleryGrid({ photos, buildFileUrl }: Props) {
  const [active, setActive] = useState<PortalPhoto | null>(null);

  if (photos.length === 0) {
    return <EmptyState />;
  }

  return (
    <>
      <ul
        role="list"
        className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
      >
        {photos.map((p) => (
          <li
            key={p.id}
            className="group overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm transition hover:shadow-md"
          >
            <button
              type="button"
              onClick={() => setActive(p)}
              className="block w-full text-left"
              aria-label={`View photo${p.caption ? `: ${p.caption}` : ''}`}
            >
              <div className="relative aspect-[4/3] w-full overflow-hidden bg-slate-100">
                <img
                  src={buildFileUrl(p.id)}
                  alt={p.caption ?? p.reason ?? 'Site photo'}
                  loading="lazy"
                  className="h-full w-full object-cover transition group-hover:scale-[1.02]"
                />
                {(p.levelCode || p.zoneCode) && (
                  <div className="absolute left-2 top-2 flex gap-1">
                    {p.levelCode && (
                      <span className="rounded bg-navy/85 px-2 py-0.5 text-[11px] font-medium text-white">
                        {p.levelCode}
                      </span>
                    )}
                    {p.zoneCode && (
                      <span className="rounded bg-orange/90 px-2 py-0.5 text-[11px] font-medium text-white">
                        {p.zoneCode}
                      </span>
                    )}
                  </div>
                )}
              </div>
              <div className="p-3">
                <p className="line-clamp-2 text-sm font-medium text-slate-800">
                  {p.caption || p.reason || 'Site photo'}
                </p>
                <p className="mt-1 text-xs text-slate-500">
                  {new Date(p.capturedAt).toLocaleDateString()}
                </p>
              </div>
            </button>
          </li>
        ))}
      </ul>

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

function EmptyState() {
  return (
    <div className="rounded-xl border border-dashed border-slate-300 bg-white p-12 text-center">
      <p className="text-base font-medium text-slate-700">
        No photos published yet
      </p>
      <p className="mt-1 text-sm text-slate-500">
        Once your project team publishes site photos to the client portal,
        they will appear here.
      </p>
    </div>
  );
}
