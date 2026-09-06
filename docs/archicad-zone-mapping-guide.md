# ArchiCAD Zone Mapping Guide

## The Problem

ArchiCAD exports two distinct pieces of information about a Zone to IFC:

| ArchiCAD source | IFC pset property | What it contains |
|---|---|---|
| Zone Number / Zone Code | `Pset_ZoneCommon.Reference` | **Spatial designator** — e.g. `Z01`, `Z02`, `ZZ` |
| Zone Category | `AC_Pset_ZoneCategory.ZoneCategoryCode` | **Functional category** — e.g. `OFFICE`, `WARD`, `PLANT` |

These serve completely different roles in the STING tag grammar:

- **`ASS_ZONE_TXT` (ZONE token, segment 3)** — identifies *which spatial zone* an element belongs to. It must match the `ZoneCode` stamped on the `IfcZone` entity the element is assigned to via `IfcRelAssignsToGroup`. Correct source: `Pset_ZoneCommon.Reference`.
- **`ASS_FUNC_TXT` (FUNC token, segment 6)** — identifies the *functional purpose* of the element or space. Correct source: `AC_Pset_ZoneCategory.ZoneCategoryCode`.

Mapping `ZoneCategoryCode` to `ASS_ZONE_TXT` is **incorrect** — it produces functional codes like `WARD` in the ZONE position, which will fail the STING IDS cross-entity rule `ZONE_MATCHES_ASSIGNEDZONE`.

---

## Correct Mapping (as of v2.0 — Phase 195)

```json
{ "pset_name": "Pset_ZoneCommon",       "property_name": "Reference",        "sting_param": "ASS_ZONE_TXT", "element_types": ["IFCZONE"] },
{ "pset_name": "AC_Pset_ZoneCategory",  "property_name": "ZoneCategoryCode", "sting_param": "ASS_FUNC_TXT", "element_types": ["IFCZONE", "IFCSPACE"] }
```

---

## Example

An ArchiCAD zone named **"Z01 — Ward"** exports to IFC as:

```
IfcZone
  Pset_ZoneCommon.Reference = "Z01"
  AC_Pset_ZoneCategory.ZoneCategoryCode = "WARD"
  AC_Pset_ZoneCategory.ZoneCategoryName = "Ward / Inpatient"
```

STING reads this as:
- `ASS_ZONE_TXT` = `Z01` ← from `Pset_ZoneCommon.Reference`
- `ASS_FUNC_TXT` = `WARD` ← from `AC_Pset_ZoneCategory.ZoneCategoryCode`

The resulting STING tag segment for a duct in this zone: `M-BLD1-**Z01**-L02-HVAC-**WARD**-AHU-0001`

---

## ArchiCAD Translator Settings

To ensure `Pset_ZoneCommon` is exported by ArchiCAD:
1. Go to **File → Publish → IFC Settings → Translators**
2. Select the active translator and open **Properties**
3. Under **IFC Properties**, ensure **"Export Zone Properties"** is checked
4. The `Pset_ZoneCommon` pset is exported when Zone properties are enabled

To ensure `AC_Pset_ZoneCategory` is exported (ArchiCAD-native pset):
- This is exported by default when zones exist — no additional setting required.

---

## IfcSpace vs IfcZone

Note: for `IfcSpace`, the `ASS_ZONE_TXT` mapping comes from `Pset_SpaceCommon.Reference`
(room number), not from `Pset_ZoneCommon.Reference`. The zone-to-space assignment is via
`IfcRelAssignsToGroup`. STING's cross-entity validator checks this assignment relationship
rather than the property value for space elements.
