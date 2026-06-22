'use client';

import { useEffect, useRef, useState } from 'react';
import { useNotifications } from '@/lib/notifications';

/** Header bell: unread badge + a dropdown of the session's live notifications. */
export function NotificationBell() {
  const { notifications, unread, markAllRead, clear } = useNotifications();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  // Close on outside click.
  useEffect(() => {
    if (!open) return;
    function onDoc(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener('mousedown', onDoc);
    return () => document.removeEventListener('mousedown', onDoc);
  }, [open]);

  function toggle() {
    const next = !open;
    setOpen(next);
    if (next && unread > 0) markAllRead();
  }

  return (
    <div ref={ref} className="relative">
      <button
        onClick={toggle}
        aria-label={`Notifications${unread > 0 ? ` (${unread} unread)` : ''}`}
        className="relative rounded border border-slate-300 px-2 py-1 transition hover:bg-slate-50"
      >
        <span aria-hidden>🔔</span>
        {unread > 0 && (
          <span className="absolute -right-1 -top-1 grid h-4 min-w-4 place-items-center rounded-full bg-red-600 px-1 text-[10px] font-semibold text-white">
            {unread > 9 ? '9+' : unread}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute right-0 z-20 mt-2 w-80 overflow-hidden rounded-lg border border-slate-200 bg-white shadow-lg">
          <div className="flex items-center justify-between border-b border-slate-100 px-3 py-2">
            <span className="text-sm font-medium">Notifications</span>
            {notifications.length > 0 && (
              <button onClick={clear} className="text-xs text-slate-400 hover:underline">
                Clear
              </button>
            )}
          </div>
          {notifications.length === 0 ? (
            <p className="px-3 py-6 text-center text-sm text-slate-400">No notifications yet.</p>
          ) : (
            <ul className="max-h-96 divide-y divide-slate-100 overflow-y-auto">
              {notifications.map((n) => (
                <li key={n.id} className="px-3 py-2">
                  <div className="flex items-baseline justify-between gap-2">
                    <span className="text-sm font-medium text-slate-800">{n.title}</span>
                    <time className="shrink-0 text-[10px] text-slate-400" dateTime={n.timestamp}>
                      {new Date(n.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </time>
                  </div>
                  {n.message && <p className="mt-0.5 text-xs text-slate-500">{n.message}</p>}
                  {n.channel && (
                    <span className="mt-1 inline-block rounded bg-slate-100 px-1.5 py-0.5 text-[10px] uppercase tracking-wide text-slate-500">
                      {n.channel}
                    </span>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
