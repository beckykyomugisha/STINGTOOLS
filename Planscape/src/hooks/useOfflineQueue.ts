import { useState, useEffect, useCallback, useRef } from 'react';
import { AppState } from 'react-native';
import type { OfflineAction } from '@/types/api';
import {
  loadQueue,
  enqueue,
  syncQueue,
  pendingCount,
  clearQueue,
  removeAction,
  type SyncResult,
} from '@/utils/offlineQueue';

export function useOfflineQueue() {
  const [queue, setQueue] = useState<OfflineAction[]>([]);
  const [pending, setPending] = useState(0);
  const [syncing, setSyncing] = useState(false);
  const [lastResult, setLastResult] = useState<SyncResult | null>(null);
  const mountedRef = useRef(true);

  const refresh = useCallback(async () => {
    const items = await loadQueue();
    const count = await pendingCount();
    if (mountedRef.current) {
      setQueue(items);
      setPending(count);
    }
  }, []);

  // Load queue on mount
  useEffect(() => {
    mountedRef.current = true;
    refresh();
    return () => {
      mountedRef.current = false;
    };
  }, [refresh]);

  // Auto-sync when app comes to foreground
  useEffect(() => {
    const sub = AppState.addEventListener('change', (state) => {
      if (state === 'active') {
        refresh();
      }
    });
    return () => sub.remove();
  }, [refresh]);

  const add = useCallback(
    async (type: OfflineAction['type'], payload: Record<string, unknown>) => {
      const action = await enqueue(type, payload);
      await refresh();
      return action;
    },
    [refresh]
  );

  const sync = useCallback(async (): Promise<SyncResult> => {
    setSyncing(true);
    try {
      const result = await syncQueue();
      if (mountedRef.current) {
        setLastResult(result);
      }
      await refresh();
      return result;
    } finally {
      if (mountedRef.current) {
        setSyncing(false);
      }
    }
  }, [refresh]);

  const remove = useCallback(
    async (id: string) => {
      await removeAction(id);
      await refresh();
    },
    [refresh]
  );

  const clear = useCallback(async () => {
    await clearQueue();
    await refresh();
  }, [refresh]);

  return {
    queue,
    pending,
    syncing,
    lastResult,
    add,
    sync,
    remove,
    clear,
    refresh,
  };
}
