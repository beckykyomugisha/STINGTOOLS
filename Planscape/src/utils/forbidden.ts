import { Alert } from 'react-native';
import { ApiError } from '../api/client';

/**
 * The app's single forbidden treatment (#558).
 *
 * THE FOUR STATES. Every server-backed screen owes the user four distinct
 * answers: loading, empty, error, and FORBIDDEN. A refusal is a correct answer
 * to a legitimate request — rendering it as an error tells the user something
 * broke and sends them to IT, when what they need is to ask whoever holds the
 * role.
 *
 * WHY `isForbidden` READS `.status` AND NOT THE MESSAGE. Five screens tested
 * `msg.includes('HTTP 403')`. `ApiError` is constructed as
 *
 *     new ApiError(res.status, body || `HTTP ${res.status}`)     client.ts:159
 *
 * so `message` is the response BODY, and the `HTTP 403` string only ever
 * appears when the body was EMPTY. Those tests were empty-body tests, never
 * status tests: a 403 that carries a reason — which is the useful kind — fell
 * through to the generic failure branch. That is the #624 defect, and it was
 * latent in five more places than #624 covered. `.status` is a number and is
 * read as one.
 *
 * THE ROLE NAMES IN CAPABILITY_COPY ARE COPY, NOT A GATE. They tell the user
 * who to ask. Nothing in this file decides anything — the server decides.
 */

/** Is this the server REFUSING, as opposed to the request failing? */
export function isForbidden(e: unknown): boolean {
  return e instanceof ApiError && e.status === 403;
}

/**
 * Named capability sentences, written once.
 *
 * `project-settings` previously said "Only BIM Managers (role K) and
 * Coordinators (role C) can change admin settings" — ISO role LETTERS in
 * user-facing text, which is the derivation problem made visible. The copy
 * here names the capability and who holds it, not a code.
 */
export const CAPABILITY_COPY = {
  curateProject:
    'Only a project manager or BIM coordinator can curate albums, checklists and distribution groups.',
  approveSitePhotos:
    'Only a project manager can approve site photos, issue share links, or change the photo policy.',
  projectAdmin:
    'Only a project manager or BIM coordinator can change this project’s admin settings.',
  manageMembers:
    'Only a project manager, the project’s author, or a tenant Owner/Admin can manage this project’s members.',
} as const;

export type CapabilityCopyKey = keyof typeof CAPABILITY_COPY;

/**
 * The sentence to show for a caught value.
 *
 * When the server sent a reason, THAT WINS — the server owns the rule and a
 * client copy of it drifts. `capabilityCopy` is the fallback for the
 * empty-body 403s that `Forbid()` produces, where there is nothing else to
 * show.
 */
export function describeFailure(
  e: unknown,
  opts: { forbidden: string; fallback: string },
): { message: string; forbidden: boolean } {
  if (e instanceof ApiError && e.status === 403) {
    const body = e.message?.trim();
    // `HTTP 403` is our own placeholder for an empty body, not something the
    // server said. Never show it — that is a status masquerading as a reason.
    const useful = body && body !== `HTTP ${e.status}` ? body : undefined;
    return { message: useful ?? opts.forbidden, forbidden: true };
  }
  return { message: e instanceof Error ? e.message : opts.fallback, forbidden: false };
}

/**
 * Alert a failure with the right title. "Permission denied" for a refusal,
 * the caller's own title for anything else — so a user can tell at the title
 * whether to retry or to go and ask someone.
 */
export function alertFailure(
  e: unknown,
  opts: { title: string; forbidden: string; fallback: string },
): void {
  const d = describeFailure(e, { forbidden: opts.forbidden, fallback: opts.fallback });
  Alert.alert(d.forbidden ? 'Permission denied' : opts.title, d.message);
}
