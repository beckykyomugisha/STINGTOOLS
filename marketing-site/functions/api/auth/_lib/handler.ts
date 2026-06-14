// Thin wrapper that gives every endpoint: CORS, JSON body parsing, and
// AuthError → JSON-response mapping. Keeps the endpoint files focused on logic.

import { AuthError, bad, serverError } from "./errors";
import { jsonResponse, isAllowedOrigin } from "./cors";
import type { Env } from "./types";

type Ctx = { request: Request; env: Env };

// Wrap an async handler. The handler returns the JSON body (or a Response for
// custom headers like Set-Cookie). Thrown AuthErrors become JSON error
// responses; anything else becomes a generic 500 (no stack/DB leak).
export function withHandler(
  fn: (ctx: Ctx) => Promise<Response | unknown>
): PagesFunction<Env> {
  return async ({ request, env }) => {
    if (!isAllowedOrigin(request)) {
      return jsonResponse(request, { error: "Origin not allowed" }, 403);
    }
    try {
      const result = await fn({ request, env });
      if (result instanceof Response) return result;
      return jsonResponse(request, result);
    } catch (e) {
      if (e instanceof AuthError) {
        return jsonResponse(request, { error: e.message }, e.status);
      }
      console.error("Unhandled auth error", e);
      const err = serverError();
      return jsonResponse(request, { error: err.message }, err.status);
    }
  };
}

// Parse a JSON request body, throwing a 400 on malformed input.
export async function readJson<T = Record<string, unknown>>(
  request: Request
): Promise<T> {
  try {
    return (await request.json()) as T;
  } catch {
    throw bad("Invalid JSON body.");
  }
}
