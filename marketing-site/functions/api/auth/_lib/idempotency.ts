// Idempotency-Key support. Payment-creating endpoints (B3) read the
// `Idempotency-Key` header; the first call runs and caches its response, and any
// replay within the TTL returns the cached response instead of re-charging.
// B1 ships the helper + table so B3 can wire it in without a migration.

import type { Env } from "./types";

const TTL_HOURS = 24;

interface CachedResponse {
  status_code: number;
  response: string;
}

// Pull the Idempotency-Key header (or null if absent).
export function idempotencyKey(request: Request): string | null {
  const key = request.headers.get("Idempotency-Key");
  return key && key.trim() ? key.trim().slice(0, 200) : null;
}

// Return a previously-cached response for this key+endpoint, or null. Expired
// rows are treated as absent (the nightly cleanup job removes them for real).
export async function getCachedResponse(
  env: Env,
  key: string,
  endpoint: string
): Promise<Response | null> {
  const row = await env.WAITLIST_DB.prepare(
    `SELECT status_code, response FROM idempotency_keys
       WHERE key = ? AND endpoint = ? AND expires_at > ?`
  )
    .bind(key, endpoint, new Date().toISOString())
    .first<CachedResponse>();
  if (!row) return null;
  return new Response(row.response, {
    status: row.status_code,
    headers: { "Content-Type": "application/json", "Idempotency-Replayed": "true" },
  });
}

// Persist a response under this key. INSERT OR IGNORE so a race between two
// concurrent identical requests keeps the first writer's result.
export async function saveResponse(
  env: Env,
  key: string,
  endpoint: string,
  tenantId: string | null,
  statusCode: number,
  bodyJson: string
): Promise<void> {
  const now = new Date();
  const expires = new Date(now.getTime() + TTL_HOURS * 3600_000);
  await env.WAITLIST_DB.prepare(
    `INSERT OR IGNORE INTO idempotency_keys
       (key, tenant_id, endpoint, response, status_code, created_at, expires_at)
     VALUES (?,?,?,?,?,?,?)`
  )
    .bind(key, tenantId, endpoint, bodyJson, statusCode, now.toISOString(), expires.toISOString())
    .run();
}
