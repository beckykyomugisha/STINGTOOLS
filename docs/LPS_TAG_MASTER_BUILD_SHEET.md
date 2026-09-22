# STING LPS Tag master — hand-authoring sheet

Copy-paste companion to `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md`, generated from
`STING_TAG_CONFIG_v5_0_*.csv` and that sheet on 2026-09-22. Regenerate rather
than hand-edit.

**Build from `Multi-Category Tag.rft`, as a TENTH family named
`STING_LPS_Tag_Universal`.** Not by editing one of the nine: a master that is
also a target is overwritten by its own propagation, and the run still reports
success.

Same mechanics as the universal sheet. Every non-T1 row is a Calculated Value
(fx): Name, Type=Text, paste Formula, then Prefix/Suffix, **Spaces=0**, Break.
Set Spaces=0 BEFORE ticking Break; Spaces is only editable when the row ABOVE
has no Break.

---

## Why T4-T10 are copied, not taken from the LPS declarations

The LPS families declare 35 shared rows. The universal master carries **61**,
and that is what the other 197 families now actually hold. The LPS
declarations predate the universal pivot, so they are 26 rows short - missing
cost, carbon, fabrication and clash rows that every other tag in the library
has.

Three of those 26 are arguably irrelevant here - ASS_CAPACITY_TXT,
ASS_POWER_RATING_TXT and ASS_FLOW_RATE_TXT. An air terminal has no flow
rate. They are kept anyway, because that is what the universal design
already does everywhere else: a Door Tag carries the flow-rate row too and
renders it blank. Dropping them would buy nothing at render time and cost a
second label shape to reason about forever.

So T4-T10 below are the UNIVERSAL master's rows, verbatim, Break values and
all. They are already verified by the 197-family run. Using the LPS
declarations instead would make these nine the only tags in the library with a
different shared label.

---

## STEP 1 - LPS rows (T1-T3, 27 rows)

`used` is how many of the nine declare the row. A row used by one family still
belongs in the master: it renders blank on the other eight, exactly as every
tier already does.

**Spaces is 0 on every row**, and the column is printed rather than stated
once: Revit defaults it to 1, and an extra gap before every value is subtle
enough to survive review.

**␣ marks a space that matters.** A markdown cell pads with a space either
side, so `| - |` reads the same whether the prefix is `-` or ` - `. Type a real
space wherever you see ␣.

**Numeric parameters read their TEXT twin.** Revit has no number-to-string
conversion in family formulas, so `if(BOOL, <NUMBER>, "")` is rejected as
"Inconsistent Units" - the branches are different types. Twelve rows here are
affected and each names the twin it reads, with the original type in italics.

> The twins are NOT yet written by anything. A label pointed at
> `ELC_LPS_PROTECTION_ANGLE_TXT` renders blank until that parameter holds a
> value, whether typed by hand or mirrored from the numeric. Build the rows
> now; the mirror is a separate job.

**Break is a suggestion here** - one per tier boundary. The declarations do not
record line breaks, so unlike the T4-T10 block below these are not verified.
Adjust as the label reads.

