'use client';

import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import {
  fetchPortalPhotos,
  buildPhotoFileUrl,
  PortalAuthError,
} from '@/components/portal/api';
import type {
  PhotosPage,
  PortalFilterState,
  PortalPhoto,
  PortalView,
} from '@/components/portal/types';
import PortalFilters from '@/components/portal/PortalFilters';
import PhotoGalleryGrid from '@/components/portal/PhotoGalleryGrid';
import PhotoTimeline from '@/components/portal/PhotoTimeline';
import PhotoBeforeAfter from '@/components/portal/PhotoBeforeAfter';

/**
 * Read-only client portal for site photos. Accessed via a signed-link URL:
 *   /portal?project={projectId}&token={signedToken}
 *
 * The page is purely client-fetched. The token is a short-lived JWT
 * (role=ClientGuest) that the server validates on every call.
 *
 * Optional: ?date=yyyy-mm-dd opens the Timeline tab pre-filtered to
 * that date — used by daily digest emails.
 */
export default function PortalPage() {
  return (
    <Suspense fallback={<PortalSkeleton />}>
      <PortalInner />
    </Suspense>
  );
}

function PortalInner() {
  const sp = useSearchParams();
  const projectId = sp.get('project') ?? '';
  const token = sp.get('token') ?? '';
  const deepLinkDate = sp.get('date') ?? undefined;

  const [view, setView] = useState<PortalView>(
    deepLinkDate ? 'timeline' : 'gallery',
  );
  const [filters, setFilters] = useState<PortalFilterState>(() =>
    deepLinkDate ? { from: deepLinkDate, to: deepLinkDate } : {},
  );

  const [page, setPage] = useState<PhotosPage | null>(null);
  const [loading, setLoading] = useState(true);
  const [authError, setAuthError] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const buildFileUrl = useCallback(
    (photoId: string) => buildPhotoFileUrl(projectId, photoId, token),
    [projectId, token],
  );

  useEffect(() => {
    if (!projectId || !token) {
      setAuthError(true);
      setLoading(false);
      return;
    }
    let cancelled = false;
    setLoading(true);
    setError(null);
    setAuthError(false);

    fetchPortalPhotos({
      projectId,
      token,
      from: filters.from,
      to: filters.to,
      levelCode: filters.levelCode,
      zoneCode: filters.zoneCode,
      page: 1,
      pageSize: 200,
    })
      .then((res) => {
        if (cancelled) return;
        setPage(res);
      })
      .catch((err) => {
        if (cancelled) return;
        if (err instanceof PortalAuthError) {
          setAuthError(true);
        } else {
          setError(err instanceof Error ? err.message : 'Failed to load');
        }
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [projectId, token, filters.from, filters.to, filters.levelCode, filters.zoneCode]);

  const photos: PortalPhoto[] = page?.items ?? [];

  const { levels, zones } = useMemo(() => {
    const ls = new Set<string>();
    const zs = new Set<string>();
    for (const p of photos) {
      if (p.levelCode) ls.add(p.levelCode);
      if (p.zoneCode) zs.add(p.zoneCode);
    }
    return {
      levels: Array.from(ls).sort(),
      zones: Array.from(zs).sort(),
    };
  }, [photos]);

  if (authError) return <AuthErrorState />;

  return (
    <main className="min-h-screen bg-slate-50">
      {/* Tell search engines to leave project photos out of the index. */}
      <PortalNoIndexMeta />

      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-6xl flex-col gap-2 px-4 py-5 sm:px-6">
          <span className="inline-block w-fit rounded-full bg-orange/15 px-3 py-1 text-xs font-medium text-orange">
            Planscape · Client Portal
          </span>
          <h1 className="text-2xl font-bold text-navy sm:text-3xl">
            Site progress photos
          </h1>
          <p className="text-sm text-slate-600">
            A read-only view of recent site photos shared with you. Photos are
            watermarked for confidentiality.
          </p>
        </div>
      </header>

      <div className="mx-auto max-w-6xl px-4 py-6 sm:px-6">
        <ViewSwitcher value={view} onChange={setView} />

        <div className="mt-4">
          <PortalFilters
            value={filters}
            levels={levels}
            zones={zones}
            onChange={setFilters}
            onClear={() => setFilters({})}
          />
        </div>

        <div className="mt-6">
          {loading ? (
            <PortalSkeleton />
          ) : error ? (
            <ErrorBanner message={error} />
          ) : view === 'gallery' ? (
            <PhotoGalleryGrid photos={photos} buildFileUrl={buildFileUrl} />
          ) : view === 'timeline' ? (
            <PhotoTimeline photos={photos} buildFileUrl={buildFileUrl} />
          ) : (
            <PhotoBeforeAfter photos={photos} buildFileUrl={buildFileUrl} />
          )}
        </div>

        {!loading && !error && page && (
          <p className="mt-6 text-center text-xs text-slate-500">
            Showing {photos.length} of {page.total} photos
          </p>
        )}
      </div>

      <footer className="mt-12 border-t border-slate-200 bg-white py-6">
        <div className="mx-auto max-w-6xl px-4 text-center text-xs text-slate-500 sm:px-6">
          Powered by Planscape — ISO 19650 BIM Coordination Platform
        </div>
      </footer>
    </main>
  );
}

function ViewSwitcher({
  value,
  onChange,
}: {
  value: PortalView;
  onChange: (v: PortalView) => void;
}) {
  const tabs: { id: PortalView; label: string }[] = [
    { id: 'gallery', label: 'Gallery' },
    { id: 'timeline', label: 'Timeline' },
    { id: 'beforeafter', label: 'Before & After' },
  ];
  return (
    <div
      role="tablist"
      aria-label="Photo view"
      className="inline-flex rounded-lg border border-slate-200 bg-white p-1 shadow-sm"
    >
      {tabs.map((t) => {
        const active = t.id === value;
        return (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={active}
            onClick={() => onChange(t.id)}
            className={`rounded-md px-4 py-1.5 text-sm font-medium transition ${
              active
                ? 'bg-orange text-white shadow-sm'
                : 'text-slate-600 hover:text-navy'
            }`}
          >
            {t.label}
          </button>
        );
      })}
    </div>
  );
}

function PortalSkeleton() {
  return (
    <div
      role="status"
      aria-live="polite"
      aria-label="Loading site photos"
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
    >
      {Array.from({ length: 6 }).map((_, i) => (
        <div
          key={i}
          className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm"
        >
          <div className="aspect-[4/3] w-full animate-pulse bg-slate-200" />
          <div className="space-y-2 p-3">
            <div className="h-4 w-3/4 animate-pulse rounded bg-slate-200" />
            <div className="h-3 w-1/3 animate-pulse rounded bg-slate-200" />
          </div>
        </div>
      ))}
    </div>
  );
}

function ErrorBanner({ message }: { message: string }) {
  return (
    <div
      role="alert"
      className="rounded-xl border border-danger/30 bg-danger/5 p-4 text-sm text-danger"
    >
      {message}
    </div>
  );
}

function AuthErrorState() {
  return (
    <main className="flex min-h-screen items-center justify-center bg-slate-50 px-4">
      <PortalNoIndexMeta />
      <div className="max-w-md rounded-xl border border-slate-200 bg-white p-8 text-center shadow-sm">
        <span className="inline-block rounded-full bg-danger/10 px-3 py-1 text-xs font-medium text-danger">
          Link invalid or expired
        </span>
        <h1 className="mt-4 text-xl font-bold text-navy">
          We couldn’t open this link
        </h1>
        <p className="mt-2 text-sm text-slate-600">
          The portal link you used is missing required information or has
          expired. Please request a new link from your project team.
        </p>
        <a
          href="mailto:support@planscape.io?subject=Client%20portal%20link%20expired"
          className="mt-6 inline-block rounded-md bg-orange px-4 py-2 text-sm font-medium text-white shadow-sm transition hover:bg-orange-dark"
        >
          Request a new link
        </a>
      </div>
    </main>
  );
}

/**
 * Inline noindex meta. We can't use Next's metadata export from a client
 * component, so inject directly into <head>.
 */
function PortalNoIndexMeta() {
  useEffect(() => {
    const tag = document.createElement('meta');
    tag.name = 'robots';
    tag.content = 'noindex, nofollow';
    document.head.appendChild(tag);
    return () => {
      document.head.removeChild(tag);
    };
  }, []);
  return null;
}
