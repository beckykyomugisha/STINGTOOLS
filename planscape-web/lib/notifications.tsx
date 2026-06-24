'use client';

import { createContext, useContext, useEffect, useRef, useState, type ReactNode } from 'react';
import { HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr';
import { API_BASE, getToken } from './api';
import { useAuth } from './auth';

export interface AppNotification {
  id: string;
  channel?: string;
  title: string;
  message?: string;
  timestamp: string;
  read: boolean;
}

interface NotificationsState {
  notifications: AppNotification[];
  unread: number;
  markAllRead: () => void;
  clear: () => void;
}

const NotificationsContext = createContext<NotificationsState | null>(null);

const MAX_KEPT = 50;

/**
 * One persistent NotificationHub connection for the signed-in user. The hub
 * auto-joins the caller's `tenant_{id}` + `user_{id}` groups on connect, so a
 * plain connection (no JoinProject) receives tenant-wide `Notification` events.
 * Session-scoped: there's no REST inbox endpoint yet, so the list lives in
 * memory and resets on reload — it's a live feed, not a persisted history.
 */
export function NotificationsProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const [notifications, setNotifications] = useState<AppNotification[]>([]);
  const connRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    if (!user || !getToken()) return;

    const conn = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/notifications`, { accessTokenFactory: () => getToken() ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();
    connRef.current = conn;

    conn.on('Notification', (payload: unknown) => {
      const p = (payload ?? {}) as { channel?: string; title?: string; message?: string; timestamp?: string };
      const n: AppNotification = {
        id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
        channel: p.channel,
        title: p.title || 'Notification',
        message: p.message,
        timestamp: p.timestamp || new Date().toISOString(),
        read: false,
      };
      setNotifications((cur) => [n, ...cur].slice(0, MAX_KEPT));
    });

    conn.start().catch(() => {
      /* non-fatal — the bell just stays empty if the hub is unreachable */
    });

    return () => {
      connRef.current = null;
      void conn.stop().catch(() => {});
    };
  }, [user]);

  const unread = notifications.reduce((n, x) => (x.read ? n : n + 1), 0);

  const value: NotificationsState = {
    notifications,
    unread,
    markAllRead: () => setNotifications((cur) => cur.map((x) => ({ ...x, read: true }))),
    clear: () => setNotifications([]),
  };

  return <NotificationsContext.Provider value={value}>{children}</NotificationsContext.Provider>;
}

/** Safe even outside the provider (returns an inert state) so pages don't crash. */
export function useNotifications(): NotificationsState {
  return (
    useContext(NotificationsContext) ?? {
      notifications: [],
      unread: 0,
      markAllRead: () => {},
      clear: () => {},
    }
  );
}
