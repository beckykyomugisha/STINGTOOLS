# Planscape information-security policy

> Statement of how Planscape handles, protects, and reasons about
> information. Pairs with `iso27001-controls.md` (the controls register
> mapping each policy clause to evidence). Living document — the
> founder reviews annually + each PR that touches security-relevant
> code closes the loop here.

## 1. Purpose

Planscape stores BIM coordination data on behalf of architecture and
engineering practices. That data — model geometry, tag metadata,
issue logs, document versions, audit trails — is commercially
sensitive and, in most jurisdictions where we operate, subject to
data-protection law (Uganda DPA 2019; POPIA; GDPR for EU clients).
This policy describes how we protect it.

## 2. Scope

Covers everything Planscape Ltd operates:

- The Planscape API and Hangfire workers (server-side)
- The mobile app (iOS, Android) and the web viewer
- The Revit plugin (StingTools)
- The CDN, marketing site, docs site
- All third-party services we route customer data through
  (Hetzner, Backblaze, Stripe, Flutterwave, Postmark, Cloudflare,
  Bunny.net — see `iso27001-controls.md` § G for the live DPA matrix)

Does **not** cover: customers' own infrastructure, customers' Revit
licences, customers' on-premise file shares.

## 3. Information classification

Three tiers:

| Tier | Examples | Handling |
|---|---|---|
| **Confidential** | Tenant data (models, issues, tags, audit logs, member identities, billing details) | Encrypted in transit (TLS 1.3); tenant-isolated at the data layer (S1.1) and the storage layer (S1.2); access logged in the audit trail (S1.8) |
| **Internal** | Aggregate metrics, MRR, churn rate, infra cost telemetry, support tickets | Visible to Planscape staff only; no customer identifiers in metrics labels |
| **Public** | Pricing page, marketing site, docs site, status page, this policy | Indexable, cacheable, anonymous |

## 4. Access control

### 4.1 Authentication

- Customer accounts: BCrypt-hashed passwords (cost 11), JWT access
  tokens (8 h TTL) + refresh tokens (30 d TTL). MFA optional in v1;
  mandatory for Owner / Admin role from Phase 8 onwards (TODO S8).
- Internal: SSH keys to production hosts; AWS / Hetzner IAM with
  least-privilege roles; no shared logins.
- Plugin: licence-key-based; revocable from the tenant admin
  dashboard.

### 4.2 Authorisation

- Tenant isolation is structural, not procedural: the global
  `HasQueryFilter` from S1.1 makes cross-tenant queries impossible
  by construction. Adding a new tenant-scoped entity without
  implementing `ITenantScoped` will not leak — but it will fail
  unit tests.
- Storage paths carry the tenant prefix (`t_<tenantId>/...`); read
  operations validate the prefix matches the resolved current
  tenant before serving the bytes (S1.2).
- Role-based authorisation in the API: Viewer → Contributor →
  Coordinator → Manager → Admin → Owner. SecurityOfficer is a
  separation-of-duties role that can revoke sessions but not edit
  data.

### 4.3 Audit

Every write operation lands a row in `AuditLogs` with a SHA-256
chain link to the previous row's hash (S1.8). The chain is
verifiable per-tenant via `SELECT verify_audit_chain('<uuid>');` —
the function returns NULL when the chain is intact or the row id
of the first inconsistency.

## 5. Data lifecycle

### 5.1 Collection

Only the data we need to operate the service. Personal data on each
user limited to: email, display name, locale, last-login timestamp,
device push tokens. No tracking pixels, no advertising IDs, no
cross-site analytics.

### 5.2 Storage

- Tenant business data: Hetzner Frankfurt (DE) — Postgres + MinIO
- Off-site backups: Backblaze B2 (US-West) — encrypted snapshots
- Cold archive: Backblaze B2 Archive tier after 90 d
- Plugin telemetry (opt-in): customer-controlled local file or the
  customer's own OTLP collector

Encryption-at-rest is provider-managed (Hetzner SED disks; Backblaze
server-side AES-256). Encryption keys never leave the provider.

### 5.3 Retention