| # | Tier | Calc Value Name | Formula | Spaces | Prefix | Suffix | Break | used |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | `ELC_LPS_AIRTERM_TAG_TXT` | _(add parameter directly - no fx)_ | 0 |  |  | YES | 2/9 |
| 2 | T1 | `ASS_TAG_1_TXT` | _(add parameter directly - no fx)_ | 0 |  |  | YES | 1/9 |
| 3 | T1 | `ELC_LPS_DOWNCOND_TAG_TXT` | _(add parameter directly - no fx)_ | 0 |  |  | YES | 1/9 |
| 4 | T1 | `ELC_LPS_EARTH_TAG_TXT` | _(add parameter directly - no fx)_ | 0 |  |  | YES | 2/9 |
| 5 | T1 | `ELC_LPS_BOND_TAG_TXT` | _(add parameter directly - no fx)_ | 0 |  |  | YES | 1/9 |
| 6 | T1 | `ELC_LPS_SPD_TAG_TXT` | _(add parameter directly - no fx)_ | 0 |  |  | YES | 1/9 |
| 7 | T1 | `ELC_LPS_TESTCLAMP_TAG_TXT` | _(add parameter directly - no fx)_ | 0 |  |  | YES | 1/9 |
| 8 | T2 | Show T2 - LPS Class | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_CLASS_TXT, "")` | 0 | Class: |  | no | 5/9 |
| 9 | T2 | Show T2 - LPS Zone | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_ZONE_TXT, "")` | 0 | LPZ: |  | no | 6/9 |
| 10 | T2 | Show T2 - LPS Conductor Material | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_CONDUCTOR_MATERIAL_TXT, "")` | 0 | Mat: |  | no | 2/9 |
| 11 | T2 | Show T2 - LPS Conductor Cross Sect | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_CONDUCTOR_CROSS_SECT_TXT, "")` <br>_(ELC_LPS_CONDUCTOR_CROSS_SECT_MM2 is NUMBER - reads its TEXT twin)_ | 0 | t: | mm | no | 3/9 |
| 12 | T2 | Show T2 - LPS Compliance Status | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_COMPLIANCE_STATUS_TXT, "")` | 0 |  |  | no | 2/9 |
| 13 | T2 | Show T2 - LPS Protection Angle | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_PROTECTION_ANGLE_TXT, "")` <br>_(ELC_LPS_PROTECTION_ANGLE_DEG is NUMBER - reads its TEXT twin)_ | 0 | α: | ° | no | 1/9 |
| 14 | T2 | Show T2 - LPS Air Terminal Count | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_AIR_TERMINAL_COUNT_TXT, "")` <br>_(ELC_LPS_AIR_TERMINAL_COUNT_NR is NUMBER - reads its TEXT twin)_ | 0 | N: |  | no | 1/9 |
| 15 | T2 | Show T2 - LPS Down Conductor Count | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_DOWN_CONDUCTOR_COUNT_TXT, "")` <br>_(ELC_LPS_DOWN_CONDUCTOR_COUNT_NR is NUMBER - reads its TEXT twin)_ | 0 | N: |  | no | 1/9 |
| 16 | T2 | Show T2 - LPS Separation Distance | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_SEPARATION_DISTANCE_TXT, "")` <br>_(ELC_LPS_SEPARATION_DISTANCE_MM is LENGTH - reads its TEXT twin)_ | 0 | s: | mm | no | 1/9 |
| 17 | T2 | Show T2 - LPS Earth Type | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_EARTH_TYPE_TXT, "")` | 0 | Type: |  | no | 2/9 |
| 18 | T2 | Show T2 - LPS Earth Resistance | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_EARTH_RESISTANCE_TXT, "")` <br>_(ELC_LPS_EARTH_RESISTANCE_OHM is NUMBER - reads its TEXT twin)_ | 0 | R: | Ω | no | 3/9 |
| 19 | T2 | Show T2 - LPS Earth Electrode Count | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_EARTH_ELECTRODE_COUNT_TXT, "")` <br>_(ELC_LPS_EARTH_ELECTRODE_COUNT_NR is NUMBER - reads its TEXT twin)_ | 0 | N: |  | no | 1/9 |
| 20 | T2 | Show T2 - LPS Test Date | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_TEST_DATE_TXT, "")` | 0 | Tested: |  | no | 3/9 |
| 21 | T2 | Show T2 - LPS Surge Protection Lvl | `if(TAG_PARA_STATE_2_BOOL, ELC_LPS_SURGE_PROTECTION_LVL_TXT, "")` | 0 | SPD: |  | YES | 1/9 |
| 22 | T3 | Show T3 - LPS Bond Type | `if(TAG_PARA_STATE_3_BOOL, ELC_LPS_BOND_TYPE_TXT, "")` | 0 | Bond: |  | no | 4/9 |
| 23 | T3 | Show T3 - LPS Risk Assessment | `if(TAG_PARA_STATE_3_BOOL, ELC_LPS_RISK_ASSESSMENT_TXT, "")` | 0 | Risk: |  | no | 2/9 |
| 24 | T3 | Show T3 - LPS Rolling Sphere Radius | `if(TAG_PARA_STATE_3_BOOL, ELC_LPS_ROLLING_SPHERE_RADIUS_TXT, "")` <br>_(ELC_LPS_ROLLING_SPHERE_RADIUS_M is LENGTH - reads its TEXT twin)_ | 0 | r: | m | no | 1/9 |
| 25 | T3 | Show T3 - LPS Mesh Size | `if(TAG_PARA_STATE_3_BOOL, ELC_LPS_MESH_SIZE_TXT, "")` <br>_(ELC_LPS_MESH_SIZE_M is LENGTH - reads its TEXT twin)_ | 0 | Mesh: | m | no | 1/9 |
| 26 | T3 | Show T3 - LPS Inspection Interval | `if(TAG_PARA_STATE_3_BOOL, ELC_LPS_INSPECTION_INTERVAL_MONTHS_TXT, "")` <br>_(ELC_LPS_INSPECTION_INTERVAL_MONTHS is NUMBER - reads its TEXT twin)_ | 0 | Inspect: | mo | no | 2/9 |
| 27 | T3 | Show T3 - LPS Cert Ref | `if(TAG_PARA_STATE_3_BOOL, ELC_LPS_CERT_REF_TXT, "")` | 0 | Cert: |  | YES | 3/9 |

## STEP 2 - Shared rows (T4-T10, 61 rows)

Copied verbatim from `UNIVERSAL_TAG_LABEL_BUILD_SHEET.md`. Revit blocks
cross-category label paste, so they must be re-typed - but nothing here needs
a decision, and the Break values are known-good.

| # | Tier | Calc Value Name | Formula | Spaces | Prefix | Suffix | Break |
|---|---|---|---|---|---|---|---|
| 28 | T4 | Show T4 - Commissioning - State | `if(TAG_PARA_STATE_4_BOOL, COMM_STATE_TXT, "")` | 0 | Comm: |  | no |
| 29 | T4 | Show T4 - Commissioning - Date | `if(TAG_PARA_STATE_4_BOOL, COMM_DATE_TXT, "")` | 0 | on |  | no |
| 30 | T4 | Show T4 - Commissioning - Operative | `if(TAG_PARA_STATE_4_BOOL, COMM_OPERATIVE_TXT, "")` | 0 | by |  | YES |
| 31 | T4 | Show T4 - Design intent - ASS_DESIGN_OPTION_TXT | `if(TAG_PARA_STATE_4_BOOL, ASS_DESIGN_OPTION_TXT, "")` | 0 | Option: |  | no |
| 32 | T4 | Show T4 - Design intent - ASS_MODEL_REF_TXT | `if(TAG_PARA_STATE_4_BOOL, ASS_MODEL_REF_TXT, "")` | 0 | Ref: |  | no |
| 33 | T4 | Show T4 - Design intent - ASS_KEYNOTE_TXT | `if(TAG_PARA_STATE_4_BOOL, ASS_KEYNOTE_TXT, "")` | 0 | Key: |  | YES |
| 34 | T5 | Show T5 - Cost - UG Price | `if(TAG_PARA_STATE_5_BOOL, CST_UG_PRICE_UGX_DISP_TXT, "")` | 0 |  | UGX | no |
| 35 | T5 | Show T5 - Cost - Intl Price | `if(TAG_PARA_STATE_5_BOOL, CST_INTL_PRICE_USD_DISP_TXT, "")` | 0 | /␣ | USD | no |
| 36 | T5 | Show T5 - Cost - Quote Ref | `if(TAG_PARA_STATE_5_BOOL, CST_QUOTE_REF_TXT, "")` | 0 | Quote: |  | YES |
| 37 | T5 | Show T5 - Cost - Unit Rate (neutral) | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_UNIT_RATE_NR_DISP_TXT, "")` | 0 | Rate: |  | no |
| 38 | T5 | Show T5 - Cost - Currency Code | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_CURRENCY_TXT, "")` | 0 | ␣ |  | no |
| 39 | T5 | Show T5 - Cost - FX to Base | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_FX_TO_BASE_NR_DISP_TXT, "")` | 0 | FX: |  | no |
| 40 | T5 | Show T5 - Cost - FX Date | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_FX_DATE_DT, "")` | 0 | ␣FX date: |  | no |
| 41 | T5 | Show T5 - Cost - As-of Date | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_AS_OF_DT, "")` | 0 | ␣As of: |  | YES |
| 42 | T5 | Show T5 - Cost - Stale Flag | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_STALE_TXT, "")` <br>_(ASS_CST_STALE_BOOL is YESNO - reads its TEXT twin; Revit rejects a number in a Text formula)_| 0 | Stale: |  | no |
| 43 | T5 | Show T5 - Cost - Stale Reason | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_STALE_REASON_TXT, "")` | 0 | ␣-␣ |  | YES |
| 44 | T5 | Show T5 - Payment - % Complete | `if(TAG_PARA_STATE_5_BOOL, ASS_PMT_PCT_COMPLETE_NR_DISP_TXT, "")` | 0 | Cmpl: | % | no |
| 45 | T5 | Show T5 - Payment - Cert No | `if(TAG_PARA_STATE_5_BOOL, ASS_PMT_CERT_NO_NR_DISP_TXT, "")` | 0 | ␣Cert#: |  | no |
| 46 | T5 | Show T5 - Payment - Cert Date | `if(TAG_PARA_STATE_5_BOOL, ASS_PMT_CERT_DATE_DT, "")` | 0 | ␣Certified: |  | no |
| 47 | T5 | Show T5 - Payment - Last Valued | `if(TAG_PARA_STATE_5_BOOL, ASS_PMT_LAST_VALUED_DT, "")` | 0 | ␣Valued: |  | YES |
| 48 | T5 | Show T5 - Variation - Number | `if(TAG_PARA_STATE_5_BOOL, ASS_VAR_NO_TXT, "")` | 0 | Var: |  | no |
| 49 | T5 | Show T5 - Variation - Instr Date | `if(TAG_PARA_STATE_5_BOOL, ASS_VAR_INSTRUCTION_DT, "")` | 0 | ␣Instr: |  | no |
| 50 | T5 | Show T5 - Variation - Valuation | `if(TAG_PARA_STATE_5_BOOL, ASS_VAR_VALUATION_NR_DISP_TXT, "")` | 0 | ␣Val: |  | YES |
| 51 | T5 | Show T5 - Performance & capacity - ASS_CAPACITY_TXT | `if(TAG_PARA_STATE_5_BOOL, ASS_CAPACITY_TXT, "")` | 0 | Cap: |  | no |
| 52 | T5 | Show T5 - Performance & capacity - ASS_POWER_RATING_TXT | `if(TAG_PARA_STATE_5_BOOL, ASS_POWER_RATING_TXT, "")` | 0 | Power: |  | no |
| 53 | T5 | Show T5 - Performance & capacity - ASS_FLOW_RATE_TXT | `if(TAG_PARA_STATE_5_BOOL, ASS_FLOW_RATE_TXT, "")` | 0 | Flow: |  | YES |
| 54 | T5 | Show T5-Ph179 - Item Code | `if(TAG_PARA_STATE_5_BOOL, ASS_ITEM_CODE_TXT, "")` | 0 | Item: |  | YES |
| 55 | T6 | Show T6 - Carbon - Product A1-A3 | `if(TAG_PARA_STATE_6_BOOL, CBN_A1_A3_KG_CO2E_DISP_TXT, "")` | 0 | A1-A3: | kgCO₂e | no |
| 56 | T6 | Show T6 - Carbon - Transport A4 | `if(TAG_PARA_STATE_6_BOOL, CBN_A4_KG_CO2E_DISP_TXT, "")` | 0 | | A4: | kgCO₂e | no |
| 57 | T6 | Show T6 - Carbon - Operational B6 | `if(TAG_PARA_STATE_6_BOOL, CBN_B6_KG_CO2E_YR_DISP_TXT, "")` | 0 | | B6: | kgCO₂e/yr | YES |
| 58 | T6 | Show T6 - Material & finish - ASS_MANUFACTURER_TXT | `if(TAG_PARA_STATE_6_BOOL, ASS_MANUFACTURER_TXT, "")` | 0 | Mfr: |  | no |
| 59 | T6 | Show T6 - Material & finish - ASS_MODEL_NR_TXT | `if(TAG_PARA_STATE_6_BOOL, ASS_MODEL_NR_TXT, "")` | 0 | |␣ |  | no |
| 60 | T6 | Show T6 - Material & finish - ASS_EXPECTED_LIFE_YEARS_YRS | `if(TAG_PARA_STATE_6_BOOL, ASS_EXPECTED_LIFE_YEARS_YRS, "")` | 0 | Life: | yrs | YES |
| 61 | T7 | Show T7 - Fabrication - Spool No | `if(TAG_PARA_STATE_7_BOOL, ASS_SPOOL_NR_TXT, "")` | 0 | Spool: |  | no |
| 62 | T7 | Show T7 - Fabrication - Status | `if(TAG_PARA_STATE_7_BOOL, ASS_FAB_STATUS_TXT, "")` | 0 | Fab: |  | no |
| 63 | T7 | Show T7 - QC - Inspector | `if(TAG_PARA_STATE_7_BOOL, ASS_QC_INSPECTOR_TXT, "")` | 0 | QC: |  | YES |
| 64 | T7 | Show T7 - Installation - ASS_INSTALL_DATE_TXT | `if(TAG_PARA_STATE_7_BOOL, ASS_INSTALL_DATE_TXT, "")` | 0 | Installed: |  | no |
| 65 | T7 | Show T7 - Installation - CST_INSTALL_HRS_DISP_TXT | `if(TAG_PARA_STATE_7_BOOL, CST_INSTALL_HRS_DISP_TXT, "")` | 0 | Hrs: | h | no |
| 66 | T7 | Show T7 - Installation - ASS_CST_INSTALL_UGX_NR | `if(TAG_PARA_STATE_7_BOOL, ASS_CST_INSTALL_UGX_NR, "")` | 0 | @ | UGX | YES |
| 67 | T8 | Show T8 - Clash - Triage Severity | `if(TAG_PARA_STATE_8_BOOL, CLASH_TRIAGE_SEVERITY_NR_DISP_TXT, "")` | 0 | Sev: | /5 | no |
| 68 | T8 | Show T8 - Clash - Triage Category | `if(TAG_PARA_STATE_8_BOOL, CLASH_TRIAGE_CATEGORY_TXT, "")` | 0 |  |  | no |
| 69 | T8 | Show T8 - Clash - Resolution Status | `if(TAG_PARA_STATE_8_BOOL, CLASH_RESOLUTION_STATUS_TXT, "")` | 0 | Res: |  | YES |
| 70 | T8 | Show T8 - Coordination - ASS_CRITICALITY_RATING_NR | `if(TAG_PARA_STATE_8_BOOL, ASS_CRITICALITY_RATING_TXT, "")` <br>_(ASS_CRITICALITY_RATING_NR is NUMBER - reads its TEXT twin; Revit rejects a number in a Text formula)_| 0 | Crit: | /5 | no |
| 71 | T8 | Show T8 - Coordination - ASS_ZONE_TXT | `if(TAG_PARA_STATE_8_BOOL, ASS_ZONE_TXT, "")` | 0 | Zone: |  | no |
| 72 | T8 | Show T8 - Coordination - ASS_LVL_COD_TXT | `if(TAG_PARA_STATE_8_BOOL, ASS_LVL_COD_TXT, "")` | 0 | Lvl: |  | YES |
| 73 | T9 | Show T9 - As-built - Deviation | `if(TAG_PARA_STATE_9_BOOL, ASBUILT_DEVIATION_MM_DISP_TXT, "")` | 0 | Δ: | mm | no |
| 74 | T9 | Show T9 - As-built - Capture Date | `if(TAG_PARA_STATE_9_BOOL, ASBUILT_CAPTURE_DATE_TXT, "")` | 0 | on |  | no |
| 75 | T9 | Show T9 - Health Score | `if(TAG_PARA_STATE_9_BOOL, HEALTH_SCORE_LAST_NR_DISP_TXT, "")` | 0 | Health: | /100 | YES |
| 76 | T9 | Show T9 - Warranty & quality - ASS_WARRANTY_PARTS_TXT | `if(TAG_PARA_STATE_9_BOOL, ASS_WARRANTY_PARTS_TXT, "")` | 0 | Parts: |  | no |
| 77 | T9 | Show T9 - Warranty & quality - ASS_WARRANTY_LABOR_TXT | `if(TAG_PARA_STATE_9_BOOL, ASS_WARRANTY_LABOR_TXT, "")` | 0 | Labor: |  | no |
| 78 | T9 | Show T9 - Warranty & quality - ASS_WARRANTY_TXT | `if(TAG_PARA_STATE_9_BOOL, ASS_WARRANTY_TXT, "")` | 0 | Terms: |  | YES |
| 79 | T10 | Show T10 - Compliance - IFC PSet Override | `if(TAG_PARA_STATE_10_BOOL, IFC_PSET_OVERRIDE_TXT, "")` | 0 | IFC: |  | no |
| 80 | T10 | Show T10 - Compliance - ACC Issue | `if(TAG_PARA_STATE_10_BOOL, ACC_ISSUE_ID_TXT, "")` | 0 | ACC: |  | no |
| 81 | T10 | Show T10 - Compliance - ACC Sync Status | `if(TAG_PARA_STATE_10_BOOL, ACC_SYNC_STATUS_TXT, "")` | 0 | Sync: |  | YES |
| 82 | T10 | Show T10 - Classification - ASS_UNICLASS_2015_TXT | `if(TAG_PARA_STATE_10_BOOL, ASS_UNICLASS_2015_TXT, "")` | 0 | Uniclass: |  | no |
| 83 | T10 | Show T10 - Classification - ASS_KEYNOTE_TXT | `if(TAG_PARA_STATE_10_BOOL, ASS_KEYNOTE_TXT, "")` | 0 | Key: |  | no |
| 84 | T10 | Show T10 - Classification - ASS_MODEL_REF_TXT | `if(TAG_PARA_STATE_10_BOOL, ASS_MODEL_REF_TXT, "")` | 0 | Ref: |  | YES |
| 85 | T10 | Show T10-Ph179 - Trace Seq | `if(TAG_PARA_STATE_10_BOOL, ASS_TRACE_SEQ_NR_DISP_TXT, "")` | 0 | Trc: |  | YES |
| 86 | T4 | Show T4 - TAG7D Commissioning | `if(TAG_PARA_STATE_4_BOOL, ASS_TAG_7D_TXT, "")` | 0 |  |  | YES |
| 87 | T5 | Show T5 - TAG7E Cost | `if(TAG_PARA_STATE_5_BOOL, ASS_TAG_7E_TXT, "")` | 0 |  |  | YES |
| 88 | T6 | Show T6 - TAG7F Carbon | `if(TAG_PARA_STATE_6_BOOL, ASS_TAG_7F_TXT, "")` | 0 |  |  | YES |

