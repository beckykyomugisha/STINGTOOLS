<!--
  AUTHORITATIVE human-authoring reference for the ONE universal STING tag label.
  Copied verbatim into the repo (was previously only at C:\Dev\TAGS 210626\) so the
  master-label spec is version-controlled alongside the code that consumes it
  (PropagateUniversalTagCommand, StampGateStatusCommand, ScheduleDisciplineTagExpanderCommand).

  This is a MANUAL Family-Editor build guide — the Revit API cannot author label rows.
  Build it once by hand on ONE family, then Propagate_UniversalTag clones it to all 206.
  Do NOT "fix" the double-pipe cells in the tables below — they are faithful to the
  source the user built from; the Prefix/Suffix columns there are intentionally blank.
-->

# Universal STING Tag Label - Build Sheet

**65 rows. One label. Every row Spaces=0. Type=Text for all calc values.** (now includes ASS_STATUS_TXT - near-universal, 71% of families.)

Every non-T1 row is a Calculated Value (fx): Name, Type=Text, paste Formula. Then Prefix/Suffix, Spaces=0, Break. Set Spaces=0 BEFORE ticking Break; Spaces editable only when the row ABOVE has no Break. Tier visibility = TAG_PARA_STATE_n_BOOL.

---

## STEP 1 - REMOVE from current master (9 rows)

Select row, click left-arrow (remove-from-label):

- HVC_DCT_FLW_CFM (T2)
- HVC_VEL_MPS (T2)
- MNT_HGT_MM (T2)
- all 6 T3 rows (Show Tier 3 - 8 ... 3 - 13)

## STEP 2 - Full universal row list (build/verify all in order)

