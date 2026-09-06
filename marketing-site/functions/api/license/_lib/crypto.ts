// Licence signing and verification. Shared by issue.ts (which mints) and
// present.ts (which checks what a plugin hands back), so the two can never
// disagree about the wire format.
//
// Wire format, byte-for-byte compatible with LicenseCrypto.VerifyAndExtract in
// StingTools/Core/Licensing/LicenseCrypto.cs:
//   base64(utf8(payloadJson)) + "." + base64(RSASSA-PKCS1-v1_5(SHA-256, jsonBytes))
//
// Note the signature covers the RAW BYTES of the first segment, not a re-parse
// of the JSON — so verification never depends on key order or whitespace.

export interface LicensePayloadV1 {
  licenseId: string;
  machineCode: string;
  licensee: string;
  issuedUnix: number;
  expiryUnix: number;
  schema: number;
}

export function pemToBinary(pem: string): ArrayBuffer {
  const body = pem
    .replace(/-----BEGIN [A-Z ]+-----/g, "")
    .replace(/-----END [A-Z ]+-----/g, "")
    .replace(/\s+/g, "");
  const raw = atob(body);
  const buf = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) buf[i] = raw.charCodeAt(i);
  return buf.buffer;
}

export function b64(bytes: Uint8Array): string {
  let s = "";
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]);
  return btoa(s);
}

export function b64ToBytes(s: string): Uint8Array {
  const raw = atob(s);
  const out = new Uint8Array(raw.length);
  for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
  return out;
}

async function importSigningKey(pem: string): Promise<CryptoKey> {
  return crypto.subtle.importKey(
    "pkcs8",
    pemToBinary(pem),
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"]
  );
}

export async function signBytes(
  pem: string,
  data: Uint8Array
): Promise<Uint8Array> {
  const key = await importSigningKey(pem);
  return new Uint8Array(
    await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, data)
  );
}

// Mint a licence: base64(payload) + "." + base64(signature).
export async function signLicense(
  pem: string,
  payloadJson: string
): Promise<string> {
  const data = new TextEncoder().encode(payloadJson);
  const sig = await signBytes(pem, data);
  return `${b64(data)}.${b64(sig)}`;
}

function bytesEqual(a: Uint8Array, b: Uint8Array): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
  return diff === 0;
}

// Verify a licence presented by a plugin, returning its payload or null.
//
// Verification RE-SIGNS the payload bytes and compares, rather than doing a
// public-key verify. RSASSA-PKCS1-v1_5 has deterministic padding — no
// randomness — so signing the same bytes with the same key always yields the
// same signature, and equality is exactly as strong as a verify.
//
// The reason to do it this way is operational, not cryptographic: the public
// half is already compiled into the plugin (LicensePublicKey.cs), so putting a
// second copy in a LICENSE_PUBLIC_KEY binding would add a secret that can be
// forgotten at deploy time or, worse, set to a mismatched key — and either
// failure mode silently rejects every genuine licence. Reusing
// LICENSE_PRIVATE_KEY means if issuing works, verification works. There is no
// privilege escalation: this Function already holds the private key in order
// to issue.
export async function verifyLicense(
  pem: string,
  licenseText: string
): Promise<LicensePayloadV1 | null> {
  const parts = licenseText.split(".");
  if (parts.length !== 2) return null;

  let payloadBytes: Uint8Array;
  let sigBytes: Uint8Array;
  try {
    payloadBytes = b64ToBytes(parts[0]);
    sigBytes = b64ToBytes(parts[1]);
  } catch {
    return null;
  }
  if (payloadBytes.length === 0 || sigBytes.length === 0) return null;

  const expected = await signBytes(pem, payloadBytes);
  if (!bytesEqual(expected, sigBytes)) return null;

  let payload: unknown;
  try {
    payload = JSON.parse(new TextDecoder().decode(payloadBytes));
  } catch {
    return null;
  }

  const p = payload as Partial<LicensePayloadV1>;
  if (
    typeof p?.licenseId !== "string" ||
    typeof p?.machineCode !== "string" ||
    typeof p?.licensee !== "string" ||
    typeof p?.issuedUnix !== "number" ||
    typeof p?.expiryUnix !== "number" ||
    typeof p?.schema !== "number"
  ) {
    return null;
  }
  return p as LicensePayloadV1;
}
