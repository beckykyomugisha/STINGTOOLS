# STING IFC Property Enumerations

This folder is the **single source of truth** for every controlled
vocabulary that STING Tools writes into IFC. Each `.xml` file defines one
`IfcPropertyEnumeration` that the Pset + IDS bundle, the host plugins
(Revit, BlenderBIM, ArchiCAD, Tekla), the Planscape Server, and the
mobile app all read from.

> **Read this before editing any enum file.** A bad edit silently flips
> the corporate-lock checksum, drifts every project's IDS validation,
> and breaks round-trip across hosts.

---

## Folder layout

```
shared/ifc/enums/
├── _README.md                      (this file)
├── _schema.xsd                     (XML Schema — validates every enum file)
├── _manifest.json                  (index of all enums + checksums + bSDD IRIs)
│
├── StingDisciplineCodes.xml        Tier 1 — corporate-locked
├── StingSystemCodes.xml            Tier 1 — corporate-locked
├── StingFunctionCodes.xml          Tier 1 — corporate-locked
├── StingProductCodes.xml           Tier 1 — corporate-locked
├── StingLocationCodes.xml          Tier 1 — project-scoped template
├── StingZoneCodes.xml              Tier 1 — project-scoped template
├── StingLevelCodes.xml             Tier 1 — project-scoped template
├── StingStatusCodes.xml            Tier 1 — corporate-locked
├── StingSuitabilityCodes.xml       Tier 1 — corporate-locked
├── StingCDEStates.xml              Tier 1 — corporate-locked
├── StingRevisionTypes.xml          Tier 1 — corporate-locked
│
├── StingDrawingPurposes.xml        Tier 2 — corporate-locked
├── StingDrawingTiers.xml           Tier 2 — corporate-locked
├── StingPaperSizes.xml             Tier 2 — corporate-locked
├── StingOrientations.xml           Tier 2 — corporate-locked
├── StingDetailLevels.xml           Tier 2 — corporate-locked
├── StingColorSchemes.xml           Tier 2 — corporate-locked
├── StingCropKinds.xml              Tier 2 — corporate-locked
│
├── StingIssueTypes.xml             Tier 3 — corporate-locked
├── StingIssuePriorities.xml        Tier 3 — corporate-locked
├── StingIssueStates.xml            Tier 3 — corporate-locked
├── StingRibaStages.xml             Tier 3 — corporate-locked
├── StingWorkflowStates.xml         Tier 3 — corporate-locked
├── StingSignoffRoles.xml           Tier 3 — corporate-locked
├── StingMaintenanceFrequencies.xml Tier 3 — corporate-locked
├── StingAssetConditions.xml        Tier 3 — corporate-locked
│
├── StingHVACPressureClasses.xml    Tier 4 — corporate-locked
├── StingHVACAirDensities.xml       Tier 4 — corporate-locked
├── StingHVACSizingStrategies.xml   Tier 4 — corporate-locked
├── StingAcousticNCTargets.xml      Tier 4 — corporate-locked
├── StingPipeServices.xml           Tier 4 — corporate-locked
├── StingFireRatings.xml            Tier 4 — corporate-locked
├── StingCableCategories.xml        Tier 4 — corporate-locked
├── StingPipeMaterials.xml          Tier 4 — corporate-locked
├── StingDuctMaterials.xml          Tier 4 — corporate-locked
├── StingInsulationTypes.xml        Tier 4 — corporate-locked
├── StingHangerTypes.xml            Tier 4 — corporate-locked
├── StingWeldTypes.xml              Tier 4 — corporate-locked
├── StingSteelGrades.xml            Tier 4 — corporate-locked
├── StingConcreteGrades.xml         Tier 4 — corporate-locked
│
├── StingHealthcareFacilityProfiles.xml  Tier 5 — corporate-locked
├── StingMGSGasTypes.xml            Tier 5 — corporate-locked
├── StingPressureRegimes.xml        Tier 5 — corporate-locked
├── StingEESTypes.xml               Tier 5 — corporate-locked
├── StingMRIZones.xml               Tier 5 — corporate-locked
├── StingRadiationZones.xml         Tier 5 — corporate-locked
├── StingLigatureRatings.xml        Tier 5 — corporate-locked
├── StingObservationLOS.xml         Tier 5 — corporate-locked
├── StingHTMWaterRiskZones.xml      Tier 5 — corporate-locked
├── StingHBNDepartments.xml         Tier 5 — corporate-locked
└── StingTheatreTypes.xml           Tier 5 — corporate-locked
```

51 enum files + 1 schema + 1 manifest + this README.

---

## Tier definitions

