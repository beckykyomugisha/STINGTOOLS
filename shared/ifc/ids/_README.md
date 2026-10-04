# STING IDS (Information Delivery Specifications)

This folder holds the buildingSMART IDS files that validate IFC payloads
against STING's Pset + enumeration contracts.

## Files

| File | Purpose |
|---|---|
| `_README.md` | This file |
| `sting-tag-grammar.ids` | Validates the 8-segment tag against `Pset_StingTags` — DISC / LOC / ZONE / LVL / SYS / FUNC / PROD / SEQ format + enum membership + FullTag + TokenLock + ModifiedAt grammar. 11 specs. |
| `sting-spatial-codes.ids` | IDS encoding of the cross-entity validation rules declared in `Pset_StingSpatialCodes.xml`. Validates that every element's tag tokens match the spatial structure they live in. 8 specs. |
| `sting-spatial-codes-rules.md` | Hand-written companion explaining how each Pset `<Rule>` element maps to an IDS specification (facets, restrictions, cardinality). |
| `sting-drawing.ids` | Validates `Pset_StingDrawing` (format side) — DrawingTypeId grammar, CropKind / ColourScheme enum membership, CropMarginMm range, PackChecksum SHA-256 grammar, TagDepth 1..10. 6 specs. The behavioural rules (`CROP_KIND_MATCHES_PROFILE`, `PACK_CHECKSUM_MATCHES`) live on the host plugin. |
| `sting-tag7.ids` | Validates `Pset_StingTag7` length bounds across the 7 narrative parts (NarrativeFull + 6 sub-sections). 7 specs. The behavioural rules (`TAG7_*`) are TAG7Builder contracts. |
| `sting-project-org.ids` | Validates `Pset_StingProjectOrg` — ProjectCode + OriginatorCode pattern, Phase enum membership, CompanyName + ClientName non-empty bounds, WorkflowProfile snake_case format. 6 specs. The `PROJECTORG_SINGLETON` rule is host-enforced. |
| `sting-coordination.ids` | `Pset_StingCoordination` — data type of every property; ClashSeverity 1..5, ModelHealthScore 0..100. 7 specs. |
| `sting-lightning-protection.ids` | `Pset_StingLightningProtection` — data type of every property; LightningProtectionClass I..IV, counts and intervals non-negative. 24 specs. |
| `sting-mep-services.ids` | `Pset_StingMEPServices` — data type of every property. 3 specs. |
| `sting-placement.ids` | `Pset_StingPlacement` — data type of every property; PlacementSide LEFT / RIGHT / EITHER. 3 specs. |
| `sting-mep.ids` | `Pset_StingMEP` (written by the Bonsai add-on) — data type of every property; ELC_FILL_STATUS OK / OVERLOADED. Files written before 5.8.0 hold the numbers as IfcLabel and fail the type checks until re-run. 9 specs. |
| (future) `sting-stage{N}-*.ids` | Stage-aware overlays per RIBA stage. |
| (future) `sting-healthcare.ids` | Validates Tier 5 healthcare Psets (Phase 187). |

## Encoding conventions

- All IDS files target `IDS schema v1.0` (April 2024 buildingSMART release).
- The `ifcVersion` attribute on each spec lists the IFC versions the rule
  applies to — usually `IFC4 IFC4X3` (`IFC2X3` is too old to encode some
  of the entity relationships STING relies on).
- Identifiers are stable UUIDs so audit can reference specs by ID
  forever.
- Descriptions are written for non-technical reviewers; instructions
  give specific remediation paths.

`stingtools-core/python/tests/test_ids_contract.py` holds every Pset_Sting* property facet in these files to its template (declared, same data type) and requires an IDS for every template. It also validates every file against the buildingSMART IDS 1.0 schema (offline, vendored in `stingtools-core/python/tests/ids_schema/`), so a schema-invalid IDS fails CI (multi-host-core.yml). IDS 1.0 has no severity attribute: an advisory specification says so in its description and uses `applicability minOccurs="0"`.

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
