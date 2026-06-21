'use client';

import { useEffect, useRef } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { API_BASE, getToken } from './api';

export type RealtimeHandler = (event: string, payload: unknown) => void;

// NotificationHub events the web app reacts to (mirrors the mobile client).
const EVENTS = [
  'IssueCreated',
  'IssueUpdated',
  'IssueDeleted',
  'IssueCommentAdded',
  'CommentAdded',
  'ComplianceChanged',
  'ComplianceSnapshotAdded',
  'Notification',
  'ClashUpdated',
];

/**
 * Live updates for a project via the NotificationHub. Connects with the bearer
 * token (SignalR appends it as access_token), joins the project group, and calls
 * `onEvent(event, payload)` for each relevant broadcast. Best-effort: if the hub
 * is unreachable the UI still works via manual refresh.
 */
export function useProjectRealtime(projectId: string | undefined, onEvent: RealtimeHandler): void {
  const handlerRef = useRef(onEvent);
  handlerRef.current = onEvent;

  useEffect(() => {
    if (!projectId || !getToken()) return;

    const conn = new HubConnectionBuilder()
      .withUrl(`${API_BASE}/hubs/notifications`, { accessTokenFactory: () => getToken() ?? '' })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    for (const e of EVENTS) {
      conn.on(e, (payload: unknown) => handlerRef.current(e, payload));
    }

    let joined = false;
    let disposed = false;
    conn
      .start()
      .then(() => conn.invoke('JoinProject', projectId))
      .then(() => {
        joined = true;
      })
      .catch(() => {
        /* non-fatal — UI degrades to manual refresh */
      });

    return () => {
      disposed = true;
      void (async () => {
        try {
          if (joined) await conn.invoke('LeaveProject').catch(() => {});
        } finally {
          await conn.stop().catch(() => {});
        }
      })();
      void disposed;
    };
  }, [projectId]);
}
