'use client';

import Link from 'next/link';
import { GLOBAL_NAV, PROJECT_NAV, type NavItem } from './nav';

function Icon({ d }: { d: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className="h-4 w-4 shrink-0"
    >
      {d.split(' M').map((seg, i) => (
        <path key={i} d={i === 0 ? seg : `M${seg}`} />
      ))}
    </svg>
  );
}

function RailLink({
  href,
  item,
  active,
  collapsed,
}: {
  href: string;
  item: NavItem;
  active: boolean;
  collapsed: boolean;
}) {
  return (
    <Link
      href={href}
      // The label is the accessible name when collapsed; title gives a native
      // tooltip so an icon-only rail is still usable without guessing.
      aria-label={collapsed ? item.label : undefined}
      title={collapsed ? item.label : undefined}
      aria-current={active ? 'page' : undefined}
      className={`flex items-center gap-2.5 rounded px-2 py-1.5 text-sm transition ${
        active ? 'bg-accent-subtle font-medium text-accent' : 'text-fg-muted hover:bg-surface-3 hover:text-fg'
      } ${collapsed ? 'justify-center' : ''}`}
    >
      <Icon d={item.icon} />
      {!collapsed && <span className="truncate">{item.label}</span>}
    </Link>
  );
}

/**
 * U2 — the fixed left rail. Two groups: global nav, and (when a project is open)
 * that project's sections. Collapsing to icons is persisted by the shell, not
 * here, so the topbar's toggle and the rail can't disagree about the state.
 */
export function Rail({
  collapsed,
  projectId,
  pathname,
}: {
  collapsed: boolean;
  projectId: string | null;
  pathname: string;
}) {
  const base = projectId ? `/projects/${projectId}` : '';

  return (
    <nav
      aria-label="Primary"
      className="flex h-full flex-col gap-4 overflow-y-auto border-r border-border bg-surface-2 p-2"
    >
      <div className="flex flex-col gap-0.5">
        {GLOBAL_NAV.map((item) => (
          <RailLink
            key={item.segment}
            href={item.segment}
            item={item}
            collapsed={collapsed}
            // Exact match only: /projects must not light up on /projects/<id>/issues,
            // where the project group below is the meaningful location.
            active={pathname === item.segment}
          />
        ))}
      </div>

      {projectId && (
        <div className="flex flex-col gap-0.5">
          {!collapsed && (
            <div className="px-2 pb-1 text-2xs font-semibold uppercase tracking-wide text-fg-subtle">Project</div>
          )}
          {PROJECT_NAV.map((item) => {
            const href = item.segment ? `${base}/${item.segment}` : base;
            const active = item.segment
              ? pathname === href || pathname.startsWith(`${href}/`)
              : pathname === base;
            return <RailLink key={item.segment || 'overview'} href={href} item={item} collapsed={collapsed} active={active} />;
          })}
        </div>
      )}
    </nav>
  );
}
