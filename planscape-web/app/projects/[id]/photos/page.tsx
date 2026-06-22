'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { listSitePhotos, photoFileUrl, approvePhoto, rejectPhoto } from '@/lib/data';
import type { SitePhoto } from '@/lib/types';

export const dynamic = 'force-dynamic';

const REASONS = ['ALL', 'Progress', 'Issue', 'Defect', 'Safety', 'AsBuilt', 'Reference'] as const;

export default function PhotosPage() {
  const params = useParams<{ id: string }>();
  const projectId = params.id;
  const [photos, setPhotos] = useState<SitePhoto[] | null>(null);
  const [reason, setReason] = useState<(typeof REASONS)[number]>('ALL');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(() => {
    setPhotos(null);
    listSitePhotos(projectId, { reason: reason === 'ALL' ? undefined : reason })
      .then(setPhotos)
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load photos'));
  }, [projectId, reason]);

  useEffect(load, [load]);

  async function onApprove(p: SitePhoto) {
    const caption = prompt('Caption (required, min 3 chars):', p.caption ?? '');
    if (caption == null) return;
    setError(null);
    setNotice(null);
    try {
      await approvePhoto(projectId, p.id, caption);
      setNotice('Photo approved.');
      load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Approve failed');
    }
  }

  async function onReject(p: SitePhoto) {
    const reasonText = prompt('Rejection reason:');
    if (!reasonText) return;
    setError(null);
    setNotice(null);
    try {
      await rejectPhoto(projectId, p.id, reasonText);
      setNotice('Photo rejected.');
      load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Reject failed');
    }
  }

  return (
    <AppShell>
      <div className="mb-4">
        <Link href={`/projects/${projectId}`} className="text-sm text-slate-400 hover:underline">
          ← Project
        </Link>
        <h1 className="text-xl font-semibold">Site photos</h1>
      </div>

      <div className="mb-3 flex flex-wrap gap-2">
        {REASONS.map((r) => (
          <button
            key={r}
            onClick={() => setReason(r)}
            className={`rounded-full px-3 py-1 text-xs ${
              reason === r ? 'bg-blue-600 text-white' : 'bg-white text-slate-600 ring-1 ring-slate-200'
            }`}
          >
            {r}
          </button>
        ))}
      </div>

      {error && <p className="mb-3 rounded bg-red-50 px-3 py-2 text-sm text-red-700">{error}</p>}
      {notice && <p className="mb-3 rounded bg-green-50 px-3 py-2 text-sm text-green-700">{notice}</p>}
      {!photos && !error && <p className="text-slate-400">Loading…</p>}
      {photos && photos.length === 0 && (
        <p className="text-slate-500">No photos. Capture happens on the mobile app; review them here.</p>
      )}

      {photos && photos.length > 0 && (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4">
          {photos.map((p) => (
            <li key={p.id} className="overflow-hidden rounded-lg border border-slate-200 bg-white">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={photoFileUrl(projectId, p.id)}
                alt={p.caption ?? p.reason ?? 'Site photo'}
                className="h-32 w-full bg-slate-100 object-cover"
                loading="lazy"
              />
              <div className="p-2">
                <div className="flex items-center justify-between gap-1 text-[10px] text-slate-400">
                  <span>{p.reason}</span>
                  <span>{p.audience}</span>
                </div>
                {p.caption && <p className="mt-0.5 truncate text-xs text-slate-600">{p.caption}</p>}
                <div className="mt-1 text-[10px] text-slate-400">
                  {p.capturedByName ?? ''}
                  {p.capturedAt ? ` · ${new Date(p.capturedAt).toLocaleDateString()}` : ''}
                </div>
                {(p.audience === 'PendingReview' || p.audience === 'Internal') && (
                  <div className="mt-2 flex gap-1">
                    <button
                      onClick={() => onApprove(p)}
                      className="flex-1 rounded bg-green-600 px-2 py-1 text-[11px] font-medium text-white hover:bg-green-700"
                    >
                      Approve
                    </button>
                    <button
                      onClick={() => onReject(p)}
                      className="flex-1 rounded border border-slate-300 px-2 py-1 text-[11px] text-red-600 hover:bg-red-50"
                    >
                      Reject
                    </button>
                  </div>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}
    </AppShell>
  );
}
