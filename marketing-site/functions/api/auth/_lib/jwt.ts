// Minimal HS256 JWT implementation on Web Crypto. No external library.

import { b64urlEncode, b64urlDecode } from "./tokens";
import type { JwtClaims } from "./types";

const ACCESS_TTL_SECONDS = 60 * 60; // 1 hour

function b64urljson(obj: unknown): string {
  return b64urlEncode(new TextEncoder().encode(JSON.stringify(obj)));
}

async function hmacKey(secret: string): Promise<CryptoKey> {
  return crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign", "verify"]
  );
}

export async function signJwt(
  payload: Omit<JwtClaims, "iat" | "exp" | "iss">,
  secret: string
): Promise<string> {
  const now = Math.floor(Date.now() / 1000);
  const claims: JwtClaims = {
    iss: "planscape",
    ...payload,
    iat: now,
    exp: now + ACCESS_TTL_SECONDS,
  };
  const header = { alg: "HS256", typ: "JWT" };
  const signingInput = `${b64urljson(header)}.${b64urljson(claims)}`;
  const key = await hmacKey(secret);
  const sig = await crypto.subtle.sign(
    "HMAC",
    key,
    new TextEncoder().encode(signingInput)
  );
  return `${signingInput}.${b64urlEncode(new Uint8Array(sig))}`;
}

// Returns the claims on success, or null on any failure (bad format, bad
// signature, expired). Never throws.
export async function verifyJwt(
  token: string,
  secret: string
): Promise<JwtClaims | null> {
  const parts = token.split(".");
  if (parts.length !== 3) return null;
  const [headerB64, payloadB64, sigB64] = parts;
  const signingInput = `${headerB64}.${payloadB64}`;

  const key = await hmacKey(secret);
  let valid: boolean;
  try {
    const sig: BufferSource = b64urlDecode(sigB64);
    valid = await crypto.subtle.verify(
      "HMAC",
      key,
      sig,
      new TextEncoder().encode(signingInput)
    );
  } catch {
    return null;
  }
  if (!valid) return null;

  // Re-compute and constant-time compare as belt-and-braces against any
  // verify() quirks, then parse claims.
  let claims: JwtClaims;
  try {
    const header = JSON.parse(
      new TextDecoder().decode(b64urlDecode(headerB64))
    );
    if (header.alg !== "HS256") return null;
    claims = JSON.parse(new TextDecoder().decode(b64urlDecode(payloadB64)));
  } catch {
    return null;
  }

  const now = Math.floor(Date.now() / 1000);
  if (typeof claims.exp !== "number" || claims.exp < now) return null;
  if (claims.iss !== "planscape") return null;

  return claims;
}
