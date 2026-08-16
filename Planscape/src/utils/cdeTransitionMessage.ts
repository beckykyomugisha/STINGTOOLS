// Imported from the RN-free module, not from '@/api/client', so this file
// stays runnable outside a React Native runtime — see apiError.ts. Relative
// (not '@/...') because the verification harness transpiles this file with
// plain tsc, which does not rewrite path aliases.
import { ApiError } from '../api/apiError';

/**
 * Which server call was in flight when the failure came back.
 *
 * This distinction is the whole point of this module (#624). A refused CDE
 * transition and a refused *approval request* are different outcomes, and the
 * screen must not describe one as the other:
 *
 *   'transition'       — `POST .../transition`. No approval record was ever
 *                        attempted. If this is refused, nothing is pending.
 *   'approval-request' — `POST .../approval-request`. An approval record was
 *                        attempted and the server refused to create it. Also
 *                        nothing pending, but for a different reason.
 *
 * Do not collapse the two into one message "to keep the code tidy". Collapsing
 * them is the defect this module exists to remove.
 */
export type TransitionAttempt = 'transition' | 'approval-request';

export type TransitionFailure = {
  /**
   * 'forbidden' — the server refused on authority grounds (403).
   * 'error'     — anything else: unreachable, 4xx, 5xx.
   *
   * Kept separate so the caller can render a refusal differently from a
   * breakage. "Ask your project manager" and "call IT" are different actions.
   */
  kind: 'forbidden' | 'error';
  title: string;
  body: string;
};

/**
 * The reason the SERVER gave, or null when it gave none.
 *
 * Never guesses. `ApiError.message` is the raw response body (see
 * `api/client.ts` — `throw new ApiError(res.status, body || \`HTTP ${status}\`)`),
 * so it is one of:
 *
 *   - a JSON object, `{"message": "..."}` or `{"error": "..."}` — the two
 *     shapes every 403 in DocumentsController actually emits;
 *   - plain prose, e.g. `Transition WIP->SHARED does not require approval`;
 *   - the literal string `HTTP 403`, which is the *fallback for an empty body*
 *     and therefore means "no reason given", not "403".
 *
 * That last point is why the old `msg.includes('HTTP 403')` test was wrong on
 * its own terms: it fires only when the body is EMPTY, and misses every 403
 * that carries a reason. Read the status off ApiError; read the reason off the
 * body; never conflate the two.
 */
export function serverReason(err: unknown): string | null {
  if (!(err instanceof ApiError)) return null;

  const raw = (err.message ?? '').trim();
  if (!raw || raw === `HTTP ${err.status}`) return null;

  try {
    const parsed: unknown = JSON.parse(raw);
    if (parsed && typeof parsed === 'object') {
      const bag = parsed as Record<string, unknown>;
      for (const key of ['message', 'error', 'detail']) {
        const value = bag[key];
        if (typeof value === 'string' && value.trim()) return value.trim();
      }
      // Structured, but not a shape we recognise (e.g. an RFC 9110
      // ProblemDetails whose `title` is just "Not Found"). Better to say
      // nothing than to dump JSON at an operator on a site.
      return null;
    }
  } catch {
    // Not JSON — the body is already prose, use it verbatim.
  }
  return raw;
}

const NOTHING_SENT =
  'Nothing was submitted. This transition was refused outright, so there is no approval pending and nothing to wait for.';

const REQUEST_REFUSED =
  'Your approval request was refused, so no approval is pending.';

const NO_REASON_GIVEN = 'The server refused this action but gave no reason.';

/**
 * Turn a failed CDE-transition call into exactly what happened.
 *
 * The predecessor of this function told every 403 that "The request has been
 * sent — check back when it is approved", on both branches, when on neither
 * branch had a request been accepted. A user who reads that waits instead of
 * acting. Stating a false outcome is worse than stating an unhelpful one.
 */
export function describeTransitionFailure(
  err: unknown,
  attempt: TransitionAttempt,
): TransitionFailure {
  const reason = serverReason(err);
  const forbidden = err instanceof ApiError && err.status === 403;

  if (forbidden) {
    return attempt === 'approval-request'
      ? {
          kind: 'forbidden',
          title: 'Approval request refused',
          body: `${reason ?? NO_REASON_GIVEN}\n\n${REQUEST_REFUSED}`,
        }
      : {
          kind: 'forbidden',
          title: 'Not permitted',
          body: `${reason ?? NO_REASON_GIVEN}\n\n${NOTHING_SENT}`,
        };
  }

  const fallback =
    reason ??
    (err instanceof Error && err.message ? err.message : 'Transition failed');

  return attempt === 'approval-request'
    ? { kind: 'error', title: 'Approval request failed', body: fallback }
    : { kind: 'error', title: 'CDE Transition Failed', body: fallback };
}
