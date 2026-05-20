# STING IDS (Information Delivery Specifications)

This folder holds the buildingSMART IDS files that validate IFC payloads
against STING's Pset + enumeration contracts.

## Files

| File | Purpose |
|---|---|
| `_README.md` | This file |
| `sting-spatial-codes.ids` | IDS encoding of the 5 cross-entity validation rules declared in `Pset_StingSpatialCodes.xml`. Validates that every element's tag tokens match the spatial structure they live in. |
| `sting-spatial-codes-rules.md` | Hand-written companion explaining how each Pset `<Rule>` element maps to an IDS specification (facets, restrictions, cardinality), for cases where the IDS XML alone isn't self-explanatory. |
| (future) `sting-tag-grammar.ids` | Validates the 8-segment tag against StingDisciplineCodes / SystemCodes / FunctionCodes / ProductCodes pattern + enumeration restrictions. |
| (future) `sting-stage{N}-*.ids` | Stage-aware overlays per RIBA stage. |
| (future) `sting-healthcare.ids` | Validates Tier 5 healthcare Psets (when authored). |

## Encoding conventions

- All IDS files target `IDS schema v1.0` (April 2024 buildingSMART release).
- The `ifcVersion` attribute on each spec lists the IFC versions the rule
  applies to — usually `IFC4 IFC4X3` (`IFC2X3` is too old to encode some
  of the entity relationships STING relies on).
- Identifiers are stable UUIDs so audit can reference specs by ID
  forever.
- Descriptions are written for non-technical reviewers; instructions
  give specific remediation paths.

## Running validation

Once `ifctester` is available:

```
ifctester sting-spatial-codes.ids <sample.ifc>
```

CI integration is the natural next step — every PR runs `ifctester`
against the test-fixture IFCs in `tests/fixtures/` and fails on any
non-compliant element.

## Limitations of IDS v1.0

A handful of STING validation rules can be expressed in IDS but only
*loosely*:

- **Cross-entity equality** (e.g. "element's LOC must equal an
  IfcBuilding's LocationCode in the same project") — IDS supports
  `partOf` facets but the "equal to a property *on the container*"
  shape isn't first-class. Workaround: emit a chained spec — first
  spec checks "element is contained in a building", second spec checks
  "the LOC value is non-empty and from the enumeration". A custom
  validator in `stingtools-core` closes the equality check that IDS
  cannot express directly.

- **Uniqueness across entities** (e.g. "all LocationCodes in the
  project are unique") — IDS is per-element. Uniqueness is enforced by
  a STING server-side check during IFC ingest, not by IDS.

These limitations are documented per-rule in `sting-spatial-codes-rules.md`.
