# STING bSDD IRI Scheme

This document fixes the URI shape for STING terms published to the
buildingSMART Data Dictionary so that every consumer can resolve them
deterministically.

## Pattern

```
https://identifier.buildingsmart.org/uri/{ORG}/{DOMAIN}/{VERSION}/class/{TERM}
```

| Segment | Source of value | Examples |
|---|---|---|
| `{ORG}` | Owning publisher | `stingtools` for STING-owned terms; `nhs`, `cibse`, `bsi` etc. for standards-body-owned where STING is downstream |
| `{DOMAIN}` | Standard / taxonomy name | `sting-codes`, `healthcare`, `htm-02-01`, `iso-19650`, `riba-plan-of-work` |
| `{VERSION}` | Major version of the term-set | `1.0` (initial); incremented for breaking term changes; minor version not in URI |
| `{TERM}` | Stable per-term identifier | `discipline`, `medical-gas-type`, `pressure-regime`, `ees-type` |

Each individual value within an enumeration is then addressed as a
child IRI: `{enum-iri}/{code}`.

## Concrete examples

| STING enum | bSDD IRI (root) | Per-value example |
|---|---|---|
| StingDisciplineCodes | `https://identifier.buildingsmart.org/uri/stingtools/sting-codes/1.0/class/discipline` | `.../discipline/M` |
| StingSystemCodes | `.../stingtools/sting-codes/1.0/class/system` | `.../system/HVAC` |
| StingMGSGasTypes | `.../nhs/htm-02-01/1.0/class/medical-gas-type` | `.../medical-gas-type/O2` |
| StingPressureRegimes | `.../nhs/htm-03-01/1.0/class/pressure-regime` | `.../pressure-regime/Positive_25` |
| StingEESTypes | `.../nhs/htm-06-01/1.0/class/ees-type` | `.../ees-type/TypeA_LifeSafety` |
| StingMRIZones | `.../acr/mr-safety/2020/class/zone` | `.../zone/Zone_IV` |
| StingRadiationZones | `.../ncrp/147/2004/class/radiation-zone` | `.../radiation-zone/Controlled` |
| StingRibaStages | `.../riba/plan-of-work/2020/class/stage` | `.../stage/Stage_4` |
| StingCDEStates | `.../buildingsmart/iso-19650/1.0/class/cdestate` | `.../cdestate/SHARED` |

## Why this matters

- IDS value-restriction enumerations can target bSDD IRIs instead of
  inline strings. A consultant's IDS file then says "value must be a
  child of `.../htm-02-01/1.0/class/medical-gas-type`" and any IDS
  validator can resolve the term set at validation time.
- Cross-vendor reads of STING IFCs work without bundled STING
  documentation. A Solibri user opens a STING IFC, sees `O2` on a pipe,
  Solibri resolves the IRI in `IfcClassificationReference`, displays
  "Oxygen — medical grade per HTM 02-01". No STING-specific code path.
- bSDD provides a stable, versioned, third-party-hosted dictionary
  that outlives any individual project.

## Publication versioning

| Change kind | Outcome |
|---|---|
| Add a new value to an existing enum | New child IRI under existing root; version stays |
| Mark a value deprecated | bSDD `status` field flipped; root + IRI unchanged |
| Rename a value | DO NOT — mint a new value with new IRI; deprecate old |
| Remove a value | Mark deprecated; never delete (other tools may reference it) |
| Change semantics of an existing value | New ROOT version (e.g. `2.0/class/discipline`); old root deprecated |

Once an IRI is live and referenced by any external IFC, it cannot be
recycled. This is the core "you only get one chance to get the IRI right"
constraint of bSDD publication — it's why we publish carefully, with
SHA-256-locked corporate snapshots, not casually.
