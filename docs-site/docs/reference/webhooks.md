# Webhook events

Webhooks let your systems react to Planscape events in real time — pushing issue updates into Slack, mirroring document transitions into a corporate DMS, triggering a CI build when a stage gate is signed off. Each event is a JSON POST to your configured endpoint, signed with HMAC-SHA256 so you can verify it's genuinely from us.

## Register a webhook

**Dashboard → Settings → Webhooks → Add endpoint**.

Enter:

- **URL** — the HTTPS endpoint that will receive events
- **Secret** — auto-generated, shown once; you'll use this for signature verification
- **Events** — tick the events you want to subscribe to (or All)
- **Active** — yes/no toggle

Save. Send yourself a test event with **Send test event** to confirm your endpoint is wired up.

## Authentication

Every webhook request carries an `X-Planscape-Signature` header. The value is `sha256=<hex digest>` where the digest is computed as:

```
HMAC-SHA256(secret, raw_request_body)
```

Verify by recomputing the digest with your stored secret and comparing in constant time. Reject the request if it doesn't match.

A typical Node.js verification:

```javascript
const crypto = require('crypto');

function verify(req, secret) {
  const sig = req.headers['x-planscape-signature'];
  const expected = 'sha256=' + crypto.createHmac('sha256', secret)
    .update(req.rawBody)
    .digest('hex');
  return crypto.timingSafeEqual(Buffer.from(sig), Buffer.from(expected));
}
```

The secret never appears in the request — only the signature. Store it as a server-side secret on your end.

## Event types

| Event | When it fires |
|---|---|
| `issue.created` | A new RFI, NCR, or SI is raised |
| `issue.updated` | Status, assignee, priority, or due date changes on an issue |
| `issue.closed` | An issue is closed |
| `document.uploaded` | A new document or new revision is uploaded to the CDE |
| `document.transitioned` | A document moves between CDE states (WIP → Shared, etc.) |
| `compliance.snapshot` | A new compliance snapshot is recorded for the project |
| `transmittal.issued` | A transmittal is issued to recipients |
| `stage_gate.signed_off` | A stage gate is signed off (or reopened) |
| `user.invited` | A new user is invited to the tenant |

## Common payload shape

All events share a common envelope:

```json
{
  "event": "issue.created",
  "timestamp": "2026-05-04T09:32:14.821Z",
  "tenant_id": "tn_01HX2A...",
  "project_id": "prj_01HX3B...",
  "actor_id": "usr_01HX4C...",
  "data": { ... }
}
```

The `data` object is event-specific. Below are the shapes for each event.

### `issue.created` / `issue.updated` / `issue.closed`

```json
{
  "id": "iss_01HXAB...",
  "type": "RFI",
  "title": "Beam B12-L02 conflicts with supply duct",
  "status": "Open",
  "priority": "Medium",
  "assignee_id": "usr_01HX...",
  "due_date": "2026-05-12",
  "linked_element_tag": "S-BLD1-Z01-L02-STR-BEAM-0042",
  "created_at": "2026-05-04T09:32:14Z",
  "updated_at": "2026-05-04T09:32:14Z"
}
```

### `document.uploaded` / `document.transitioned`

```json
{
  "id": "doc_01HX...",
  "name": "STR-Drawings-L02.pdf",
  "revision": "P02",
  "state": "Shared",
  "previous_state": "WIP",
  "suitability": "S1",
  "size_bytes": 2841052
}
```

### `transmittal.issued`

```json
{
  "id": "tm_01HX...",
  "number": "TM-PLNS-2026-001-014",
  "suitability": "S3",
  "purpose": "For Stage 3 review",
  "recipient_emails": ["client@example.com"],
  "document_ids": ["doc_01HX...", "doc_01HX..."]
}
```

### `stage_gate.signed_off`

```json
{
  "stage_id": "stg_01HX...",
  "stage_name": "RIBA Stage 3 — Spatial Coordination",
  "approver_ids": ["usr_01HX...", "usr_01HX..."],
  "snapshot_id": "snp_01HX...",
  "signed_off_at": "2026-05-04T16:08:00Z"
}
```

### `compliance.snapshot`

```json
{
  "snapshot_id": "cmp_01HX...",
  "compliance_pct": 87.4,
  "tagged_complete": 12842,
  "tagged_incomplete": 1832,
  "untagged": 28,
  "captured_at": "2026-05-04T00:00:00Z"
}
```

## Retry policy

If your endpoint returns a non-2xx status (or doesn't respond within 10 seconds), Planscape retries up to **3 times** with exponential backoff: 30 s, 5 min, 30 min. After 3 failed retries the event is logged as failed in the webhook delivery log and not retried further.

For idempotency, use the event's `event_id` field (a unique ULID) — replay the same event id and your handler should produce the same result.

## Testing

The webhook detail page in the dashboard has a **Send test event** button. Pick an event type, click send, and a synthetic event with realistic data is POSTed to your endpoint. Useful for development and for one-off "is the integration alive?" checks.

## Disabling

Set **Active** to off to pause delivery without removing the endpoint. Events that fire while paused are not queued — they're just not delivered. Re-enabling resumes from the next event.
