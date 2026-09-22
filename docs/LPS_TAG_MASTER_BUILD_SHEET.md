# STING LPS Tag — universal master build sheet

Generated from `STING_TAG_CONFIG_v5_0_*.csv` on 2026-09-22. Regenerate rather
than hand-edit: the CSVs are the declaration, this is a view of them.

## What this is for

Nine tag families are declared Multi-Category, and they are the ONLY nine in
the 206-family library. They cannot take the universal label: Revit will not
move a family into Multi-Category, and the universal master carries no LPS
rows anyway, so a successful propagation would delete exactly what makes them
LPS tags.

So they get their own master, built by hand ONCE from **Multi-Category
Tag.rft**, and propagated to the other eight by the existing conveyor. That
works because master and targets are then BOTH Multi-Category: the
recategorise step is a no-op, which is the step Revit refuses.

## The nine

- STING - LPS Air Terminal Tag
- STING - LPS Bond Tag
- STING - LPS Down Conductor Tag
- STING - LPS Earth Electrode Tag
- STING - LPS Foundation Earth (Structural Reuse) Tag
- STING - LPS Generic Component Tag
- STING - LPS Natural Air Termination (Architectural Reuse) Tag
- STING - LPS SPD Tag
- STING - LPS Test Clamp Tag

Build the master as a **tenth** family — `STING_LPS_Tag_Universal` — rather
than promoting one of the nine. A master that is also a target gets
overwritten by its own propagation, and the run reports success.

## Row inventory

| tier | rows | what they are |
|---|---:|---|
| T1 | 7 | LPS-specific |
| T2 | 14 | LPS-specific |
| T3 | 6 | LPS-specific |
| T4 | 3 | shared with the universal master — same parameters, same order |
| T5 | 17 | shared with the universal master — same parameters, same order |
| T6 | 3 | shared with the universal master — same parameters, same order |
| T7 | 3 | shared with the universal master — same parameters, same order |
| T8 | 3 | shared with the universal master — same parameters, same order |
| T9 | 3 | shared with the universal master — same parameters, same order |
| T10 | 3 | shared with the universal master — same parameters, same order |
| **total** | **62** | 27 LPS-specific + 35 shared |

Plus **21 warning rows** (10 LPS, 11 inherited generic — see the last section).

**Tiers T4-T10 are the same 35 rows as the universal master**, in the same
order. They are not LPS at all - commissioning, cost, carbon, fabrication,
clash. Author them exactly as `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` does;
Revit blocks cross-category label paste, so they must be re-typed, but
nothing about them needs a decision.

## The LPS rows — T1 to T3

`used` is how many of the nine declare that row. A row used by one family
still belongs in the master: it renders blank on the other eight, which is
how every tier in this system already behaves.

