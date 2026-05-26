import type { OfflineAction } from '@/types/api';

/**
 * N-G17 conflict resolver: policies for handling HTTP 409 responses from
 * the server during offline-queue drain.
 *
 * Server-wins: drop local changes, refetch the server record, invalidate caches.
 *   Safest default for records the coordinator should own (issue status
 *   transitions, CDE state changes, revision stamps).
 *
 * Client-wins: retry with a "force" flag on the payload. Useful when the user
 *   genuinely has the latest context (e.g. on-site photo uploads of work that
 *   superseded a desk-side edit).
 *
 * Merge-fields: for partial updates, keep server values on fields the server
 *   has newer timestamps for, and overwrite fields the client last edited.
 *   Requires per-field `lastEditedAt` metadata — used for issue description
 *   and notes updates where collaborators commonly both edit.
 *
 * In a real deployment the policy is chosen per-action-type via the map below,
 * and the handler is wired into offlineQueue.syncQueue's 409 path. This module
 * exports both the policies and a pure classifier so tests can exercise them
 * without network mocks.
 */

export type ConflictPolicy = 'server-wins' | 'client-wins' | 'merge-fields';

export const DEFAULT_POLICIES: Record<OfflineAction['type'], ConflictPolicy> = {
  CREATE_ISSUE:    'client-wins',    // Idempotency handles duplicate-create.
  UPDATE_ISSUE:    'merge-fields',   // Coordinators often race on notes/status.
  TRANSITION_CDE:  'server-wins',    // CDE state machine is authoritative.
  ATTACH_PHOTO:    'client-wins',    // On-site photo supersedes desk edit.
  POST_COMMENT:           'client-wins',   // Comments append; never lose one.
  PIN_PLACE:              'client-wins',
  PIN_DELETE:             'client-wins',
  ADD_MEETING_ACTION:     'client-wins',
  UPDATE_MEETING_ACTION:  'merge-fields',
  DIARY_ENTRY:            'client-wins',
  STAGE_SIGNOFF:          'server-wins',   // Stage gate is authoritative.
  ATTACH_AUDIO:           'client-wins',
  ATTACH_MARKUP:          'client-wins',
  CAPTURE_SITE_PHOTO:     'client-wins',
  CREATE_DELIVERABLE:     'client-wins',
  UPDATE_DELIVERABLE:     'merge-fields',
  TRANSITION_DELIVERABLE: 'server-wins',
  HC_MGAS_VERIFICATION:   'client-wins',
  HC_PRESSURE_LOG:        'client-wins',
  HC_ANTI_LIGATURE_AUDIT: 'client-wins',
};

export interface ConflictContext {
  action: OfflineAction;
  /** Raw 409 body from server (if any) — usually includes the current record. */
  serverState?: Record<string, unknown>;
  /** ISO timestamp when the local edit happened (offlineQueue.createdAt). */
  clientEditedAt?: string;
}

export interface ResolutionPlan {
  /** What the drain should do next with the action. */
  outcome: 'retry-force' | 'drop' | 'requeue-merged' | 'move-to-failed';
  /** Optional merged payload when outcome is 'requeue-merged'. */
  mergedPayload?: Record<string, unknown>;
  /** Human-readable reason for the audit log. */
  reason: string;
}

/**
 * Resolve a conflict according to the configured policy.
 * Pure function — no network, no storage.
 */
export function resolve(ctx: ConflictContext, policy?: ConflictPolicy): ResolutionPlan {
  const p = policy ?? DEFAULT_POLICIES[ctx.action.type] ?? 'server-wins';

  if (p === 'server-wins') {
    return {
      outcome: 'drop',
      reason: 'Server-wins policy: dropping local change; UI will show server state on next refresh.',
    };
  }

  if (p === 'client-wins') {
    const next = { ...(ctx.action.payload ?? {}), force: true };
    return {
      outcome: 'retry-force',
      mergedPayload: next,
      reason: 'Client-wins policy: replaying with force=true.',
    };
  }

  // merge-fields: keep server values, overwrite only fields the client edited
  // more recently than the server's record lastEditedAt.
  const localPayload = extractUpdates(ctx.action);
  const server = ctx.serverState ?? {};
  const serverEditedAt = (server['lastEditedAt'] as string | undefined);
  const clientEditedAt = ctx.clientEditedAt;

  const merged: Record<string, unknown> = { ...server };
  if (clientEditedAt && serverEditedAt && Date.parse(clientEditedAt) > Date.parse(serverEditedAt)) {
    // Client edit is newer — overlay the client fields on the server doc.
    Object.assign(merged, localPayload);
    return {
      outcome: 'requeue-merged',
      mergedPayload: merged,
      reason: `Merge-fields: client edit ${clientEditedAt} > server ${serverEditedAt}; client fields win.`,
    };
  }

  return {
    outcome: 'drop',
    reason: 'Merge-fields: server edit is newer; dropping local change.',
  };
}

/** Extract the update payload from an offline action, if any. */
function extractUpdates(action: OfflineAction): Record<string, unknown> {
  const p = action.payload ?? {};
  if (action.type === 'UPDATE_ISSUE') return (p.updates as Record<string, unknown>) ?? {};
  if (action.type === 'CREATE_ISSUE') return (p.issue as Record<string, unknown>) ?? {};
  return {};
}
