'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { listProjects } from '@/lib/data';
import { listTenants, switchTenant, type TenantMembership } from '@/lib/tenants';
import type { Project } from '@/lib/types';
import { Menu, MenuItem, MenuLabel, MenuSeparator } from './Menu';

function Chevron() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" className="h-3 w-3 shrink-0">
      <path d="M6 9l6 6 6-6" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

/** U2 — jump between projects without going back to the list. */
export function ProjectSwitcher({ projectId, projectName }: { projectId: string | null; projectName?: string }) {
  const router = useRouter();
  const [projects, setProjects] = useState<Project[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  // Loaded lazily on first open, not on every page load: most navigations never
  // touch this menu and the list is a network call.
  async function load() {
    if (projects || error) return;
    try {
      setProjects(await listProjects());
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load projects');
    }
  }

  const current = projects?.find((p) => p.id === projectId);
  const label = projectName || current?.name || (projectId ? 'Project' : 'Select project');

  return (
    <div onPointerEnter={load}>
      <Menu
        label="Switch project"
        widthClass="w-72"
        align="start"
        trigger={
          <>
            <span className="max-w-[12rem] truncate font-medium text-fg">{label}</span>
            <Chevron />
          </>
        }
      >
        {(close) => (
          <>
            <MenuLabel>Projects</MenuLabel>
            {error && <div className="px-3 py-2 text-xs text-danger">{error}</div>}
            {!projects && !error && <div className="px-3 py-2 text-xs text-fg-subtle">Loading…</div>}
            {projects?.length === 0 && <div className="px-3 py-2 text-xs text-fg-subtle">No projects yet.</div>}
            <div className="max-h-72 overflow-y-auto">
              {projects?.map((p) => (
                <MenuItem
                  key={p.id}
                  active={p.id === projectId}
                  onClick={() => {
                    close();
                    router.push(`/projects/${p.id}`);
                  }}
                >
                  <span className="truncate">{p.name}</span>
                </MenuItem>
              ))}
            </div>
            <MenuSeparator />
            <MenuItem
              onClick={() => {
                close();
                router.push('/projects');
              }}
            >
              All projects…
            </MenuItem>
          </>
        )}
      </Menu>
    </div>
  );
}

/**
 * U2 — tenant (firm) switcher. `GET /api/auth/tenants` and
 * `POST /api/auth/switch-tenant` have existed server-side for a while with no web
 * caller; this is the first one.
 *
 * Renders NOTHING when the user belongs to a single tenant — a switcher with one
 * option is noise, and most users are in exactly one firm.
 */
export function TenantSwitcher() {
  const [memberships, setMemberships] = useState<TenantMembership[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    listTenants()
      .then((rows) => {
        if (!cancelled) setMemberships(rows);
      })
      .catch(() => {
        // Non-fatal: an older API without the endpoint should cost a switcher,
        // not the whole shell.
        if (!cancelled) setMemberships([]);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  if (!memberships || memberships.length < 2) return null;

  const active = memberships.find((m) => m.isActiveTenant);

  async function choose(tenantId: string, close: () => void) {
    close();
    setBusy(true);
    setError(null);
    try {
      await switchTenant(tenantId);
      // HARD navigation on purpose. Every list in memory belongs to the previous
      // tenant; a soft re-render would briefly paint one firm's data under
      // another firm's name, which is exactly the failure two-firm isolation is
      // meant to prevent being visible.
      window.location.href = '/projects';
    } catch (e) {
      setBusy(false);
      setError(e instanceof Error ? e.message : 'Switch failed');
    }
  }

  return (
    <Menu
      label="Switch organisation"
      widthClass="w-72"
      trigger={
        <>
          <span className="rounded bg-surface-3 px-1.5 py-0.5 text-2xs font-semibold uppercase tracking-wide text-fg-muted">
            {busy ? 'Switching…' : active?.tenantName || 'Org'}
          </span>
          <Chevron />
        </>
      }
    >
      {(close) => (
        <>
          <MenuLabel>Organisation</MenuLabel>
          {error && <div className="px-3 py-2 text-xs text-danger">{error}</div>}
          {memberships.map((m) => (
            <MenuItem key={m.tenantId} active={m.isActiveTenant} disabled={busy} onClick={() => void choose(m.tenantId, close)}>
              <span className="flex-1 truncate">{m.tenantName}</span>
              {m.role && <span className="text-2xs text-fg-subtle">{m.role}</span>}
            </MenuItem>
          ))}
          <MenuSeparator />
          <div className="px-3 py-1.5 text-2xs text-fg-subtle">Switching reloads the app in the new organisation.</div>
        </>
      )}
    </Menu>
  );
}