| # | tier | parameter | prefix | suffix | style | colour | size | used |
|---:|---|---|---|---|---|---|---|---:|
| 1 | T1 | `ELC_LPS_AIRTERM_TAG_TXT` | — | — | BOLD | BLUE | 2.5 | 2/9 |
| 2 | T1 | `ASS_TAG_1_TXT` | — | — | BOLD | BLUE | 2.5 | 1/9 |
| 3 | T1 | `ELC_LPS_DOWNCOND_TAG_TXT` | — | — | BOLD | BLUE | 2.5 | 1/9 |
| 4 | T1 | `ELC_LPS_EARTH_TAG_TXT` | — | — | BOLD | BLUE | 2.5 | 2/9 |
| 5 | T1 | `ELC_LPS_BOND_TAG_TXT` | — | — | BOLD | BLUE | 2.5 | 1/9 |
| 6 | T1 | `ELC_LPS_SPD_TAG_TXT` | — | — | BOLD | BLUE | 2.5 | 1/9 |
| 7 | T1 | `ELC_LPS_TESTCLAMP_TAG_TXT` | — | — | BOLD | BLUE | 2.5 | 1/9 |
| 8 | T2 | `ELC_LPS_CLASS_TXT` | Class: | — | NOM | BLACK | 2.0 | 5/9 |
| 9 | T2 | `ELC_LPS_ZONE_TXT` | LPZ: | — | NOM | BLACK | 2.0 | 6/9 |
| 10 | T2 | `ELC_LPS_CONDUCTOR_MATERIAL_TXT` | Mat: | — | NOM | BLACK | 2.0 | 2/9 |
| 11 | T2 | `ELC_LPS_CONDUCTOR_CROSS_SECT_MM2` | t: | mm | NOM | BLACK | 2.0 | 3/9 |
| 12 | T2 | `ELC_LPS_COMPLIANCE_STATUS_TXT` | — | — | BOLD | RED | 2.0 | 2/9 |
| 13 | T2 | `ELC_LPS_PROTECTION_ANGLE_DEG` | α: | ° | NOM | BLACK | 2.0 | 1/9 |
| 14 | T2 | `ELC_LPS_AIR_TERMINAL_COUNT_NR` | N: | — | NOM | BLACK | 2.0 | 1/9 |
| 15 | T2 | `ELC_LPS_DOWN_CONDUCTOR_COUNT_NR` | N: | — | NOM | BLACK | 2.0 | 1/9 |
| 16 | T2 | `ELC_LPS_SEPARATION_DISTANCE_MM` | s: | mm | NOM | BLACK | 2.0 | 1/9 |
| 17 | T2 | `ELC_LPS_EARTH_TYPE_TXT` | Type: | — | NOM | BLACK | 2.0 | 2/9 |
| 18 | T2 | `ELC_LPS_EARTH_RESISTANCE_OHM` | R: | Ω | NOM | BLACK | 2.0 | 3/9 |
| 19 | T2 | `ELC_LPS_EARTH_ELECTRODE_COUNT_NR` | N: | — | NOM | BLACK | 2.0 | 1/9 |
| 20 | T2 | `ELC_LPS_TEST_DATE_TXT` | Tested: | — | NOM | BLACK | 2.0 | 3/9 |
| 21 | T2 | `ELC_LPS_SURGE_PROTECTION_LVL_TXT` | SPD: | — | NOM | BLACK | 2.0 | 1/9 |
| 22 | T3 | `ELC_LPS_BOND_TYPE_TXT` | Bond: | — | NOM | BLACK | 2.0 | 4/9 |
| 23 | T3 | `ELC_LPS_RISK_ASSESSMENT_TXT` | Risk: | — | NOM | BLACK | 2.0 | 2/9 |
| 24 | T3 | `ELC_LPS_ROLLING_SPHERE_RADIUS_M` | r: | m | NOM | BLACK | 2.0 | 1/9 |
| 25 | T3 | `ELC_LPS_MESH_SIZE_M` | Mesh: | m | NOM | BLACK | 2.0 | 1/9 |
| 26 | T3 | `ELC_LPS_INSPECTION_INTERVAL_MONTHS` | Inspect: | mo | NOM | BLACK | 2.0 | 2/9 |
| 27 | T3 | `ELC_LPS_CERT_REF_TXT` | Cert: | — | NOM | BLACK | 2.0 | 3/9 |

### Visibility formulas

Every row follows the tier pattern already in use:

```
if(TAG_PARA_STATE_<n>_BOOL, <PARAMETER>, "")
```

where `<n>` is the tier number. T1 rows have no gate - they are always on.

## Warning rows

### LPS (10) — build these

