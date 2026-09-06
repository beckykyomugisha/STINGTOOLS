/**
 * The error every non-OK `apiFetch` response is thrown as.
 *
 * Lives in its own module — with no React Native / Expo imports — so that
 * pure message-formatting code (and its harness) can depend on the error
 * *shape* without dragging in `expo-secure-store` and the whole client.
 * `client.ts` re-exports it, so existing `from '@/api/client'` imports are
 * unaffected.
 *
 * `message` is the raw response body. It falls back to the literal string
 * `HTTP <status>` only when the body is EMPTY — so testing `message` for
 * "HTTP 403" is a test for an *empty body*, not for the status. Read the
 * status from `.status`.
 */
export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message);
    this.name = 'ApiError';
  }
}
