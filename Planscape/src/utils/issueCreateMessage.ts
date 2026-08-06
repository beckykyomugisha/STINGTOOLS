/**
 * What actually went wrong when creating an issue — keyed off the SERVER's
 * reason, never guessed from the HTTP status.
 *
 * The predecessor of this module tested `msg.includes('HTTP 403')` and
 * reported the result as "Outside project geofence — move on site…". Any 403,
 * for any reason, sent the user to physically walk somewhere else. Since #540
 * brought eleven previously-dead authorization gates to life, 403s that could
 * not be raised when that message was written are raised now.
 *
 * The rule: do not infer a cause from a status code when the body carries the
 * reason. That inference is what makes a message confidently wrong rather than
 * merely unhelpful.
 *
 * NOTE ON DUPLICATION. `serverReason` below is deliberately a near-twin of the
 * one in `cdeTransitionMessage.ts` (#624). The two fixes are separate bugs and
 * ship as separate PRs so each stays reviewable; #558 consolidates both into
 * one shared forbidden treatment per client and this copy goes away then.
 */

/**
 * The HTTP status, if the error carries one.
 *
 * Duck-typed rather than `err instanceof ApiError` so this module has no
 * import from `@/api/client` — that module imports `expo-secure-store` and
 * would make this logic impossible to execute outside a React Native runtime,
 * which is where it is verified. Any error object exposing a numeric `status`
 * is honoured; anything else yields null.
 */
function statusOf(err: unknown): number | null {
  const candidate = (err as { status?: unknown } | null | undefined)?.status;
  return typeof candidate === 'number' ? candidate : null;
}

/**
 * The reason the SERVER gave, or null when it gave none.
 *
 * `ApiError.message` is the raw response body, falling back to the literal
 * `HTTP <status>` only when that body is EMPTY (see `api/client.ts`). So a
 * `message` of "HTTP 403" means "no reason given", not "403" — which is why
 * the old `msg.includes('HTTP 403')` test was never a status test at all.
 */
export function serverReason(err: unknown): string | null {
  if (!(err instanceof Error)) return null;
  const status = statusOf(err);
  const raw = (err.message ?? '').trim();
  if (!raw) return null;
  if (status !== null && raw === `HTTP ${status}`) return null;
  // A gateway or WAF can answer with an HTML error page. That is not a reason,
  // and rendering markup into a text banner helps nobody.
  if (raw.startsWith('<')) return null;

  try {
    const parsed: unknown = JSON.parse(raw);
    if (parsed && typeof parsed === 'object') {
      const bag = parsed as Record<string, unknown>;
      for (const key of ['error', 'message', 'detail']) {
        const value = bag[key];
        if (typeof value === 'string' && value.trim()) return value.trim();
      }
      // Structured, but not a shape we recognise — say nothing rather than
      // dump JSON at someone standing on a site.
      return null;
    }
  } catch {
    // Not JSON — the body is already prose.
  }
  return raw;
}

export type IssueCreateFailure = {
  /**
   * 'geofence'  — the caller really is outside the project boundary.
   * 'forbidden' — refused on authority grounds. Nothing to do with location.
   * 'error'     — everything else: validation, unreachable, 5xx.
   *
   * Consumed by the harness today and by the shared forbidden treatment
   * (#558) when that lands; the screen currently renders `message` only.
   */
  kind: 'geofence' | 'forbidden' | 'error';
  message: string;
};

/**
 * Wording preserved verbatim from the original. It is correct for an actual
 * boundary violation, and this is the one case it should ever appear in.
 */
const GEOFENCE =
  'Outside project geofence — move on site or ask your BIM manager to widen the boundary.';

const NO_REASON_GIVEN =
  'The server refused this, but gave no reason. Ask your project manager whether you have permission to raise issues here.';

/** True only for a reason that describes being outside the boundary. */
function isBoundaryViolation(reason: string): boolean {
  const r = reason.toLowerCase();
  return r.includes('outside the project') || r.includes('outside the boundary');
}

export function describeIssueCreateFailure(err: unknown): IssueCreateFailure {
  const status = statusOf(err);
  const reason = serverReason(err);

  if (status === 403) {
    // Only a reason that actually says "outside the …" earns the geofence
    // wording. A 403 with no reason, or any other reason, is a refusal — and
    // telling someone to walk somewhere else would be a fabricated cause.
    if (reason && isBoundaryViolation(reason)) {
      return { kind: 'geofence', message: GEOFENCE };
    }
    return { kind: 'forbidden', message: reason ?? NO_REASON_GIVEN };
  }

  if (status === 400 && reason) {
    const r = reason.toLowerCase();
    // These two arms existed before but could never fire: they tested the
    // message for "HTTP 400", and the message is the response BODY, which
    // never contains that string. Keyed off the status now, so the friendlier
    // wording actually reaches the user.
    if (r.includes('latitude') && r.includes('range')) {
      return { kind: 'error', message: 'Invalid GPS reading — try again in a moment.' };
    }
    if (r.includes('assignee')) {
      return { kind: 'error', message: 'Chosen assignee is not a member of this project.' };
    }
    // Deliberately NOT matched by the geofence wording: a 400 saying
    // coordinates are *required* means none arrived, which is the opposite of
    // being outside a boundary. Report what the server said and do not invent
    // a remedy — "move on site" cannot help when no location was sent.
    return { kind: 'error', message: reason };
  }

  if (reason) return { kind: 'error', message: reason };
  return {
    kind: 'error',
    message: err instanceof Error && err.message ? err.message : 'Failed to create issue',
  };
}
