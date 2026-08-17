'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AppShell } from '@/components/AppShell';
import { ErrorNote, ForbiddenNote, LoadingBlock } from '@/components/ui';
import { describeFailure } from '@/lib/api';
import { CAPABILITY_COPY, useProjectCapabilities } from '@/lib/capabilities';
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
  const [forbidden, setForbidden] = useState(false);

  // Approve / reject are gated on canApproveSitePhotos. This starts 'unknown',
  // which leaves both buttons live — correct, because the server is still the
  // gate and an unanswered question must not render as a "no".
  const caps = useProjectCapabilities(projectId);
  const cannotApprove = caps.approveSitePhotos === 'denied';

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
    setForbidden(false);
    setNotice(null);
    try {
      await approvePhoto(projectId, p.id, caption);
      setNotice('Photo approved.');
      load();
    } catch (e) {
      const d = describeFailure(e, {
        forbidden: CAPABILITY_COPY.approveSitePhotos,
        fallback: 'Approve failed',
      });
      setError(d.message);
      setForbidden(d.tone === 'forbidden');
    }
  }

  async function onReject(p: SitePhoto) {
    const reasonText = prompt('Rejection reason:');
    if (!reasonText) return;
    setError(null);
    setForbidden(false);
    setNotice(null);
    try {
      await rejectPhoto(projectId, p.id, reasonText);
      setNotice('Photo rejected.');
      load();
    } catch (e) {
      const d = describeFailure(e, {
        forbidden: CAPABILITY_COPY.approveSitePhotos,
        fallback: 'Reject failed',
      });
      setError(d.message);
      setForbidden(d.tone === 'forbidden');
    }
  }

  return (
    <AppShell>
      <div className="mb-4">
        <Link href={`/projects/${projectId}`} className="text-sm text-fg-subtle hover:underline">
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
              reason === r ? 'bg-accent text-fg-on-accent' : 'bg-surface text-fg-muted ring-1 ring-border'
            }`}
          >
            {r}
          </button>
        ))}
      </div>

      {error && (
        <div className="mb-3">{forbidden ? <ForbiddenNote>{error}</ForbiddenNote> : <ErrorNote>{error}</ErrorNote>}</div>
      )}
      {cannotApprove && (
        <div className="mb-3">
          <ForbiddenNote>{CAPABILITY_COPY.approveSitePhotos}</ForbiddenNote>
        </div>
      )}
      {notice && <p className="mb-3 rounded bg-success-subtle px-3 py-2 text-sm text-success">{notice}</p>}
      {!photos && !error && <LoadingBlock />}
      {photos && photos.length === 0 && (
        <p className="text-fg-muted">No photos. Capture happens on the mobile app; review them here.</p>
      )}

      {photos && photos.length > 0 && (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 md:grid-cols-4">
          {photos.map((p) => (
            <li key={p.id} className="overflow-hidden rounded-lg border border-border bg-surface">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src={photoFileUrl(projectId, p.id)}
                alt={p.caption ?? p.reason ?? 'Site photo'}
                className="h-32 w-full bg-surface-3 object-cover"
                loading="lazy"
              />
              <div className="p-2">
                <div className="flex items-center justify-between gap-1 text-[10px] text-fg-subtle">
                  <span>{p.reason}</span>
                  <span>{p.audience}</span>
                </div>
                {p.caption && <p className="mt-0.5 truncate text-xs text-fg-muted">{p.caption}</p>}
                <div className="mt-1 text-[10px] text-fg-subtle">
                  {p.capturedByName ?? ''}
                  {p.capturedAt ? ` · ${new Date(p.capturedAt).toLocaleDateString()}` : ''}
                </div>
                {(p.audience === 'PendingReview' || p.audience === 'Internal') && (
                  <div className="mt-2 flex gap-1">
                    <button
                      onClick={() => onApprove(p)}
                      disabled={cannotApprove}
                      title={cannotApprove ? CAPABILITY_COPY.approveSitePhotos : undefined}
                      className="flex-1 rounded bg-success px-2 py-1 text-[11px] font-medium text-fg-on-accent hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                      Approve
                    </button>
                    <button
                      onClick={() => onReject(p)}
                      disabled={cannotApprove}
                      title={cannotApprove ? CAPABILITY_COPY.approveSitePhotos : undefined}
                      className="flex-1 rounded border border-border-strong px-2 py-1 text-[11px] text-danger hover:bg-danger-subtle disabled:cursor-not-allowed disabled:opacity-50"
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
