'use client';

import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import Link from 'next/link';
import { useAuth } from '@/lib/auth';
import { useTheme } from '@/lib/theme';
import { NotificationBell } from '@/components/NotificationBell';
import { Rail } from '@/components/shell/Rail';
import { ProjectSwitcher, TenantSwitcher } from '@/components/shell/Switchers';
import { Menu, MenuItem, MenuLabel, MenuSeparator } from '@/components/shell/Menu';
import { crumbsFor } from '@/components/shell/nav';

const RAIL_KEY = 'planscape_rail_collapsed';

/** The project id, when the current route is inside a project. */
function projectIdFrom(pathname: string): string | null {
  const m = pathname.match(/^\/projects\/([^/]+)/);
  return m && m[1] !== 'new' ? m[1] : null;
}

/**
 * U2 — the ACC-style app shell: fixed collapsible left rail + top bar
 * (project switcher · tenant switcher · breadcrumb · search · bell · avatar),
 * with a FULL-BLEED content area. Replaces the previous top-bar-only chrome
 * whose content was capped at `max-w-5xl` — a width that made every grid in the
 * app scroll horizontally for no reason.
 *
 * Still gates on auth exactly as before: no session → /login.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const { ready, user, logout } = useAuth();
  const { theme, setTheme } = useTheme();
  const router = useRouter();
  const pathname = usePathname() || '/';
  const [q, setQ] = useState('');
  const [collapsed, setCollapsed] = useState(false);
  const [railOpenMobile, setRailOpenMobile] = useState(false);

  const projectId = projectIdFrom(pathname);
  const crumbs = useMemo(() => crumbsFor(pathname), [pathname]);

  // Restore the rail state after mount — reading localStorage during render
  // would desync hydration between server and client markup.
  useEffect(() => {
    try {
      setCollapsed(window.localStorage.getItem(RAIL_KEY) === '1');
    } catch {
      /* private mode */
    }
  }, []);

  // Route change closes the mobile drawer; leaving it open would cover the page
  // the user just navigated to.
  useEffect(() => setRailOpenMobile(false), [pathname]);

  useEffect(() => {
    if (ready && !user) router.replace('/login');
  }, [ready, user, router]);

  function toggleRail() {
    setCollapsed((v) => {
      const next = !v;
      try {
        window.localStorage.setItem(RAIL_KEY, next ? '1' : '0');
      } catch {
        /* private mode */
      }
      return next;
    });
  }

  if (!ready || !user) {
    return <main className="grid min-h-screen place-items-center text-fg-subtle">Loading…</main>;
  }

  const railWidth = collapsed ? 'w-rail-collapsed' : 'w-rail';

  return (
    <div className="flex min-h-screen bg-bg text-fg">
      {/* Skip link — first tab stop, so keyboard users aren't forced through
          the whole rail on every page. */}
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:absolute focus:left-2 focus:top-2 focus:z-50 focus:rounded focus:bg-surface focus:px-3 focus:py-2 focus:shadow-lg"
      >
        Skip to content
      </a>

      {/* Rail — fixed on desktop, a drawer under lg. */}
      <aside
        className={`${railWidth} fixed inset-y-0 left-0 z-30 hidden shrink-0 transition-[width] lg:block`}
      >
        <Rail collapsed={collapsed} projectId={projectId} pathname={pathname} />
      </aside>
      {railOpenMobile && (
        <>
          <div
            className="fixed inset-0 z-30 bg-black/40 lg:hidden"
            onClick={() => setRailOpenMobile(false)}
            aria-hidden="true"
          />
          <aside className="fixed inset-y-0 left-0 z-40 w-rail animate-slide-in-right lg:hidden">
            <Rail collapsed={false} projectId={projectId} pathname={pathname} />
          </aside>
        </>
      )}

      <div className={`flex min-w-0 flex-1 flex-col ${collapsed ? 'lg:pl-rail-collapsed' : 'lg:pl-rail'} transition-[padding]`}>
        <header className="sticky top-0 z-20 flex h-topbar items-center gap-2 border-b border-border bg-surface/95 px-3 backdrop-blur">
          <button
            type="button"
            onClick={() => setRailOpenMobile(true)}
            aria-label="Open navigation"
            className="rounded p-1.5 text-fg-muted transition hover:bg-surface-3 hover:text-fg lg:hidden"
          >
            <Bars />
          </button>
          <button
            type="button"
            onClick={toggleRail}
            aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'}
            aria-pressed={collapsed}
            className="hidden rounded p-1.5 text-fg-muted transition hover:bg-surface-3 hover:text-fg lg:block"
          >
            <Bars />
          </button>

          <Link href="/projects" className="shrink-0 px-1 font-semibold tracking-tight">
            Planscape
          </Link>

          <div className="mx-1 hidden h-4 w-px bg-border sm:block" />
          <div className="hidden sm:block">
            <ProjectSwitcher projectId={projectId} />
          </div>

          {/* Breadcrumb — hidden on small screens where the switcher already
              says where you are. */}
          <nav aria-label="Breadcrumb" className="ml-1 hidden min-w-0 flex-1 items-center gap-1 text-xs text-fg-muted xl:flex">
            {crumbs.map((c, i) => (
              <span key={`${c.label}-${i}`} className="flex min-w-0 items-center gap-1">
                {i > 0 && <span className="text-fg-subtle">/</span>}
                {c.href ? (
                  <Link href={c.href} className="truncate transition hover:text-fg">
                    {c.label}
                  </Link>
                ) : (
                  <span className="truncate font-medium text-fg">{c.label}</span>
                )}
              </span>
            ))}
          </nav>

          <div className="ml-auto flex items-center gap-1.5">
            <form
              onSubmit={(e) => {
                e.preventDefault();
                if (q.trim().length >= 2) router.push(`/search?q=${encodeURIComponent(q.trim())}`);
              }}
            >
              <input
                value={q}
                onChange={(e) => setQ(e.target.value)}
                placeholder="Search…"
                aria-label="Search"
                className="w-32 rounded border border-border bg-surface-2 px-2 py-1 text-sm text-fg placeholder:text-fg-subtle transition focus:w-56 focus:border-border-strong md:w-40"
              />
            </form>

            <TenantSwitcher />
            <NotificationBell />

            <Menu
              label="Account menu"
              trigger={
                <span className="grid h-6 w-6 place-items-center rounded-full bg-accent text-2xs font-semibold text-fg-on-accent">
                  {(user.email || '?').slice(0, 2).toUpperCase()}
                </span>
              }
            >
              {(close) => (
                <>
                  <MenuLabel>Signed in as</MenuLabel>
                  <div className="truncate px-3 pb-1 text-sm text-fg">{user.email}</div>
                  <MenuSeparator />
                  <MenuLabel>Theme</MenuLabel>
                  {(['light', 'dark', 'system'] as const).map((t) => (
                    <MenuItem key={t} active={theme === t} onClick={() => setTheme(t)}>
                      <span className="capitalize">{t}</span>
                    </MenuItem>
                  ))}
                  <MenuSeparator />
                  {/* Firm-wide, deliberately NOT inside a project — inviting
                      someone to the practice is not a project action. Owner /
                      Admin only server-side; the page says so rather than
                      hiding, since the token doesn't tell us the tenant role
                      reliably enough to gate the menu on it. */}
                  <MenuItem
                    onClick={() => {
                      close();
                      router.push('/settings/team');
                    }}
                  >
                    Team
                  </MenuItem>
                  <MenuItem
                    onClick={() => {
                      close();
                      router.push('/settings/tokens');
                    }}
                  >
                    Access tokens
                  </MenuItem>
                  <MenuItem
                    onClick={() => {
                      close();
                      logout();
                      router.replace('/login');
                    }}
                  >
                    Sign out
                  </MenuItem>
                </>
              )}
            </Menu>
          </div>
        </header>

        <main id="main" className="min-w-0 flex-1 p-4 lg:p-6">
          {children}
        </main>
      </div>
    </div>
  );
}

function Bars() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" aria-hidden="true" className="h-4 w-4">
      <path d="M3 6h18M3 12h18M3 18h18" strokeLinecap="round" />
    </svg>
  );
}