| Tier | Scope | Edit policy |
|---|---|---|
| 1 (Day-one essentials) | Tag grammar — DISC/SYS/FUNC/PROD + spatial codes + status/suitability/CDE/revision | Corporate-locked; spatial templates ship empty for projects to fill |
| 2 (Drawing / template engine) | Drawing purpose, tier, paper size, orientation, detail, colour, crop | Corporate-locked |
| 3 (Workflow / standards) | Issue type/priority/state, RIBA stage, workflow state, signoff role, maintenance frequency, asset condition | Corporate-locked |
| 4 (Engineering domains) | HVAC pressure classes / sizing / air density, acoustic NC targets, pipe services + materials, duct materials, insulation, hangers, weld types, steel + concrete grades, cable categories, fire ratings | Corporate-locked; load when engineering disciplines active |
| 5 (Healthcare pack) | Facility profile, MGS gas types, pressure regimes, EES types, MRI zones, radiation zones, ligature ratings, observation LOS, HTM water risk zones, HBN departments, theatre types | Corporate-locked; load when PRJ_ORG_HEALTH_PACK_PROFILE_TXT set |

Future tiers (6+) would add e.g. composition / classification system
mirrors and per-domain extensions (industrial, civil, education).

---

## File format — `StingPropertyEnumeration`

Each enum file is a single XML document with this top-level structure:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<StingPropertyEnumeration xmlns="https://stingtools.io/schema/ifc/enums/v1"
                          version="1.0.0"
                          schema_version="1">
  <Identity>
    <Name>StingDisciplineCodes</Name>
    <Definition>Long human-readable definition.</Definition>
    <IfdGuid>uuid:...</IfdGuid>
    <BsddIri>https://identifier.buildingsmart.org/uri/...</BsddIri>  <!-- optional -->
  </Identity>

  <Governance>
    <Scope>corporate | project_template</Scope>
    <Origin>baseline | project</Origin>
    <SinceVersion>5.0.0</SinceVersion>
    <Maintainer>STING Tools (Planscape)</Maintainer>
    <StandardsBasis>CIBSE | Uniclass 2015 | ISO 19650-2 | HTM 02-01 | ...</StandardsBasis>
  </Governance>

  <IfcMapping>
    <PrimaryType>IfcLabel</PrimaryType>
    <UseAsEnumeratedValue>true</UseAsEnumeratedValue>
    <ApplicableIfcVersions>IFC4 IFC4X3 IFC2X3</ApplicableIfcVersions>
  </IfcMapping>

  <Values>
    <Value code="M" sentinel="false">
      <Definition>Mechanical / HVAC services</Definition>
      <SinceVersion>5.0.0</SinceVersion>
    </Value>
    <Value code="XX" sentinel="true">
      <Definition>Unknown / not yet assigned.</Definition>
      <SinceVersion>5.0.0</SinceVersion>
    </Value>
  </Values>

  <CorporateLock>
    <Sha256>HEX64</Sha256>
    <LockedAtVersion>5.0.0</LockedAtVersion>
    <Algorithm>sha256(canonical_json(values_with_metadata))</Algorithm>
  </CorporateLock>
