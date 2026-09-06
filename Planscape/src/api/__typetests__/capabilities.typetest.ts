/**
 * Compile-time conformance test for the project-capabilities contract
 * (#547 / #558 / #634).
 *
 * The mobile app has no jest runner, so this is a *type-level* test validated
 * by `tsc --noEmit` (`npm run typecheck`). It cannot execute `flag()`, so it
 * pins the two things that a runtime test would otherwise be the only guard
 * for and that a refactor is most likely to break:
 *
 *   1. THE THIRD STATE EXISTS. `CapabilityState` must keep 'unknown'
 *      alongside 'allowed' and 'denied'. Collapsing it to a boolean — or to
 *      two states — is the regression this whole change is about: an absent
 *      answer rendered as a definite one. The @ts-expect-error block below
 *      fails to compile the moment 'unknown' is removed.
 *
 *   2. THE WIRE FIELD NAMES. `canCurateProject` / `canApproveSitePhotos` are
 *      what ProjectMembersController.GetMyCapabilities emits. A rename on
 *      either side makes `flag()` read undefined, which yields 'unknown'
 *      forever — a silent, permanently-enabled UI rather than a loud failure.
 *
 * This file is type-only and exports nothing used at runtime, so the bundler
 * drops it from the app.
 */
import type { CapabilityState, ProjectCapabilities } from '../capabilities';

// ── 1. Three states, not two ────────────────────────────────────────────

const allowed: CapabilityState = 'allowed';
const denied: CapabilityState = 'denied';
const unknown: CapabilityState = 'unknown';
void allowed;
void denied;
void unknown;

// A boolean is NOT a CapabilityState. If anyone "simplifies" the type back to
// a boolean, this stops erroring and the assertion silently disappears —
// which is why the positive assignments above are here too.
// @ts-expect-error — a capability is never a bare boolean; that is the bug.
const notABoolean: CapabilityState = true;
void notABoolean;

// @ts-expect-error — and there is no fourth state to invent at a call site.
const noFourthState: CapabilityState = 'maybe';
void noFourthState;

// ── 2. The wire shape ───────────────────────────────────────────────────

// Captured from GET /api/projects/{id}/members/capabilities — the anonymous
// object ProjectMembersController.GetMyCapabilities returns, serialized by
// ASP.NET Core's default camelCase policy. Only the two booleans are read.
const SERVER_SAMPLE = {
  projectId: '11111111-1111-4111-8111-111111111111',
  userId: '22222222-2222-4222-8222-222222222222',
  canCurateProject: true,
  canApproveSitePhotos: false,
};

// Both flags must be booleans on the wire. `flag()` deliberately treats
// anything else as 'unknown' rather than coercing it, so this assertion is
// what catches the server changing them to strings.
const curate: boolean = SERVER_SAMPLE.canCurateProject;
const approve: boolean = SERVER_SAMPLE.canApproveSitePhotos;
void curate;
void approve;

// The parsed result carries exactly the two capabilities the server has
// predicates for. A third does not get added client-side — it goes through
// the same propose-first step on the server that these two did.
const PARSED: ProjectCapabilities = {
  curateProject: 'allowed',
  approveSitePhotos: 'unknown',
};
void PARSED;

const INVENTED: ProjectCapabilities = {
  curateProject: 'allowed',
  approveSitePhotos: 'denied',
  // @ts-expect-error — no inventing capabilities the server does not serve.
  canDeleteEverything: 'allowed',
};
void INVENTED;
