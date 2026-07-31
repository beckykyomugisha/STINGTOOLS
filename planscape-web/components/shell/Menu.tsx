'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';

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

  useEffect(() => {
    if (!open) return;
    function onPointerDown(e: MouseEvent) {
      if (!wrapRef.current?.contains(e.target as Node)) setOpen(false);
    }
    function items(): HTMLElement[] {
      return Array.from(wrapRef.current?.querySelectorAll<HTMLElement>('[role="menuitem"]:not([disabled])') ?? []);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setOpen(false);
        triggerRef.current?.focus(); // don't strand focus at the top of the page
        return;
      }
      // U5 — arrow keys move through the menu, matching the WAI-ARIA menu
      // pattern. Without this an open menu is only reachable by Tab, which
      // walks straight past it into the page behind.
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
  }, [open]);

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
