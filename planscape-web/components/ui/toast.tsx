'use client';

import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { cn } from '@/lib/cn';

/**
 * U3 — toasts. The grid contract makes these load-bearing rather than
 * decorative: an optimistic edit that fails rolls the cell back, and the toast
 * is the ONLY signal the user gets that their change did not stick. So it
 * defaults to an error tone that does not auto-dismiss, while a success
 * disappears on its own.
 */

export type ToastTone = 'success' | 'error' | 'info';

interface ToastItem {
  id: number;
  tone: ToastTone;
  message: string;
}

interface ToastApi {
  toast: (message: string, tone?: ToastTone) => void;
}

const ToastContext = createContext<ToastApi>({ toast: () => {} });

let nextId = 1;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);

  const dismiss = useCallback((id: number) => setItems((xs) => xs.filter((x) => x.id !== id)), []);

  const toast = useCallback(
    (message: string, tone: ToastTone = 'info') => {
      const id = nextId++;
      setItems((xs) => [...xs, { id, tone, message }]);
      // Errors persist until dismissed — a failed save that vanishes after four
      // seconds is how a user ends up believing a value saved when it didn't.
      if (tone !== 'error') setTimeout(() => dismiss(id), 4000);
    },
    [dismiss],
  );

  const api = useMemo(() => ({ toast }), [toast]);

  return (
    <ToastContext.Provider value={api}>
      {children}
      <div
        className="pointer-events-none fixed bottom-4 right-4 z-[60] flex w-80 flex-col gap-2"
        role="status"
        aria-live="polite"
      >
        {items.map((t) => (
          <div
            key={t.id}
            className={cn(
              'pointer-events-auto flex items-start gap-2 rounded-md border px-3 py-2 text-sm shadow-lg animate-fade-in',
              t.tone === 'success' && 'border-success/40 bg-success-subtle text-success',
              t.tone === 'error' && 'border-danger/40 bg-danger-subtle text-danger',
              t.tone === 'info' && 'border-border bg-surface text-fg',
            )}
          >
            <span className="flex-1">{t.message}</span>
            <button
              type="button"
              aria-label="Dismiss"
              onClick={() => dismiss(t.id)}
              className="rounded p-0.5 opacity-60 transition hover:opacity-100"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="h-3 w-3">
                <path d="M18 6L6 18M6 6l12 12" strokeLinecap="round" />
              </svg>
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast(): ToastApi {
  return useContext(ToastContext);
}
