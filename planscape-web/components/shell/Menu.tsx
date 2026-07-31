'use client';

import { useEffect, useLayoutEffect, useRef, useState, type ReactNode, type RefObject } from 'react';

/**
 * The behaviour both popups share: Escape closes and hands focus back, an
 * outside click closes, and ↑/↓/Home/End walk the items (WAI-ARIA menu pattern).
 *
 * Extracted so the right-click menu added in U6 is the SAME menu as the shell's
 * click menus rather than a second implementation that drifts. If you fix a
 * keyboard bug, fix it here once.
 */
function useMenuBehaviour(
  open: boolean,
  wrapRef: RefObject<HTMLElement | null>,
  close: () => void,
  restoreFocus?: () => void,
) {
  useEffect(() => {
    if (!open) return;
    function onPointerDown(e: MouseEvent) {
      if (!wrapRef.current?.contains(e.target as Node)) close();
    }
    function items(): HTMLElement[] {
      return Array.from(wrapRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]:not([disabled])') ?? []);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        close();
        restoreFocus?.(); // don't strand focus at the top of the page
        return;
      }
      if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp' && e.key !== 'Home' && e.key !== 'End') return;
      const list = items();
      if (!list.length) return;
      e.preventDefault();
      const at = list.indexOf(document.activeElement as HTMLElement);
      const next =
        e.key === 'Home' ? 0
        : e.key === 'End' ? list.length - 1
        : e.key === 'ArrowDown' ? (at + 1) % list.length
        : (at - 1 + list.length) % list.length;
      list[next]?.focus();
    }
    document.addEventListener('mousedown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('mousedown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open, wrapRef, close, restoreFocus]);
}

/**
 * U2 — a minimal accessible popover menu for the shell (avatar, tenant, project
 * switchers). Deliberately hand-rolled and ~80 lines: U3 brings in the Radix
 * primitives, and the shell should not block on that. It already does the four
 * things that actually matter — Escape closes, an outside click closes, focus
 * returns to the trigger, and the trigger carries `aria-expanded`/`aria-haspopup`.
 */
export function Menu({
  label,
  trigger,
  children,
  align = 'end',
  widthClass = 'w-64',
}: {
  /** Accessible name for the trigger — the visual content is often just an avatar. */
  label: string;
  trigger: ReactNode;
  children: (close: () => void) => ReactNode;
  align?: 'start' | 'end';
  widthClass?: string;
}) {
  const [open, setOpen] = useState(false);
  const wrapRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  // U5 — arrow keys move through the menu, matching the WAI-ARIA menu pattern.
  // Without this an open menu is only reachable by Tab, which walks straight
  // past it into the page behind.
  const close = useRef(() => setOpen(false)).current;
  const restore = useRef(() => triggerRef.current?.focus()).current;
  useMenuBehaviour(open, wrapRef, close, restore);

  return (
    <div ref={wrapRef} className="relative">
      <button
        ref={triggerRef}
        type="button"
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        className="flex items-center gap-1.5 rounded px-2 py-1 text-sm text-fg-muted transition hover:bg-surface-3 hover:text-fg"
      >
        {trigger}
      </button>
      {open && (
        <div
          role="menu"
          className={`absolute ${align === 'end' ? 'right-0' : 'left-0'} z-30 mt-1 ${widthClass} animate-zoom-in overflow-hidden rounded-md border border-border bg-surface py-1 shadow-lg`}
        >
          {children(() => setOpen(false))}
        </div>
      )}
    </div>
  );
}

/**
 * U6 — the same menu, opened at a point instead of under a trigger. Used by the
 * DataGrid for right-click row actions.
 *
 * It is a panel, not a wrapper: the caller owns "where and when", because only
 * the caller knows what was right-clicked. Rendering is unconditional — mount it
 * when open, unmount it when closed — so focus management runs exactly once per
 * opening.
 *
 * Deliberately not a portal. The grid is inside `<main>` with no clipping
 * ancestor, and `position: fixed` already escapes the grid's `overflow-x: auto`.
 * A portal would add a mount point for no behaviour.
 */
export function ContextMenuPanel({
  x,
  y,
  onClose,
  children,
  label = 'Row actions',
  widthClass = 'w-56',
}: {
  x: number;
  y: number;
  onClose: () => void;
  children: ReactNode;
  label?: string;
  widthClass?: string;
}) {
  const wrapRef = useRef<HTMLDivElement>(null);
  const returnTo = useRef<HTMLElement | null>(null);
  const [pos, setPos] = useState({ x, y });

  const restore = useRef(() => returnTo.current?.focus?.()).current;
  useMenuBehaviour(true, wrapRef, onClose, restore);

  useLayoutEffect(() => {
    returnTo.current = document.activeElement as HTMLElement | null;
    const el = wrapRef.current;
    if (!el) return;
    // Clamp into the viewport: a right-click near the bottom-right otherwise
    // opens a menu that is half off-screen and unreachable.
    const r = el.getBoundingClientRect();
    const pad = 8;
    setPos({
      x: Math.max(pad, Math.min(x, window.innerWidth - r.width - pad)),
      y: Math.max(pad, Math.min(y, window.innerHeight - r.height - pad)),
    });
    el.querySelector<HTMLElement>('[role="menuitem"]:not([disabled])')?.focus();
  }, [x, y]);

  return (
    <div
      ref={wrapRef}
      role="menu"
      aria-label={label}
      style={{ top: pos.y, left: pos.x }}
      className={`fixed z-40 ${widthClass} animate-zoom-in overflow-hidden rounded-md border border-border bg-surface py-1 shadow-lg`}
      // A right-click inside the menu should not open the browser's own menu on
      // top of ours.
      onContextMenu={(e) => e.preventDefault()}
    >
      {children}
    </div>
  );
}

/** A row inside {@link Menu}. Renders as a button so keyboard activation is free. */
export function MenuItem({
  onClick,
  children,
  active = false,
  disabled = false,
}: {
  onClick?: () => void;
  children: ReactNode;
  active?: boolean;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      role="menuitem"
      disabled={disabled}
      onClick={onClick}
      className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm transition disabled:opacity-50 ${
        active ? 'bg-accent-subtle text-accent' : 'text-fg hover:bg-surface-3'
      }`}
    >
      {children}
    </button>
  );
}

export function MenuLabel({ children }: { children: ReactNode }) {
  return <div className="px-3 py-1 text-2xs font-semibold uppercase tracking-wide text-fg-subtle">{children}</div>;
}

export function MenuSeparator() {
  return <div className="my-1 h-px bg-border" />;
}
