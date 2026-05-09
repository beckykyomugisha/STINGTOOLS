import AsyncStorage from '@react-native-async-storage/async-storage';
import * as FileSystem from 'expo-file-system/legacy';
import { createIssue, updateIssue, transitionCDE, uploadIssueAttachment, captureSitePhoto } from '@/api/endpoints';
import type { OfflineAction, SitePhotoCaptureMeta } from '@/types/api';

const QUEUE_KEY = 'planscape_offline_queue';
const LAST_SYNC_KEY = 'planscape_last_sync';
const FAILED_KEY = 'planscape_offline_failed';

/**
 * Phase 96 — offline queue now stamps every action with an `idempotencyKey`
 * (UUID-ish) sent as `X-Idempotency-Key` / body `idempotencyKey` so the server
 * can dedup replays safely. Failed actions move to a side-queue with retry
 * metadata instead of being dropped or blocking the whole FIFO. Subscribers
 * can listen for sync-completion broadcasts via `onSyncComplete()` so screens
 * (issue-detail gallery, documents list) refresh the moment queued work lands.
 */

type SyncListener = (result: SyncResult) => void;
const _listeners = new Set<SyncListener>();

/** Subscribe to sync completion events. Returns an unsubscribe function. */
export function onSyncComplete(listener: SyncListener): () => void {
  _listeners.add(listener);
  return () => { _listeners.delete(listener); };
}

function emitSyncComplete(result: SyncResult): void {
  for (const l of Array.from(_listeners)) {
    try { l(result); } catch { /* one bad listener doesn't poison the rest */ }
  }
}

/** NEW-INFO-08 — Persist the last successful drain timestamp. */
export async function getLastSyncAt(): Promise<Date | null> {
  const raw = await AsyncStorage.getItem(LAST_SYNC_KEY);
  if (!raw) return null;
  const parsed = new Date(raw);
  return isNaN(parsed.getTime()) ? null : parsed;
}
async function markSyncedNow(): Promise<void> {
  await AsyncStorage.setItem(LAST_SYNC_KEY, new Date().toISOString());
}

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

/** Load the failed-actions side-queue. */
export async function loadFailedQueue(): Promise<OfflineAction[]> {
  const raw = await AsyncStorage.getItem(FAILED_KEY);
  if (!raw) return [];
  try { return JSON.parse(raw) as OfflineAction[]; } catch { return []; }
}

async function saveFailedQueue(queue: OfflineAction[]): Promise<void> {
  await AsyncStorage.setItem(FAILED_KEY, JSON.stringify(queue));
}

/** Requeue a failed action for another drain attempt. */
export async function retryFailedAction(id: string): Promise<void> {
  const failed = await loadFailedQueue();
  const target = failed.find(a => a.id === id);
  if (!target) return;
  target.synced = false;
  target.retryCount = 0;
  target.lastError = undefined;
  const live = await loadQueue();
  live.push(target);
  await saveQueue(live);
  await saveFailedQueue(failed.filter(a => a.id !== id));
}

/** Drop a failed action permanently. */
export async function discardFailedAction(id: string): Promise<void> {
  const failed = await loadFailedQueue();
  await saveFailedQueue(failed.filter(a => a.id !== id));
}

function makeIdempotencyKey(): string {
  // RFC 4122-ish — not cryptographic, just unique enough to dedup server-side.
  const rand = () => Math.random().toString(16).slice(2, 10);
  return `${Date.now().toString(16)}-${rand()}-${rand()}`;
}

