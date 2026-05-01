# ISO 27001-light controls register

> Not certification — that costs $25 k a year and an auditor on retainer.
> This is the evidence pack we'll show enterprise buyers and security
> questionnaires. Each control points at a commit / file / runbook so
> we can answer "show me how" in 30 seconds.

## Section A — Access control (A.5, A.9)

| Control                                  | Evidence (commit / file)                                       |
|------------------------------------------|----------------------------------------------------------------|
| A.5.1 Information-security policies      | `docs/security-policy.md` (TODO)                               |
| A.9.1 Access-control policy              | `Planscape.API/Program.cs` JWT + role-based policies           |
| A.9.2 User-access management             | `TenantAdminController.Invite/Remove`                          |
| A.9.4 Multi-factor authentication        | `AuthController.Login` + TOTP option (TODO — S8.1)             |
| A.9.5 Tenant isolation                   | `S1.1` global query filter + `ITenantScoped` interface          |
| A.9.6 Storage path isolation             | `S1.2` `LocalFileStorageService.EnforceTenantOwnership`         |

## Section B — Cryptography (A.10)

| Control                                  | Evidence                                                       |
|------------------------------------------|----------------------------------------------------------------|
| A.10.1 Cryptographic policy              | TLS 1.3 only at Caddy; password hashing BCrypt cost 11         |
| A.10.2 Key management                    | Stripe webhook secret + JWT key env-var only; rotated quarterly|

## Section C — Operations (A.12, A.16)

| Control                                  | Evidence                                                       |
|------------------------------------------|----------------------------------------------------------------|
| A.12.1 Operational procedures            | `docs/runbooks/fire-drill.md` (S7.3)                            |
| A.12.3 Backup                            | Backblaze B2 nightly + WAL replication (`docker-compose`)      |
| A.12.4 Logging and monitoring            | Serilog + Seq (opt-in profile) + SLA burn alert (S7.2)         |
| A.12.6 Vulnerability management          | Dependabot (TODO); Trivy on image build (TODO)                 |
| A.16.1 Incident management               | `runbooks/fire-drill.md` Tier-by-tier playbook                 |

## Section D — Communications (A.13)

| Control                                  | Evidence                                                       |
|------------------------------------------|----------------------------------------------------------------|
| A.13.1 Network security                  | Cloudflare WAF (Stage 6 deploy plan); HSTS, no plain HTTP      |
| A.13.2 Information transfer              | Multipart uploads tenant-scoped (S1.2); webhook signatures     |

## Section E — Compliance (A.18)

| Control                                  | Evidence                                                       |
|------------------------------------------|----------------------------------------------------------------|
| A.18.1.1 Identification of legislation   | Uganda Data Protection Act 2019; POPIA; GDPR (any EU clients)  |
| A.18.1.3 Records protection               | `S1.8` audit log SHA-256 chain + `verify_audit_chain()`         |
| A.18.1.4 Privacy & PII protection         | `S7.4` data-rights endpoints (export + erasure)                 |
| A.18.2.1 Independent review              | Quarterly drill schedule (S7.3) + annual third-party pentest   |

## Section F — Asset management (A.8)

| Control                                  | Evidence                                                       |
|------------------------------------------|----------------------------------------------------------------|
| A.8.1.1 Asset inventory                   | `Planscape.csproj` + dependency lock files                      |
| A.8.2.1 Information classification        | Tenant data = Confidential; aggregates = Internal; pricing =   |
|                                          | Public                                                          |
| A.8.3.2 Disposal of media                 | `S7.4` erasure endpoint; nightly backup retention 90 days then |
|                                          | offsite tier                                                    |

## Section G — Suppliers (A.15)

| Vendor                | Service                  | Data shared                | DPA       |
|-----------------------|--------------------------|----------------------------|-----------|
| Hetzner Online        | Hosting (DE)             | All                        | Standard  |
| Backblaze             | Backups (US)             | Encrypted snapshots        | Standard  |
| Stripe                | Payments (USD/EUR/GBP)   | Customer email + amount    | DPA       |
| Flutterwave           | Payments (EA currencies) | Customer email + amount    | DPA       |
| Postmark              | Transactional email      | Recipient email            | DPA       |
| Cloudflare            | DNS + WAF (planned)      | IP addresses               | DPA       |
| Bunny.net             | CDN                      | None (signed URLs)         | Standard  |

## Section H — TODOs (open, with sprint owners)

- [ ] A.5.1 — write `docs/security-policy.md` (S7.5.1)
- [ ] A.9.4 — TOTP for Owner / Admin role (S8 follow-up)
- [ ] A.12.6 — wire Dependabot + Trivy into CI (S7.5.2)
- [ ] A.13.1 — Cloudflare WAF + rate-limit at edge (Stage 6 deploy)
- [ ] A.18.2.1 — annual external pentest (engagement queued for Q3)

## How to use this register

When a prospect's security questionnaire arrives:

1. Map each questionnaire row to a section above.
2. Paste the evidence link.
3. For TODOs, give the realistic ETA (no overcommitting — under-promise +
   beat the date).
4. Save the answered questionnaire under `docs/customer-questionnaires/`
   so the next one is easier.

This document is a living artefact. Every PR that closes a TODO ticks
it here.
