import AsyncStorage from '@react-native-async-storage/async-storage';
import { createIssue, updateIssue, transitionCDE } from '@/api/endpoints';
import type { OfflineAction } from '@/types/api';

const QUEUE_KEY = 'planscape_offline_queue';

/** Load all queued actions from storage. */
export async function loadQueue(): Promise<OfflineAction[]> {
  const raw = await AsyncStorage.getItem(QUEUE_KEY);
  if (!raw) return [];
  try {
    return JSON.parse(raw) as OfflineAction[];
  } catch {
    return [];
  }
}

/** Persist the full queue to storage. */
async function saveQueue(queue: OfflineAction[]): Promise<void> {
  await AsyncStorage.setItem(QUEUE_KEY, JSON.stringify(queue));
}

/** Enqueue an offline action. Returns the created action. */
export async function enqueue(
  type: OfflineAction['type'],
  payload: Record<string, unknown>
): Promise<OfflineAction> {
  const action: OfflineAction = {
    id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    type,
    payload,
    createdAt: new Date().toISOString(),
    synced: false,
  };

  const queue = await loadQueue();
  queue.push(action);
  await saveQueue(queue);
  return action;
}

/** Remove a single action by id. */
export async function removeAction(id: string): Promise<void> {
  const queue = await loadQueue();
  await saveQueue(queue.filter((a) => a.id !== id));
}

/** Return count of unsynced actions. */
export async function pendingCount(): Promise<number> {
  const queue = await loadQueue();
  return queue.filter((a) => !a.synced).length;
}

/**
 * Replay a single action through the live API.
 * Throws on network / server error so the caller can decide retry policy.
 */
async function replayAction(action: OfflineAction): Promise<void> {
  const p = action.payload;

  switch (action.type) {
    case 'CREATE_ISSUE':
      await createIssue(
        p.projectId as string,
        p.issue as Record<string, unknown>
      );
      break;

    case 'UPDATE_ISSUE':
      await updateIssue(
        p.projectId as string,
        p.issueId as string,
        p.updates as Record<string, unknown>
      );
      break;

    case 'TRANSITION_CDE':
      await transitionCDE(
        p.projectId as string,
        p.docId as string,
        p.newStatus as string
      );
      break;
  }
}

export interface SyncResult {
  total: number;
  succeeded: number;
  failed: number;
}

/**
 * Attempt to sync all unsynced actions in FIFO order.
 * Successfully synced actions are marked `synced: true`.
 * On first failure the sync stops (preserves ordering guarantees).
 */
export async function syncQueue(): Promise<SyncResult> {
  const queue = await loadQueue();
  const result: SyncResult = { total: 0, succeeded: 0, failed: 0 };

  const pending = queue.filter((a) => !a.synced);
  result.total = pending.length;

  for (const action of pending) {
    try {
      await replayAction(action);
      action.synced = true;
      result.succeeded++;
    } catch {
      result.failed++;
      break; // stop on first failure to preserve ordering
    }
  }

  // Persist updated sync flags, drop fully synced actions
  await saveQueue(queue.filter((a) => !a.synced));
  return result;
}

/** Clear all queued actions (synced and unsynced). */
export async function clearQueue(): Promise<void> {
  await AsyncStorage.removeItem(QUEUE_KEY);
}