- Active tenant data: indefinite
- Frozen tenant data (trial expired, payment lapsed): 30 days
  beyond the trigger event, then automatic hard-delete (S7.4.1)
- Backups: 90 days hot, then archived 12 months, then purged
- Audit log: monthly partitions; hot in-DB for 12 months, then
  exported to S3 Parquet for an additional 7 years (UDA / KRA
  retention requirements for accounting records)
- Telemetry (opt-in): customer-determined

### 5.4 Disposal

- Soft delete for user-driven removals (issue closure, document
  archive) — recoverable for 30 days from the tenant admin UI
- Hard delete via DataErasureJob (S7.4.1) walks 38 tenant-scoped
  tables in FK-respecting order + the storage prefix
- Deletion-of-evidence trail: every erasure logs a `tenant.erased`
  row on the platform tenant, surviving the deletion of the erased
  tenant

## 6. Operations security

### 6.1 Patching

- Operating system: Ubuntu LTS, unattended-upgrades enabled
- .NET runtime: tracked against the active LTS, upgraded on its
  schedule
- Dependencies: Dependabot proposes updates weekly; security patches
  applied within 72 h of publication, others reviewed monthly

### 6.2 Backup

Daily `pg_dump` + `litestream` continuous WAL ship to Backblaze B2.
Quarterly fire drill restores from a fresh box (`docs/runbooks/fire-drill.md`).
RPO 30 s (replica) → 24 h (B2 fallback). RTO 15 min (replica
promotion) → 2 h (full B2 restore).

### 6.3 Logging + monitoring

- Serilog → local files + optional Seq aggregator
- Prometheus metrics exposed at `/metrics`; Grafana dashboards via
  the observability profile (`docker compose --profile observability`)
- SLA burn-rate alerts (S7.2) page the founder when 99.5% target
  budget burns >14× in 1 h or >6× in 6 h
- Public status page at `/api/status/html` for customer-facing
  awareness during incidents

### 6.4 Incident response

Runbook at `docs/runbooks/fire-drill.md` covers five tiers from
single-component degradation to total dataloss. Reportable
incidents (data leak, prolonged downtime, billing error >$1k) are
posted on the public status page within 4 h, followed by a written
post-mortem within 5 business days.

Tenant breach notification: 72 h from confirmation, per GDPR Art.
33 / POPIA Reg. 22.

## 7. Communications security

- All API endpoints HTTPS only (HSTS preload list pending stable
  cert; HSTS header already set with 1-year max-age)
- TLS 1.3 only at the edge (Caddy config)
- Webhook payloads signature-verified (Stripe v1 HMAC; Flutterwave
  verif-hash) before any state mutation (S2.2 / S2.3)
- Per-tenant rate-limit policy (S7.6) prevents one tenant's
  automation-account misbehaviour from degrading service for others

## 8. Supplier management

We use third-party services for hosting, payments, email, and CDN.
Each supplier is reviewed annually for:

- Data Processing Agreement (or equivalent contract clause)
- Country of processing (data-residency considerations)
- Sub-processor list
- Their own breach-notification commitments

Live matrix in `iso27001-controls.md` § G.

## 9. Compliance

Active legislation we operate under:

| Law | Jurisdiction | Compliance posture |
|---|---|---|
| Uganda Data Protection and Privacy Act 2019 | UG | Registered as a data controller with PDPO; data-rights endpoints implemented (S7.4) |
| POPIA (Protection of Personal Information Act) | ZA | 30-day cooling-off on erasure (S7.4) satisfies §24 |
| GDPR | EU clients | Data-subject access + erasure endpoints; breach-notification window 72 h |

ISO 27001 controls register: `iso27001-controls.md`. Not certified
in v1 (cost-prohibitive at our scale); evidence pack supports
buyer-side security questionnaires.

## 10. Review

This policy is reviewed:

- Annually by the founder (calendar reminder Q4)
- After every reportable incident
- When a new supplier is onboarded
- When a new jurisdiction is entered

Changes land via PR. The PR description must reference which
section changed and why.

---

**Owner**: Founder, Planscape Ltd
**Last reviewed**: April 2026
**Next review**: April 2027
