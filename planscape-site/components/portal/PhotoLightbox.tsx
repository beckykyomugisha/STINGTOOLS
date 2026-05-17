'use client';

import { useEffect } from 'react';
import { X } from 'lucide-react';
import type { PortalPhoto } from './types';

interface Props {
  photo: PortalPhoto;
  fileUrl: string;
  onClose: () => void;
}

export default function PhotoLightbox({ photo, fileUrl, onClose }: Props) {
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') onClose();
    }
    window.addEventListener('keydown', onKey);
    document.body.style.overflow = 'hidden';
    return () => {
      window.removeEventListener('keydown', onKey);
      document.body.style.overflow = '';
    };
  }, [onClose]);

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Photo viewer"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/85 p-4"
      onClick={onClose}
    >
      <button
        type="button"
        aria-label="Close viewer"
        onClick={onClose}
        className="absolute right-4 top-4 rounded-full bg-white/10 p-2 text-white transition hover:bg-white/20"
      >
        <X size={24} />
      </button>

      <div
        className="flex max-h-full max-w-6xl flex-col items-center gap-3"
        onClick={(e) => e.stopPropagation()}
      >
        <img
          src={fileUrl}
          alt={photo.caption ?? photo.reason ?? 'Site photo'}
          className="max-h-[75vh] w-auto rounded-md object-contain shadow-2xl"
        />
        <div className="w-full rounded-md bg-white/95 px-4 py-3 text-sm text-slate-800">
          {photo.caption && (
            <p className="font-medium">{photo.caption}</p>
          )}
          <p className="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-600">
            <span>
              <strong>Captured:</strong>{' '}
              {new Date(photo.capturedAt).toLocaleString()}
            </span>
            {photo.levelCode && (
              <span>
                <strong>Level:</strong> {photo.levelCode}
              </span>
            )}
            {photo.zoneCode && (
              <span>
                <strong>Zone:</strong> {photo.zoneCode}
              </span>
            )}
            {photo.reason && (
              <span>
                <strong>Reason:</strong> {photo.reason}
              </span>
            )}
          </p>
        </div>
      </div>
    </div>
  );
}
