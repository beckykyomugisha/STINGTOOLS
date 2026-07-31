/**
 * U2 — the navigation model, kept as data rather than JSX so the rail, the
 * breadcrumb and any future command palette all read from one list. Adding a
 * route means adding a line here, not editing three components.
 *
 * Icons are inline SVG path data (no icon dependency — this app has 5 runtime
 * deps and the shell is not the place to add a sixth).
 */

export interface NavItem {
  /** Path relative to the project, e.g. `issues`. Empty string = the project overview. */
  segment: string;
  label: string;
  /** `d` attribute of a single 24x24 stroke path. */
  icon: string;
}

/** Project-scoped nav — the rail's main group when a project is open. */
export const PROJECT_NAV: NavItem[] = [
  { segment: '', label: 'Overview', icon: 'M3 12l9-9 9 9M5 10v10h14V10' },
  { segment: 'issues', label: 'Issues', icon: 'M12 9v4m0 4h.01M10.3 3.9L1.8 18a2 2 0 001.7 3h17a2 2 0 001.7-3L14.7 3.9a2 2 0 00-3.4 0z' },
  { segment: 'clashes', label: 'Clashes', icon: 'M13 2L3 14h9l-1 8 10-12h-9l1-8z' },
  { segment: 'models', label: 'Models', icon: 'M21 16V8a2 2 0 00-1-1.7l-7-4a2 2 0 00-2 0l-7 4A2 2 0 003 8v8a2 2 0 001 1.7l7 4a2 2 0 002 0l7-4A2 2 0 0021 16z' },
  { segment: 'viewer', label: '3D viewer', icon: 'M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7-10-7-10-7z M12 9a3 3 0 100 6 3 3 0 000-6z' },
  { segment: 'documents', label: 'Documents', icon: 'M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z M14 2v6h6' },
  { segment: 'transmittals', label: 'Transmittals', icon: 'M22 2L11 13M22 2l-7 20-4-9-9-4 20-7z' },
  { segment: 'meetings', label: 'Meetings', icon: 'M17 21v-2a4 4 0 00-4-4H5a4 4 0 00-4 4v2 M9 11a4 4 0 100-8 4 4 0 000 8 M23 21v-2a4 4 0 00-3-3.87' },
  { segment: 'photos', label: 'Site photos', icon: 'M23 19a2 2 0 01-2 2H3a2 2 0 01-2-2V8a2 2 0 012-2h4l2-3h6l2 3h4a2 2 0 012 2z M12 17a4 4 0 100-8 4 4 0 000 8z' },
  { segment: 'members', label: 'Members', icon: 'M16 21v-2a4 4 0 00-4-4H6a4 4 0 00-4 4v2 M9 11a4 4 0 100-8 4 4 0 000 8z' },
];

/** Global nav — always available, project or not.
 *  No Dashboard entry: `/dashboard` is a redirect stub that bounces straight to
 *  `/projects`, and a nav item that instantly navigates to another nav item is
 *  noise. The route stays for old bookmarks. */
export const GLOBAL_NAV: NavItem[] = [
  { segment: '/projects', label: 'Projects', icon: 'M22 19a2 2 0 01-2 2H4a2 2 0 01-2-2V5a2 2 0 012-2h5l2 3h9a2 2 0 012 2z' },
  { segment: '/search', label: 'Search', icon: 'M11 19a8 8 0 100-16 8 8 0 000 16z M21 21l-4.35-4.35' },
];

/** Human labels for path segments the nav model doesn't own (detail routes). */
const SEGMENT_LABELS: Record<string, string> = {
  projects: 'Projects',
  dashboard: 'Dashboard',
  search: 'Search',
  settings: 'Settings',
  tokens: 'Access tokens',
  team: 'Team',
  handoff: 'Handoff',
  new: 'New',
  live: 'Live',
  issues: 'Issues',
  clashes: 'Clashes',
  models: 'Models',
  viewer: '3D viewer',
  documents: 'Documents',
  transmittals: 'Transmittals',
  meetings: 'Meetings',
  photos: 'Site photos',
  members: 'Members',
};

export interface Crumb {
  label: string;
  href?: string;
}

/**
 * Derive breadcrumbs from the pathname.
 *
 * Two deliberate behaviours: a GUID segment renders as a short `#a1b2c3d4` chip
 * rather than a 36-character wall (the human-readable title lives in the page,
 * which the shell can't see), and `projectName` replaces the project GUID when
 * the caller knows it. The last crumb is never a link.
 */
export function crumbsFor(pathname: string, projectName?: string): Crumb[] {
  const parts = pathname.split('/').filter(Boolean);
  const out: Crumb[] = [];
  let href = '';
  parts.forEach((part, i) => {
    href += `/${part}`;
    const isGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-/i.test(part);
    const isProjectId = isGuid && parts[i - 1] === 'projects';
    const label = isProjectId
      ? projectName || `#${part.slice(0, 8)}`
      : isGuid
        ? `#${part.slice(0, 8)}`
        : SEGMENT_LABELS[part] || part.replace(/-/g, ' ');
    out.push({ label, href: i === parts.length - 1 ? undefined : href });
  });
  return out;
}
