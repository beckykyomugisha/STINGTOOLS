# Data rights — GDPR, DPPA, and POPIA

Planscape complies with three primary data-protection regimes, each of which gives data subjects the same core set of rights:

- **Uganda Data Protection and Privacy Act 2019 (DPPA)** — applies to all our customers and to data we process about them
- **EU General Data Protection Regulation (GDPR)** — applies to data subjects in the EU/EEA
- **South African Protection of Personal Information Act (POPIA)** — applies to South African data subjects

This guide tells you how to exercise your rights, what we'll do, and how long it takes.

## Your rights at a glance

Under all three regimes you have the right to:

- **Access** — see what data we hold about you
- **Rectification** — correct inaccurate data
- **Erasure** — have your data deleted, subject to legal exceptions
- **Portability** — receive your data in a machine-readable format
- **Objection** — object to certain types of processing (e.g. legitimate-interest analytics)
- **Withdraw consent** — for any processing based on consent (e.g. marketing emails)

## Submit a request

Email <privacy@planscape.app> with one of the following subjects:

| Subject line | What we do |
|---|---|
| `DSAR` | Data Subject Access Request — we send you a complete export of your personal data |
| `Erasure Request` | Account-data erasure — we delete what we can, redact what we must keep |
| `Rectification` | Correction — we update the field you specify |
| `Portability` | Export — same as DSAR but specifically for transferring to another service |
| `Objection` | Stop processing under legitimate interests |
| `Withdraw Consent — Marketing` | Unsubscribe from marketing email |

Include enough information to identify your account — the email address you signed up with is enough in most cases. We may ask for additional verification (e.g. a confirmation email loop) if we cannot verify identity from the request alone.

## Response times

- **Acknowledgement** within **5 working days**
- **Substantive response** within **30 calendar days** (extendable by 60 days for complex requests, with notification)

These are the maximum windows under DPPA 2019, GDPR Article 12, and POPIA s23. We aim to respond significantly faster — most DSARs are completed within a week.

## Data portability — what you get

Project owners can export their entire project data from the dashboard themselves: **Settings → Export**. The exports cover:

- **JSON** (complete) — everything Planscape knows about your projects: tags, issues, documents (metadata + content), audit log, team membership, configuration. Suitable for re-import into another Planscape instance or for archival.
- **CSV** — flat-tabular dumps of elements and issues. Useful for spreadsheet analysis.
- **BCF 2.1** — issues only, in BIM Collaboration Format. Suitable for import into ACC, Solibri, Navisworks, and other BCF-compatible tools.

Document content (PDFs, RVTs, images) is included as a ZIP alongside the metadata.

## Erasure — what's possible, what isn't

We will erase, on request:

- Your account data — name, email, role, password hash
- Personal preferences — theme, notification settings, recently-opened items
- Support correspondence (chat transcripts, email threads)
- Device push tokens
- Marketing-list membership and consent records

We **cannot** fully erase, because of legal/contract retention requirements:

- **Audit log records** — these are part of an immutable hash chain that satisfies ISO 19650-2 §5.6. Erasing them would invalidate the chain and breach our contract with your appointing party. Instead, we redact the `payload` field of records relating to you, leaving the chain intact. The redaction itself is recorded.
- **Billing records** — Uganda Income Tax Act requires retention of accounting records for 6 years. We retain invoice records but can remove your name from non-statutory fields.
- **Legal-hold data** — if a record is subject to a court order or pending litigation, we must retain it until the hold is released.

We will tell you exactly what we erased, what we redacted, and what we retained — with the legal basis for retention.

## Cross-border transfers

Our primary data region is the EU (Frankfurt, Germany). Standard contractual clauses (SCCs) cover transfers between Planscape Ltd (Uganda) and our infrastructure providers (EU). For South African data subjects, transfers to the EU rely on the Information Regulator's general authorisation under POPIA s72 and our SCCs.

If your firm requires that all data stays in-country (Uganda, South Africa, or elsewhere), our [self-host option](self-host.md) lets you run the platform on infrastructure you control.

## Supervisory authorities

If you are not satisfied with our handling of a data-rights request, you can complain to:

- **Uganda** — Personal Data Protection Office of Uganda (PDPO) — <https://pdpo.go.ug>
- **EU/EEA** — your country's national data protection authority. The list is at <https://edpb.europa.eu/about-edpb/board/members_en>
- **South Africa** — Information Regulator — <https://inforegulator.org.za>

We'd appreciate the chance to address any concern directly first — but the right to complain is yours unconditionally.

## Children's data

Planscape is a B2B product not directed at children. We do not knowingly process personal data of children under 16. If you believe we hold data about a child, contact <privacy@planscape.app> and we will erase it without verification of the standard DSAR identity check.

## Related

- [Privacy Policy](https://planscape.app/privacy) — the full picture of what we collect, why, and for how long
- [Verify the audit log](audit-verify.md) — what the chain is and why erasure interacts with it
