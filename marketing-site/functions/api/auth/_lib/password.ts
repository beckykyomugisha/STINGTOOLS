// Password hashing with PBKDF2-SHA256 via Web Crypto.
// Stored format: "pbkdf2$<iterations>$<salt_b64url>$<hash_b64url>"
// 600,000 iterations, 32-byte random salt, 32-byte derived key.

import { b64urlEncode, b64urlDecode, timingSafeEqual } from "./tokens";

const ITERATIONS = 600_000;
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
  return `pbkdf2$${ITERATIONS}$${b64urlEncode(salt)}$${b64urlEncode(hash)}`;
}

export async function verifyPassword(
  password: string,
  stored: string
): Promise<boolean> {
  const parts = stored.split("$");
  if (parts.length !== 4 || parts[0] !== "pbkdf2") return false;
  const iterations = parseInt(parts[1], 10);
  if (!Number.isFinite(iterations) || iterations < 1) return false;
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
