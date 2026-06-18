// Password hashing with PBKDF2-SHA256 via Web Crypto.
// Stored format: "pbkdf2-v1$<iterations>$<salt_b64url>$<hash_b64url>"
// 100,000 iterations, 32-byte random salt, 32-byte derived key.
//
// The prefix is VERSIONED (pbkdf2-v1) so a future algorithm — e.g. Argon2id via
// @noble/hashes/argon2 — can ship as pbkdf2-v2 / argon2-v1 and verify old hashes
// during a rolling migration without a flag-day rehash.

import { b64urlEncode, b64urlDecode, timingSafeEqual } from "./tokens";

// Cloudflare Workers' Web Crypto caps PBKDF2 at 100,000 iterations — requesting
// more (the OWASP-recommended 600k) throws `NotSupportedError: iteration counts
// above 100000 are not supported` at runtime. 100k is the hard ceiling on this
// platform; the upgrade path is Argon2id, not more PBKDF2 rounds.
// Ref: https://developers.cloudflare.com/workers/runtime-apis/web-crypto/
const ITERATIONS = 100_000;
const HASH_PREFIX = "pbkdf2-v1";
const SALT_BYTES = 32;
const KEY_BYTES = 32;

async function derive(
  password: string,
  salt: BufferSource,
  iterations: number
): Promise<Uint8Array> {
  const keyMaterial = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    { name: "PBKDF2" },
    false,
    ["deriveBits"]
  );
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", salt, iterations, hash: "SHA-256" },
    keyMaterial,
    KEY_BYTES * 8
  );
  return new Uint8Array(bits);
}

export async function hashPassword(password: string): Promise<string> {
  const salt = new Uint8Array(SALT_BYTES);
  crypto.getRandomValues(salt);
  const hash = await derive(password, salt, ITERATIONS);
  return `${HASH_PREFIX}$${ITERATIONS}$${b64urlEncode(salt)}$${b64urlEncode(hash)}`;
}

export async function verifyPassword(
  password: string,
  stored: string
): Promise<boolean> {
  const parts = stored.split("$");
  if (parts.length !== 4 || parts[0] !== HASH_PREFIX) return false;
  const iterations = parseInt(parts[1], 10);
  // Cap matches the runtime ceiling — a stored hash claiming more would throw.
  if (!Number.isFinite(iterations) || iterations < 1 || iterations > 100_000)
    return false;
  let salt: Uint8Array;
  let expected: string;
  try {
    salt = b64urlDecode(parts[2]);
    expected = parts[3];
  } catch {
    return false;
  }
  const computed = await derive(password, salt, iterations);
  return timingSafeEqual(b64urlEncode(computed), expected);
}
