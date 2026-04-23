# TIER_VARIATION_MATRIX.md — Per-family T4 to T10 tier decisions

Source of truth for `PerFamilyTierMap.cs` and the CSV regenerator (`regenerate_tag_config_csvs.py`).
Each cell is one of:

| Cell | Meaning |
|---|---|
| `KEEP` | Use the default 3-row block for that tier (COMM/CST/CBN/ASS_SPOOL/CLASH/ASBUILT/IFC). |
| `OMIT` | Suppress all rows for that tier **and** drop the `TAG_PARA_STATE_N_BOOL` from `VisibilityParams` for this family. |
| `REPLACE:<SET>` | Replace the default 3-row block with the named set from Section 2 of the task brief. |

## Classification summary

- **Annotation** (OMIT T4/T6/T7/T8/T9/T10, KEEP T5): 4 families — the 4 sheet-document tags.
- **Spatial / analytical** (OMIT T7+T9, KEEP rest): 26 families — rooms, spaces, zones, areas, loads, analytical, groups/links/property lines.
- **Physical default** (all KEEP): 93 families.
- **Physical with REPLACE** (at least one REPLACE set): 19 families.
- **Total:** 142 families.

## REPLACE sets referenced

| Set ID | Applies to (family names) | Description |
|---|---|---|
| `STR_REBAR_T7` | Structural Rebar, Rebar Couplers, Fabric/Area/Path Reinforcement | Bar mark + BBS ref + cutting list ref (BS 8666:2020). |
| `STR_CONN_T7` | Structural Connection | WPS + bolt torque + reused ASS_QC_INSPECTOR_TXT (BS EN ISO 15609-1, BS EN 1090-2). |
| `HVC_REFRIG_T7` | Mechanical Equipment, Mechanical Equipment Sets | Refrigerant charge + factory flash test date + factory QR (BS EN 378-2:2016). |
| `ELC_PANEL_T7` | Electrical Equipment | Panel schedule ref + FAT cert + factory QR (BS 7671:2018+A2:2022, IEC 61439-1). |
| `PLM_PRESSURE_T7` | Plumbing Equipment, MEP Fabrication Pipework | Hydrostatic test cert + weld map + reused ASS_QC_INSPECTOR_TXT (BS EN 806-4, BS EN ISO 15609-1). |
| `ARC_DOOR_WIN_T7` | Door, Window, Curtain Panel | Factory order + glazing spec + reused ASS_WARRANTY_DURATION_PARTS_YRS (BS 8000-5, BS 6262-1). |
| `CBN_OPERATIONAL_T6` | Electrical Equipment, Lighting Device/Fixture, Mechanical Control Devices, Fire Protection, Audio Visual Devices | Reused CBN_B6_KG_CO2E_YR + CBN_A1_A3_KG_CO2E + new CBN_RUNTIME_HRS_YR_NR (BS EN 16798-1). |
| `CBN_REFRIG_T6` | Mechanical Equipment, Mechanical Equipment Sets | CBN_OPERATIONAL_T6 + CBN_REFRIGERANT_GWP_KG_CO2E (BS EN 378-2). Overrides CBN_OPERATIONAL_T6. |
| `ASBUILT_REBAR_T9` | Structural Rebar, Rebar Couplers, Fabric/Area/Path Reinforcement | ASBUILT_COVER_DEVIATION_MM + reused ASBUILT_CAPTURE_DATE_TXT + reused HEALTH_SCORE_LAST_NR (BS EN 13670). |

## Per-family matrix (142 rows)

