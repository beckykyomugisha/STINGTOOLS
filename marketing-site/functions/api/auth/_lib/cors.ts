// Shared CORS handling for the auth Functions.
// Only the marketing site and the (future) app origin may call these endpoints
// from a browser. Everything else is rejected.

const ALLOWED_ORIGINS = new Set([
  "https://planscape.build",
  "https://app.planscape.build",
]);

// Echo back the request origin only if it is allow-listed; otherwise fall back
// to the canonical site so the browser blocks the disallowed origin.
export function corsHeaders(request: Request): Record<string, string> {
  const origin = request.headers.get("Origin") || "";
  const allowed = ALLOWED_ORIGINS.has(origin);
  return {
    "Access-Control-Allow-Origin": allowed ? origin : "https://planscape.build",
    "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type, Authorization, Idempotency-Key",
    "Access-Control-Allow-Credentials": "true",
    "Access-Control-Max-Age": "86400",
    Vary: "Origin",
  };
}

export function isAllowedOrigin(request: Request): boolean {
  const origin = request.headers.get("Origin");
  // Non-browser callers (curl, server-to-server) send no Origin — allow them.
  if (!origin) return true;
  return ALLOWED_ORIGINS.has(origin);
}

// Shared preflight responder. Wire as `onRequestOptions` in each Function.
export function handlePreflight(request: Request): Response {
  return new Response(null, { status: 204, headers: corsHeaders(request) });
}

// Build a JSON Response with CORS + content-type already applied.
export function jsonResponse(
  request: Request,
  body: unknown,
  status = 200,
  extraHeaders: Record<string, string> = {}
): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: {
      "Content-Type": "application/json",
      ...corsHeaders(request),
      ...extraHeaders,
    },
  });
}
