# Spatial-Codes Rule Encoding — companion to `sting-spatial-codes.ids`

This document explains how the 5 `<Rule>` elements declared in
`shared/ifc/psets/Pset_StingSpatialCodes.xml` map to IDS specifications in
`sting-spatial-codes.ids`. IDS v1.0 cannot express every shape directly;
this document records each encoding decision and the place where STING
server-side validators close any gap.

## Encoding overview

Each cross-entity rule is split into **partial specs**: one that
validates the value's format / membership of the enumeration, and a
second that validates the structural relationship (containment /
assignment) on the IFC graph. A full `value-equals-property-of-related-entity`
check is closed by a STING-side validator in `stingtools-core` that
walks the IFC graph after IDS validation passes.

## Per-rule encoding

### Rule LOC_MATCHES_BUILDING

> An element's `Pset_StingTags.Location` must equal the `LocationCode`
> of the `IfcBuilding` the element is contained in (or its `IfcSite`).
> Active from Stage_3.

| Spec id | Encoding | Coverage |
|---|---|---|
| `01a-LOC-IS-ENUM` | Property facet: `Pset_StingTags.Location` is required, of type `IfcLabel`, matches enumeration pattern, non-empty | Full |
| `01b-LOC-PARTOF-BUILDING` | PartOf facet: element must be in an `IfcRelContainedInSpatialStructure` ending at an `IfcBuilding`; Property facet: that building must carry `Pset_StingSpatialCodes.LocationCode` | Structural — does not check equality |
| **STING-side close-out** | After IDS pass, `stingtools-core.spatial_check.locations_equal(element, building)` walks the IFC graph and asserts `element.LOC == containing_building.LocationCode`. Logs a Planscape audit entry on mismatch. | Equality |

### Rule LVL_MATCHES_STOREY

> An element's `Pset_StingTags.Level` must equal the `LevelCode` of the
> `IfcBuildingStorey` the element is contained in via
> `IfcRelContainedInSpatialStructure`. Active from Stage_3.

| Spec id | Encoding | Coverage |
|---|---|---|
| `02a-LVL-IS-ENUM` | Property facet on `Pset_StingTags.Level` — required, non-empty, pattern-restricted | Full |
| `02b-LVL-PARTOF-STOREY` | PartOf `IfcRelContainedInSpatialStructure` → `IfcBuildingStorey`; that storey must have `Pset_StingSpatialCodes.LevelCode` | Structural |
| **STING-side close-out** | `stingtools-core.spatial_check.levels_equal(element, storey)` asserts equality after IDS pass. | Equality |

### Rule ZONE_MATCHES_ASSIGNEDZONE

> An element's `Pset_StingTags.Zone` must equal the `ZoneCode` of at least
> one `IfcZone` the element is assigned to via `IfcRelAssignsToGroup`.
> Active from Stage_3.

| Spec id | Encoding | Coverage |
|---|---|---|
| `03a-ZONE-IS-ENUM` | Property facet on `Pset_StingTags.Zone` — required, non-empty | Full |
| `03b-ZONE-ASSIGNED` | PartOf `IfcRelAssignsToGroup` → `IfcZone`; that zone must have `Pset_StingSpatialCodes.ZoneCode` | Structural — verifies the element is in *some* IfcZone with a ZoneCode; doesn't verify the *specific* zone matches |
| **STING-side close-out** | `stingtools-core.spatial_check.zone_member(element)` walks every assigned IfcZone and asserts at least one carries the element's Zone token. Multi-zone membership supported (fire + acoustic + clinical). | Equality with set-membership semantics |

### Rule BUILDING_LOC_UNIQUE

> Every LocationCode in the project must be unique across IfcBuildings.
> Active from Stage_2.

| Spec id | Encoding | Coverage |
|---|---|---|
| `04-BUILDING-HAS-LOC` | Property facet on every `IfcBuilding` requiring `LocationCode` to be present and non-empty | Presence only |
| **STING-side close-out** | At `/api/projects/{id}/ifc/data` ingest, Planscape Server builds a `LocationCode → [IfcBuilding GUIDs]` index. Any code with >1 building emits an IDS-style violation report keyed off this rule id. | Uniqueness |

### Rule STOREY_LVL_UNIQUE_WITHIN_BUILDING

> Every LevelCode in the project must be unique within an IfcBuilding.
> Two storeys in the same building cannot share a code. Active from
> Stage_2.

| Spec id | Encoding | Coverage |
|---|---|---|
| `05-STOREY-HAS-LVL` | Property facet on every `IfcBuildingStorey` requiring `LevelCode` non-empty | Presence only |
| **STING-side close-out** | Server ingest builds a `(IfcBuilding GUID, LevelCode) → [IfcBuildingStorey GUIDs]` index. Any tuple with >1 storey violates. | Within-building uniqueness |

## IDS v1.0 limitations encountered

1. **No cross-entity equality** — IDS can require "element X has property P" and "element X is partOf entity Y" but not "X.P equals Y.Q". The STING-side closeout is unavoidable until IDS v2.0 adds something like a `referenceFacet`.

2. **No global uniqueness** — IDS specs are per-element; uniqueness across a collection of elements is out of scope. Server-side ingestion provides this.

3. **No conditional applicability based on container's property** — would let us write "if container.purpose=Healthcare then property X is required". IDS supports applicability `Property` facets but only on the element itself, not on related entities. Workaround: stage-aware overlay IDS files (`sting-stage{N}-*.ids`) that pre-filter the population.

4. **Enumeration restriction without bSDD IRI** — IDS specs that reference enums by bSDD IRI auto-update as the enumeration evolves. Until our enums are actually posted to bSDD (currently all carry `proposed: true`), the IDS files use inline patterns / value lists instead.

## Test strategy

Once `ifctester` is available in `tools/`:

```
# IDS-side validation
ifctester shared/ifc/ids/sting-spatial-codes.ids tests/fixtures/spatial_codes_ok.ifc
ifctester shared/ifc/ids/sting-spatial-codes.ids tests/fixtures/spatial_codes_mismatch.ifc

# STING-side closeout
python -m stingtools_core.spatial_check tests/fixtures/spatial_codes_mismatch.ifc
```

Both tools should produce structured-JSON results that the Planscape
server consumes as `BimIssue` records for any failure.
