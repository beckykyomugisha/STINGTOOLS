// Tiny client-side API helper for the read-only client portal.
// Reads NEXT_PUBLIC_API_BASE once and exposes typed wrappers.

import type { PhotosPage, PortalFilterState } from './types';

const API_BASE =
  (typeof process !== 'undefined' && process.env.NEXT_PUBLIC_API_BASE) ||
  'https://api.planscape.app';

function buildHeaders(token: string): HeadersInit {
  return {
    Authorization: `Bearer ${token}`,
    Accept: 'application/json',
  };
}

export class PortalAuthError extends Error {
  constructor(message = 'Unauthorized') {
    super(message);
    this.name = 'PortalAuthError';
  }
}

export interface FetchPhotosArgs extends PortalFilterState {
  projectId: string;
  token: string;
  page?: number;
  pageSize?: number;
}

export async function fetchPortalPhotos({
  projectId,
  token,
  from,
  to,
  levelCode,
  zoneCode,
  page = 1,
  pageSize = 60,
}: FetchPhotosArgs): Promise<PhotosPage> {
  const params = new URLSearchParams();
  params.set('audience', 'ClientPortal');
  if (from) params.set('from', from);
  if (to) params.set('to', to);
  if (levelCode) params.set('levelCode', levelCode);
  if (zoneCode) params.set('zoneCode', zoneCode);
  params.set('page', String(page));
  params.set('pageSize', String(pageSize));

  const url = `${API_BASE}/api/projects/${encodeURIComponent(
    projectId,
  )}/photos?${params.toString()}`;

  const res = await fetch(url, {
    method: 'GET',
    headers: buildHeaders(token),
    cache: 'no-store',
  });

  if (res.status === 401 || res.status === 403) {
    throw new PortalAuthError(`Link invalid or expired (${res.status})`);
  }
  if (!res.ok) {
    throw new Error(`Failed to load photos (${res.status})`);
  }
  return (await res.json()) as PhotosPage;
}

/**
 * Build the photo file URL. The server returns the watermarked / blurred
 * derivative when the JWT role is ClientGuest. We attach the token via a
 * query string here because <img src> can't carry custom headers — the
 * server already supports `?access_token=` for ClientGuest-signed links.
 *
 * If your deployment requires Authorization headers only, replace this
 * with a blob fetch + object URL pattern in the consuming component.
 */
export function buildPhotoFileUrl(
  projectId: string,
  photoId: string,
  token: string,
): string {
  return `${API_BASE}/api/projects/${encodeURIComponent(
    projectId,
  )}/photos/${encodeURIComponent(photoId)}/file?access_token=${encodeURIComponent(
    token,
  )}`;
}
