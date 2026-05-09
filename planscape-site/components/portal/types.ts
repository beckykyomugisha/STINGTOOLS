// Shared types for the client portal.
// Mirrors the server DTO returned by GET /api/projects/{pid}/photos.

export interface PortalPhoto {
  id: string;
  reason?: string | null;
  caption?: string | null;
  capturedAt: string; // ISO 8601 date-time
  levelCode?: string | null;
  zoneCode?: string | null;
  pairKey?: string | null;
}

export interface PhotosPage {
  items: PortalPhoto[];
  total: number;
  page: number;
  pageSize: number;
}

export interface PortalFilterState {
  from?: string; // yyyy-mm-dd
  to?: string; // yyyy-mm-dd
  levelCode?: string;
  zoneCode?: string;
}

export type PortalView = 'gallery' | 'timeline' | 'beforeafter';
