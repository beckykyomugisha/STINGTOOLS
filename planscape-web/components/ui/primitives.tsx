'use client';

import { forwardRef, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes } from 'react';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/cn';

/**
 * U3 — the small primitives. Input / Select / Badge / EmptyState / Skeleton /
 * Toolbar. Grouped in one file because each is a handful of lines and eleven
 * one-component files is harder to navigate than one obvious one; Button,
 * Modal, Drawer, Tabs and DataGrid earn their own files.
 *
 * Native <input>/<select> rather than the Radix versions: a data grid renders
 * hundreds of these, Radix Select mounts a portal per instance, and a native
 * select is what a keyboard and a screen reader already understand. Radix is
 * used where it earns its keep — focus traps and dismissal (Modal, Drawer).
 */

const field =
  'w-full rounded border border-border bg-surface px-2 py-1 text-sm text-fg placeholder:text-fg-subtle transition hover:border-border-strong focus:border-border-strong disabled:opacity-60';

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement>>(function Input(
  { className, ...props },
  ref,
) {
  return <input ref={ref} className={cn(field, 'h-8', className)} {...props} />;
});

export const Select = forwardRef<HTMLSelectElement, SelectHTMLAttributes<HTMLSelectElement>>(function Select(
  { className, children, ...props },
  ref,
) {
  return (
    <select ref={ref} className={cn(field, 'h-8 cursor-pointer', className)} {...props}>
      {children}
    </select>
  );
});

const badge = cva('inline-flex items-center gap-1 rounded-sm px-1.5 py-0.5 text-2xs font-medium', {
  variants: {
    tone: {
      neutral: 'bg-surface-3 text-fg-muted',
      accent: 'bg-accent-subtle text-accent',
      success: 'bg-success-subtle text-success',
      warning: 'bg-warning-subtle text-warning',
      danger: 'bg-danger-subtle text-danger',
      info: 'bg-info-subtle text-info',
    },
  },
  defaultVariants: { tone: 'neutral' },
});

export interface BadgeProps extends VariantProps<typeof badge> {
  children: ReactNode;
  className?: string;
}

export function Badge({ tone, children, className }: BadgeProps) {
  return <span className={cn(badge({ tone }), className)}>{children}</span>;
}

/**
 * Map a domain status to a badge tone in ONE place, so "Open" isn't amber on
 * one screen and grey on another. Unknown values fall back to neutral rather
 * than throwing — statuses are server-defined strings and new ones appear.
 */
export function toneForStatus(status?: string | null): NonNullable<BadgeProps['tone']> {
  const s = (status || '').toLowerCase();
  if (/(closed|resolved|approved|complete|acknowledged|published)/.test(s)) return 'success';
  if (/(open|new|active|in ?progress|pending|review)/.test(s)) return 'info';
  if (/(critical|high|blocked|rejected|failed|overdue)/.test(s)) return 'danger';
  if (/(medium|warning|draft|shared)/.test(s)) return 'warning';
  return 'neutral';
}

/** A blank panel with no explanation is a bug report waiting to happen. */
export function EmptyState({
  title,
  description,
  action,
}: {
  title: string;
  description?: string;
  action?: ReactNode;
}) {
  return (
    <div className="grid place-items-center gap-2 rounded-md border border-dashed border-border bg-surface-2 px-6 py-12 text-center">
      <p className="text-sm font-medium text-fg">{title}</p>
      {description && <p className="max-w-sm text-xs text-fg-muted">{description}</p>}
      {action}
    </div>
  );
}

/** Loading placeholder. `aria-hidden` because a screen reader should hear the
 *  live region's "Loading…", not a stack of empty boxes. */
export function Skeleton({ className }: { className?: string }) {
  return (
    <div
      aria-hidden="true"
      className={cn('relative overflow-hidden rounded bg-surface-3', className)}
    >
      <div className="absolute inset-0 -translate-x-full animate-shimmer bg-gradient-to-r from-transparent via-surface/60 to-transparent" />
    </div>
  );
}

export function SkeletonRows({ rows = 5, cols = 4 }: { rows?: number; cols?: number }) {
  return (
    <div className="flex flex-col gap-1.5 p-2" role="status" aria-label="Loading">
      {Array.from({ length: rows }).map((_, r) => (
        <div key={r} className="flex gap-2">
          {Array.from({ length: cols }).map((_, c) => (
            <Skeleton key={c} className={cn('h-6 flex-1', c === 0 && 'max-w-[8rem]')} />
          ))}
        </div>
      ))}
    </div>
  );
}

/** Sticky action bar above a grid: filters left, actions right. */
export function Toolbar({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <div
      className={cn(
        'flex flex-wrap items-center gap-2 rounded-t-md border border-b-0 border-border bg-surface-2 px-2 py-1.5',
        className,
      )}
    >
      {children}
    </div>
  );
}

export function ToolbarSpacer() {
  return <div className="ml-auto" />;
}

/** Page header — title, optional description, right-aligned actions. */
export function PageHeader({
  title,
  description,
  actions,
}: {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
      <div className="min-w-0">
        <h1 className="truncate text-xl font-semibold tracking-tight text-fg">{title}</h1>
        {description && <p className="mt-0.5 text-sm text-fg-muted">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </div>
  );
}

/** Surface card for non-grid content. */
export function Card({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('rounded-md border border-border bg-surface p-4', className)}>{children}</div>;
}

/** Inline error. Uses role="alert" so it is announced when it appears. */
export function ErrorNote({ children }: { children: ReactNode }) {
  return (
    <div role="alert" className="rounded border border-danger/40 bg-danger-subtle px-3 py-2 text-sm text-danger">
      {children}
    </div>
  );
}