</StingPropertyEnumeration>
```

### Per-value attributes

| Attribute | Required | Meaning |
|---|---|---|
| `code` | yes | The enumerated value as written into IFC. Case-sensitive. |
| `sentinel` | yes | `true` for `XX` / `*` / wildcard placeholders. Sentinels skip pattern validation in IDS. |
| `deprecated` | no | If present and `true`, IDS emits a warning, not an error, for 3 release grace period. |
| `deprecated_in_version` | no | Version at which deprecation began. |
| `replaced_by` | no | Code that replaces this one. |

### Sentinels and wildcards

Two reserved codes appear in almost every enumeration:

- **`XX`** — "unknown / not yet assigned". Always carried; IDS treats
  as the empty state. Tags carrying `XX` at Stage 4 fail IDS;
  at Stage 2 they pass.
- **`*`** — wildcard. Only used in routing rules
  (`DrawingRoutingRule.disciplineMatches`, `phaseMatches`, etc.) —
  never tagged onto an element. Skipped during element-level validation.

---

## Corporate lock — SHA-256 checksum protocol

Every corporate-locked enum carries a SHA-256 checksum that pins the
canonical value set at the release version. The checksum is computed
deterministically over the **canonical JSON form** of the values:

### Canonical JSON form

```json
{
  "name": "StingDisciplineCodes",
  "version": "1.0.0",
  "values": [
    { "code": "A", "sentinel": false, "since": "5.0.0" },
    { "code": "E", "sentinel": false, "since": "5.0.0" },
    ...
    { "code": "XX", "sentinel": true, "since": "5.0.0" }
  ]
}
```

**Rules**:
1. Values are sorted alphabetically by `code` (UTF-8 code-point order).
2. Boolean fields use `true` / `false` (lower case, JSON literals).
3. Only `code`, `sentinel`, `since`, `deprecated`, `replaced_by` are
   serialised — definitions and other metadata are **excluded**
   (description edits don't flip the lock).
4. JSON is serialised with `sort_keys=true`, `separators=(',', ':')`,
   no trailing newline.
5. `sha256(utf8_bytes(canonical_json))` → 64-char lower-case hex.

### Drift detection

At plugin / server load time:

1. Read the file's stored `<Sha256>` value.
2. Recompute the SHA-256 from the file's `<Values>`.
3. If they differ:
   - **If `<Scope>` is `corporate`**: the file has been tampered with.
     Flip `<Origin>` to `project`, emit drift warning. Project's local
     copy now overrides corporate baseline for this enum.
   - **If `<Scope>` is `project_template`**: expected — the project
     has populated their own values. No warning.

### Recomputing the checksum

Use `tools/enums/compute_checksums.py` (to be added with the build
tooling) — it walks every `*.xml`, computes the SHA-256 from the canonical
form, and rewrites the `<Sha256>` and `<LockedAtVersion>` fields.

> Until the build tooling lands, draft checksums in this folder are
> placeholders (`UNCOMPUTED_<name>`); the manifest tracks which need to
> be recomputed before any release tag.

---

## Project-scoped templates

Three enums in Tier 1 are **project-scoped templates** — they ship
empty (with sentinels only + example placeholders) and projects fill
the actual values:

- `StingLocationCodes` — the project's building / site codes
- `StingZoneCodes` — the project's fire / acoustic / smoke / clinical zones
- `StingLevelCodes` — the project's floor / storey codes

For these, `<Scope>` is `project_template` and `<CorporateLock>` is
absent. Each project's actual values live at:

```
<project>/_BIM_COORD/enums/StingLocationCodes.xml
<project>/_BIM_COORD/enums/StingZoneCodes.xml
<project>/_BIM_COORD/enums/StingLevelCodes.xml
```

The IDS validates that:
1. Element tag values for LOC/LVL/ZONE are members of the project's
   current enumeration; AND
2. Each LOC value matches an `IfcBuilding.Pset_StingSpatialCodes.LocationCode`
   in the same project; AND
3. Each LVL value matches an `IfcBuildingStorey.Pset_StingSpatialCodes.LevelCode`;
   AND
4. Each ZONE value matches an `IfcZone.Pset_StingSpatialCodes.ZoneCode`.

This ties the tag taxonomy to the **native IFC spatial structure** —
the tag is a reference, not a duplicate.

---

## IFC export — embedded enumerations

At IFC export time, every host plugin **also embeds** the `IfcPropertyEnumeration`
entities for the enums actually referenced by the exported elements.
The receiving party then has the enum definition inline, no external
file needed. Mechanics:

1. Plugin scans every Pset value on every exported element.
2. Collects the set of `IfcPropertyEnumeratedValue.EnumerationReference`
   references.
3. For each referenced enum, emits an `IfcPropertyEnumeration` entity
   whose `Name` matches the enum file's `<Identity>/<Name>` and whose
   `EnumerationValues` mirror the file's `<Values>` (sentinels included).

This makes every STING-emitted IFC **self-describing** in the
buildingSMART sense — no out-of-band documentation needed for any
downstream tool to interpret STING-tagged data.

---

## bSDD integration

For corporate-locked enumerations whose semantics derive from public
standards (HTM, NCRP, BS 9999, BS 8300, ISO 19650, RIBA), the enum
optionally carries a `<BsddIri>` pointing at the buildingSMART Data
Dictionary entry. Tools that don't know about STING can resolve
`StingDisciplineCodes:M` against bSDD and learn "this means Mechanical
per CIBSE Building Services Discipline Taxonomy".

Publication is optional, asymmetric, and per-enum:
- Healthcare enums (Tier 5, future) — publish (rooted in HTM/NCRP)
- Discipline / suitability / RIBA stage / CDE state — publish (rooted in ISO 19650 / RIBA)
- Drawing purposes / colour schemes / paper sizes — don't publish (STING-internal taxonomy)
- Product codes — **don't publish** (proprietary tag grammar)

The `<BsddIri>` field is left empty for unpublished enums.

---

## Adding a new value

1. Decide whether it's additive (no behaviour change for existing IFCs)
   or breaking (existing IFCs become invalid).
2. **Additive**: add `<Value>` element with `since_version` set to next
   release; recompute checksum; bump enum file version (patch increment
   `1.0.0` → `1.0.1`); manifest updated; ship in next release.
3. **Breaking** (deprecation / removal):
   - Mark old `<Value>` with `deprecated="true"` and `replaced_by="..."`.
   - Add new `<Value>` if replacing.
   - Bump enum file version major (`1.0.0` → `2.0.0`); also bump the
     enum **schema name** (`StingProductCodes` → `StingProductCodes_v2`).
   - Recompute checksum (will differ; that's expected).
   - Migration tool walks IFCs and rewrites old codes → new codes.
   - Old enum file is retained read-only in `_archive/` for IFC files
     stamped with the previous schema name.

---

## Adding a new enumeration

1. Author the XML following the format above. Use an existing file as
   template.
2. Mint a new `IfdGuid` (UUID v4).
3. Compute SHA-256 via build tooling.
4. Add an entry to `_manifest.json`.
5. Reference from the Pset XML and IDS specs that consume it.
6. Run round-trip test against sample IFC in every host.

---

## Index

See `_manifest.json` for the full machine-readable index — enum names,
file paths, IfdGuids, BsddIris, checksums, value counts, governance
metadata.