| # | Tier | Calc Value Name | Formula | Prefix | Suffix | Break |
|---|---|---|---|---|---|---|
| 1 | T1 | --- | - |  |  | YES |
| 2 | T2 | Show Tier 2 - 2 | `if(TAG_PARA_STATE_2_BOOL, ASS_TAG_2_TXT, "")` |  |  | YES |
| 3 | T2 | Show Tier 2 - 3 | `if(TAG_PARA_STATE_2_BOOL, ASS_DESCRIPTION_TXT, "")` |  |  | YES |
| 4 | T2 | Show T2 - Status | `if(TAG_PARA_STATE_2_BOOL, ASS_STATUS_TXT, "")` | Status: |  | YES |
| 5 | T2 | Show Tier 2 - 7 | `if(TAG_PARA_STATE_2_BOOL, RGL_STD_TXT, "")` | Std: |  | YES |
| 6 | T2 | Show T2-Ph179 - Asset Systems | `if(TAG_PARA_STATE_2_BOOL, ASS_SYSTEMS_TXT, "")` | Sys: |  | no |
| 7 | T2 | Show T2-Ph179 - Mec System | `if(TAG_PARA_STATE_2_BOOL, MEC_SYS_TXT, "")` | MSys: |  | YES |
| 8 | T4 | Show T4 - Commissioning - State | `if(TAG_PARA_STATE_4_BOOL, COMM_STATE_TXT, "")` | Comm: |  | no |
| 9 | T4 | Show T4 - Commissioning - Date | `if(TAG_PARA_STATE_4_BOOL, COMM_DATE_TXT, "")` | on |  | no |
| 10 | T4 | Show T4 - Commissioning - Operative | `if(TAG_PARA_STATE_4_BOOL, COMM_OPERATIVE_TXT, "")` | by |  | YES |
| 11 | T4 | Show T4 - Design intent - ASS_DESIGN_OPTION_TXT | `if(TAG_PARA_STATE_4_BOOL, ASS_DESIGN_OPTION_TXT, "")` | Option: |  | no |
| 12 | T4 | Show T4 - Design intent - ASS_MODEL_REF_TXT | `if(TAG_PARA_STATE_4_BOOL, ASS_MODEL_REF_TXT, "")` | Ref: |  | no |
| 13 | T4 | Show T4 - Design intent - ASS_KEYNOTE_TXT | `if(TAG_PARA_STATE_4_BOOL, ASS_KEYNOTE_TXT, "")` | Key: |  | YES |
| 14 | T5 | Show T5 - Cost - UG Price | `if(TAG_PARA_STATE_5_BOOL, CST_UG_PRICE_UGX_DISP_TXT, "")` |  | UGX | no |
| 15 | T5 | Show T5 - Cost - Intl Price | `if(TAG_PARA_STATE_5_BOOL, CST_INTL_PRICE_USD_DISP_TXT, "")` | / | USD | no |
| 16 | T5 | Show T5 - Cost - Quote Ref | `if(TAG_PARA_STATE_5_BOOL, CST_QUOTE_REF_TXT, "")` | Quote: |  | YES |
| 17 | T5 | Show T5 - Cost - Unit Rate (neutral) | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_UNIT_RATE_NR_DISP_TXT, "")` | Rate: |  | no |
| 18 | T5 | Show T5 - Cost - Currency Code | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_CURRENCY_TXT, "")` |  |  | no |
| 19 | T5 | Show T5 - Cost - FX to Base | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_FX_TO_BASE_NR_DISP_TXT, "")` | FX: |  | no |
| 20 | T5 | Show T5 - Cost - FX Date | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_FX_DATE_DT, "")` | FX date: |  | no |
| 21 | T5 | Show T5 - Cost - As-of Date | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_AS_OF_DT, "")` | As of: |  | YES |
| 22 | T5 | Show T5 - Cost - Stale Flag | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_STALE_BOOL, "")` | Stale: |  | no |
| 23 | T5 | Show T5 - Cost - Stale Reason | `if(TAG_PARA_STATE_5_BOOL, ASS_CST_STALE_REASON_TXT, "")` | - |  | YES |
| 24 | T5 | Show T5 - Payment - % Complete | `if(TAG_PARA_STATE_5_BOOL, ASS_PMT_PCT_COMPLETE_NR_DISP_TXT, "")` | Cmpl: | % | no |
| 25 | T5 | Show T5 - Payment - Cert No | `if(TAG_PARA_STATE_5_BOOL, ASS_PMT_CERT_NO_NR_DISP_TXT, "")` | Cert#: |  | no |
| 26 | T5 | Show T5 - Payment - Cert Date | `if(TAG_PARA_STATE_5_BOOL, ASS_PMT_CERT_DATE_DT, "")` | Certified: |  | no |
| 27 | T5 | Show T5 - Payment - Last Valued | `if(TAG_PARA_STATE_5_BOOL, ASS_PMT_LAST_VALUED_DT, "")` | Valued: |  | YES |
| 28 | T5 | Show T5 - Variation - Number | `if(TAG_PARA_STATE_5_BOOL, ASS_VAR_NO_TXT, "")` | Var: |  | no |
| 29 | T5 | Show T5 - Variation - Instr Date | `if(TAG_PARA_STATE_5_BOOL, ASS_VAR_INSTRUCTION_DT, "")` | Instr: |  | no |
| 30 | T5 | Show T5 - Variation - Valuation | `if(TAG_PARA_STATE_5_BOOL, ASS_VAR_VALUATION_NR_DISP_TXT, "")` | Val: |  | YES |
| 31 | T5 | Show T5 - Performance & capacity - ASS_CAPACITY_TXT | `if(TAG_PARA_STATE_5_BOOL, ASS_CAPACITY_TXT, "")` | Cap: |  | no |
| 32 | T5 | Show T5 - Performance & capacity - ASS_POWER_RATING_TXT | `if(TAG_PARA_STATE_5_BOOL, ASS_POWER_RATING_TXT, "")` | Power: |  | no |
| 33 | T5 | Show T5 - Performance & capacity - ASS_FLOW_RATE_TXT | `if(TAG_PARA_STATE_5_BOOL, ASS_FLOW_RATE_TXT, "")` | Flow: |  | YES |
| 34 | T5 | Show T5-Ph179 - Item Code | `if(TAG_PARA_STATE_5_BOOL, ASS_ITEM_CODE_TXT, "")` | Item: |  | YES |
| 35 | T6 | Show T6 - Carbon - Product A1-A3 | `if(TAG_PARA_STATE_6_BOOL, CBN_A1_A3_KG_CO2E_DISP_TXT, "")` | A1-A3: | kgCO₂e | no |
| 36 | T6 | Show T6 - Carbon - Transport A4 | `if(TAG_PARA_STATE_6_BOOL, CBN_A4_KG_CO2E_DISP_TXT, "")` | A4: | kgCO₂e | no |
| 37 | T6 | Show T6 - Carbon - Operational B6 | `if(TAG_PARA_STATE_6_BOOL, CBN_B6_KG_CO2E_YR_DISP_TXT, "")` | B6: | kgCO₂e/yr | YES |
| 38 | T6 | Show T6 - Material & finish - ASS_MANUFACTURER_TXT | `if(TAG_PARA_STATE_6_BOOL, ASS_MANUFACTURER_TXT, "")` | Mfr: |  | no |
| 39 | T6 | Show T6 - Material & finish - ASS_MODEL_NR_TXT | `if(TAG_PARA_STATE_6_BOOL, ASS_MODEL_NR_TXT, "")` | Model: |  | no |
| 40 | T6 | Show T6 - Material & finish - ASS_EXPECTED_LIFE_YEARS_YRS | `if(TAG_PARA_STATE_6_BOOL, ASS_EXPECTED_LIFE_YEARS_YRS, "")` | Life: | yr | YES |
| 41 | T7 | Show T7 - Fabrication - Spool No | `if(TAG_PARA_STATE_7_BOOL, ASS_SPOOL_NR_TXT, "")` | Spool: |  | no |
| 42 | T7 | Show T7 - Fabrication - Status | `if(TAG_PARA_STATE_7_BOOL, ASS_FAB_STATUS_TXT, "")` | Fab: |  | no |
| 43 | T7 | Show T7 - QC - Inspector | `if(TAG_PARA_STATE_7_BOOL, ASS_QC_INSPECTOR_TXT, "")` | QC: |  | YES |
| 44 | T7 | Show T7 - Installation - ASS_INSTALL_DATE_TXT | `if(TAG_PARA_STATE_7_BOOL, ASS_INSTALL_DATE_TXT, "")` | Installed: |  | no |
| 45 | T7 | Show T7 - Installation - CST_INSTALL_HRS_DISP_TXT | `if(TAG_PARA_STATE_7_BOOL, CST_INSTALL_HRS_DISP_TXT, "")` | Hrs: | h | no |
| 46 | T7 | Show T7 - Installation - ASS_CST_INSTALL_UGX_NR | `if(TAG_PARA_STATE_7_BOOL, ASS_CST_INSTALL_UGX_NR, "")` | @ | UGX | YES |
| 47 | T8 | Show T8 - Clash - Triage Severity | `if(TAG_PARA_STATE_8_BOOL, CLASH_TRIAGE_SEVERITY_NR_DISP_TXT, "")` | Sev: | /5 | no |
| 48 | T8 | Show T8 - Clash - Triage Category | `if(TAG_PARA_STATE_8_BOOL, CLASH_TRIAGE_CATEGORY_TXT, "")` |  |  | no |
| 49 | T8 | Show T8 - Clash - Resolution Status | `if(TAG_PARA_STATE_8_BOOL, CLASH_RESOLUTION_STATUS_TXT, "")` | Res: |  | YES |
| 50 | T8 | Show T8 - Coordination - ASS_CRITICALITY_RATING_NR | `if(TAG_PARA_STATE_8_BOOL, ASS_CRITICALITY_RATING_NR, "")` | Crit: | /5 | no |
| 51 | T8 | Show T8 - Coordination - ASS_ZONE_TXT | `if(TAG_PARA_STATE_8_BOOL, ASS_ZONE_TXT, "")` | Zone: |  | no |
| 52 | T8 | Show T8 - Coordination - ASS_LVL_COD_TXT | `if(TAG_PARA_STATE_8_BOOL, ASS_LVL_COD_TXT, "")` | Lvl: |  | YES |
| 53 | T9 | Show T9 - As-built - Deviation | `if(TAG_PARA_STATE_9_BOOL, ASBUILT_DEVIATION_MM_DISP_TXT, "")` | Δ: | mm | no |
| 54 | T9 | Show T9 - As-built - Capture Date | `if(TAG_PARA_STATE_9_BOOL, ASBUILT_CAPTURE_DATE_TXT, "")` | on |  | no |
| 55 | T9 | Show T9 - Health Score | `if(TAG_PARA_STATE_9_BOOL, HEALTH_SCORE_LAST_NR_DISP_TXT, "")` | Health: | /100 | YES |
| 56 | T9 | Show T9 - Warranty & quality - ASS_WARRANTY_PARTS_TXT | `if(TAG_PARA_STATE_9_BOOL, ASS_WARRANTY_PARTS_TXT, "")` | Parts: |  | no |
| 57 | T9 | Show T9 - Warranty & quality - ASS_WARRANTY_LABOR_TXT | `if(TAG_PARA_STATE_9_BOOL, ASS_WARRANTY_LABOR_TXT, "")` | Labor: |  | no |
| 58 | T9 | Show T9 - Warranty & quality - ASS_WARRANTY_TXT | `if(TAG_PARA_STATE_9_BOOL, ASS_WARRANTY_TXT, "")` | Terms: |  | YES |
| 59 | T10 | Show T10 - Compliance - IFC PSet Override | `if(TAG_PARA_STATE_10_BOOL, IFC_PSET_OVERRIDE_TXT, "")` | IFC: |  | no |
| 60 | T10 | Show T10 - Compliance - ACC Issue | `if(TAG_PARA_STATE_10_BOOL, ACC_ISSUE_ID_TXT, "")` | ACC: |  | no |
| 61 | T10 | Show T10 - Compliance - ACC Sync Status | `if(TAG_PARA_STATE_10_BOOL, ACC_SYNC_STATUS_TXT, "")` | Sync: |  | YES |
| 62 | T10 | Show T10 - Classification - ASS_UNICLASS_2015_TXT | `if(TAG_PARA_STATE_10_BOOL, ASS_UNICLASS_2015_TXT, "")` | Uniclass: |  | no |
| 63 | T10 | Show T10 - Classification - ASS_KEYNOTE_TXT | `if(TAG_PARA_STATE_10_BOOL, ASS_KEYNOTE_TXT, "")` | Key: |  | no |
| 64 | T10 | Show T10 - Classification - ASS_MODEL_REF_TXT | `if(TAG_PARA_STATE_10_BOOL, ASS_MODEL_REF_TXT, "")` | Ref: |  | YES |
| 65 | T10 | Show T10-Ph179 - Trace Seq | `if(TAG_PARA_STATE_10_BOOL, ASS_TRACE_SEQ_NR_DISP_TXT, "")` | Trc: |  | YES |

