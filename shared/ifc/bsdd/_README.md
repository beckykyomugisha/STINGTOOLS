# bSDD Publication Plan

The buildingSMART Data Dictionary (bSDD) is the public registry of
classification terms that AEC tools can resolve against to interpret
cross-vendor data. This folder documents which STING enumerations get
published to bSDD, under what IRI scheme, and at what cadence.

Publication is **per-enum opt-in**, not blanket. STING's tag-grammar
codes are proprietary IP and stay private; codes that derive from
public standards (HTM, NCRP, ACR, RIBA, ISO 19650) carry public value
and get published.

See `publication_plan.json` for the machine-readable list of enums in
scope, their bSDD IRIs, classification names, and intended cadence.

## Files

| File | Purpose |
|---|---|
| `_README.md` | This file |
| `publication_plan.json` | Machine-readable plan — which enums get published, IRI scheme, version, status |
| `iri_scheme.md` | Documented IRI scheme for STING-published terms |
| `publish.py` (future) | Idempotent publish-to-bSDD tool — reads enum XMLs, posts to bSDD API |
| `verification_log.jsonl` (future) | Append-only log of bSDD publication events with timestamps + bSDD-side IDs |

## Publication policy

A STING enumeration is published to bSDD if AND only if:

1. **It derives from a public standard.** HTM, NCRP, ACR, BS 9999, BS 8300, ISO 19650, RIBA, IEC, BS EN. Internal STING taxonomies (tag-grammar PROD codes, drawing-purpose taxonomy, colour schemes) stay private.
2. **The standard owner has not already published it.** ISO 19650 cdeStates may already exist in bSDD under a buildingSMART URI — in that case STING references the existing entry rather than duplicating.
3. **A documented derivation is available** — i.e. we can cite the standard clause that defines each value.
4. **The corporate lock is computed and stable** — published bSDD terms are immutable; we don't push placeholder hashes.

## What this gives us

- Third-party tools (Solibri, BIMcollab, Navisworks) that don't know STING can resolve `StingMGSGasTypes:O2` to "Oxygen — medical grade per HTM 02-01" via bSDD lookup.
- IDS files become more portable — the value-restriction enumeration can be expressed as a bSDD IRI rather than an inline value list, so any IDS-aware validator can fetch the current term set.
- Cross-vendor projects (consultant runs Revit + STING; main contractor uses ACC + non-STING tooling) can read STING-tagged IFCs with semantic fidelity.

## What this does NOT give us

- IP protection — bSDD publication is permanent and public; treat anything pushed as open-source.
- Schema-evolution flexibility — once a bSDD IRI is published, deprecating a value is hard; you usually mint a new IRI.
- Validation enforcement — bSDD is a directory, not a validator. The IDS is still where rules live.