| Family | Category | Class | T4 | T5 | T6 | T7 | T8 | T9 | T10 |
|---|---|---|---|---|---|---|---|---|---|
| STING - Wall Tag | Walls | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Floor Tag | Floors | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Ceiling Tag | Ceilings | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Roof Tag | Roofs | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Door Tag | Doors | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:ARC_DOOR_WIN_T7 | KEEP | KEEP | KEEP |
| STING - Window Tag | Windows | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:ARC_DOOR_WIN_T7 | KEEP | KEEP | KEEP |
| STING - Room Tag | Rooms | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Stair Tag | Stairs | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Ramp Tag | Ramps | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Railing Tag | Railings | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Furniture Tag | Furniture | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Casework Tag | Casework | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Curtain Panel Tag | Curtain Panels | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:ARC_DOOR_WIN_T7 | KEEP | KEEP | KEEP |
| STING - Curtain Wall Mullion Tag | Curtain Wall Mullions | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Signage Tag | Signage | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Parking Tag | Parking | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Areas Tag | Areas | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Wall Sweeps Tag | Wall Sweeps | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Slab Edges Tag | Slab Edges | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Roof Soffits Tag | Roof Soffits | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Fascia Tag | Fascia | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Gutter Tag | Gutters | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Mass Tag | Mass | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Furniture Systems Tag | Furniture Systems | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Food Service Equipment Tag | Food Service Equipment | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Medical Equipment Tag | Medical Equipment | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Entourage Tag | Entourage | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Stair Runs Tag | Stair Runs | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Stair Landings Tag | Stair Landings | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Stair Supports Tag | Stair Supports | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Top Rails Tag | Top Rails | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Handrails Tag | Handrails | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Vertical Circulation Tag | Vertical Circulation | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Architectural Sheet Tag | Sheets — Architectural discipline | ANNOTATION | OMIT | KEEP | OMIT | OMIT | OMIT | OMIT | OMIT |
| STING - Architectural Column Tag | Columns — Architectural discipline | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Generic Models Tag | Generic Models | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Specialty Equipment Tag | Specialty Equipment | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Site Tag | Site | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Planting Tag | Planting | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Hardscape Tag | Hardscape | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Roads Tag | Roads | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Pads Tag | Pads | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Toposolid Tag | Toposolid | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Parts Tag | Parts | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Assemblies Tag | Assemblies | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Detail Items Tag | Detail Items | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Profiles Tag | Profiles | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Materials Tag | Materials | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Point Loads Tag | Point Loads | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Line Loads Tag | Line Loads | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Area Loads Tag | Area Loads | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Internal Point Loads Tag | Internal Point Loads | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Internal Line Loads Tag | Internal Line Loads | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Internal Area Loads Tag | Internal Area Loads | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Analytical Members Tag | Analytical Members | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Analytical Nodes Tag | Analytical Nodes | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Analytical Panels Tag | Analytical Panels | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Analytical Openings Tag | Analytical Openings | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Analytical Links Tag | Analytical Links | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Model Groups Tag | Model Groups | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - RVT Links Tag | RVT Links | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Property Lines Tag | Property Lines | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Property Line Segments Tag | Property Line Segments | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Bolt Tag | Bolt | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Toposolid Links Tag | Toposolid Links | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Weld Tag | Weld | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Wire Tag | Wire | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Sheet Document Tag | Sheets (ViewSheet) | ANNOTATION | OMIT | KEEP | OMIT | OMIT | OMIT | OMIT | OMIT |
| STING - Air Terminal Tag | Air Terminals | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Duct Accessory Tag | Duct Accessories | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Duct Fitting Tag | Duct Fittings | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Duct Tag | Ducts | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Flex Duct Tag | Flex Ducts | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Mechanical Equipment Tag | Mechanical Equipment | PHYSICAL | KEEP | KEEP | REPLACE:CBN_REFRIG_T6 | REPLACE:HVC_REFRIG_T7 | KEEP | KEEP | KEEP |
| STING - Flex Pipe Tag | Flex Pipes | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Pipe Accessory Tag | Pipe Accessories | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Pipe Fitting Tag | Pipe Fittings | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Pipe Tag | Pipes | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Plumbing Fixture Tag | Plumbing Fixtures | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Fire Alarm Device Tag | Fire Alarm Devices | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Sprinkler Tag | Sprinklers | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Cable Tray Fitting Tag | Cable Tray Fittings | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Cable Tray Tag | Cable Trays | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Conduit Fitting Tag | Conduit Fittings | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Conduit Tag | Conduits | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Electrical Equipment Tag | Electrical Equipment | PHYSICAL | KEEP | KEEP | REPLACE:CBN_OPERATIONAL_T6 | REPLACE:ELC_PANEL_T7 | KEEP | KEEP | KEEP |
| STING - Electrical Fixture Tag | Electrical Fixtures | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Lighting Device Tag | Lighting Devices | PHYSICAL | KEEP | KEEP | REPLACE:CBN_OPERATIONAL_T6 | KEEP | KEEP | KEEP | KEEP |
| STING - Lighting Fixture Tag | Lighting Fixtures | PHYSICAL | KEEP | KEEP | REPLACE:CBN_OPERATIONAL_T6 | KEEP | KEEP | KEEP | KEEP |
| STING - Communication Device Tag | Communication Devices | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Data Device Tag | Data Devices | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Nurse Call Device Tag | Nurse Call Devices | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Security Device Tag | Security Devices | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Telephone Devices Tag | Telephone Devices | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Generic Model Tag | Generic Models | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Specialty Equipment Tag | Specialty Equipment | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Duct Insulation Tag | Duct Insulation | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Duct Lining Tag | Duct Lining | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Pipe Insulation Tag | Pipe Insulation | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Plumbing Equipment Tag | Plumbing Equipment | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:PLM_PRESSURE_T7 | KEEP | KEEP | KEEP |
| STING - Mechanical Control Devices Tag | Mechanical Control Devices | PHYSICAL | KEEP | KEEP | REPLACE:CBN_OPERATIONAL_T6 | KEEP | KEEP | KEEP | KEEP |
| STING - Mechanical Equipment Sets Tag | Mechanical Equipment Sets | PHYSICAL | KEEP | KEEP | REPLACE:CBN_REFRIG_T6 | REPLACE:HVC_REFRIG_T7 | KEEP | KEEP | KEEP |
| STING - Electrical Connectors Tag | Electrical Connectors | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Fire Protection Tag | Fire Protection | PHYSICAL | KEEP | KEEP | REPLACE:CBN_OPERATIONAL_T6 | KEEP | KEEP | KEEP | KEEP |
| STING - MEP Fabrication Containment Tag | MEP Fabrication Containment | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - MEP Fabrication Ductwork Tag | MEP Fabrication Ductwork | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - MEP Fabrication Ductwork Stiffeners Tag | MEP Fabrication Ductwork Stiffeners | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - MEP Fabrication Hangers Tag | MEP Fabrication Hangers | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - MEP Fabrication Pipework Tag | MEP Fabrication Pipework | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:PLM_PRESSURE_T7 | KEEP | KEEP | KEEP |
| STING - Materials Tag | Materials | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Audio Visual Devices Tag | Audio Visual Devices | PHYSICAL | KEEP | KEEP | REPLACE:CBN_OPERATIONAL_T6 | KEEP | KEEP | KEEP | KEEP |
| STING - Spaces Tag | Spaces (MEP) | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Zones Tag | Zones (HVAC) | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Tie-In Point Tag (Pipe — Plumbing & Hydraulic) | Pipes | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Tie-In Point Tag (Duct — HVAC) | Ducts | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Tie-In Point Tag (Conduit — Electrical LV/ELV) | Conduits | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Tie-In Point Tag (Cable Tray — Electrical) | Cable Trays | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Tie-In Point Tag (Fire Protection — Sprinkler / Suppression) | Pipes (Fire Protection System) | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Tie-In Point Tag (Gas — Medical / Industrial / Natural Gas) | Pipes (Gas System) | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - MEP Sheet Tag | Sheets — MEP disciplines (M/E/P/FP/LV) | ANNOTATION | OMIT | KEEP | OMIT | OMIT | OMIT | OMIT | OMIT |
| STING - MEP Sleeve Tag | Generic Models | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Column Tag | Structural Columns | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Framing Tag | Structural Framing | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Foundation Tag | Structural Foundations | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Slab Tag | Floors (Structural) | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Wall Tag | Walls (Structural/Load-bearing) | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Brace / Truss Tag | Structural Framing (Bracing) | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Rebar Tag | Structural Rebar | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:STR_REBAR_T7 | KEEP | REPLACE:ASBUILT_REBAR_T9 | KEEP |
| STING - Structural Connection Tag | Structural Connections | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:STR_CONN_T7 | KEEP | KEEP | KEEP |
| STING - Columns Tag | Columns (Architectural) | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Trusses Tag | Structural Trusses | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Stiffeners Tag | Structural Stiffeners | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Beam Systems Tag | Structural Beam Systems | PHYSICAL | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP | KEEP |
| STING - Structural Rebar Couplers Tag | Structural Rebar Couplers | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:STR_REBAR_T7 | KEEP | REPLACE:ASBUILT_REBAR_T9 | KEEP |
| STING - Structural Fabric Reinforcement Tag | Structural Fabric Reinforcement | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:STR_REBAR_T7 | KEEP | REPLACE:ASBUILT_REBAR_T9 | KEEP |
| STING - Structural Area Reinforcement Tag | Structural Area Reinforcement | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:STR_REBAR_T7 | KEEP | REPLACE:ASBUILT_REBAR_T9 | KEEP |
| STING - Structural Path Reinforcement Tag | Structural Path Reinforcement | PHYSICAL | KEEP | KEEP | KEEP | REPLACE:STR_REBAR_T7 | KEEP | REPLACE:ASBUILT_REBAR_T9 | KEEP |
| STING - Internal Point Loads Tag | Internal Point Loads | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Internal Line Loads Tag | Internal Line Loads | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Internal Area Loads Tag | Internal Area Loads | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Analytical Members Tag | Analytical Members | SPATIAL | KEEP | KEEP | KEEP | OMIT | KEEP | OMIT | KEEP |
| STING - Structural Sheet Tag | Sheets — Structural discipline | ANNOTATION | OMIT | KEEP | OMIT | OMIT | OMIT | OMIT | OMIT |

## Authority notes

- `FabricationEngine.cs` (StingTools/Core/Fabrication) — scanned for Spool/Fab/Weld/Bolt — no parameter references found, so Section 2 suggestions are authoritative for T7 fabrication rows.
- `SustainabilityEngine.cs` — CBN_* parameter references confirmed, Section 2 T6 sets preserved.
- `AcousticAnalysisEngine.cs` — checked for Noise/Reverberation/Acoustic content — not material to T4 to T10 since acoustic rating lives in T2.
- No engine overrides applied. Any flagged discrepancies in future should be logged here.

## Integration note

The default 3-row T4..T10 block emitted by the CSV regenerator is defined in the script's `DEFAULT_TIER_ROWS` dict (mirrors the current CSV content exactly).
`REPLACE` blocks are defined in `REPLACE_TIER_ROWS` keyed by set-id. See Section 2 of the task prompt for the canonical prefix/suffix/spc/brk per row.