| severity | parameter | condition | used |
|---|---|---|---:|
| HIGH | `WARN_ELC_LPS_CONDUCTOR_CROSS_SECT_LOW` | Sheet thickness below natural-component minimum (BS EN 62305-3 Tab 3 — typ. 0.5 mm Cu /  | 2/9 |
| HIGH | `WARN_ELC_LPS_NO_BONDING` | Bonding method to LPS network not specified — natural component is non-functional withou | 3/9 |
| CRITICAL | `WARN_ELC_LPS_NO_CLASS` | LPS class not assigned — required to verify natural-component thickness vs BS EN 62305-3 | 3/9 |
| MEDIUM | `WARN_ELC_LPS_NO_ZONE` | Lightning protection zone (LPZ) not assigned | 2/9 |
| HIGH | `WARN_ELC_LPS_MESH_SIZE_EXCEEDED` | Air-termination mesh exceeds class limit (I=5×5 II=10×10 III=15×15 IV=20×20 m) | 1/9 |
| HIGH | `WARN_ELC_LPS_NO_RISK_ASSESSMENT` | BS EN 62305-2 risk assessment reference (R1..R4) not recorded | 1/9 |
| HIGH | `WARN_ELC_LPS_DOWN_COND_INSUFFICIENT` | Down conductor count below class minimum (Class I=4 II=3 III/IV=2) | 1/9 |
| HIGH | `WARN_ELC_LPS_SEPARATION_FAIL` | Separation distance below s = ki·kc·l/km — services flashover risk | 1/9 |
| HIGH | `WARN_ELC_LPS_EARTH_RESISTANCE_HIGH` | Earth resistance > 10 ohm — supplement electrodes or add ring earth | 2/9 |
| MEDIUM | `WARN_ELC_LPS_INSPECTION_OVERDUE` | Inspection interval exceeded (Class I/II=12mo III/IV=24mo) | 2/9 |

### Inherited generic (11) — check before building

These appear in the LPS declarations but are not about lightning protection.
The `PRJ_*` and `STING_*` ones are project/drawing hygiene and are carried by
many families. The **`ELC_PNL_*` ones are panel-schedule warnings** - spare
ways, SCCR, AIC, TVSS - and look like copy-paste from an electrical panel
tag. Decide whether they belong before authoring them.

| severity | parameter | condition | used |
|---|---|---|---:|
| HIGH | `WARN_PRJ_CLIENT_NAME_MISSING` | PRJ_ORG_CLIENT_NAME_TXT empty | 1/9 |
| HIGH | `WARN_PRJ_CODE_MISSING` | PRJ_ORG_PROJECT_CODE_TXT empty | 1/9 |
| HIGH | `WARN_PRJ_ORIGINATOR_MISSING` | PRJ_ORG_ORIGINATOR_CODE_TXT empty | 1/9 |
| HIGH | `WARN_STING_DRAWING_TYPE_MISSING` | STING_DRAWING_TYPE_ID_TXT empty on sheet | 1/9 |
| HIGH | `WARN_STING_CROP_DRIFT` | Crop kind/margin drift from profile | 1/9 |
| HIGH | `WARN_STING_PACK_DRIFT` | Pack checksum drift on managed template | 1/9 |
| MEDIUM | `WARN_STING_STYLE_LOCKED_NOTICE` | STING_STYLE_LOCKED_BOOL=1 | 1/9 |
| HIGH | `WARN_ELC_PNL_SPARES_LOW` | ELC_PNL_SPARE_WAYS_NR < 10% of NUM_OF_WAYS | 1/9 |
| HIGH | `WARN_ELC_PNL_SCCR_LOW` | ELC_PNL_SHORT_CIRCUIT_RATING_KA < fault | 1/9 |
| HIGH | `WARN_ELC_PNL_AIC_INADEQUATE` | ELC_PNL_AIC_RATING_KA < available fault | 1/9 |
| HIGH | `WARN_ELC_PNL_NO_TVSS` | Critical panel without surge protection | 1/9 |

## After the master is built

One blocker remains, and it is in the plugin, not in Revit.

The nine declare `Universal: No`, which propagation reads as "skip" -
unconditionally, whichever master is running. So the LPS master would be
skipped for its own nine targets.

The flag needs to become a GROUP rather than a boolean: a family takes its
label from the master of its group, and propagation skips a family whose
group is not the group of the master being propagated. `Universal: No`
becomes `LabelMaster: LPS`, the default stays universal, and a tenth
specialist master later needs no new mechanism.

That is a small change to `TagConfigDeclarations`, `TagCategoryResolver` and
the propagation pre-flight, and the three existing gates carry over intact.
