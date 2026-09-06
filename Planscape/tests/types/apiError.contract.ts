/**
 * Type-level contract for `ApiError`.
 *
 * This app has no jest runner, so `tsc --noEmit` is the strongest compile-time
 * guarantee available. Every assertion below is a compile error if the contract
 * breaks — `npm run typecheck` already covers this file via tsconfig's
 * `"include": ["**\/*.ts"]`.
 *
 * What is being pinned, and why:
 *
 *   The status-from-message defect class (#646, and #624/#625 before it) was
 *   possible because callers reconstructed the HTTP status by matching prose
 *   against `err.message`. The number was on the object the whole time. These
 *   assertions make `status` a load-bearing part of the type, so that removing
 *   it, renaming it, widening it to `number | undefined`, or turning it into a
 *   string cannot land silently.
 *
 *   It also pins the *negative* half — that `message` is a plain `string` and
 *   carries no status guarantee — because that is the half people forget.
 */

import { ApiError } from '@/api/client';

/** Compile-time assertion helper. `Expect<false>` is an error. */
type Expect<T extends true> = T;
/** Invariant (not merely assignable) type equality. */
type Exact<A, B> = (<T>() => T extends A ? 1 : 2) extends (<T>() => T extends B ? 1 : 2) ? true : false;

// ── 1. `status` exists, is exactly `number`, and is not optional ────────────
// `Exact` rather than `extends` so `number | undefined`, `403`, `any` and
// `unknown` all fail. A widened status is the failure mode that would let the
// old prose-matching creep back in as a "safe" fallback.
export type _StatusIsExactlyNumber = Expect<Exact<ApiError['status'], number>>;

// Not optional: `Partial` of the field must not be assignable back to it.
export type _StatusIsRequired = Expect<Exact<undefined extends ApiError['status'] ? true : false, false>>;

// ── 2. `status` is reachable on a constructed instance ─────────────────────
// Guards against `status` becoming a getter that is dropped, or the
// constructor parameter property being replaced by something private.
const _instanceStatus: number = new ApiError(403, 'forbidden').status;

// ── 3. The constructor still takes (status, message) in that order ─────────
export type _CtorShape = Expect<Exact<ConstructorParameters<typeof ApiError>, [number, string]>>;

// ── 4. `message` is a plain string and promises nothing about the status ───
// This is the assertion that documents the trap: `message` is the response
// BODY, falling back to `HTTP <status>` only when that body is empty. Nothing
// in the type says otherwise, so nothing may be inferred from it.
export type _MessageIsPlainString = Expect<Exact<ApiError['message'], string>>;

// ── 5. `ApiError` is a real class, so `instanceof` narrowing works ─────────
// The narrowing in offlineQueue.statusOf and in the project-settings screens
// depends on this; an interface or a plain object factory would break it.
declare const maybe: unknown;
if (maybe instanceof ApiError) {
  const _narrowed: number = maybe.status;
  void _narrowed;
}

void _instanceStatus;
