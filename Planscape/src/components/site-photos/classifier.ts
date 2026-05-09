// Phase 178 — Auto-classifier for the Six-Reason capture taxonomy.
//
// Inputs are weak signals that the capture screen can collect cheaply:
//   - GPS reading (vs. project geofence — present-or-absent, not strict)
//   - Time of day (working hours vs. handover window)
//   - "Recent action" hint passed in by the FAB caller (e.g. "issue-context"
//     when launched from inside an Issue, "diary-context" when launched
//     from the diary FAB).
//   - Active anchor — if the FAB was launched against an element/issue,
//     bias toward Issue/Defect.
//
// The classifier returns a ranked list with confidence scores. The capture
// strip pre-selects the top pick but always shows all six chips so the user
// can override in one tap. We never block on the classifier — it must run
// synchronously and be < 1ms.

import type { SitePhotoReason } from '@/types/api';

export type ClassifierContext =
  | 'dashboard'
  | 'diary'
  | 'issue-context'
  | 'element-context'
  | 'gallery'
  | 'unknown';

export interface ClassifierInput {
  context: ClassifierContext;
  hasAnchorIssueId: boolean;
  hasAnchorElementGuid: boolean;
  /** Local hour 0–23. If undefined, no time-of-day signal. */
  hour?: number;
  /** Whether GPS is currently available. We don't compare against the
   *  project polygon here (server already enforces geofence); just whether
   *  a reading is in hand. */
  hasGps: boolean;
  /** Whether the user has an active work-package filter on the project. */
  hasActiveWorkPackage: boolean;
}

export interface ClassifierResult {
  /** Top reason. The capture screen pre-selects the chip matching this. */
  reason: SitePhotoReason;
  /** 0..1 — UI shows this faintly so users know whether they can trust it. */
  confidence: number;
  /** Signals that fed the decision. Posted to the server as
   *  `classifierSignals` JSON for later analytics/tuning. */
  signals: Record<string, unknown>;
}

/**
 * Pure scoring function: given context, return the top-ranked reason and
 * a structured `signals` record for the audit trail.
 *
 * Rules in order (highest weight first):
 *   1. anchorIssueId    → "Issue"
 *   2. anchorElementGuid → "Defect"
 *   3. context==='diary' + working hours + GPS → "Progress"
 *   4. context==='dashboard' + late afternoon (16-20h) → "Progress"
 *   5. handover window (8-10h, 16-18h) without anchor → "AsBuilt"
 *   6. fallback → "Reference"
 */
export function classifyCapture(input: ClassifierInput): ClassifierResult {
  const signals: Record<string, unknown> = { ...input };

  if (input.hasAnchorIssueId) {
    return { reason: 'Issue', confidence: 0.92, signals: { ...signals, rule: 'anchor-issue' } };
  }

  if (input.hasAnchorElementGuid) {
    return { reason: 'Defect', confidence: 0.78, signals: { ...signals, rule: 'anchor-element' } };
  }

  const isWorkingHours = input.hour !== undefined && input.hour >= 7 && input.hour <= 18;
  const isLateAfternoon = input.hour !== undefined && input.hour >= 16 && input.hour <= 20;
  const isHandoverWindow =
    input.hour !== undefined && ((input.hour >= 8 && input.hour <= 10) || (input.hour >= 16 && input.hour <= 18));

  if (input.context === 'diary' && isWorkingHours && input.hasGps) {
    return { reason: 'Progress', confidence: 0.7, signals: { ...signals, rule: 'diary-progress' } };
  }
  if (input.context === 'dashboard' && isLateAfternoon) {
    return { reason: 'Progress', confidence: 0.55, signals: { ...signals, rule: 'late-afternoon' } };
  }
  if (isHandoverWindow && input.hasActiveWorkPackage) {
    return { reason: 'AsBuilt', confidence: 0.5, signals: { ...signals, rule: 'handover-window' } };
  }

  return { reason: 'Reference', confidence: 0.3, signals: { ...signals, rule: 'fallback' } };
}

/** All six reasons in display order — used to render the chip strip. */
export const REASONS: SitePhotoReason[] = [
  'Progress',
  'Issue',
  'Defect',
  'Safety',
  'AsBuilt',
  'Reference',
];

export function describeReason(r: SitePhotoReason): string {
  switch (r) {
    case 'Progress':  return 'Progress photo for the daily diary.';
    case 'Issue':     return 'Evidence for an existing issue.';
    case 'Defect':    return 'Defect / non-conformance — auto-creates an NCR.';
    case 'Safety':    return 'Safety hazard — auto-creates a SAFETY issue.';
    case 'AsBuilt':   return 'As-built record for handover.';
    case 'Reference': return 'Internal reference shot — never reaches the client.';
    default:          return '';
  }
}

/** Helper: would this reason auto-route to PendingReview by default? */
export function reasonRoutesToReview(r: SitePhotoReason): boolean {
  return r === 'Progress' || r === 'AsBuilt';
}

/** Helper: would this reason auto-create an issue server-side? */
export function reasonAutoCreatesIssue(r: SitePhotoReason): boolean {
  return r === 'Issue' || r === 'Defect' || r === 'Safety';
}
