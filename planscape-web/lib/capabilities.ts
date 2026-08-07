'use client';

import { useEffect, useState } from 'react';
import { api, ApiError } from './api';

/**
 * Project capabilities, resolved server-side (#547) and consumed here (#558).
 *
 * WHY THIS EXISTS. Clients were re-deriving authority from `projectRole` /
 * `iso19650Role`. Three surfaces re-implementing one rule is how the eleven dead
 * `ProjectRole == "PM"` gates happened. The server owns the rule; this renders
 * what it returns.
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
 * AFFORDANCE; the server remains the gate. Failing closed on a dropped
 * connection locks out legitimate users and looks identical to a permissions
 * problem — an absent answer displayed as a definite one, the same mistake as
 * an empty list standing in for a failed load.
 *
 * A 404 IS authoritative-false: it says the caller cannot see the project at
 * all. A dropped connection says nothing about permissions. See #634 for the
 * correction to the #547 docstring that originally said otherwise.
 */
export type CapabilityState = 'allowed' | 'denied' | 'unknown';

export interface ProjectCapabilities {
  /** Albums, checklists, distribution groups. */
  curateProject: CapabilityState;
  /** Photo approve / reject, share-link issuance, photo policy. */
  approveSitePhotos: CapabilityState;
}

export const UNKNOWN_CAPABILITIES: ProjectCapabilities = {
  curateProject: 'unknown',
  approveSitePhotos: 'unknown',
};

const ALL_DENIED: ProjectCapabilities = {
  curateProject: 'denied',
  approveSitePhotos: 'denied',
};

/**
 * Read only an EXPLICIT boolean. A missing field, a null, or a string `"true"`
 * is a contract mismatch, not a "no" — coercing it would silently grant a
 * capability and reading it as false would silently remove one. Neither is an
 * answer we were given.
 */
function flag(v: unknown): CapabilityState {
  if (v === true) return 'allowed';
  if (v === false) return 'denied';
  return 'unknown';
}

/**
 * `GET /api/projects/{id}/members/capabilities`.
 * Never throws — every failure mode yields all-`unknown`, which disables
 * nothing.
 */
export async function getProjectCapabilities(projectId: string): Promise<ProjectCapabilities> {
  try {
    const raw = await api<Record<string, unknown>>(`/api/projects/${projectId}/members/capabilities`);
    return {
      curateProject: flag(raw?.canCurateProject),
      approveSitePhotos: flag(raw?.canApproveSitePhotos),
    };
  } catch (e) {
    // The caller cannot see this project at all — nothing is possible on it.
    // This is the ONE status that may narrow the UI.
    if (e instanceof ApiError && e.status === 404) return ALL_DENIED;
    // 5xx, a proxy error page, a dropped fetch: the server did not answer the
    // question. Not a denial.
    return UNKNOWN_CAPABILITIES;
  }
}

/**
 * Hook form. Starts all-`unknown`, so a component renders with every control
 * live and only ever loses one to an explicit answer. A fetch that never
 * returns leaves the surface in its honest default.
 */
export function useProjectCapabilities(projectId: string | undefined): ProjectCapabilities {
  const [caps, setCaps] = useState<ProjectCapabilities>(UNKNOWN_CAPABILITIES);

  useEffect(() => {
    if (!projectId) return;
    let live = true;
    void getProjectCapabilities(projectId).then((c) => {
      if (live) setCaps(c);
    });
    return () => {
      live = false;
    };
  }, [projectId]);

  return caps;
}

/**
 * The user-facing sentence for a capability — named here so it is written once
 * rather than at each control.
 *
 * THE ROLE NAMES ARE COPY, NOT A GATE. They tell the user who to ask. Nothing
 * in this file decides anything; the server decides and `CapabilityState`
 * carries its answer. If the server's rule changes this is stale text, not a
 * broken permission check.
 */
export const CAPABILITY_COPY = {
  curateProject: 'Only a project manager or BIM coordinator can curate albums, checklists and distribution groups.',
  approveSitePhotos: 'Only a project manager can approve site photos, issue share links, or change the photo policy.',
} as const;