> NEW row this revision: **ASS_STATUS_TXT** - "Show T2 - Status", prefix "Status:", after ASS_DESCRIPTION. Add it if your current master doesn't have it.

---

## STEP 4 - Status badge system (optional visual warnings)

Two badges: LEFT = data-completeness gate, RIGHT = QA / sign-off gate. When warnings are turned on, each shows green/amber/red; hidden otherwise and optional on print.

### New params
| Param | Type | Scope | Set by | Meaning |
|---|---|---|---|---|
| STING_GATE_DATA_STATUS_INT | Integer | Instance | Plugin | 0 = red / 1 = amber / 2 = green (data gate) |
| STING_GATE_QA_STATUS_INT | Integer | Instance | Plugin | 0 = red / 1 = amber / 2 = green (QA gate) |
| TAG_WARN_VISIBLE_BOOL | Yes/No | Instance | User toggle | master on/off for all badges (already in STINGTags_ISO19650 group) |

Add the two INT params to MR_PARAMETERS.txt (shared params, group STINGTags_ISO19650) if not present.

### Build the badges (in the family)
1. Place 6 glyphs: LEFT position = green check + amber triangle + red triangle (overlaid); RIGHT position = same three overlaid. Use nested generic-annotation symbols (recommended) or symbolic geometry, coloured via their subcategory.
2. Put all 6 glyphs on a new subcategory **STING_TagStatus** (Object Styles) - this is what makes them optional on print.
3. Add 6 family Yes/No params (family params, formula-driven) and link each glyph's **Visible** property to its param:

   LEFT (data gate):
   - vis_data_green = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 2)
   - vis_data_amber = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 1)
   - vis_data_red   = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 0)

   RIGHT (QA gate):
   - vis_qa_green = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_STATUS_INT = 2)
   - vis_qa_amber = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_STATUS_INT = 1)
   - vis_qa_red   = and(TAG_WARN_VISIBLE_BOOL, STING_GATE_QA_STATUS_INT = 0)

   If TAG_WARN_VISIBLE_BOOL is stored as TEXT, write the first argument as (TAG_WARN_VISIBLE_BOOL = "Yes"); if YESNO, use it bare.

### Print-optional
- Subcategory STING_TagStatus -> turn OFF in print / issue view templates (VG). On-screen QA views leave it ON. Revit has no per-element print flag; this subcategory switch is the clean equivalent.

### Plugin step (Claude codes this) — DONE (Phase 195 Task 2)
- Extend the compliance engine (ComplianceScan + validators) to compute each element's data gate and QA gate status and stamp STING_GATE_DATA_STATUS_INT / STING_GATE_QA_STATUS_INT (0/1/2). The badges read these automatically - no per-family logic.
- Implemented as `ComplianceScan.ComputeElementGates` + the `Gate_StampStatus` command ("Stamp Gates" button on the CREATE tab).

### Conveyor note
- Badges are family elements + params, so they ride the universal master and propagate to all 206 via SaveAs + recategorise. Nested-symbol survival is one item to confirm in the one-family sample test (see `UNIVERSAL_TAG_DUCT_SMOKE_TEST.md`).