**Total: 88 rows** (27 LPS + 61 shared).

## STEP 3 - Warnings (17) - NOT label rows

**Nothing to author here.** The universal master contains zero warning label
rows, and neither should this one. They are listed so the set is visible, not
so it can be typed in.

Warnings reach a tag by a different route entirely:

1. They are DECLARED in `STING_TAG_CONFIG_v5_0_*.csv` - already done, all 17.
2. The plugin evaluates each against its `warning_thresholds` entry in
   `PARAMETER_REGISTRY.json` (`EvaluateAndPopulateWarnings`) and writes the
   `WARN_*` parameters onto the element.
3. The text is concatenated into the **TAG7 narrative**, which reaches the
   label through the `ASS_TAG_7A..7F_TXT` rows - already in STEP 2, so the
   master gets them with the shared block.
4. Optionally, the green/amber/red **badges** read
   `STING_GATE_DATA_STATUS_INT` / `STING_GATE_QA_STATUS_INT`. Those are
   family glyphs, not label rows - see STEP 4 of the universal sheet.

Two switches control all of it at tag level, and both are already shared
parameters: `TAG_WARN_VISIBLE_BOOL` (master on/off) and
`TAG_WARN_SEVERITY_FILTER_TXT`.

> `TAG_WARN_VISIBLE_BOOL` is YESNO - write it BARE in a formula. Comparing it
> to "Yes" fails with the same "Inconsistent Units" that blocked the twelve
> numeric rows.

