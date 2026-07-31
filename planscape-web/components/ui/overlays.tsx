'use client';

import { type ReactNode } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import * as TabsPrimitive from '@radix-ui/react-tabs';
import { cn } from '@/lib/cn';

/**
 * U3 — Modal, Drawer and Tabs on Radix.
 *
 * These are the three places Radix genuinely earns a dependency: focus trapping,
 * restoring focus to the trigger on close, Escape and outside-click dismissal,
 * `aria-modal` + inert background, and roving-tabindex arrow-key navigation for
 * tabs. Re-deriving that by hand is exactly the accessibility work the findings
 * doc said not to re-derive.
 *
 * Radix requires an accessible title on every dialog; `description` is optional
 * but `Dialog.Description` is still rendered (visually hidden when absent) so
 * the console warning doesn't get normalised into background noise.
 */

function Overlay() {
  return <Dialog.Overlay className="fixed inset-0 z-40 animate-fade-in bg-black/50" />;
}

export function Modal({
  open,
  onOpenChange,
  title,
  description,
  children,
  footer,
  size = 'md',
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
  children: ReactNode;
  footer?: ReactNode;
  size?: 'sm' | 'md' | 'lg';
}) {
  const width = { sm: 'max-w-sm', md: 'max-w-lg', lg: 'max-w-2xl' }[size];
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Overlay />
        <Dialog.Content
          className={cn(
            'fixed left-1/2 top-1/2 z-50 w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 animate-zoom-in rounded-md border border-border bg-surface shadow-lg',
            width,
          )}
        >
          <div className="border-b border-border px-4 py-3">
            <Dialog.Title className="text-base font-semibold text-fg">{title}</Dialog.Title>
            <Dialog.Description className={cn('mt-0.5 text-xs text-fg-muted', !description && 'sr-only')}>
              {description || title}
            </Dialog.Description>
          </div>
          <div className="max-h-[70vh] overflow-y-auto px-4 py-3">{children}</div>
          {footer && <div className="flex justify-end gap-2 border-t border-border px-4 py-3">{footer}</div>}
          <Dialog.Close
            aria-label="Close"
            className="absolute right-3 top-3 rounded p-1 text-fg-subtle transition hover:bg-surface-3 hover:text-fg"
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="h-3.5 w-3.5">
              <path d="M18 6L6 18M6 6l12 12" strokeLinecap="round" />
            </svg>
          </Dialog.Close>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

/** Right-hand drawer — same Dialog machinery, different geometry. Used for
 *  record detail beside a grid, so the list keeps its scroll position. */
export function Drawer({
  open,
  onOpenChange,
  title,
  description,
  children,
  footer,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: string;
  children: ReactNode;
  footer?: ReactNode;
}) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Overlay />
        <Dialog.Content className="fixed inset-y-0 right-0 z-50 flex w-full max-w-md animate-slide-in-right flex-col border-l border-border bg-surface shadow-lg">
          <div className="border-b border-border px-4 py-3">
            <Dialog.Title className="text-base font-semibold text-fg">{title}</Dialog.Title>
            <Dialog.Description className={cn('mt-0.5 text-xs text-fg-muted', !description && 'sr-only')}>
              {description || title}
            </Dialog.Description>
          </div>
          <div className="flex-1 overflow-y-auto px-4 py-3">{children}</div>
          {footer && <div className="flex justify-end gap-2 border-t border-border px-4 py-3">{footer}</div>}
          <Dialog.Close
            aria-label="Close"
            className="absolute right-3 top-3 rounded p-1 text-fg-subtle transition hover:bg-surface-3 hover:text-fg"
          >
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="h-3.5 w-3.5">
              <path d="M18 6L6 18M6 6l12 12" strokeLinecap="round" />
            </svg>
          </Dialog.Close>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

export function Tabs({
  tabs,
  value,
  onValueChange,
  children,
}: {
  tabs: { value: string; label: ReactNode }[];
  value: string;
  onValueChange: (v: string) => void;
  children: ReactNode;
}) {
  return (
    <TabsPrimitive.Root value={value} onValueChange={onValueChange}>
      <TabsPrimitive.List className="flex gap-0.5 border-b border-border">
        {tabs.map((t) => (
          <TabsPrimitive.Trigger
            key={t.value}
            value={t.value}
            className="-mb-px border-b-2 border-transparent px-3 py-1.5 text-sm text-fg-muted transition hover:text-fg data-[state=active]:border-accent data-[state=active]:font-medium data-[state=active]:text-fg"
          >
            {t.label}
          </TabsPrimitive.Trigger>
        ))}
      </TabsPrimitive.List>
      {children}
    </TabsPrimitive.Root>
  );
}

export function TabPanel({ value, children }: { value: string; children: ReactNode }) {
  return (
    <TabsPrimitive.Content value={value} className="pt-4">
      {children}
    </TabsPrimitive.Content>
  );
}