/** Enqueue an offline action. Returns the created action. */
export async function enqueue(
  type: OfflineAction['type'],
  payload: Record<string, unknown>
): Promise<OfflineAction> {
  const action: OfflineAction = {
    id: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    type,
    payload: { ...payload, idempotencyKey: payload.idempotencyKey ?? makeIdempotencyKey() },
    createdAt: new Date().toISOString(),
    synced: false,
    retryCount: 0,
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

/** Return count of permanently-failed actions (moved to side-queue). */
export async function failedCount(): Promise<number> {
  const failed = await loadFailedQueue();
  return failed.length;
}

/**
 * Replay a single action through the live API.
 * Throws on network / server error so the caller can decide retry policy.
 *
 * Idempotency: every payload carries an `idempotencyKey` that the server
 * inspects to dedup replays. When the network drops mid-write (server succeeded
 * but client never saw the 2xx), the next replay hits the same key and the
 * server returns the original record instead of double-creating.
 */
async function replayAction(action: OfflineAction): Promise<void> {
  const p = action.payload;

  switch (action.type) {
    case 'CREATE_ISSUE':
      await createIssue(
        p.projectId as string,
        { ...(p.issue as Record<string, unknown>), idempotencyKey: p.idempotencyKey }
      );
      break;

    case 'UPDATE_ISSUE':
      await updateIssue(
        p.projectId as string,
        p.issueId as string,
        { ...(p.updates as Record<string, unknown>), idempotencyKey: p.idempotencyKey }
      );
      break;

    case 'TRANSITION_CDE':
      await transitionCDE(
        p.projectId as string,
        p.docId as string,
        p.newStatus as string
      );
      break;

    // Phase 94 — replay a queued photo upload as multipart/form-data.
    // uploadIssueAttachment wraps the React Native FormData file object
    // { uri, name, type } and POSTs to the server's /attachments endpoint.
    case 'ATTACH_PHOTO':
      await uploadIssueAttachment({
        projectId: p.projectId as string,
        issueId: p.issueId as string,
        uri: p.localUri as string,
        fileName: (p.fileName as string) ?? `photo-${Date.now()}.jpg`,
        contentType: (p.mimeType as string) ?? 'image/jpeg',
        latitude: p.latitude as number | undefined,
        longitude: p.longitude as number | undefined,
        idempotencyKey: p.idempotencyKey as string | undefined,
      });
      break;

    // S3.7 — extended replay handlers. Each one delegates to its endpoint
    // module; the modules add the `idempotencyKey` header on every call so
    // a retried action can't double-apply.
    case 'POST_COMMENT': {
      const { postIssueComment } = await import('@/api/endpoints');
      await postIssueComment(
        p.projectId as string,
        p.issueId as string,
        { body: p.body as string, idempotencyKey: p.idempotencyKey as string },
      );
      break;
    }
    case 'PIN_PLACE': {
      const { placeIssuePin } = await import('@/api/endpoints');
      await placeIssuePin(p.projectId as string, p.issueId as string, {
        modelId: p.modelId as string,
        x: p.x as number, y: p.y as number, z: p.z as number,
        elementGuid: p.elementGuid as string | undefined,
        idempotencyKey: p.idempotencyKey as string,
      });
      break;
    }
    case 'PIN_DELETE': {
      const { deleteIssuePin } = await import('@/api/endpoints');
      await deleteIssuePin(p.projectId as string, p.issueId as string, p.pinId as string);
      break;
    }
    case 'ADD_MEETING_ACTION': {
      const { addMeetingAction } = await import('@/api/endpoints');
      await addMeetingAction(p.projectId as string, p.meetingId as string, {
        ...(p.action as Record<string, unknown>),
        idempotencyKey: p.idempotencyKey as string,
      });
      break;
    }
    case 'UPDATE_MEETING_ACTION': {
      const { updateMeetingAction } = await import('@/api/endpoints');
      await updateMeetingAction(
        p.projectId as string, p.meetingId as string, p.actionId as string,
        p.updates as Record<string, unknown>,
      );
      break;
    }
    case 'DIARY_ENTRY': {
      const { upsertDiaryEntry } = await import('@/api/endpoints');
      await upsertDiaryEntry(p.projectId as string, {
        ...(p.entry as Record<string, unknown>),
        idempotencyKey: p.idempotencyKey as string,
      });
      break;
    }
    case 'STAGE_SIGNOFF': {
      const { signOffStageCriterion } = await import('@/api/endpoints');
      await signOffStageCriterion(
        p.projectId as string, p.gateId as string, p.criterionId as string,
        { decision: p.decision as string, comment: p.comment as string | undefined },
      );
      break;
    }
    case 'ATTACH_AUDIO': {
      const { uploadAudioNote } = await import('@/api/endpoints');
      await uploadAudioNote({
        projectId: p.projectId as string,
        issueId: p.issueId as string,
        uri: p.localUri as string,
        fileName: (p.fileName as string) ?? `voice-${Date.now()}.m4a`,
        contentType: (p.mimeType as string) ?? 'audio/mp4',
        durationSec: p.durationSec as number | undefined,
        idempotencyKey: p.idempotencyKey as string | undefined,
      });
      break;
    }
    case 'ATTACH_MARKUP': {
      const { uploadModelMarkup } = await import('@/api/endpoints');
      await uploadModelMarkup({
        projectId: p.projectId as string,
        modelId: p.modelId as string,
        polylines: p.polylines as Array<{ points: number[][]; color: string; thickness: number }>,
        label: p.label as string | undefined,
        idempotencyKey: p.idempotencyKey as string | undefined,
      });
      break;
    }

    // Phase 178 — replay a queued site-photo capture. The photo bytes
    // live on disk under FileSystem.documentDirectory + queued-photos/
    // so AsyncStorage doesn't bloat with base64. On success we delete
    // the local file; on permanent failure (4xx) the file is dropped
    // when the action moves to the failed side-queue (see drainFailedFiles).
    case 'CAPTURE_SITE_PHOTO': {
      const localUri = p.localUri as string;
      const fileName = (p.fileName as string) ?? `photo-${Date.now()}.jpg`;
      const contentType = (p.mimeType as string) ?? 'image/jpeg';
      const meta = p.meta as SitePhotoCaptureMeta;
      await captureSitePhoto({
        projectId: p.projectId as string,
        uri: localUri,
        fileName,
        contentType,
        meta: { ...meta, queuedClient: true },
      });
      // Best-effort cleanup — failure to delete the local copy is
      // non-fatal; drainQueuedPhotoFiles() reaps stragglers later.
      try { await FileSystem.deleteAsync(localUri, { idempotent: true }); } catch { /* ignore */ }
      break;
    }

    // T3-17 — deliverable CRUD + transition.
    case 'CREATE_DELIVERABLE': {
      const { createDeliverable } = await import('@/api/endpoints');
      await createDeliverable(
        p.projectId as string,
        p.body as Record<string, unknown>,
      );
      break;
    }
    case 'UPDATE_DELIVERABLE': {
      const { updateDeliverable } = await import('@/api/endpoints');
      await updateDeliverable(
        p.projectId as string,
        p.deliverableId as string,
        p.body as Record<string, unknown>,
      );
      break;
    }
    case 'TRANSITION_DELIVERABLE': {
      const { transitionDeliverable } = await import('@/api/endpoints');
      await transitionDeliverable(
        p.projectId as string,
        p.deliverableId as string,
        p.newStatus as string,
        { documentId: p.documentId as string | undefined, reason: p.reason as string | undefined },
      );
      break;
    }
  }
}

// ── Queued photo file management (Phase 178) ────────────────────────────
// Photos are persisted to FileSystem.documentDirectory + queued-photos/
// so we don't blow out AsyncStorage with base64 strings. The capture flow
// calls `persistPhotoForQueue` to copy the camera URI into a stable path
// before enqueuing the action; the replay handler calls
// `FileSystem.deleteAsync` on success.

const QUEUED_PHOTO_DIR = `${FileSystem.documentDirectory ?? ''}queued-photos/`;
const QUEUED_PHOTO_WARN_AT = 200;
const QUEUED_PHOTO_HARD_CAP = 500;

/** Make sure the queued-photos directory exists. Idempotent. */
export async function ensureQueuedPhotoDir(): Promise<string> {
  if (!FileSystem.documentDirectory) throw new Error('No document directory on this platform');
  const info = await FileSystem.getInfoAsync(QUEUED_PHOTO_DIR);
  if (!info.exists) {
    await FileSystem.makeDirectoryAsync(QUEUED_PHOTO_DIR, { intermediates: true });
  }
  return QUEUED_PHOTO_DIR;
}

/** Copy a camera/picker URI into a stable path under queued-photos/.
 *  Returns the on-disk URI; caller stores it on the OfflineAction payload. */
export async function persistPhotoForQueue(srcUri: string, suffix = '.jpg'): Promise<string> {
  const dir = await ensureQueuedPhotoDir();
  const id = `${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
  const dest = `${dir}${id}${suffix}`;
  await FileSystem.copyAsync({ from: srcUri, to: dest });
  return dest;
}

export interface QueuedPhotoStats {
  count: number;
  bytes: number;
  warn: boolean;
  hardCap: boolean;
}

/** Cheap stats for the capture flow's "you have N queued, Wi-Fi recommended" hint. */
export async function queuedPhotoStats(): Promise<QueuedPhotoStats> {
  try {
    if (!FileSystem.documentDirectory) return { count: 0, bytes: 0, warn: false, hardCap: false };
    const info = await FileSystem.getInfoAsync(QUEUED_PHOTO_DIR);
    if (!info.exists) return { count: 0, bytes: 0, warn: false, hardCap: false };
    const files = await FileSystem.readDirectoryAsync(QUEUED_PHOTO_DIR);
    let bytes = 0;
    for (const f of files) {
      const fi = await FileSystem.getInfoAsync(`${QUEUED_PHOTO_DIR}${f}`);
      if (fi.exists && !fi.isDirectory) bytes += (fi.size ?? 0);
    }
    return {
      count: files.length,
      bytes,
      warn: files.length >= QUEUED_PHOTO_WARN_AT,
      hardCap: files.length >= QUEUED_PHOTO_HARD_CAP,
    };
  } catch {
    return { count: 0, bytes: 0, warn: false, hardCap: false };
  }
}

/** Reap orphan files: any file under queued-photos/ NOT referenced by an
 *  action in the live or failed queue. Called opportunistically after a
 *  drain; prevents orphans from accumulating after a permanent failure. */
export async function reapOrphanQueuedPhotos(): Promise<number> {
  try {
    if (!FileSystem.documentDirectory) return 0;
    const info = await FileSystem.getInfoAsync(QUEUED_PHOTO_DIR);
    if (!info.exists) return 0;
    const files = await FileSystem.readDirectoryAsync(QUEUED_PHOTO_DIR);
    if (files.length === 0) return 0;
    const live = await loadQueue();
    const failed = await loadFailedQueue();
    const referenced = new Set<string>();
    for (const a of [...live, ...failed]) {
      if (a.type === 'CAPTURE_SITE_PHOTO') {
        const uri = a.payload?.localUri as string | undefined;
        if (uri) referenced.add(uri);
      }
    }
    let reaped = 0;
    for (const f of files) {
      const full = `${QUEUED_PHOTO_DIR}${f}`;
      if (!referenced.has(full)) {
        try { await FileSystem.deleteAsync(full, { idempotent: true }); reaped++; } catch { /* ignore */ }
      }
    }
    return reaped;
  } catch {
    return 0;
  }
}

export interface SyncResult {
  total: number;
  succeeded: number;
  failed: number;
  moved: number;
  conflicts: number;
}

const MAX_RETRIES_PER_ACTION = 3;

/**
 * N-G17 — compute exponential-backoff delay in milliseconds.
 * Sequence: 2s → 4s → 8s → 16s (capped), with ±20% jitter so reconnect
 * storms across many devices don't hammer the server simultaneously.
 */
function backoffMs(retryCount: number): number {
  const base = Math.min(2_000 * Math.pow(2, Math.max(0, retryCount - 1)), 16_000);
  const jitter = 1 + (Math.random() * 0.4 - 0.2);
  return Math.round(base * jitter);
}

const MAX_RETRIES_PER_ACTION = 3;

/**
 * Attempt to sync all unsynced actions in FIFO order.
 * Phase 96 — each action gets up to MAX_RETRIES_PER_ACTION shots in the live
 * queue. On transient failure (network) we stop the drain but keep the action
 * in the live queue for the next sync. On repeated failure or permanent error
 * (400/403 — invalid payload, forbidden) we MOVE the action to the failed
 * side-queue so the next drain isn't blocked forever by a poison-pill item.
 *
 * Successfully synced actions are dropped from storage.
 * Sync broadcasts to `onSyncComplete` subscribers so dependent screens refresh.
 */
export async function syncQueue(): Promise<SyncResult> {
  const queue = await loadQueue();
  const failed = await loadFailedQueue();
  const result: SyncResult = { total: 0, succeeded: 0, failed: 0, moved: 0, conflicts: 0 };

  // N-G17 — honour the exponential-backoff gate. Actions whose nextRetryAt
  // is still in the future are skipped this drain and retried later.
  const nowMs = Date.now();
  const pending = queue.filter((a) => !a.synced && (a.nextRetryAt == null || a.nextRetryAt <= nowMs));
  result.total = pending.length;

  for (let i = 0; i < pending.length; i++) {
    const action = pending[i];
    try {
      await replayAction(action);
      action.synced = true;
      action.nextRetryAt = undefined;
      result.succeeded++;
    } catch (err) {
      const msg = err instanceof Error ? err.message : String(err);
      action.retryCount = (action.retryCount ?? 0) + 1;
      action.lastError = msg;
      action.nextRetryAt = Date.now() + backoffMs(action.retryCount);

      // Permanent errors — move to failed side-queue immediately rather than
      // retry. 4xx (except 408/429) indicate the client payload is the problem
      // so retrying will never succeed.
      const isPermanent = /HTTP 4(0[0-79]|1[013-9]|2[013-9])/.test(msg)
        || /HTTP 40[0137]/.test(msg); // 400, 401, 403, 407
      const isConflict = /HTTP 409/.test(msg);
      if (isConflict) result.conflicts++;

      if (isPermanent || (action.retryCount >= MAX_RETRIES_PER_ACTION)) {
        failed.push(action);
        action.synced = true; // tombstone so it gets dropped from live queue
        result.moved++;
      }
      result.failed++;
      // Stop the drain on the first failure to preserve FIFO ordering within
      // the live queue. The poison-pill action has already been moved to the
      // failed side-queue above, so the next drain won't get blocked by it.
      break;
    }
  }

  // Persist updated sync flags, drop fully synced actions (including tombstones).
  await saveQueue(queue.filter((a) => !a.synced));
  await saveFailedQueue(failed);
  if (result.succeeded > 0 || result.total === 0) {
    await markSyncedNow();
  }
  emitSyncComplete(result);
  return result;
}

/** Clear all queued actions (synced and unsynced). */
export async function clearQueue(): Promise<void> {
  await AsyncStorage.removeItem(QUEUE_KEY);
}

/** Clear the failed side-queue — BIM coordinator should review first. */
export async function clearFailedQueue(): Promise<void> {
  await AsyncStorage.removeItem(FAILED_KEY);
}
