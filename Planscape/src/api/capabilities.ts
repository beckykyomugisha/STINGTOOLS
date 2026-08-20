import { ApiError, apiFetch } from './client';

/**
 * Project capabilities, resolved server-side (#547) and consumed here (#558).
 *
 * WHY THIS EXISTS. Two screens in this app derived authority from
 * `projectRole` by hand — `APPROVER_ROLES.has(role)` in site-photos/review,
 * and a role-name list in issue-detail. Three clients each keeping their own
 * copy of the server's rules is how the eleven dead `ProjectRole == "PM"`
 * gates happened, and mobile's copy had already drifted: `APPROVER_ROLES` is
 * `{PM, Admin, Owner}`, but "PM" is not a ProjectRole at all — it lives in
 * `Iso19650Role`. A project Manager or Coordinator, whom the server permits,
 * failed mobile's own gate.
 *
 * THREE STATES, NOT TWO
 * ---------------------
 *   'allowed'  the server said true            offer the control
 *   'denied'   the server said false, or 404   disable it, name the capability
 *   'unknown'  no answer at all — transport    LEAVE IT ENABLED and let the
 *              failure, timeout, 5xx, or a     attempt report
 *              body that will not parse
 *
 * Unknown is deliberately NOT rendered as denied. Capabilities drive
 * AFFORDANCE; the server remains the gate. On a phone this matters more than
 * anywhere else — the network drops constantly — and both screens above were
 * failing CLOSED, so a tunnel or a lift locked a legitimate reviewer out of
 * the review screen entirely and told them it was a permissions problem.
 *
 * A 404 IS authoritative-false: it says the caller cannot see the project at
 * all. A dropped connection says nothing about permissions. See #634.
 */
export type CapabilityState = 'allowed' | 'denied' | 'unknown';

export interface ProjectCapabilities {
  /** Albums, checklists, distribution groups. */
  curateProject: CapabilityState;
  /** Photo approve / reject, share-link issuance, photo policy. */
  approveSitePhotos: CapabilityState;
  /**
   * Project-level settings — ISO naming enforcement, the deliverable state
   * machine, the preferences blob. Proposed in #666 before being written, the
   * same propose-first step the first two took.
   *
   * Replaces a fourth client-side copy of the rule: project-settings/index.tsx
   * tested projectRole against {Admin, Owner, PM, BIM_Manager, BIMManager}, of
   * which only Admin and Owner are ProjectRoles. Its own gate had drifted the
   * same way the two this module already documents had.
   */
  administerProject: CapabilityState;
}

export const UNKNOWN_CAPABILITIES: ProjectCapabilities = {
  curateProject: 'unknown',
  approveSitePhotos: 'unknown',
  administerProject: 'unknown',
};

const ALL_DENIED: ProjectCapabilities = {
  curateProject: 'denied',
  approveSitePhotos: 'denied',
  administerProject: 'denied',
};

/**
 * Read only an EXPLICIT boolean. A missing field, a null, or the string
 * `"true"` is a contract mismatch, not a "no" — coercing it would silently
 * grant a capability and reading it as false would silently remove one.
 * Neither is an answer we were given.
 */
function flag(v: unknown): CapabilityState {
  if (v === true) return 'allowed';
  if (v === false) return 'denied';
  return 'unknown';
}

/**
 * `GET /api/projects/{id}/members/capabilities`.
 * Never throws — every failure mode yields all-`unknown`, which hides nothing.
 */
export async function getProjectCapabilities(projectId: string): Promise<ProjectCapabilities> {
  try {
    const raw = await apiFetch<Record<string, unknown>>(
      `/api/projects/${projectId}/members/capabilities`,
    );
    return {
      curateProject: flag(raw?.canCurateProject),
      approveSitePhotos: flag(raw?.canApproveSitePhotos),
      // A server that predates the field returns undefined here, which flag()
      // reads as 'unknown' — NOT denied. That is the correct answer during a
      // rollout: the old server has not refused, it was never asked.
      administerProject: flag(raw?.canAdministerProject),
    };
  } catch (e) {
    // The caller cannot see this project at all — nothing is possible on it.
    // This is the ONE status that may narrow the UI.
    if (e instanceof ApiError && e.status === 404) return ALL_DENIED;
    // Offline, 5xx, a captive-portal HTML page: the server did not answer the
    // question. Not a denial.
    return UNKNOWN_CAPABILITIES;
  }
}