The set, for reference:

| severity | parameter | condition |
|---|---|---|
| HIGH | `WARN_ELC_LPS_CONDUCTOR_CROSS_SECT_LOW` | Sheet thickness below natural-component minimum (BS EN 62305-3 Tab 3 — typ. 0.5 mm Cu / 0.7  |
| HIGH | `WARN_ELC_LPS_NO_BONDING` | Bonding method to LPS network not specified — natural component is non-functional without co |
| CRITICAL | `WARN_ELC_LPS_NO_CLASS` | LPS class not assigned — required to verify natural-component thickness vs BS EN 62305-3 Tab |
| HIGH | `WARN_PRJ_CLIENT_NAME_MISSING` | PRJ_ORG_CLIENT_NAME_TXT empty |
| HIGH | `WARN_PRJ_CODE_MISSING` | PRJ_ORG_PROJECT_CODE_TXT empty |
| HIGH | `WARN_PRJ_ORIGINATOR_MISSING` | PRJ_ORG_ORIGINATOR_CODE_TXT empty |
| HIGH | `WARN_STING_DRAWING_TYPE_MISSING` | STING_DRAWING_TYPE_ID_TXT empty on sheet |
| HIGH | `WARN_STING_CROP_DRIFT` | Crop kind/margin drift from profile |
| HIGH | `WARN_STING_PACK_DRIFT` | Pack checksum drift on managed template |
| MEDIUM | `WARN_STING_STYLE_LOCKED_NOTICE` | STING_STYLE_LOCKED_BOOL=1 |
| MEDIUM | `WARN_ELC_LPS_NO_ZONE` | Lightning protection zone (LPZ) not assigned |
| HIGH | `WARN_ELC_LPS_MESH_SIZE_EXCEEDED` | Air-termination mesh exceeds class limit (I=5×5 II=10×10 III=15×15 IV=20×20 m) |
| HIGH | `WARN_ELC_LPS_NO_RISK_ASSESSMENT` | BS EN 62305-2 risk assessment reference (R1..R4) not recorded |
| HIGH | `WARN_ELC_LPS_DOWN_COND_INSUFFICIENT` | Down conductor count below class minimum (Class I=4 II=3 III/IV=2) |
| HIGH | `WARN_ELC_LPS_SEPARATION_FAIL` | Separation distance below s = ki·kc·l/km — services flashover risk |
| HIGH | `WARN_ELC_LPS_EARTH_RESISTANCE_HIGH` | Earth resistance > 10 ohm — supplement electrodes or add ring earth |
| MEDIUM | `WARN_ELC_LPS_INSPECTION_OVERDUE` | Inspection interval exceeded (Class I/II=12mo III/IV=24mo) |

## STEP 4 - After the master is built

1. Load it into the project.
2. **Propagate Universal Tag**, master = `STING_LPS_Tag_Universal`.
3. The confirmation must say it is propagating the **LPS** master and that 197
   families belong to `universal` and will be skipped. If it says it is
   propagating the universal master, the master's own declaration is not being
   read - stop, because it would then skip all nine targets and report a clean
   run having done nothing.
4. Expect `9 propagated, 0 failed, 197 skipped (declared)`.
