# Verify the audit log

Planscape's audit log is a hash-chained, append-only record of every write operation across the platform. It satisfies ISO 19650-2 §5.6 audit requirements and is designed to be **independently verifiable** — a third-party auditor can confirm its integrity without access to Planscape's internal systems.

This guide explains the format, how to export the log, and how to verify it.

## Format

Every write — issue created, document transitioned, stage gate signed off, parameter updated — appends one record to the log. Records are JSONL (one JSON object per line). Each record contains:

```json
{
  "id": "01HX9F2A8K3B7M0V4N5T6QY1WE",
  "timestamp": "2026-05-04T09:32:14.821Z",
  "tenant_id": "tn_01HX2A...",
  "project_id": "prj_01HX3B...",
  "actor_id": "usr_01HX4C...",
  "action": "issue.create",
  "entity_id": "iss_01HXAB...",
  "payload_hash": "f3a9c8b2d1e4...",
  "prev_hash": "9c2e7b3a5d8f...",
  "hash": "1a4d2f6e9b8c..."
}
```

The hash chain links every record to the one before it: a record's `hash` field is computed as

```
SHA-256( prev_hash || serialized_record_without_hash )
```

The first record in a tenant's log has `prev_hash = "0000…0000"` (64 zeros). Every subsequent record's `prev_hash` is the previous record's `hash`. **Any tampering — changing a payload, deleting a record, inserting a forged record — breaks the chain at and after the point of tampering.**

The same chain mechanism runs in the Revit plugin's local sidecar file, so even offline-authored writes preserve the chain when they sync.

## Export the log

From the dashboard, **Admin → Audit Log → Export**.

Choose:

- **Date range** — start and end timestamps, inclusive
- **Tenant** — required (admin can export for any tenant they own)
- **Project filter** — optional, restricts to a single project
- **Format** — JSONL (recommended for verification) or CSV (for human reading)

Click **Export**. The file is generated server-side and downloadable from the export page.

For very long ranges the export is split into chunks of up to 100 MB each. They share a common manifest with the chunk order so verification still works across the full range.

## Verify the chain

Verification is a streaming check: read each record, confirm its hash matches `SHA-256(prev_hash || serialized_record_without_hash)`, and confirm `prev_hash` matches the previous record's `hash`. Any mismatch is a tamper indicator.

Pseudocode:

```
expected_prev = "0000...0000"
for record in jsonl_lines:
    serialized  = record without the "hash" field, in canonical JSON form
    computed    = sha256(expected_prev || serialized).hex()
    if computed != record.hash:
        FAIL — record N hash mismatch
    if record.prev_hash != expected_prev:
        FAIL — record N prev_hash mismatch
    expected_prev = record.hash
print("OK — N records verified")
```

Two important detail rules:

1. **Canonical JSON.** Sort object keys alphabetically; serialise without whitespace; UTF-8 encode. Different JSON serialisers produce different byte streams from the same logical object, and the hash is byte-sensitive. Planscape uses `JsonSerializer` with `JsonSerializerOptions.WriteIndented = false` and a key-sorted contract.
2. **Field exclusion.** The `hash` field is not part of the input to its own computation. Strip it before hashing.

A reference verifier — single-file Python script, no third-party dependencies — is shipped as `tools/verify-audit.py` in the Planscape repository. Run it with:

```
python verify-audit.py audit-2026-05.jsonl
```

It prints OK with the record count, or FAIL with the line number of the first inconsistency.

## Court admissibility

The hash chain has been designed in consultation with legal counsel to satisfy:

- **Uganda Evidence Act** — admissibility of electronic records
- **GDPR Article 5(1)(f)** — integrity and confidentiality
- **ISO 19650-2 §5.6** — audit trail requirements for Common Data Environments
- **English law of evidence** (relevant to international donor projects) — Civil Evidence Act 1995 §9 documentary evidence

In practice, an auditor asks for the log export, runs the verifier, and confirms either:

- ✓ All records verify — the log is intact, no tampering
- ✗ A specific record fails — the chain is broken at line N; everything before line N is trustworthy, everything after is suspect

The fact that any tampering is detectable (and pinpointable to a specific record) is what makes the chain useful.

## What's logged, what isn't

**Logged** — every state change: issue lifecycle events, document transitions, parameter writes, stage-gate sign-offs, user invites and role changes, transmittal issuance, model uploads.

**Not logged** — read operations (page views, document downloads), session events (login/logout — these are in a separate security log), and ephemeral UI state. Read access is captured separately in the access log under **Admin → Access Log**.

## Retention

Audit log records are retained for **7 years** to satisfy contract and statutory audit requirements. They are exempt from individual erasure requests under our Privacy Policy because erasure would break the chain — instead, on an erasure request we redact the `payload` field of records relating to the data subject, while leaving the chain intact. Redaction itself is logged.

## Related

- [Privacy Policy](https://planscape.app/privacy) — full retention policy and erasure-request handling
- [Sign off a stage gate](../howto/stage-gate.md) — sign-off events are recorded in the audit log
