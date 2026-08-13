# Tag Tier Authoring Checklist (per-family, from current v5.0 CSVs / LABEL_DEFINITIONS.json v5.12)

155 families · 7146 total label rows · generated from the authoritative CSVs (not the lossy v7.8 xlsx).

Each row = one Label element to place, in order, styled as shown. Blank style/color/size = tier default. brk=Y means line-break after.

## [ARCH] STING - Wall Tag  (59 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_WALL_TYPE_CLASSIFICATION_TXT | Type: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_WALL_THICKNESS_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_ELE_AREA_SQ_M |  | m² | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | PER_THERM_U_VALUE_W_M2K |  | W/m²K | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | BLE_WALL_SOUND_TRANSMISSION_CLASS_RATING_NR | STC: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | BLE_WALL_EXTERIOR_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 |  |
| 11 | T2 | BLE_WALL_CST_METHOD_TXT | Const: |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ARCH_TAG_7_PARA_WALL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | BLE_WALL_PRIMARY_WALL_MAT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | BLE_WALL_CORE_MATERIAL_TXT | Core: |  | NOM | BLACK | 2.0 |  |
| 16 | T3 | BLE_WALL_INSULATION_TXT | | Insul: |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | PER_CONDENSATION_RISK_BOOL | Cond Risk: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | BLE_WALL_WATERPROOF_TXT | WP: |  | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 22 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 23 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 24 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 25 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 26 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 27 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 28 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 29 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 30 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 31 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 32 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 33 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 34 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 35 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 37 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 38 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 39 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 40 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 41 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 42 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 43 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 44 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 45 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 46 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 47 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 48 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 49 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 50 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 51 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 52 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 53 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 54 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 55 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 56 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 57 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 58 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 59 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Floor Tag  (56 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_FLR_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_FLR_THICKNESS_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_FLR_AREA_SQ_M |  | m² | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | PER_THERM_U_VALUE_W_M2K |  | W/m²K | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | BLE_WALL_INTERIOR_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 |  |
| 10 | T2 | BLE_FLOOR_SLIP_RESISTANCE_TXT | Slip: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ARCH_TAG_7_PARA_FLOOR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | BLE_FLR_FINISH_MAT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | BLE_FLOOR_LOAD_KN_M2 | Load: | kN/m² | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | BLE_WALL_SOUND_TRANSMISSION_CLASS_RATING_NR | STC: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | BLE_FLR_STRUCTURAL_SYSTEM_TXT | Const: |  | NOM | BLACK | 2.0 |  |
| 17 | T3 | BLE_FLOOR_WATERPROOF_TXT | | WP: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 21 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 22 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 23 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 24 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 25 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 26 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 27 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 31 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 32 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 34 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 36 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 37 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 38 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 39 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 40 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 41 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 42 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 43 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 44 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 45 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 46 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 47 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 48 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 49 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 50 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 51 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 52 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 53 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 54 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 55 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 56 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Ceiling Tag  (52 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_CEILING_TYPE_CLASSIFICATION_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_CEILING_HEIGHT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | BLE_WALL_SOUND_TRANSMISSION_CLASS_RATING_NR | STC: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | BLE_CEILING_NOISE_REDUCTION_COEFFICIENT_NRC_NR | NRC: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | BLE_WALL_INTERIOR_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ARCH_TAG_7_PARA_CEIL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | BLE_CEILING_MAT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | BLE_CEILING_GRID_TXT | Grid: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 17 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 18 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 20 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 21 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 28 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 29 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 30 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 34 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 35 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 37 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 38 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 40 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 41 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 42 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 43 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 44 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 46 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 47 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 48 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 49 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 50 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 52 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Roof Tag  (54 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_ROOF_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_ROOF_THICKNESS_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | PER_THERM_U_VALUE_W_M2K |  | W/m²K | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | BLE_ROOF_SLOPE_DEG | Slope: | ° | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | BLE_ROOF_AREA_SQ_M |  | m² | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ARCH_TAG_7_PARA_ROOF_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | BLE_ROOF_COVERING_MAT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | BLE_ROOF_PLM_DRN_SYSTEM_TYPE_TXT | Drain: |  | NOM | BLACK | 2.0 |  |
| 14 | T3 | PER_CONDENSATION_RISK_BOOL | | Cond: |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | BLE_ROOF_WATERPROOFING_SYSTEM_TXT | WP: |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 19 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 21 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 22 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 24 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 30 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 32 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 36 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 38 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 39 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 41 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 42 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 43 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 44 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 45 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 46 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 47 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 48 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 49 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 50 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 51 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 52 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 53 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 54 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Door Tag  (55 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_DOOR_WIDTH_MM | W: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_DOOR_HEIGHT_MM | × H: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_DOOR_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | BLE_DOOR_OPERATION_TYPE_TXT | Op: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | BLE_WALL_SOUND_TRANSMISSION_CLASS_RATING_NR | STC: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | BLE_DOOR_WIDTH_MM | Clear: | mm | NOM | BLACK | 2.0 | ✓ |
| 11 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ARCH_TAG_7_PARA_DOOR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | BLE_DOOR_MAT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | BLE_DOOR_HARDWARE_SPECIFICATION_TXT | HW: |  | NOM | BLACK | 2.0 |  |
| 15 | T3 | BLE_DOOR_GLAZING_BOOL | | Glazed: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | BLE_DOOR_THRESHOLD_TXT | Thresh: |  | NOM | BLACK | 2.0 |  |
| 17 | T3 | BLE_DOOR_CLOSER_TXT | | Closer: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 20 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 21 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 22 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 23 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 24 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 25 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 26 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 31 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 33 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 36 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 37 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 38 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 39 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 40 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 41 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 42 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 43 | T7 | ARC_FACTORY_ORDER_REF_TXT | FO: |  | BOLD | ORANGE | 2.0 |  |
| 44 | T7 | ARC_GLAZING_SPEC_TXT | Glz: |  | BOLD | ORANGE | 2.0 |  |
| 45 | T7 | ASS_WARRANTY_DURATION_PARTS_YRS | Warr: | yr | BOLD | ORANGE | 2.0 | ✓ |
| 46 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 47 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 48 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 49 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 50 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 51 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 52 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 53 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 54 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 55 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Window Tag  (53 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_WINDOW_WIDTH_MM | W: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_WINDOW_HEIGHT_MM | × H: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_WINDOW_TYPE_CLASSIFICATION_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | BLE_WINDOW_OPERATION_TYPE_TXT | Op: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PER_THERM_U_VALUE_W_M2K |  | W/m²K | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | BLE_WINDOW_SOLAR_HEAT_GAIN_COEFFICIENT_NR | g: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ARCH_TAG_7_PARA_WIN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | BLE_WINDOW_FRAME_MAT_TXT | Frame: |  | NOM | BLACK | 2.0 |  |
| 13 | T3 | BLE_WINDOW_GLAZING_TYPE_SINGLE_DOUBLE_TRIPLE_TXT | | Glaze: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | BLE_WINDOW_AREA_SQ_M | Vent: | m² | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | BLE_WALL_SOUND_TRANSMISSION_CLASS_RATING_NR | STC: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 18 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 21 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 29 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 31 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 35 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 38 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 41 | T7 | ARC_FACTORY_ORDER_REF_TXT | FO: |  | BOLD | ORANGE | 2.0 |  |
| 42 | T7 | ARC_GLAZING_SPEC_TXT | Glz: |  | BOLD | ORANGE | 2.0 |  |
| 43 | T7 | ASS_WARRANTY_DURATION_PARTS_YRS | Warr: | yr | BOLD | ORANGE | 2.0 | ✓ |
| 44 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 46 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 47 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 48 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 49 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 50 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 52 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 53 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Room Tag  (47 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_ROOM_AREA_SQ_M | Area: | m² | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ASS_DEPARTMENT_ASSIGNMENT_TXT | Dept: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | BLE_ROOM_OCCUPANCY_NR | | Occ: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | BLE_HEADROOM_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | BLE_ROOM_VENTILATION_RATE_LPS | Vent: | L/s | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | ASS_DESIGN_LTG_LVL_LUX_NR | Lux: | lx | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ARCH_TAG_7_PARA_ROOM_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | BLE_FLR_FINISH_TXT | Floor Fin: |  | NOM | BLACK | 2.0 |  |
| 13 | T3 | BLE_WALL_FINISH_TXT | | Wall Fin: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | BLE_CEILING_FINISH_TXT | | Ceil Fin: |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | BLE_ROOM_FIRE_ESCAPE_CAPACITY_NR | Esc Cap: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 18 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 21 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 29 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 31 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 35 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 38 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 41 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 42 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 43 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 44 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 47 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Stair Tag  (51 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_STAIR_WIDTH_MM | W: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_STAIR_RISE_MM | R: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_STAIR_GOING_MM | | G: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | BLE_STAIR_HEADROOM_MM | Hd: | mm | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ARCH_TAG_7_PARA_STAIR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | BLE_STAIR_HANDRAIL_MAT_TXT | Handrail: |  | NOM | BLACK | 2.0 |  |
| 12 | T3 | BLE_STAIR_NOSING_TYPE_TXT | | Nosing: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | BLE_STAIR_TREAD_FINISH_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 16 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 17 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 18 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 19 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 20 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 21 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 27 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 28 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 29 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 33 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 34 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 35 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 36 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 37 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 38 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 39 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 40 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 41 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 42 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 43 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 44 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 45 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 46 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 47 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 48 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 49 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 50 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 51 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Ramp Tag  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_RAMP_SLOPE_PCT | Slope: | % | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_RAMP_WIDTH_MM | W: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_RAMP_LENGTH_MM | L: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ARCH_TAG_7_PARA_RAMP_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | BLE_RAMP_LANDING_TXT | Landing: |  | NOM | BLACK | 2.0 |  |
| 10 | T3 | BLE_RAMP_HANDRAIL_BOOL | | Handrail: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 13 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 16 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 25 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 26 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 30 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 33 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 36 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 48 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Railing Tag  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_RAILING_HEIGHT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_RAILING_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | BLE_RAILING_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ARCH_TAG_7_PARA_RAILING_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | BLE_RAILING_INFILL_TXT | Infill: |  | NOM | BLACK | 2.0 |  |
| 10 | T3 | BLE_RAILING_BALUSTER_SPACING_MM | | Bal: | mm | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 13 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 16 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 25 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 26 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 30 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 33 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 36 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 48 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Furniture Tag  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_MODEL_NR_TXT | |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_FURNITURE_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ARCH_TAG_7_PARA_FURNITURE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_EXPECTED_LIFE_YEARS_YRS | Life: | yrs | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 13 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 16 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 25 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 26 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 30 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 33 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 36 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 48 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Casework Tag  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_CASEWORK_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_CASEWORK_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | BLE_CASEWORK_COUNTERTOP_TXT | Top: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ARCH_TAG_7_PARA_CASEWORK_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | BLE_CASEWORK_HARDWARE_TXT | HW: |  | NOM | BLACK | 2.0 |  |
| 10 | T3 | BLE_CASEWORK_ACCESSIBLE_BOOL | | Access: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 13 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 16 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 25 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 26 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 30 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 33 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 36 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 48 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Curtain Panel Tag  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_PANEL_WIDTH_MM | W: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_PANEL_HEIGHT_MM | × H: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | PER_THERM_U_VALUE_W_M2K |  | W/m²K | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | BLE_WINDOW_SOLAR_HEAT_GAIN_COEFFICIENT_NR | g: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ARCH_TAG_7_PARA_CURTAIN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | BLE_PANEL_GLASS_TYPE_TXT | Glass: |  | NOM | BLACK | 2.0 |  |
| 11 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 13 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 16 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 25 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 26 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 30 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 33 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 36 | T7 | ARC_FACTORY_ORDER_REF_TXT | FO: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ARC_GLAZING_SPEC_TXT | Glz: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_WARRANTY_DURATION_PARTS_YRS | Warr: | yr | BOLD | ORANGE | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 48 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Curtain Wall Mullion Tag  (50 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_MULLION_DEPTH_MM | D: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_MULLION_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_MULLION_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | BLE_PANEL_GLASS_TYPE_TXT | Glass: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | BLE_PANEL_HEIGHT_MM | PH: | mm | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | BLE_PANEL_WIDTH_MM | PW: | mm | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ARCH_TAG_7_PARA_MULLION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | PER_THERM_U_VALUE_W_M2K |  | W/m²K | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 15 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 16 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 17 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 18 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 19 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 20 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 26 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 27 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 28 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 32 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 33 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 34 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 35 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 36 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 37 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 38 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 39 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 40 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 41 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 42 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 43 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 44 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 45 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 46 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 47 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 48 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 49 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 50 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Signage Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_SIGN_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_SIGN_ILLUMINATED_BOOL | Illum: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ARCH_TAG_7_PARA_SIGNAGE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Parking Tag  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_PARK_BAY_WIDTH_MM | W: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_PARK_BAY_LENGTH_MM | × L: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_PARK_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ARCH_TAG_7_PARA_PARKING_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | BLE_PARK_ACCESSIBLE_BOOL | Access: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | BLE_PARK_EV_CHARGING_BOOL | | EV: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 13 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 16 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 25 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 26 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 30 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 33 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 36 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 48 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Areas Tag  (39 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_ROOM_AREA_SQ_M | Area: | m² | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ASS_DEPARTMENT_ASSIGNMENT_TXT | Dept: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_ROOM_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 36 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 37 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 38 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 39 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Wall Sweeps Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_WALL_TYPE_CLASSIFICATION_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_WALL_PRIMARY_WALL_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_WALL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Slab Edges Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_FLR_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_FLR_THICKNESS_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_FLOOR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | BLE_FLR_FINISH_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Roof Soffits Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_CEILING_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_WALL_INTERIOR_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_ROOF_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Fascia Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_ROOF_COVERING_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_WALL_EXTERIOR_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_ROOF_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Gutter Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_ROOF_PLM_DRN_SYSTEM_TYPE_TXT | Drain: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_ROOF_COVERING_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_ROOF_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Mass Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_ROOM_AREA_SQ_M | GIA: | m² | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ARCH_TAG_7_PARA_WALL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Furniture Systems Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_FURNITURE_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_FURNITURE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Food Service Equipment Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_MODEL_NR_TXT | |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_FURNITURE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Medical Equipment Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_MODEL_NR_TXT | |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_FURNITURE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_EXPECTED_LIFE_YEARS_YRS | Life: | yrs | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Entourage Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ARCH_TAG_7_PARA_FURNITURE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Stair Runs Tag  (47 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_STAIR_RISE_MM | R: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_STAIR_GOING_MM | | G: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_STAIR_WIDTH_MM | W: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ARCH_TAG_7_PARA_STAIR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | BLE_STAIR_TREAD_FINISH_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 12 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 15 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 24 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 25 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 29 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 32 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 35 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 38 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 41 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 42 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 43 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 44 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 47 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Stair Landings Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_STAIR_WIDTH_MM | W: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_STAIR_TREAD_FINISH_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_STAIR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Stair Supports Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_RAILING_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_STAIR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Top Rails Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_RAILING_HEIGHT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_RAILING_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_RAILING_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Handrails Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_RAILING_HEIGHT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_RAILING_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_RAILING_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ARCH_TAG_7_PARA_RAILING_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Vertical Circulation Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ARCH_TAG_7_PARA_STAIR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Architectural Sheet Tag  (25 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | SHT_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | SHT_DISC_TXT | Disc: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | SHT_FORM_TXT | Form: |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | SHT_LEVEL_TXT | Level: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | SHT_NUMBER_TXT | No: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | SHT_NAME_TXT | Name: |  | NOM | BLACK | 2.0 |  |
| 7 | T3 | SHT_TAG_7_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 9 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 11 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 12 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 17 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 18 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 19 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 22 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 23 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 25 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - Architectural Column Tag  (53 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_STRUCT_COLUMN_SZ_TXT | Size: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_COLUMN_SLENDERNESS | λ: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | BLE_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ARCH_TAG_7_PARA_COLUMNS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_GRID_REF_TXT | Grid: |  | NOM | BLACK | 2.0 |  |
| 12 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 15 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | RGL_QA_COMMISSIONING_REQ_TXT | Comm: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 21 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 29 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 31 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 35 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 38 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 41 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 42 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 43 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 44 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 46 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 47 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 48 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 49 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 50 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 52 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 53 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [ARCH] STING - LPS Natural Air Termination (Architectural Reuse) Tag  (42 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ELC_LPS_AIRTERM_TAG_TXT |  |  | BOLD | BLUE | 2.5 | ✓ |
| 2 | T2 | ELC_LPS_CLASS_TXT | Class: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_LPS_ZONE_TXT | LPZ: |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_LPS_CONDUCTOR_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ELC_LPS_CONDUCTOR_CROSS_SECT_MM2 | t: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ELC_LPS_BOND_TYPE_TXT | Bond: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ELC_LPS_RISK_ASSESSMENT_TXT | Risk: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 38 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 39 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 40 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [GEN] STING - Generic Model Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_PRODCT_COD_TXT | Prod: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_TAG_7_PARA_EQUIPMENT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Specialty Equipment Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_PRODCT_COD_TXT | Prod: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | ASS_FUNC_TXT | Func: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_TAG_7_PARA_EQUIPMENT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Site Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_LOC_TXT | Loc: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_ZONE_TXT | | Zone: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | GEN_ROAD_TYPE_TXT | Road: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ARCH_TAG_7_PARA_SITE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Planting Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | GEN_SPECIES_TXT | Species: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | GEN_HEIGHT_AT_MATURITY_M |  | m (H) | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ARCH_TAG_7_PARA_SITE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Hardscape Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_WALL_EXTERIOR_FINISH_TXT | Fin: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_WALL_THICKNESS_MM | Depth: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ARCH_TAG_7_PARA_SITE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Roads Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | GEN_ROAD_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | BLE_WALL_THICKNESS_MM | Depth: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ARCH_TAG_7_PARA_SITE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Pads Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_WALL_THICKNESS_MM | Thickness: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_BEARING_CAPACITY_KN_M2 | Bearing: | kN/m² | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ARCH_TAG_7_PARA_SITE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Toposolid Tag  (43 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_MAT_ELEM_TYPE_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 5 | T3 | ARCH_TAG_7_PARA_SITE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 38 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 39 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 40 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 43 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Parts Tag  (37 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_PRODCT_COD_TXT | Prod: |  | NOM | BLACK | 2.0 |  |
| 5 | T3 | GEN_TAG_7_PARA_MISC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 34 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 37 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Assemblies Tag  (38 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_PRODCT_COD_TXT | Code: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | GEN_TAG_7_PARA_MISC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 37 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 38 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Detail Items Tag  (41 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | GEN_TAG_7_PARA_MISC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 6 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 7 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 9 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 12 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 18 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 19 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 23 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 26 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 27 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 29 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 36 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 37 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 38 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 41 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Profiles Tag  (41 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | GEN_TAG_7_PARA_MISC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 6 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 7 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 9 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 12 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 18 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 19 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 23 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 26 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 27 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 29 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 36 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 37 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 38 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 41 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Materials Tag  (55 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | MAT_NAME |  |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T1 | MAT_CATEGORY |  |  | NOM | BLACK | 2.5 | ✓ |
| 4 | T2 | MAT_MANUFACTURER | Mfr: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | MAT_STANDARD | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MAT_CODE | Code: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | PROP_DENSITY_KG_M3 | ρ: | kg/m³ | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PROP_FIRE_RATING | Fire: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | PROP_THERMAL_COND_W_MK | λ: | W/mK | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | PROP_THERMAL_RES_M2K_W | R: | m²K/W | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | PROP_SPECIFIC_HEAT_J_KGK | Cp: | J/kgK | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | PROP_ACOUSTIC_ABS | α: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | PROP_SOUND_RED_DB | Rw: | dB | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | PROP_CARBON_KG_M3 | EC: | kgCO₂/m³ | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | PROP_COMP_STRENGTH_MPA | fc: | MPa | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | PROP_TENS_STRENGTH_MPA | ft: | MPa | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | MAT_DURABILITY | Durability: |  | NOM | BLACK | 2.0 |  |
| 18 | T3 | MAT_APPLICATION | Use: |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 20 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 21 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 22 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 23 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 24 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 25 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 26 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 31 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 33 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 36 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 37 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 38 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 39 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 40 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 41 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 42 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 43 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 44 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 45 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 46 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 47 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 48 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 49 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 50 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 51 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 52 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 53 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 54 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 55 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Point Loads Tag  (38 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_POINT_LOAD_KN |  | kN | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_LOAD_CASE_TXT | LC: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_LOAD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 37 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 38 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Line Loads Tag  (38 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_LINE_LOAD_KN_M |  | kN/m | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_LOAD_CASE_TXT | LC: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_LOAD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 37 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 38 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Area Loads Tag  (38 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_AREA_LOAD_KN_M2 |  | kN/m² | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_LOAD_CASE_TXT | LC: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_LOAD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 37 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 38 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Internal Point Loads Tag  (38 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_POINT_LOAD_KN |  | kN | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_LOAD_CASE_TXT | LC: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_LOAD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 37 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 38 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Internal Line Loads Tag  (38 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_LINE_LOAD_KN_M |  | kN/m | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_LOAD_CASE_TXT | LC: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_LOAD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 37 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 38 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Internal Area Loads Tag  (38 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_AREA_LOAD_KN_M2 |  | kN/m² | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_LOAD_CASE_TXT | LC: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_LOAD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 37 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 38 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Analytical Members Tag  (38 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_SECTION_PROFILE_TXT | Section: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_MATERIAL_GRADE_TXT | Grade: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_ANLYT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 37 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 38 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Analytical Nodes Tag  (36 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | STR_TAG_7_PARA_ANLYT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 31 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 33 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 34 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 36 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Analytical Panels Tag  (37 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_MATERIAL_GRADE_TXT | Grade: |  | NOM | BLACK | 2.0 |  |
| 5 | T3 | STR_TAG_7_PARA_ANLYT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 34 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 37 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Analytical Openings Tag  (36 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | STR_TAG_7_PARA_ANLYT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 31 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 33 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 34 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 36 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Analytical Links Tag  (36 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | STR_TAG_7_PARA_ANLYT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 31 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 33 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 34 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 36 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Model Groups Tag  (36 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | GEN_TAG_7_PARA_MISC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 31 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 33 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 34 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 36 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - RVT Links Tag  (36 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | GEN_TAG_7_PARA_MISC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 31 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 33 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 34 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 36 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Property Lines Tag  (37 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | RGL_PLOT_AREA_SQ_M |  | m² | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ARCH_TAG_7_PARA_SITE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 34 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 36 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 37 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Property Line Segments Tag  (36 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | ARCH_TAG_7_PARA_SITE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 31 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 33 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 34 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 36 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Bolt Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_BOLT_GRADE_TXT | Grade: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_BOLT_DIAMETER_MM | Ø: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | STR_TAG_7_PARA_CONN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Toposolid Links Tag  (36 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | ARCH_TAG_7_PARA_SITE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 31 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 33 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 34 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 35 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 36 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Weld Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_WELD_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_WELD_SIZE_MM | Weld: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | STR_TAG_7_PARA_CONN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Wire Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_WIRE_SIZE_TXT | Size: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ELC_CBL_INS_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | ELC_VOLTAGE_V |  | V | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ELC_TAG_7_PARA_CIRCUIT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Sheet Document Tag  (27 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | SHT_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | SHT_DISC_TXT | Disc: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | SHT_FORM_TXT | Form: |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | SHT_LEVEL_TXT | Level: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | SHT_ORIGINATOR_TXT | Orig: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | SHT_REV_TXT | Rev: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | SHT_NUMBER_TXT | No: |  | NOM | BLACK | 2.0 |  |
| 8 | T2 | SHT_NAME_TXT | Name: |  | NOM | BLACK | 2.0 |  |
| 9 | T3 | SHT_TAG_7_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - LPS Generic Component Tag  (39 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | BOLD | BLUE | 2.5 | ✓ |
| 2 | T2 | ELC_LPS_CLASS_TXT | Class: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_LPS_ZONE_TXT | LPZ: |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_LPS_COMPLIANCE_STATUS_TXT |  |  | BOLD | RED | 2.0 | ✓ |
| 5 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 6 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 7 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 8 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 9 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 11 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 12 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 17 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 18 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 19 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 22 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 23 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 25 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 26 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 27 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 28 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 29 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 31 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 34 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 35 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 36 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 37 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 38 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [GEN] STING - Specialty Equipment Tag Asset  (47 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_SERIAL_NR_TXT | Ser: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_ID_TXT | ID: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_INSTALL_DATE_TXT | Installed: |  | NOM | BLACK | 2.0 |  |
| 7 | T3 | ASS_EXPECTED_LIFE_YEARS_YRS | Life: | yr | NOM | BLACK | 2.0 |  |
| 8 | T3 | ASS_MAINTENANCE_FREQUENCY_MONTHS | Maint: | m | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_WARRANTY_TXT | Warr: |  | NOM | BLACK | 2.0 |  |
| 10 | T3 | ASS_CRITICALITY_RATING_NR | Crit: |  | NOM | BLACK | 2.0 |  |
| 11 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 15 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 24 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 25 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 29 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 32 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 35 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 38 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 41 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 42 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 43 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 44 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 47 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [GEN] STING - Specialty Equipment Tag General  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_FUNC_TXT | Func: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_SYSTEM_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 7 | T3 | ASS_MODEL_NR_TXT | Model: |  | NOM | BLACK | 2.0 |  |
| 8 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Air Terminal Tag  (53 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | HVC_DCT_FLW_CFM | Flow: | CFM | NOM | BLACK | 2.0 |  |
| 5 | T2 | HVC_VEL_MPS | | | m/s | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | HVC_TAG_7_PARA_AT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | HVC_DCT_TERMINAL_TYPE_SD_RG_EG_VAV_TXT |  | V | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | HVC_DCT_TERMINAL_SZ_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | HVC_TERMINAL_MAT_TXT |  | m | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | HVC_TERMINAL_FINISH_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 14 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 16 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 17 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 19 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 26 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 27 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 31 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 33 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 34 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 36 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 37 | T7 | HVC_SEGMENT_ROLE_TXT | Role: |  | BOLD | BLUE | 2.0 |  |
| 38 | T7 | HVC_PRESSURE_CLASS_TXT |  PrClass: |  | NOM | BLUE | 2.0 |  |
| 39 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 40 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 41 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 42 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 43 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 44 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 46 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 47 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 48 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 49 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 50 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 52 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 53 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Duct Accessory Tag  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | HVC_DCT_PROPERTY_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | HVC_TAG_7_PARA_DCTACC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | HVC_SEGMENT_ROLE_TXT | Role: |  | BOLD | BLUE | 2.0 |  |
| 32 | T7 | HVC_PRESSURE_CLASS_TXT |  PrClass: |  | NOM | BLUE | 2.0 |  |
| 33 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 35 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 36 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 47 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |
| 48 | T2 | HVC_DCT_INSULATION_THK_MM_DISP_TXT | Ins: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Duct Fitting Tag  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | HVC_DCT_PROPERTY_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | HVC_TAG_7_PARA_DCTACC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | HVC_DUCT_CLASS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | HVC_SEGMENT_ROLE_TXT | Role: |  | BOLD | BLUE | 2.0 |  |
| 32 | T7 | HVC_PRESSURE_CLASS_TXT |  PrClass: |  | NOM | BLUE | 2.0 |  |
| 33 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 35 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 36 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 47 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |
| 48 | T2 | HVC_DCT_INSULATION_THK_MM_DISP_TXT | Ins: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Duct Tag  (69 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | HVC_DCT_PROPERTY_TXT | [ | ] | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | HVC_DCT_FLW_CFM | Flow: | CFM | NOM | BLACK | 2.0 |  |
| 5 | T2 | HVC_VEL_MPS | | | m/s | NOM | BLACK | 2.0 |  |
| 6 | T2 | HVC_PIPE_PRESSURE_KPA | | | kPa | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | HVC_DCT_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 8 | T2 | HVC_INSULATION_TXT | | Insul: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | HVC_DCT_SHOP_DRAWING_REQ_BOOL | Shop: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | HVC_TAG_7_PARA_DUCT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | HVC_DUCT_CLASS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | HVC_DUCT_LINING_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | HVC_DUCT_AREA_SQ_M |  | m² | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | HVC_DUCT_FLOWRATE_M3H |  | m³/h | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | HVC_DCT_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | HVC_DCT_TAG_02_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | HVC_DCT_TAG_03_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | HVC_PIPE_LENGTH_M |  | m | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | HVC_AIR_CHANGES_PER_HR |  | A | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | HVC_DCT_FAB_METHOD_TXT | Fab: |  | NOM | BLACK | 2.0 |  |
| 22 | T3 | HVC_DCT_SEAM_TYPE_TXT | | Seam: |  | NOM | BLACK | 2.0 | ✓ |
| 23 | T3 | HVC_DCT_SUPPORTS_SPACING_MM | Supt: | mm | NOM | BLACK | 2.0 |  |
| 24 | T3 | HVC_DCT_HANGER_TYPE_TXT | | Hanger: |  | NOM | BLACK | 2.0 | ✓ |
| 25 | T3 | CST_CALC_LENGTH_M | Length: | m | NOM | BLACK | 2.0 |  |
| 26 | T3 | CST_CALC_AREA_M2 | | Area: | m² | NOM | BLACK | 2.0 | ✓ |
| 27 | T3 | CST_LOCAL_MAT_BOOL | Local: |  | NOM | BLACK | 2.0 |  |
| 28 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 29 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 30 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 31 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 32 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 33 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 34 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 35 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 36 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 37 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 38 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 39 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 40 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 41 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 42 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 43 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 44 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 45 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 46 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 47 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 48 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 49 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 50 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 51 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 52 | T7 | HVC_SEGMENT_ROLE_TXT | Role: |  | BOLD | BLUE | 2.0 |  |
| 53 | T7 | HVC_PRESSURE_CLASS_TXT |  PrClass: |  | NOM | BLUE | 2.0 |  |
| 54 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 55 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 56 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 57 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 58 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 59 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 60 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 61 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 62 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 63 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 64 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 65 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 66 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 67 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 68 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |
| 69 | T2 | HVC_DCT_INSULATION_THK_MM_DISP_TXT | Ins: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Flex Duct Tag  (47 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | HVC_DCT_FLW_CFM | Flow: | CFM | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | HVC_DCT_SZ_TXT | Size: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | INS_MATERIAL_TXT | Ins: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | HVC_TAG_7_PARA_FLEXDUCT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | HVC_DUCT_FLOWRATE_M3H |  | m³/h | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | HVC_SEGMENT_ROLE_TXT | Role: |  | BOLD | BLUE | 2.0 |  |
| 34 | T7 | HVC_PRESSURE_CLASS_TXT |  PrClass: |  | NOM | BLUE | 2.0 |  |
| 35 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 37 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 38 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - Mechanical Equipment Tag  (78 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | HVC_CAP_KW | Cooling: | kW | NOM | BLACK | 2.0 |  |
| 5 | T2 | HVC_PWR_KW | / | kW | NOM | BLACK | 2.0 |  |
| 6 | T2 | HVC_DCT_FLW_CFM | | | CFM | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | HVC_REFRIGERANT_TXT | Ref: |  | NOM | BLACK | 2.0 |  |
| 8 | T2 | HVC_EFF_RATIO_NR | | EER: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | HVC_BMS_CONTROL_TYPE_TXT | BMS: |  | NOM | BLACK | 2.0 |  |
| 10 | T2 | MNT_HGT_MM | | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 11 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 12 | T2 | ASS_MODEL_NR_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T2 | MEC_SET_CAPACITY_KW | Cap: | kW | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | HVC_TAG_7_PARA_SPEC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | HVC_EQP_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | HVC_EQP_TAG_02_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | HVC_EQP_TAG_03_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | HVC_CAP_TR |  | TR | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | HVC_VLT_V |  | V | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | HVC_CONTROL_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 22 | T3 | ASS_SERIAL_NR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 23 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 24 | T3 | ASS_SYSTEM_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 25 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 26 | T3 | ASS_CRITICALITY_RATING_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 27 | T3 | ASS_EXPECTED_LIFE_YEARS_YRS |  | yr | NOM | BLACK | 2.0 | ✓ |
| 28 | T3 | ASS_MAINTENANCE_FREQUENCY_MONTHS |  | m | NOM | BLACK | 2.0 | ✓ |
| 29 | T3 | RGL_NEMA_APPROVAL_REQ_BOOL |  | A | NOM | BLACK | 2.0 | ✓ |
| 30 | T3 | RGL_QA_APVD_VENDOR_TXT |  | A | NOM | BLACK | 2.0 | ✓ |
| 31 | T3 | RGL_NEMA_CERT_NR | Cert: |  | NOM | BLACK | 2.0 |  |
| 32 | T3 | RGL_QA_COMMISSIONING_REQ_TXT | | Comm: |  | NOM | BLACK | 2.0 | ✓ |
| 33 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 34 | T3 | CST_IMPORT_REQUIRED_BOOL | | Import: |  | NOM | BLACK | 2.0 | ✓ |
| 35 | T3 | CST_ENERGY_ANNUAL_UGX_NUM_NR | Energy: | UGX/yr | NOM | BLACK | 2.0 |  |
| 36 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 37 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 38 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 39 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 40 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 41 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 42 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 43 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 44 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 45 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 46 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 47 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 48 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 49 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 50 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 51 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 52 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 53 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 54 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 55 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 56 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 |  |
| 57 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 58 | T6 | CBN_RUNTIME_HRS_YR_NR_DISP_TXT | | Run: | hr/yr | ITALIC | GREEN | 2.0 |  |
| 59 | T6 | CBN_REFRIGERANT_GWP_KG_CO2E_DISP_TXT | | GWP: | kgCO₂e | ITALIC | GREEN | 2.0 | ✓ |
| 60 | T7 | HVC_REFRIGERANT_CHARGE_KG_NR_DISP_TXT | R-chg: | kg | BOLD | ORANGE | 2.0 |  |
| 61 | T7 | HVC_FACTORY_FLASH_TEST_DATE_TXT | Flash: |  | BOLD | ORANGE | 2.0 |  |
| 62 | T7 | HVC_FACTORY_QR_TXT | QR: |  | BOLD | ORANGE | 2.0 |  |
| 63 | T7 | HVC_CAPACITY_KW | Cap: | kW | BOLD | BLUE | 2.0 |  |
| 64 | T7 | HVC_REFRIGERANT_TYPE_TXT |  R: |  | NOM | BLUE | 2.0 |  |
| 65 | T7 | HVC_REFRIGERANT_KG_NR |   | kg | NOM | BLUE | 2.0 |  |
| 66 | T7 | HVC_SIZE_STALE_BOOL | StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 67 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 68 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 69 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 70 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 71 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 72 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 73 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 74 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 75 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 76 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 77 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 78 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - Flex Pipe Tag  (47 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | PLM_PPE_SZ_MM | DN | mm | NOM | BLACK | 2.0 |  |
| 4 | T2 | PLM_PPE_MAT_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | PLM_TAG_7_PARA_PIPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | PLM_PPE_LENGTH_M |  | m | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | HVC_PIPE_SERVICE_TXT | Svc: |  | BOLD | BLUE | 2.0 |  |
| 33 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 35 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 36 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 47 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Pipe Accessory Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | PLM_PPE_SZ_MM | DN | mm | NOM | BLACK | 2.0 |  |
| 4 | T2 | ASS_DESCRIPTION_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | PLM_TAG_7_PARA_PIPEACC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | HVC_PIPE_SERVICE_TXT | Svc: |  | BOLD | BLUE | 2.0 |  |
| 32 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 34 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 35 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 46 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Pipe Fitting Tag  (47 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | PLM_PPE_SZ_MM | DN | mm | NOM | BLACK | 2.0 |  |
| 4 | T2 | PLM_PPE_MAT_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | PLM_TAG_7_PARA_PIPEACC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | PLM_PPE_PSR_RATING_BAR |  | bar | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | HVC_PIPE_SERVICE_TXT | Svc: |  | BOLD | BLUE | 2.0 |  |
| 33 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 35 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 36 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 47 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Pipe Tag  (66 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | PLM_PPE_SZ_MM | DN | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | PLM_PPE_FLW_LPS | Flow: | L/s | NOM | BLACK | 2.0 |  |
| 5 | T2 | PLM_VEL_MPS | | | m/s | NOM | BLACK | 2.0 |  |
| 6 | T2 | PLM_PSR_KPA | | | kPa | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | PLM_PPE_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 8 | T2 | PLM_INSULATION_TXT | | Insul: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | PLM_PPE_SHOP_DRAWING_REQ_BOOL | Shop: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | PLM_TAG_7_PARA_PIPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | PLM_PPE_JOINT_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | PLM_PPE_PSR_RATING_BAR |  | bar | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | PLM_PPE_INS_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | PLM_PPE_INS_THK_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | PLM_PPE_LENGTH_M |  | m | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | PLM_EQP_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | PLM_EQP_TAG_02_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | PLM_VLV_END_CONNECTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | PLM_VLV_PSR_CLASS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | PLM_PPE_FAB_METHOD_TXT | Fab: |  | NOM | BLACK | 2.0 |  |
| 22 | T3 | PLM_PPE_WELD_TYPE_TXT | | Weld: |  | NOM | BLACK | 2.0 | ✓ |
| 23 | T3 | PLM_PPE_SUPPORTS_SPACING_MM | | Supt: | mm | NOM | BLACK | 2.0 | ✓ |
| 24 | T3 | PLM_PPE_HANGER_TYPE_TXT | Hanger: |  | NOM | BLACK | 2.0 |  |
| 25 | T3 | CST_CALC_LENGTH_M | | Length: | m | NOM | BLACK | 2.0 | ✓ |
| 26 | T3 | CST_LOCAL_MAT_BOOL | Local: |  | NOM | BLACK | 2.0 |  |
| 27 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 28 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 29 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 30 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 31 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 32 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 33 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 34 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 35 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 36 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 37 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 38 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 39 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 40 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 41 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 42 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 43 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 44 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 45 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 46 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 47 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 48 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 49 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 50 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 51 | T7 | HVC_PIPE_SERVICE_TXT | Svc: |  | BOLD | BLUE | 2.0 |  |
| 52 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 53 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 54 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 55 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 56 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 57 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 58 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 59 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 60 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 61 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 62 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 63 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 64 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 65 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 66 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Plumbing Fixture Tag  (60 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | PLM_PPE_SZ_MM | DN | mm | NOM | BLACK | 2.0 |  |
| 4 | T2 | PLM_PPE_FLW_LPS | | | L/s | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | ASS_MODEL_NR_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | RGL_NWSC_APPROVAL_REQ_BOOL | NWSC: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | PLM_TAG_7_PARA_FIXTURE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | PLM_VLV_ACTUATION_TYPE_TXT |  | A | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | PLM_VLV_BODY_MAT_TXT |  | V | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | PLM_VLV_CV_NR |  | V | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | PLM_ACC_CONNECTION_SZ_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | PLM_VNT_SZ_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_EXPECTED_LIFE_YEARS_YRS |  | yr | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | PER_SUST_WTR_RATING_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | RGL_NWSC_CERT_NR | Cert: |  | NOM | BLACK | 2.0 |  |
| 18 | T3 | PER_SUST_WATER_RATING_TXT | | Water: |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | CST_LOCAL_MAT_BOOL | Local: |  | NOM | BLACK | 2.0 |  |
| 20 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | | Lead: | days | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 22 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 23 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 24 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 25 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 26 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 27 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 28 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 31 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 32 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 33 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 35 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 36 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 37 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 38 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 39 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 40 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 41 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 42 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 43 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 44 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 45 | T7 | HVC_PIPE_SERVICE_TXT | Svc: |  | BOLD | BLUE | 2.0 |  |
| 46 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 47 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 48 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 49 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 50 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 51 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 52 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 53 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 54 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 55 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 56 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 57 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 58 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 59 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 60 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Fire Alarm Device Tag  (53 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | FLS_SFTY_DEV_TYPE_TXT |  |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | FLS_SFTY_DEV_ZONE_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_KCCA_APPROVAL_REQ_BOOL | KCCA: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | FLS_TAG_7_PARA_FA_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | FLS_SFTY_DEV_LOOP_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | FLS_SFTY_DEV_ADDRESS_TXT |  | A | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | FLS_SFTY_DEV_DETECTOR_SENSITIVITY_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | FLS_ALARM_CIRCUIT_COUNT_NR |  | A | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | FLS_DETECTOR_COUNT_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | FLS_DETECTION_COST_UGX |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | RGL_QA_TST_REPORT_REF_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | RGL_KCCA_CERT_NR | Cert: |  | NOM | BLACK | 2.0 |  |
| 17 | T3 | RGL_QA_COMMISSIONING_REQ_TXT | | Comm: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 19 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 21 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 22 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 24 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 30 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 32 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 36 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 38 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 39 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 41 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 42 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 43 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 44 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 45 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 46 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 47 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 48 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 49 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 50 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 51 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 52 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 53 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - Sprinkler Tag  (55 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | FLS_SFTY_COVERAGE_AREA_SQ_M | Coverage: | m² | NOM | BLACK | 2.0 |  |
| 4 | T2 | FLS_SFTY_K_FACTOR_NR | | K= |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_KCCA_APPROVAL_REQ_BOOL | KCCA: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | FLS_TAG_7_PARA_SPR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | FLS_SFTY_TEMP_RATING_C |  | °C | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | FLS_PROT_SPRINKLER_HED_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | FLS_SFTY_FLW_RATE_LPM_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | FLS_DEV_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | FLS_DEV_TAG_02_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | FLS_DETECTION_COST_UGX |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | FLS_DETECTOR_COUNT_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | FLS_SFTY_HEADS_OPERATING_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | RGL_KCCA_CERT_NR | Cert: |  | NOM | BLACK | 2.0 |  |
| 18 | T3 | RGL_QA_COMMISSIONING_REQ_TXT | | Comm: |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | CST_LOCAL_MAT_BOOL | Local: |  | NOM | BLACK | 2.0 |  |
| 20 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 21 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 22 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 23 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 24 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 25 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 26 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 27 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 31 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 32 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 34 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 36 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 37 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 38 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 39 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 40 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 41 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 42 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 43 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 44 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 45 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 46 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 47 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 48 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 49 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 50 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 51 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 52 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 53 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 54 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 55 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - Cable Tray Fitting Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_CTR_MAT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ELC_TAG_7_PARA_TRAY_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 38 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 39 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 40 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 43 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 44 | T2 | BLE_CBL_TRAY_WIDTH_MM_DISP_TXT | W: | mm | NOM | BLACK | 2.0 |  |
| 45 | T2 | BLE_CBL_TRAY_DEPTH_MM_DISP_TXT | D: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Cable Tray Tag  (54 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_CTR_MAT_TXT |  |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | MNT_HGT_MM | | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | ELC_CBT_SHOP_DRAWING_REQ_BOOL | Shop: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | ELC_CTR_SZ_TXT | Size: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ELC_TAG_7_PARA_TRAY_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ELC_CTR_WIDTH_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ELC_CTR_FILL_PCT |  | % | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ELC_CTR_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ELC_CBT_FAB_TYPE_TXT | Fab: |  | NOM | BLACK | 2.0 |  |
| 13 | T3 | ELC_CBT_SUPPORT_SPACING_MM | | Supt: | mm | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | CST_CALC_LENGTH_M | Length: | m | NOM | BLACK | 2.0 |  |
| 15 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 17 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 18 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 20 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 21 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 28 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 29 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 30 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 34 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 35 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 37 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 38 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 40 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 41 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 42 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 43 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 44 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 46 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 47 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 48 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 49 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 50 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 52 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 53 | T2 | BLE_CBL_TRAY_WIDTH_MM_DISP_TXT | W: | mm | NOM | BLACK | 2.0 |  |
| 54 | T2 | BLE_CBL_TRAY_DEPTH_MM_DISP_TXT | D: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Conduit Fitting Tag  (43 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_CDT_SZ_MM | Conduit: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ELC_TAG_7_PARA_CONDUIT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 38 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 39 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 40 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 43 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Conduit Tag  (58 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | ELC_CDT_SZ_MM | Conduit: | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_CKT_NR | Ckt: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ELC_CDT_MAT_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | ELC_CBL_SOURCE_TXT | From: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | ELC_CBL_DEST_TXT | → |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | ELC_CBL_INSTALL_METHOD_TXT | Install: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ELC_TAG_7_PARA_CONDUIT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ELC_CDT_INSTALL_METHOD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ELC_CDT_CBL_FILL_PCT |  | % | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ELC_CDT_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | ELC_CDT_TAG_02_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ELC_CBL_NUM_OF_CORES_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ELC_CKT_PHASE_COUNT_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | ELC_CDT_FAB_METHOD_TXT | Fab: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | ELC_CDT_BEND_RADIUS_MM | Bend R: | mm | NOM | BLACK | 2.0 |  |
| 19 | T3 | ELC_CDT_SUPPORT_SPACING_MM | | Supt: | mm | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | CST_CALC_LENGTH_M | Length: | m | NOM | BLACK | 2.0 |  |
| 21 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 22 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 23 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 24 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 25 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 26 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 27 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 28 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 29 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 31 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 32 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 33 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 34 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 36 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 37 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 38 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 39 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 40 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 41 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 42 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 43 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 44 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 45 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 46 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 47 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 48 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 49 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 50 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 51 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 52 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 53 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 54 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 55 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 56 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 57 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 58 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Electrical Equipment Tag  (73 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | ELC_PNL_DESIGNATION_NAME_TXT | Panel: |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_PNL_VLT_V |  | V | NOM | BLACK | 2.0 |  |
| 5 | T2 | ELC_PNL_PHS_COUNT_NR | | | Ph | NOM | BLACK | 2.0 |  |
| 6 | T2 | ELC_PNL_NUM_OF_WAYS_NR | | | Ways | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | ELC_PNL_RATED_KW | Rated: | kW | NOM | BLACK | 2.0 |  |
| 8 | T2 | ELC_PNL_CONNECTED_LOAD_KW | | Load: | kW | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | ELC_PNL_MAIN_BRK_A | MCB: | A | NOM | BLACK | 2.0 |  |
| 10 | T2 | ELC_PNL_SPARE_WAYS_NR | | Spare: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T2 | ELC_VLT_DROP_PCT | VD: | % | NOM | BLACK | 2.0 |  |
| 12 | T2 | ELC_PNL_SHORT_CIRCUIT_KA | | Fault: | kA | NOM | BLACK | 2.0 | ✓ |
| 13 | T2 | ELC_CBL_FEEDER_SZ_MM2 | Feeder: | mm² | NOM | BLACK | 2.0 |  |
| 14 | T2 | ELC_PNL_IP_RATING_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T2 | RGL_UMEME_APPROVAL_REQ_BOOL | UMEME: |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T2 | ELC_VOLTAGE_V | V: | V | NOM | BLACK | 2.0 | ✓ |
| 18 | T2 | ELC_CONN_TYPE_TXT | Conn: |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | ELC_TAG_7_PARA_PANEL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | ELC_EQP_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | ELC_EQP_TAG_02_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 22 | T3 | ELC_PNL_FED_FROM_PNL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 23 | T3 | ELC_SPARE_WAYS_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 24 | T3 | ELC_DIV_FCT_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 25 | T3 | ELC_IP_RATING_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 26 | T3 | ASS_SERIAL_NR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 27 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 28 | T3 | ASS_CRITICALITY_RATING_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 29 | T3 | ASS_EXPECTED_LIFE_YEARS_YRS |  | yr | NOM | BLACK | 2.0 | ✓ |
| 30 | T3 | ELC_CBL_AMPACITY_A |  | A | NOM | BLACK | 2.0 | ✓ |
| 31 | T3 | ELC_CBL_LENGTH_M |  | m | NOM | BLACK | 2.0 | ✓ |
| 32 | T3 | RGL_UMEME_APPROVAL_BOOL |  | A | NOM | BLACK | 2.0 | ✓ |
| 33 | T3 | RGL_NEMA_APPROVAL_REQ_BOOL |  | A | NOM | BLACK | 2.0 | ✓ |
| 34 | T3 | RGL_UMEME_CERT_NR | Cert: |  | NOM | BLACK | 2.0 |  |
| 35 | T3 | RGL_QA_COMMISSIONING_REQ_TXT | Comm: |  | NOM | BLACK | 2.0 |  |
| 36 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | | Lead: | days | NOM | BLACK | 2.0 | ✓ |
| 37 | T3 | CST_IMPORT_REQUIRED_BOOL | Import: |  | NOM | BLACK | 2.0 |  |
| 38 | T3 | CST_ENERGY_ANNUAL_UGX_NUM_NR | | Energy: | UGX/yr | NOM | BLACK | 2.0 | ✓ |
| 39 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 40 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 41 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 42 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 43 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 44 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 45 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 46 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 47 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 48 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 49 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 50 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 51 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 52 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 53 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 54 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 55 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 56 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 57 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 58 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 59 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 |  |
| 60 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 61 | T6 | CBN_RUNTIME_HRS_YR_NR_DISP_TXT | | Run: | hr/yr | ITALIC | GREEN | 2.0 | ✓ |
| 62 | T7 | ELC_PANEL_SCHEDULE_REF_TXT | PS: |  | BOLD | ORANGE | 2.0 |  |
| 63 | T7 | ELC_FAT_CERT_REF_TXT | FAT: |  | BOLD | ORANGE | 2.0 |  |
| 64 | T7 | ELC_FACTORY_QR_TXT | QR: |  | BOLD | ORANGE | 2.0 | ✓ |
| 65 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 66 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 67 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 68 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 69 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 70 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 71 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 72 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 73 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - Electrical Fixture Tag  (49 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_PWR_KW | Power: | kW | NOM | BLACK | 2.0 |  |
| 4 | T2 | ELC_VLT_PRIMARY_RATING_V | | | V | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ELC_CKT_NR | Ckt: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | ELC_PNL_DESIGNATION_NAME_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | ELC_VOLTAGE_V | V: | V | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ELC_TAG_7_PARA_CIRCUIT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 12 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 15 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 24 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 25 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 29 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 32 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 35 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 38 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 41 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 42 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 43 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 44 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 47 | T2 | EV_CHARGER_TYPE_TXT | EV: |  | NOM | BLACK | 2.0 | ✓ |
| 48 | T2 | EV_CHARGER_KW |  | kW | NOM | BLACK | 2.0 | ✓ |
| 49 | T2 | EV_CHARGER_CIRCUIT_REF | Ckt: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Lighting Device Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_CKT_NR | Ckt: |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | ELC_PNL_DESIGNATION_NAME_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ELC_TAG_7_PARA_CIRCUIT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | LTG_CTRL_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_RUNTIME_HRS_YR_NR_DISP_TXT | | Run: | hr/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Lighting Fixture Tag  (59 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | LTG_FIX_TYPE_CLASSIFICATION_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | LTG_FIX_LMP_WATTAGE_W |  | W | NOM | BLACK | 2.0 |  |
| 5 | T2 | CST_FIX_LUMEN_OUTPUT_LM | | | lm | NOM | BLACK | 2.0 |  |
| 6 | T2 | LTG_CLR_TEMP_K | | | K | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | LTG_EFFICACY_LM_W | Eff: | lm/W | NOM | BLACK | 2.0 |  |
| 8 | T2 | ELC_CKT_NR | | Ckt: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 10 | T2 | ASS_MODEL_NR_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 12 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ELC_TAG_7_PARA_CIRCUIT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | LTG_FIX_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | LTG_FIX_TAG_02_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | LTG_EMRG_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | LTG_CLR_RENDERING_INDEX_NR | CRI: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | LTG_CKT_NR_TXT | Ckt: |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | LTG_CTRL_TYPE_TXT | Ctrl: |  | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | LTG_DESIGN_ILLUMINANCE_LUX |  | lux | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | ASS_EXPECTED_LIFE_YEARS_YRS |  | yr | NOM | BLACK | 2.0 | ✓ |
| 22 | T3 | PER_SUST_ENERGY_RATING_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 23 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 24 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 25 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 26 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 27 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 28 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 29 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 30 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 31 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 32 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 33 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 34 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 35 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 37 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 38 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 39 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 40 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 41 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 42 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 43 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 44 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 |  |
| 45 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 46 | T6 | CBN_RUNTIME_HRS_YR_NR_DISP_TXT | | Run: | hr/yr | ITALIC | GREEN | 2.0 | ✓ |
| 47 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 48 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 49 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 50 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 51 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 52 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 53 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 54 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 55 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 56 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 57 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 58 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 59 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Communication Device Tag  (51 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | COM_C_PROTOCOL_TXT |  |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | COM_C_NETWORK_ADDRESS_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | COM_TAG_7_PARA_BMS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | COM_DEV_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | COM_C_DEV_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | COM_C_MEASUREMENT_RANGE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | COM_C_PWR_SUPPLY_V |  | V | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | COM_BMS_RANGE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | COM_C_DEV_BACNET_INSTANCE_INT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | COM_C_DEV_CONTROLLER_NAME_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 16 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 17 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 18 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 19 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 20 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 21 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 27 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 28 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 29 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 33 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 34 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 35 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 36 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 37 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 38 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 39 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 40 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 41 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 42 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 43 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 44 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 45 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 46 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 47 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 48 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 49 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 50 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 51 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Data Device Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ICT_OUTLET_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ICT_TAG_7_PARA_DATA_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ICT_DEV_TAG_01_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Nurse Call Device Tag  (43 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | NCL_ZONE_TXT | Zone: |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | NCL_TAG_7_PARA_NURSECALL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 38 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 39 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 40 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 43 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Security Device Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | SEC_DEV_ZONE_TXT | Zone: |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | ASS_DESCRIPTION_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | SEC_TAG_7_PARA_SECURITY_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 12 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 15 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 21 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 22 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 26 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 29 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 32 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Telephone Devices Tag  (47 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ICT_OUTLET_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ICT_PORT_NR_TXT | Port: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ICT_PATCH_PANEL_TXT | | PP: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ICT_TAG_7_PARA_TEL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ICT_TEL_OUTLET_QTY_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ICT_TEL_PORT_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 12 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 15 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 24 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 25 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 29 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 32 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 35 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 38 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 41 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 42 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 43 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 44 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 47 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Generic Model Tag  (54 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_MODEL_NR_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_TAG_7_PARA_EQUIPMENT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_CAT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_SERIAL_NR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_CST_QUANTITY_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_CST_UNIT_PRICE_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ASS_EXPECTED_LIFE_YEARS_YRS |  | yr | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | CST_LOCAL_MAT_BOOL | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | CST_IMPORT_REQUIRED_BOOL | Import: |  | NOM | BLACK | 2.0 |  |
| 16 | T3 | PER_SUST_EPD_REF_TXT | | EPD: |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | RGL_QA_STD_COMPLY_TXT | Standard: |  | NOM | BLACK | 2.0 |  |
| 18 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 19 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 21 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 22 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 24 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 30 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 32 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 36 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 38 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 39 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 41 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 42 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 43 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 44 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 45 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 46 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 47 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 48 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 49 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 50 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 51 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 52 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 53 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 54 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Specialty Equipment Tag  (58 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_MODEL_NR_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_TAG_7_PARA_EQUIPMENT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_SERIAL_NR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_SYSTEM_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_CAT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ASS_ID_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_CRITICALITY_RATING_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ASS_MAINTENANCE_FREQUENCY_MONTHS |  | m | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | RGL_QA_APVD_VENDOR_TXT |  | A | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | RGL_QA_TST_REPORT_REF_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | CST_IMPORT_REQUIRED_BOOL | Import: |  | NOM | BLACK | 2.0 |  |
| 21 | T3 | CST_SPECIAL_EQUIPMENT_TXT | | Special: |  | NOM | BLACK | 2.0 | ✓ |
| 22 | T3 | RGL_QA_COMMISSIONING_REQ_TXT | Comm: |  | NOM | BLACK | 2.0 |  |
| 23 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 24 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 25 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 26 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 27 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 28 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 29 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 31 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 32 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 33 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 34 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 36 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 37 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 38 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 39 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 40 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 41 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 42 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 43 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 44 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 45 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 46 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 47 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 48 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 49 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 50 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 51 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 52 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 53 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 54 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 55 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 56 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 57 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 58 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Duct Insulation Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | PLM_INS_THICKNESS_MM |  | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | INS_MATERIAL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T7 | HVC_SEGMENT_ROLE_TXT | Role: |  | BOLD | BLUE | 2.0 |  |
| 31 | T7 | HVC_PRESSURE_CLASS_TXT |  PrClass: |  | NOM | BLUE | 2.0 |  |
| 32 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 34 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 35 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - Duct Lining Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | PLM_INS_THICKNESS_MM |  | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | INS_MATERIAL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | HVC_TAG_7_PARA_DUCT_LINING_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 33 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 36 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 37 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 38 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 39 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 42 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 43 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |
| 44 | T2 | HVC_DCT_INSULATION_THK_MM_DISP_TXT | Ins: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Pipe Insulation Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | PLM_INS_THICKNESS_MM |  | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | INS_MATERIAL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | PLM_TAG_7_PARA_PIPE_INSULATION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T7 | HVC_PIPE_SERVICE_TXT | Svc: |  | BOLD | BLUE | 2.0 |  |
| 31 | T7 | HVC_SIZE_STALE_BOOL |  StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 33 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 34 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 39 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 40 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 41 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 44 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 45 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Plumbing Equipment Tag  (56 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | PLM_EQP_CAPACITY_L | Cap: | L | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | ASS_MODEL_NR_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | PLM_TAG_7_PARA_PLM_EQUIP_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | PLM_EQP_PRESSURE_KPA | P: | kPa | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | PLM_HOTWTR_STORAGE_CAP_GAL_NR |  | L | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | PLM_HOTWTR_INPUT_PWR_KW |  | kW | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | PLM_HOTWTR_TEMP_C |  | °C | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | PLM_CAP_CU_M |  | m³ | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | PLM_HED_M |  | m | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | PLM_PWR_KW |  | kW | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ASS_SERIAL_NR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | ASS_CRITICALITY_RATING_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | ASS_MAINTENANCE_FREQUENCY_MONTHS |  | m | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | RGL_NWSC_APPROVAL_BOOL |  | A | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | RGL_QA_APVD_VENDOR_TXT |  | A | NOM | BLACK | 2.0 | ✓ |
| 22 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 23 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 24 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 25 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 26 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 27 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 28 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 31 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 32 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 33 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 35 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 36 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 37 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 38 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 39 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 40 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 41 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 42 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 43 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 44 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 45 | T7 | PLM_PRESSURE_TEST_REF_TXT | HT: |  | BOLD | ORANGE | 2.0 |  |
| 46 | T7 | PLM_WELD_MAP_REF_TXT | WM: |  | BOLD | ORANGE | 2.0 |  |
| 47 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 48 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 49 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 50 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 51 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 52 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 53 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 54 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 55 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 56 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - Mechanical Control Devices Tag  (43 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | MEC_CTRL_FUNCTION_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | MEC_CTRL_SIGNAL_TXT | Signal: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | HVC_TAG_7_PARA_MECH_CTRL_DEV_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_RUNTIME_HRS_YR_NR_DISP_TXT | | Run: | hr/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 33 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 36 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 37 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 38 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 39 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 42 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 43 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Mechanical Equipment Sets Tag  (49 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T3 | MEC_SET_CAPACITY_KW | Capacity: | kW | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 5 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 6 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 7 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 8 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 9 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 11 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 12 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 17 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 18 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 19 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 22 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 23 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 25 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 |  |
| 26 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 27 | T6 | CBN_RUNTIME_HRS_YR_NR_DISP_TXT | | Run: | hr/yr | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_REFRIGERANT_GWP_KG_CO2E_DISP_TXT | | GWP: | kgCO₂e | ITALIC | GREEN | 2.0 | ✓ |
| 29 | T7 | HVC_REFRIGERANT_CHARGE_KG_NR_DISP_TXT | R-chg: | kg | BOLD | ORANGE | 2.0 |  |
| 30 | T7 | HVC_FACTORY_FLASH_TEST_DATE_TXT | Flash: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | HVC_FACTORY_QR_TXT | QR: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | HVC_CAPACITY_KW | Cap: | kW | BOLD | BLUE | 2.0 |  |
| 33 | T7 | HVC_REFRIGERANT_TYPE_TXT |  R: |  | NOM | BLUE | 2.0 |  |
| 34 | T7 | HVC_REFRIGERANT_KG_NR |   | kg | NOM | BLUE | 2.0 |  |
| 35 | T7 | HVC_SIZE_STALE_BOOL | StaleSize: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | HVC_SIZE_MODIFIED_DT |  Sized: |  | NOM | ORANGE | 2.0 |  |
| 37 | T7 | HVC_SIZE_PREV_TXT |  Prev: |  | NOM | GREY | 2.0 |  |
| 38 | T7 | HVC_SIZE_RULE_ID_TXT |  Rule: |  | ITALIC | GREY | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 48 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 49 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Electrical Connectors Tag  (42 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | ELC_CONN_TYPE_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_CKT_BRK_RATING_A |  | A | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ELC_TAG_7_PARA_ELC_CONNECTORS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 33 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 36 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 37 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 38 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 39 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 42 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Fire Protection Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | FLS_PROT_FLS_RESISTANCE_RATING_MINUTES_MIN |  | min | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | FLS_PROT_PRODUCT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | FLS_PROT_CORROSION_PROT_LVL_NR |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | FLS_PROT_FLS_ESCAPE_STAIR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | FLS_EXIT_TRAVEL_DIST_M |  | m | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | FLS_EVACUATION_TIME_MIN |  | m | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | FLS_PROT_FLS_RESISTANCE_MINUTES_CALC |  | m | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | RGL_OCCUPANCY_CERT_REQ_BOOL |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 12 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 15 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 24 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 25 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 29 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_RUNTIME_HRS_YR_NR_DISP_TXT | | Run: | hr/yr | ITALIC | GREEN | 2.0 | ✓ |
| 35 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 38 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 41 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 42 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 43 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 44 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - MEP Fabrication Containment Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | FAB_CONTAINMENT_W_MM |  | × | NOM | BLACK | 2.5 |  |
| 3 | T1 | FAB_CONTAINMENT_D_MM |  | mm | NOM | BLACK | 2.5 | ✓ |
| 4 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 33 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 36 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 37 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 38 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 39 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 42 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 43 | T2 | BLE_CBL_TRAY_WIDTH_MM_DISP_TXT | W: | mm | NOM | BLACK | 2.0 |  |
| 44 | T2 | BLE_CBL_TRAY_DEPTH_MM_DISP_TXT | D: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - MEP Fabrication Ductwork Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | FAB_DCT_W_MM |  | × | NOM | BLACK | 2.5 |  |
| 3 | T1 | FAB_DCT_H_MM |  | mm | NOM | BLACK | 2.5 | ✓ |
| 4 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 33 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 36 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 37 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 38 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 39 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 42 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 43 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |
| 44 | T2 | HVC_DCT_INSULATION_THK_MM_DISP_TXT | Ins: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - MEP Fabrication Ductwork Stiffeners Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | FAB_STIFF_TYPE_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | FAB_DCT_W_MM | W: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | FAB_DCT_H_MM | H: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 38 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 39 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 40 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 43 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 44 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |
| 45 | T2 | HVC_DCT_INSULATION_THK_MM_DISP_TXT | Ins: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - MEP Fabrication Hangers Tag  (41 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | FAB_HANGER_TYPE_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | FAB_HANGER_LOAD_KN | Load: | kN | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 7 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 10 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 13 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 19 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 20 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 24 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 27 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 30 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 33 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 36 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 37 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 38 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 39 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - MEP Fabrication Pipework Tag  (42 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | FAB_PIPE_DN_MM | DN | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 6 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 7 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 9 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 12 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 18 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 19 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 23 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 26 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 27 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 29 | T7 | PLM_PRESSURE_TEST_REF_TXT | HT: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T7 | PLM_WELD_MAP_REF_TXT | WM: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 36 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 37 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 38 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 41 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 42 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Materials Tag  (55 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | MAT_NAME |  |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T1 | MAT_CATEGORY |  |  | NOM | BLACK | 2.5 | ✓ |
| 4 | T2 | MAT_MANUFACTURER | Mfr: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | MAT_STANDARD | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MAT_CODE | Code: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | PROP_DENSITY_KG_M3 | ρ: | kg/m³ | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PROP_FIRE_RATING | Fire: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | PROP_THERMAL_COND_W_MK | λ: | W/mK | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | PROP_THERMAL_RES_M2K_W | R: | m²K/W | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | PROP_SPECIFIC_HEAT_J_KGK | Cp: | J/kgK | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | PROP_ACOUSTIC_ABS | α: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | PROP_SOUND_RED_DB | Rw: | dB | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | PROP_CARBON_KG_M3 | EC: | kgCO₂/m³ | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | PROP_COMP_STRENGTH_MPA | fc: | MPa | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | PROP_TENS_STRENGTH_MPA | ft: | MPa | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | MAT_DURABILITY | Durability: |  | NOM | BLACK | 2.0 |  |
| 18 | T3 | MAT_APPLICATION | Use: |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 20 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 21 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 22 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 23 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 24 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 25 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 26 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 31 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 33 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 36 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 37 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 38 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 39 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 40 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 41 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 42 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 43 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 44 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 45 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 46 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 47 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 48 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 49 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 50 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 51 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 52 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 53 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 54 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 55 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Audio Visual Devices Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_MODEL_NR_TXT | | |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ELC_TAG_7_PARA_COMM_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_RUNTIME_HRS_YR_NR_DISP_TXT | | Run: | hr/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Spaces Tag  (41 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_ROOM_AREA_SQ_M | Area: | m² | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | BLE_ROOM_VENTILATION_RATE_LPS | Vent: | L/s | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | HVC_AIR_CHANGES_PER_HR | ACH: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | HVC_TAG_7_PARA_SPEC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | BLE_ROOM_OCCUPANCY_NR | Occ: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 12 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 15 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 18 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 24 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 25 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 29 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 32 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 35 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 38 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 41 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Zones Tag  (40 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_ROOM_AREA_SQ_M | Zone Area: | m² | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | HVC_CONTROL_TYPE_TXT | Ctrl: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | HVC_BMS_CONTROL_TYPE_TXT | BMS: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | HVC_TAG_7_PARA_SPEC_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 38 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 40 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Tie-In Point Tag (Pipe — Plumbing & Hydraulic)  (53 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TIEIN_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | PLM_PPE_SZ_MM | DN | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TIEIN_STATUS_TXT | [ | ] | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_TIEIN_FLOW_DIR_TXT | Dir: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | PLM_PPE_FLW_LPS | Flow: | L/s | NOM | BLACK | 2.0 |  |
| 6 | T2 | PLM_PSR_KPA | | | kPa | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | ASS_TIEIN_ELEV_TXT | Elev: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | PLM_TAG_7_PARA_TIEIN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_TIEIN_PHASE_TXT | Phase: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_TIEIN_BY_TXT | By: |  | NOM | BLACK | 2.0 |  |
| 12 | T3 | ASS_TIEIN_IFC_REF_TXT | IFC: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | PLM_PPE_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 14 | T3 | PLM_PPE_PSR_RATING_BAR |  | bar | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_TIEIN_CONNECTED_BOOL | Resolved: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 17 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 18 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 20 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 21 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 28 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 29 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 30 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 34 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 35 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 37 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 38 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 40 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 41 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 42 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 43 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 44 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 46 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 47 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 48 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 49 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 50 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 52 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 53 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Tie-In Point Tag (Duct — HVAC)  (54 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TIEIN_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | HVC_DCT_SZ_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TIEIN_STATUS_TXT | [ | ] | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_TIEIN_FLOW_DIR_TXT | Dir: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | HVC_DCT_FLW_CFM | Flow: | CFM | NOM | BLACK | 2.0 |  |
| 6 | T2 | HVC_VEL_MPS | | | m/s | NOM | BLACK | 2.0 |  |
| 7 | T2 | ASS_TIEIN_ELEV_TXT | Elev: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | HVC_DCT_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 9 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | HVC_TAG_7_PARA_TIEIN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_TIEIN_PHASE_TXT | Phase: |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_TIEIN_BY_TXT | By: |  | NOM | BLACK | 2.0 |  |
| 13 | T3 | ASS_TIEIN_IFC_REF_TXT | IFC: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | HVC_DUCT_CLASS_TXT | Class: |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_TIEIN_CONNECTED_BOOL | Resolved: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 17 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 18 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 20 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 21 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 23 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 28 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 29 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 30 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 34 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 35 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 37 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 38 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 40 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 41 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 42 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 43 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 44 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 46 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 47 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 48 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 49 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 50 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 52 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 53 | T2 | MEC_SYS_TXT | MSys: |  | NOM | BLACK | 2.0 | ✓ |
| 54 | T2 | HVC_DCT_INSULATION_THK_MM_DISP_TXT | Ins: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Tie-In Point Tag (Conduit — Electrical LV/ELV)  (49 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TIEIN_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | ELC_CDT_SZ_MM | ∅ | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TIEIN_STATUS_TXT | [ | ] | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_CDT_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_TIEIN_ELEV_TXT | Elev: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ELC_TAG_7_PARA_TIEIN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_TIEIN_PHASE_TXT | Phase: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_TIEIN_BY_TXT | By: |  | NOM | BLACK | 2.0 |  |
| 10 | T3 | ASS_TIEIN_IFC_REF_TXT | IFC: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ELC_CDT_CBL_FILL_PCT | Fill: | % | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_TIEIN_CONNECTED_BOOL | Resolved: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 14 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 16 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 17 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 19 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 26 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 27 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 31 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 33 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 34 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 36 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 37 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 39 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 40 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 42 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 43 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 44 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 45 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 46 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 48 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 49 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Tie-In Point Tag (Cable Tray — Electrical)  (51 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TIEIN_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | ELC_CTR_SZ_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TIEIN_STATUS_TXT | [ | ] | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_CTR_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ELC_CTR_FILL_PCT | Fill: | % | NOM | BLACK | 2.0 |  |
| 6 | T2 | ASS_TIEIN_ELEV_TXT | Elev: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ELC_TAG_7_PARA_TIEIN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_TIEIN_PHASE_TXT | Phase: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_TIEIN_BY_TXT | By: |  | NOM | BLACK | 2.0 |  |
| 11 | T3 | ASS_TIEIN_IFC_REF_TXT | IFC: |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_TIEIN_CONNECTED_BOOL | Resolved: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 14 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 16 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 17 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 19 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 26 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 27 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 31 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 33 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 34 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 36 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 37 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 39 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 40 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 42 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 43 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 44 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 45 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 46 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 48 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 49 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 50 | T2 | BLE_CBL_TRAY_WIDTH_MM_DISP_TXT | W: | mm | NOM | BLACK | 2.0 |  |
| 51 | T2 | BLE_CBL_TRAY_DEPTH_MM_DISP_TXT | D: | mm | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Tie-In Point Tag (Fire Protection — Sprinkler / Suppression)  (51 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TIEIN_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | PLM_PPE_SZ_MM | DN | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TIEIN_STATUS_TXT | [ | ] | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | PLM_PPE_FLW_LPS | Flow: | L/s | NOM | BLACK | 2.0 |  |
| 5 | T2 | PLM_PSR_KPA | | | kPa | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | ASS_TIEIN_ELEV_TXT | Elev: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | FLS_TAG_7_PARA_TIEIN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_TIEIN_PHASE_TXT | Phase: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_TIEIN_BY_TXT | By: |  | NOM | BLACK | 2.0 |  |
| 11 | T3 | ASS_TIEIN_IFC_REF_TXT | IFC: |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | PLM_PPE_PSR_RATING_BAR |  | bar | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ASS_TIEIN_CONNECTED_BOOL | Resolved: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 15 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 16 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 17 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 18 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 19 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 20 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 26 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 27 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 28 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 32 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 33 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 34 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 35 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 36 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 37 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 38 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 39 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 40 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 41 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 42 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 43 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 44 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 45 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 46 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 47 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 48 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 49 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 50 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 51 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - Tie-In Point Tag (Gas — Medical / Industrial / Natural Gas)  (52 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TIEIN_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T1 | PLM_PPE_SZ_MM | DN | mm | NOM | BLACK | 2.5 | ✓ |
| 3 | T2 | ASS_TIEIN_STATUS_TXT | [ | ] | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | PLM_PSR_KPA | Press: | kPa | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ASS_TIEIN_FLOW_DIR_TXT | Dir: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | ASS_TIEIN_ELEV_TXT | Elev: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | PLM_PPE_MAT_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 8 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | PLM_TAG_7_PARA_TIEIN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_TIEIN_PHASE_TXT | Phase: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_TIEIN_BY_TXT | By: |  | NOM | BLACK | 2.0 |  |
| 12 | T3 | ASS_TIEIN_IFC_REF_TXT | IFC: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | PLM_PPE_PSR_RATING_BAR |  | bar | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | ASS_TIEIN_CONNECTED_BOOL | Resolved: |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 16 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 17 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 18 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 19 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 20 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 21 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 27 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 28 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 29 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 33 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 34 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 35 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 36 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 37 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 38 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 39 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 40 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 41 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 42 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 43 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 44 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 45 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 46 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 47 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 48 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 49 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 50 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 51 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 52 | T2 | PLM_SYS_TXT | PSys: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - MEP Sheet Tag  (25 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | SHT_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | SHT_DISC_TXT | Disc: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | SHT_FORM_TXT | Form: |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | SHT_LEVEL_TXT | Level: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | SHT_NUMBER_TXT | No: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | SHT_NAME_TXT | Name: |  | NOM | BLACK | 2.0 |  |
| 7 | T3 | SHT_TAG_7_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 9 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 11 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 12 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 17 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 18 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 19 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 22 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 23 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 25 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - MEP Sleeve Tag  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | SLV_SZ_MM | Ø | mm | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | SLV_FIRE_RATING_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | SLV_SERVICE_TXT | Service: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | SLV_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | SLV_MAT_TXT | Material: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | SLV_SEAL_TYPE_TXT | Seal: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | SLV_WALL_TYPE_TXT | Host: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | SLV_FIRESTOP_PRODUCT_TXT | Firestop: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | SLV_TESTED_TO_TXT | Tested: |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | SLV_INSPECTION_DATE_TXT | Inspected: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 16 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 25 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 26 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 30 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 33 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 36 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 48 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - LPS Air Terminal Tag  (44 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ELC_LPS_AIRTERM_TAG_TXT |  |  | BOLD | BLUE | 2.5 | ✓ |
| 2 | T2 | ELC_LPS_CLASS_TXT | Class: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_LPS_ZONE_TXT | LPZ: |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_LPS_PROTECTION_ANGLE_DEG | α: | ° | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ELC_LPS_AIR_TERMINAL_COUNT_NR | N: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ELC_LPS_ROLLING_SPHERE_RADIUS_M | r: | m | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ELC_LPS_MESH_SIZE_M | Mesh: | m | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ELC_LPS_RISK_ASSESSMENT_TXT | Risk: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ELC_LPS_COMPLIANCE_STATUS_TXT |  |  | BOLD | RED | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - LPS Down Conductor Tag  (42 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ELC_LPS_DOWNCOND_TAG_TXT |  |  | BOLD | BLUE | 2.5 | ✓ |
| 2 | T2 | ELC_LPS_DOWN_CONDUCTOR_COUNT_NR | N: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_LPS_CONDUCTOR_CROSS_SECT_MM2 | A: | mm² | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_LPS_CONDUCTOR_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ELC_LPS_SEPARATION_DISTANCE_MM | s: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ELC_LPS_CLASS_TXT | Class: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ELC_LPS_ZONE_TXT | LPZ: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 38 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 39 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 40 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - LPS Earth Electrode Tag  (42 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ELC_LPS_EARTH_TAG_TXT |  |  | BOLD | BLUE | 2.5 | ✓ |
| 2 | T2 | ELC_LPS_EARTH_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_LPS_EARTH_RESISTANCE_OHM | R: | Ω | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_LPS_EARTH_ELECTRODE_COUNT_NR | N: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ELC_LPS_TEST_DATE_TXT | Tested: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ELC_LPS_INSPECTION_INTERVAL_MONTHS | Inspect: | mo | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ELC_LPS_CERT_REF_TXT | Cert: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 38 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 39 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 40 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - LPS Bond Tag  (39 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ELC_LPS_BOND_TAG_TXT |  |  | BOLD | BLUE | 2.5 | ✓ |
| 2 | T2 | ELC_LPS_BOND_TYPE_TXT | Bond: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_LPS_CONDUCTOR_CROSS_SECT_MM2 | A: | mm² | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_LPS_ZONE_TXT | LPZ: |  | NOM | BLACK | 2.0 | ✓ |
| 5 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 6 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 7 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 8 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 9 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 11 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 12 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 17 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 18 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 19 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 22 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 23 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 25 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 26 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 27 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 28 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 29 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 31 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 32 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 34 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 35 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 36 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 37 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 38 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - LPS SPD Tag  (4 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ELC_LPS_SPD_TAG_TXT |  |  | BOLD | BLUE | 2.5 | ✓ |
| 2 | T2 | ELC_LPS_SURGE_PROTECTION_LVL_TXT | SPD: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_LPS_ZONE_TXT | LPZ: |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_LPS_BOND_TYPE_TXT | Bond: |  | NOM | BLACK | 2.0 | ✓ |

## [MEP] STING - LPS Test Clamp Tag  (40 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ELC_LPS_TESTCLAMP_TAG_TXT |  |  | BOLD | BLUE | 2.5 | ✓ |
| 2 | T2 | ELC_LPS_TEST_DATE_TXT | Tested: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_LPS_EARTH_RESISTANCE_OHM | R: | Ω | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_LPS_INSPECTION_INTERVAL_MONTHS | Next: | mo | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ELC_LPS_CERT_REF_TXT | Cert: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 7 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 8 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 9 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 11 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 12 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 18 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 19 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 23 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 25 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 26 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 27 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 28 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 29 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 32 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 33 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 34 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 35 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 36 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 37 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 38 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 40 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |

## [MEP] STING - Specialty Equipment Tag Asset  (48 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_SERIAL_NR_TXT | Ser: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_ID_TXT | ID: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_INSTALL_DATE_TXT | Installed: |  | NOM | BLACK | 2.0 |  |
| 8 | T3 | ASS_EXPECTED_LIFE_YEARS_YRS | Life: | yr | NOM | BLACK | 2.0 |  |
| 9 | T3 | ASS_MAINTENANCE_FREQUENCY_MONTHS | Maint: | m | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_WARRANTY_TXT | Warr: |  | NOM | BLACK | 2.0 |  |
| 11 | T3 | ASS_CRITICALITY_RATING_NR | Crit: |  | NOM | BLACK | 2.0 |  |
| 12 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 14 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 16 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 17 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 25 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 26 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 30 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 31 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 33 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 34 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 36 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 39 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 40 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 42 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 43 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 44 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 45 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 46 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 48 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [MEP] STING - Specialty Equipment Tag General  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ASS_FUNC_TXT | Func: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | ASS_SYSTEM_TYPE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | MNT_HGT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 8 | T3 | ASS_MODEL_NR_TXT | Model: |  | NOM | BLACK | 2.0 |  |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Column Tag  (60 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_COL_SECTION_TXT | Sec: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_COL_SIZE_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | STR_COL_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | BLE_STRUCT_STEEL_GRADE_TXT | | Gr: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | STR_COL_HEIGHT_MM | Ht: | mm | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | STR_LOAD_AXIAL_KN | Load: | kN | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 11 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | STR_TAG_7_PARA_COL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | STR_COL_GRID_REF_TXT | Grid: |  | NOM | BLACK | 2.0 |  |
| 14 | T3 | STR_COL_SPLICE_DETAIL_TXT | | Splice: |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | STR_FIRE_PROTECTION_TYPE_TXT | FP: |  | NOM | BLACK | 2.0 |  |
| 16 | T3 | STR_COL_BASE_PLATE_TXT | | BP: |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | CST_S_REI_WEIGHT_KG |  | kg | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | STR_COL_MOMENT_CAPACITY_KNM | Mom: | kNm | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 22 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 23 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 24 | T3 | RGL_QA_COMMISSIONING_REQ_TXT | Comm: |  | NOM | BLACK | 2.0 | ✓ |
| 25 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 26 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 27 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 28 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 29 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 30 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 31 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 32 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 33 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 34 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 35 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 36 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 38 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 39 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 40 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 41 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 42 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 43 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 44 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 45 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 46 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 47 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 48 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 49 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 50 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 51 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 52 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 53 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 54 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 55 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 56 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 57 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 58 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 59 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 60 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Framing Tag  (61 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_BEAM_SECTION_TXT | Sec: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_BEAM_DEPTH_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | STR_BEAM_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | BLE_STRUCT_STEEL_GRADE_TXT | | Gr: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | STR_BEAM_SPAN_MM | Span: | mm | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | STR_LOAD_UDL_KN_M | UDL: | kN/m | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 11 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | STR_TAG_7_PARA_BEAM_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | STR_BEAM_CONNECTION_TYPE_TXT | Conn: |  | NOM | BLACK | 2.0 |  |
| 14 | T3 | STR_BEAM_CAMBER_MM | | Camber: | mm | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | STR_BEAM_DEFLECTION_LIM_TXT | Defl: |  | NOM | BLACK | 2.0 |  |
| 16 | T3 | STR_FIRE_PROTECTION_TYPE_TXT | | FP: |  | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | CST_S_REI_WEIGHT_KG |  | kg | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | STR_BEAM_MOMENT_CAPACITY_KNM | Mom: | kNm | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | STR_BEAM_WEB_OPENING_TXT | Web: |  | NOM | BLACK | 2.0 |  |
| 20 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 22 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 23 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 24 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 25 | T3 | RGL_QA_COMMISSIONING_REQ_TXT | | Comm: |  | NOM | BLACK | 2.0 | ✓ |
| 26 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 27 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 28 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 29 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 30 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 31 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 32 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 33 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 34 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 35 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 36 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 37 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 39 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 40 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 41 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 42 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 43 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 44 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 45 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 46 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 47 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 48 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 49 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 50 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 51 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 52 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 53 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 54 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 55 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 56 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 57 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 58 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 59 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 60 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 61 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Foundation Tag  (63 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_FDN_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_FDN_SIZE_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | STR_FDN_DEPTH_MM | D: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | STR_FDN_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 8 | T2 | BLE_STRUCT_STEEL_GRADE_TXT | | Gr: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | STR_FDN_BEARING_KN_M2 | Bearing: | kN/m² | NOM | BLACK | 2.0 | ✓ |
| 10 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | STR_TAG_7_PARA_FDN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | STR_FDN_REBAR_TXT | Rebar: |  | NOM | BLACK | 2.0 |  |
| 13 | T3 | STR_FDN_COVER_MM | | Cover: | mm | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | STR_FDN_WATERPROOF_TXT | WP: |  | NOM | BLACK | 2.0 |  |
| 15 | T3 | STR_FDN_EXPOSURE_CLASS_TXT | | Exp: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | CST_S_REI_WEIGHT_KG |  | kg | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | STR_FDN_SETTLEMENT_MM | Settl: | mm | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | STR_FDN_PILE_TYPE_TXT | Pile: |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 20 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 21 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 22 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 23 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 24 | T3 | RGL_QA_COMMISSIONING_REQ_TXT | | Comm: |  | NOM | BLACK | 2.0 | ✓ |
| 25 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 26 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 27 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 28 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 29 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 30 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 31 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 32 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 33 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 34 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 35 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 36 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 37 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 38 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 39 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 40 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 41 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 42 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 43 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 44 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 45 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 46 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 47 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 48 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 49 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 50 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 51 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 52 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 53 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 54 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 55 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 56 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 57 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 58 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 59 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 60 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |
| 61 | T2 | FOUND_SOIL_CLASS_TXT | Soil: |  | NOM | BLACK | 2.0 | ✓ |
| 62 | T2 | FOUND_BEAR_UTIL_TXT | Util: |  | NOM | BLACK | 2.0 | ✓ |
| 63 | T2 | FOUND_SUMMARY_TXT |  |  | NOM | BLACK | 2.0 | ✓ |

## [STR] STING - Structural Slab Tag  (56 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_STRUCT_SLAB_THICKNESS_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_SLAB_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | BLE_STRUCT_STEEL_GRADE_TXT | Gr: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | STR_SLAB_LOAD_KN_M2 | Load: | kN/m² | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | STR_TAG_7_PARA_SLAB_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | STR_SLAB_REBAR_TXT | Rebar: |  | NOM | BLACK | 2.0 |  |
| 12 | T3 | STR_SLAB_MESH_TXT | | Mesh: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | STR_SLAB_COVER_MM | Cover: | mm | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | STR_SLAB_DEFLECTION_LIMIT_TXT | Defl: |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | BLE_WALL_SOUND_TRANSMISSION_CLASS_RATING_NR | STC: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 19 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 20 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 21 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 22 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 23 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 24 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 25 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 26 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 27 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 30 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 31 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 32 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 34 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 36 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 37 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 38 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 39 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 40 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 41 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 42 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 43 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 44 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 45 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 46 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 47 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 48 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 49 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 50 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 51 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 52 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 53 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 54 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 55 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 56 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Wall Tag  (53 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | BLE_WALL_THICKNESS_MM |  | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_WALL_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | BLE_STRUCT_STEEL_GRADE_TXT | Gr: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | STR_LOAD_AXIAL_KN | Load: | kN/m | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | STR_TAG_7_PARA_WALL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | STR_WALL_REBAR_TXT | Rebar: |  | NOM | BLACK | 2.0 |  |
| 12 | T3 | STR_WALL_COVER_MM | | Cover: | mm | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 17 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 21 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 29 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 31 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 35 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 38 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 41 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 42 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 43 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 44 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 46 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 47 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 48 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 49 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 50 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 52 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 53 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Brace / Truss Tag  (51 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_BRACE_SECTION_TXT | Sec: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_BRACE_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | BLE_STRUCT_STEEL_GRADE_TXT | | Gr: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | STR_BRACE_LENGTH_MM | L: | mm | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | STR_TAG_7_PARA_BRACE_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | STR_BRACE_CONNECTION_TXT | Conn: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | CST_S_REI_WEIGHT_KG |  | kg | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 15 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 17 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 18 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 19 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 20 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 21 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 27 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 28 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 29 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 31 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 33 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 34 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 35 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 36 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 37 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 38 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 39 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 40 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 41 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 42 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 43 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 44 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 45 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 46 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 47 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 48 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 49 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 50 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 51 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Rebar Tag  (53 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_REBAR_SIZE_MM | Size: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | CST_S_REI_GRADE_TXT | Gr: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | CST_S_REI_SPACING_MM | Spc: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | CST_S_REI_COVER_MM | Cover: | mm | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | STR_TAG_7_PARA_REBAR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | STR_REBAR_SHAPE_TXT | Shape: |  | NOM | BLACK | 2.0 |  |
| 11 | T3 | CST_S_REI_OVERLAP_LENGTH_MM | | Lap: | mm | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | STR_REBAR_HOOK_TYPE_TXT | Hook: |  | NOM | BLACK | 2.0 |  |
| 13 | T3 | CST_S_REI_WEIGHT_KG |  | kg | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 17 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 21 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 29 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 31 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 35 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 38 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 41 | T7 | STR_BAR_MARK_TXT | Mark: |  | BOLD | ORANGE | 2.0 |  |
| 42 | T7 | STR_BEND_SCHEDULE_REF_TXT | BBS: |  | BOLD | ORANGE | 2.0 |  |
| 43 | T7 | STR_CUTTING_LIST_REF_TXT | CL: |  | BOLD | ORANGE | 2.0 | ✓ |
| 44 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 46 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 47 | T9 | ASBUILT_COVER_DEVIATION_MM_DISP_TXT | ΔCvr: | mm | ITALIC | GREY | 2.0 |  |
| 48 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 49 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 50 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 52 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 53 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Connection Tag  (53 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_CONN_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_CONN_CAPACITY_KN | Cap: | kN | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | STR_CONN_BOLT_SIZE_TXT | Bolt: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | STR_TAG_7_PARA_CONN_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | STR_CONN_WELD_TYPE_TXT | Weld: |  | NOM | BLACK | 2.0 |  |
| 10 | T3 | STR_CONN_PLATE_THICKNESS_MM | | Plt: | mm | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | STR_CONN_ROTATION_CAPACITY_TXT | Rot: |  | NOM | BLACK | 2.0 |  |
| 12 | T3 | STR_FIRE_PROTECTION_TYPE_TXT | FP: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | CST_S_REI_WEIGHT_KG |  | kg | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 17 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 18 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 19 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 21 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 22 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 24 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 25 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 29 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 30 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 31 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 32 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 35 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 36 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 38 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 39 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 41 | T7 | STR_WELD_PROCEDURE_REF_TXT | WPS: |  | BOLD | ORANGE | 2.0 |  |
| 42 | T7 | STR_BOLT_TORQUE_NM_NR_DISP_TXT | Tq: | Nm | BOLD | ORANGE | 2.0 |  |
| 43 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 44 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 45 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 46 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 47 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 48 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 49 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 50 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 51 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 52 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 53 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Columns Tag  (49 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_COL_SECTION_TXT | Sec: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_COL_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | STR_TAG_7_PARA_COL_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | STR_FIRE_PROTECTION_TYPE_TXT | FP: |  | NOM | BLACK | 2.0 |  |
| 10 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 13 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 16 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 17 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 19 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 26 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 27 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 31 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 33 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 34 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 36 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 37 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 39 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 40 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 42 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 43 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 44 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 45 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 46 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 48 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 49 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Trusses Tag  (54 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_BEAM_SECTION_TXT | Sec: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_BEAM_SPAN_MM | Span: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | STR_BEAM_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | BLE_STRUCT_STEEL_GRADE_TXT | | Gr: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | PER_FIRE_RATING_HR |  | hr | NOM | BLACK | 2.0 | ✓ |
| 9 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | STR_TAG_7_PARA_BEAM_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | STR_BEAM_DEFLECTION_LIM_TXT | Defl: |  | NOM | BLACK | 2.0 |  |
| 12 | T3 | STR_FIRE_PROTECTION_TYPE_TXT | | FP: |  | NOM | BLACK | 2.0 | ✓ |
| 13 | T3 | CST_S_REI_WEIGHT_KG |  | kg | NOM | BLACK | 2.0 | ✓ |
| 14 | T3 | PER_SUST_EMBODIED_CARBON_KG |  | kgCO₂/m² | NOM | BLACK | 2.0 | ✓ |
| 15 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 16 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 17 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 18 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 19 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 20 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 21 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 22 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 23 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 24 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 26 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 27 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 28 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 29 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 30 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 31 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 32 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 33 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 34 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 35 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 36 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 37 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 38 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 39 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 40 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 41 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 42 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 43 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 44 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 45 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 46 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 47 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 48 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 49 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 50 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 51 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 52 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 53 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 54 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Stiffeners Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_BEAM_SECTION_TXT | Sec: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_BEAM_MATERIAL_TXT | Mat: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | BLE_STRUCT_STEEL_GRADE_TXT | | Gr: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | STR_TAG_7_PARA_BEAM_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | CST_S_REI_WEIGHT_KG |  | kg | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Beam Systems Tag  (49 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_BEAM_SECTION_TXT | Sec: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_BEAM_SPAN_MM | Span: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | STR_LOAD_UDL_KN_M | UDL: | kN/m | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | STR_TAG_7_PARA_BEAM_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | STR_BEAM_DEFLECTION_LIM_TXT | Defl: |  | NOM | BLACK | 2.0 |  |
| 10 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 13 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 16 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 17 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 19 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 26 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 27 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 31 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 33 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 34 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 36 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 37 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 39 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 40 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 42 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 43 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 44 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 45 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 46 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 48 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 49 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Rebar Couplers Tag  (45 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_REBAR_SIZE_MM | Size: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | CST_S_REI_GRADE_TXT | Gr: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | ASS_MANUFACTURER_TXT | Mfr: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | STR_TAG_7_PARA_REBAR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 11 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 13 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 14 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 21 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 22 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 23 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 27 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 28 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 30 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 31 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 33 | T7 | STR_BAR_MARK_TXT | Mark: |  | BOLD | ORANGE | 2.0 |  |
| 34 | T7 | STR_BEND_SCHEDULE_REF_TXT | BBS: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | STR_CUTTING_LIST_REF_TXT | CL: |  | BOLD | ORANGE | 2.0 | ✓ |
| 36 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 37 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 39 | T9 | ASBUILT_COVER_DEVIATION_MM_DISP_TXT | ΔCvr: | mm | ITALIC | GREY | 2.0 |  |
| 40 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 41 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 42 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 43 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 45 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Fabric Reinforcement Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_REBAR_SIZE_MM | Wire: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | CST_S_REI_SPACING_MM | | Spc: | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | CST_S_REI_COVER_MM | Cover: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | STR_TAG_7_PARA_REBAR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | CST_S_REI_WEIGHT_KG |  | kg/m² | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | STR_BAR_MARK_TXT | Mark: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | STR_BEND_SCHEDULE_REF_TXT | BBS: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | STR_CUTTING_LIST_REF_TXT | CL: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_COVER_DEVIATION_MM_DISP_TXT | ΔCvr: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Area Reinforcement Tag  (46 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_REBAR_SIZE_MM | Bar: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | CST_S_REI_SPACING_MM | | @ | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | CST_S_REI_GRADE_TXT | Gr: |  | NOM | BLACK | 2.0 |  |
| 7 | T2 | CST_S_REI_COVER_MM | Cover: | mm | NOM | BLACK | 2.0 | ✓ |
| 8 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | STR_TAG_7_PARA_REBAR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T7 | STR_BAR_MARK_TXT | Mark: |  | BOLD | ORANGE | 2.0 |  |
| 35 | T7 | STR_BEND_SCHEDULE_REF_TXT | BBS: |  | BOLD | ORANGE | 2.0 |  |
| 36 | T7 | STR_CUTTING_LIST_REF_TXT | CL: |  | BOLD | ORANGE | 2.0 | ✓ |
| 37 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 38 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 39 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 40 | T9 | ASBUILT_COVER_DEVIATION_MM_DISP_TXT | ΔCvr: | mm | ITALIC | GREY | 2.0 |  |
| 41 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 42 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 43 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 44 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 45 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 46 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Path Reinforcement Tag  (49 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_REBAR_SIZE_MM | Bar: | mm | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | CST_S_REI_SPACING_MM | | @ | mm | NOM | BLACK | 2.0 | ✓ |
| 6 | T2 | CST_S_REI_COVER_MM | Cover: | mm | NOM | BLACK | 2.0 | ✓ |
| 7 | T2 | RGL_STD_TXT | Std: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | STR_TAG_7_PARA_REBAR_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | CST_S_REI_OVERLAP_LENGTH_MM | | Lap: | mm | NOM | BLACK | 2.0 | ✓ |
| 10 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 12 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 13 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 14 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 15 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 16 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 17 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 18 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 19 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 20 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 22 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 23 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 24 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 25 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 26 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 27 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 28 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 29 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 30 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 31 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 32 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 33 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 34 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 35 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 36 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 37 | T7 | STR_BAR_MARK_TXT | Mark: |  | BOLD | ORANGE | 2.0 |  |
| 38 | T7 | STR_BEND_SCHEDULE_REF_TXT | BBS: |  | BOLD | ORANGE | 2.0 |  |
| 39 | T7 | STR_CUTTING_LIST_REF_TXT | CL: |  | BOLD | ORANGE | 2.0 | ✓ |
| 40 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 41 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 42 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 43 | T9 | ASBUILT_COVER_DEVIATION_MM_DISP_TXT | ΔCvr: | mm | ITALIC | GREY | 2.0 |  |
| 44 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 45 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 46 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 47 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 48 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 49 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Internal Point Loads Tag  (40 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_POINT_LOAD_KN |  | kN | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_LOAD_CASE_TXT | LC: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_LOAD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 10 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 38 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 40 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Internal Line Loads Tag  (40 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_LINE_LOAD_KN_M |  | kN/m | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_LOAD_CASE_TXT | LC: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_LOAD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 10 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 38 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 40 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Internal Area Loads Tag  (40 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_AREA_LOAD_KN_M2 |  | kN/m² | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | STR_LOAD_CASE_TXT | LC: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_LOAD_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 10 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 38 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 40 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Analytical Members Tag  (40 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ASS_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | ASS_TAG_2_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ASS_DESCRIPTION_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | STR_SECTION_PROFILE_TXT | Section: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | STR_MATERIAL_GRADE_TXT | Grade: |  | NOM | BLACK | 2.0 |  |
| 6 | T3 | STR_TAG_7_PARA_ANLYT_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ASS_STATUS_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T3 | ASS_CST_TOTAL_UGX_NR |  | UGX | NOM | BLACK | 2.0 | ✓ |
| 9 | T3 | CST_DELIVERY_LEAD_TIME_DAYS | Lead: | days | NOM | BLACK | 2.0 |  |
| 10 | T3 | CST_LOCAL_MAT_BOOL | | Local: |  | NOM | BLACK | 2.0 | ✓ |
| 11 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 12 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 13 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 14 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 15 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 16 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 17 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 19 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 20 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 21 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 22 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 23 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 24 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 25 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 26 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 27 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 28 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 29 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 30 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 31 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 32 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 33 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 38 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 39 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
| 40 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - Structural Sheet Tag  (25 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | SHT_TAG_1_TXT |  |  | NOM | BLACK | 2.5 | ✓ |
| 2 | T2 | SHT_DISC_TXT | Disc: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | SHT_FORM_TXT | Form: |  | NOM | BLACK | 2.0 |  |
| 4 | T2 | SHT_LEVEL_TXT | Level: |  | NOM | BLACK | 2.0 |  |
| 5 | T2 | SHT_NUMBER_TXT | No: |  | NOM | BLACK | 2.0 |  |
| 6 | T2 | SHT_NAME_TXT | Name: |  | NOM | BLACK | 2.0 |  |
| 7 | T3 | SHT_TAG_7_TXT |  |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 9 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 10 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 11 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 12 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 13 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 14 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 16 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 17 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 18 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 19 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 20 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 21 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 22 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 23 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 24 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 25 | T2 | ASS_SYSTEMS_TXT | Sys: |  | NOM | BLACK | 2.0 |  |

## [STR] STING - LPS Foundation Earth (Structural Reuse) Tag  (42 rows)

| # | Tier | Parameter | Prefix | Suffix | Style | Color | Size | Brk |
|---|---|---|---|---|---|---|---|---|
| 1 | T1 | ELC_LPS_EARTH_TAG_TXT |  |  | BOLD | BLUE | 2.5 | ✓ |
| 2 | T2 | ELC_LPS_EARTH_TYPE_TXT | Type: |  | NOM | BLACK | 2.0 | ✓ |
| 3 | T2 | ELC_LPS_CLASS_TXT | Class: |  | NOM | BLACK | 2.0 | ✓ |
| 4 | T2 | ELC_LPS_EARTH_RESISTANCE_OHM | R: | Ω | NOM | BLACK | 2.0 | ✓ |
| 5 | T2 | ELC_LPS_TEST_DATE_TXT | Tested: |  | NOM | BLACK | 2.0 | ✓ |
| 6 | T3 | ELC_LPS_BOND_TYPE_TXT | Bond: |  | NOM | BLACK | 2.0 | ✓ |
| 7 | T3 | ELC_LPS_CERT_REF_TXT | Cert: |  | NOM | BLACK | 2.0 | ✓ |
| 8 | T4 | COMM_STATE_TXT | Comm: |  | BOLD | BLUE | 2.0 |  |
| 9 | T4 | COMM_DATE_TXT | on |  | BOLD | BLUE | 2.0 |  |
| 10 | T4 | COMM_OPERATIVE_TXT | by |  | BOLD | BLUE | 2.0 | ✓ |
| 11 | T5 | CST_UG_PRICE_UGX_DISP_TXT |  | UGX | NOM | PURPLE | 2.0 |  |
| 12 | T5 | CST_INTL_PRICE_USD_DISP_TXT | /  | USD | NOM | PURPLE | 2.0 |  |
| 13 | T5 | CST_QUOTE_REF_TXT | Quote: |  | NOM | PURPLE | 2.0 | ✓ |
| 14 | T5 | ASS_CST_UNIT_RATE_NR_DISP_TXT | Rate: |  | NOM | PURPLE | 2.0 |  |
| 15 | T5 | ASS_CST_CURRENCY_TXT |   |  | NOM | PURPLE | 2.0 |  |
| 16 | T5 | ASS_CST_FX_TO_BASE_NR_DISP_TXT | FX: |  | NOM | PURPLE | 2.0 |  |
| 17 | T5 | ASS_CST_FX_DATE_DT |  FX date: |  | NOM | PURPLE | 2.0 |  |
| 18 | T5 | ASS_CST_AS_OF_DT |  As of: |  | NOM | PURPLE | 2.0 | ✓ |
| 19 | T5 | ASS_CST_STALE_BOOL | Stale: |  | BOLD | ORANGE | 2.0 |  |
| 20 | T5 | ASS_CST_STALE_REASON_TXT |  -  |  | NOM | ORANGE | 2.0 | ✓ |
| 21 | T5 | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | Cmpl: | % | BOLD | GREEN | 2.0 |  |
| 22 | T5 | ASS_PMT_CERT_NO_NR_DISP_TXT |  Cert#: |  | NOM | GREEN | 2.0 |  |
| 23 | T5 | ASS_PMT_CERT_DATE_DT |  Certified: |  | NOM | GREEN | 2.0 |  |
| 24 | T5 | ASS_PMT_LAST_VALUED_DT |  Valued: |  | NOM | GREEN | 2.0 | ✓ |
| 25 | T5 | ASS_VAR_NO_TXT | Var: |  | BOLD | RED | 2.0 |  |
| 26 | T5 | ASS_VAR_INSTRUCTION_DT |  Instr: |  | NOM | RED | 2.0 |  |
| 27 | T5 | ASS_VAR_VALUATION_NR_DISP_TXT |  Val: |  | NOM | RED | 2.0 | ✓ |
| 28 | T6 | CBN_A1_A3_KG_CO2E_DISP_TXT | A1-A3: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 29 | T6 | CBN_A4_KG_CO2E_DISP_TXT | | A4: | kgCO₂e | ITALIC | GREEN | 2.0 |  |
| 30 | T6 | CBN_B6_KG_CO2E_YR_DISP_TXT | | B6: | kgCO₂e/yr | ITALIC | GREEN | 2.0 | ✓ |
| 31 | T7 | ASS_SPOOL_NR_TXT | Spool: |  | BOLD | ORANGE | 2.0 |  |
| 32 | T7 | ASS_FAB_STATUS_TXT | Fab: |  | BOLD | ORANGE | 2.0 |  |
| 33 | T7 | ASS_QC_INSPECTOR_TXT | QC: |  | BOLD | ORANGE | 2.0 | ✓ |
| 34 | T8 | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | Sev: | /5 | BOLD | RED | 2.0 |  |
| 35 | T8 | CLASH_TRIAGE_CATEGORY_TXT |  |  | BOLD | RED | 2.0 |  |
| 36 | T8 | CLASH_RESOLUTION_STATUS_TXT | Res: |  | BOLD | RED | 2.0 | ✓ |
| 37 | T9 | ASBUILT_DEVIATION_MM_DISP_TXT | Δ: | mm | ITALIC | GREY | 2.0 |  |
| 38 | T9 | ASBUILT_CAPTURE_DATE_TXT | on |  | ITALIC | GREY | 2.0 |  |
| 39 | T9 | HEALTH_SCORE_LAST_NR_DISP_TXT | Health: | /100 | ITALIC | GREY | 2.0 | ✓ |
| 40 | T10 | IFC_PSET_OVERRIDE_TXT | IFC: |  | NOM | GREY | 2.0 |  |
| 41 | T10 | ACC_ISSUE_ID_TXT | ACC: |  | NOM | GREY | 2.0 |  |
| 42 | T10 | ACC_SYNC_STATUS_TXT | Sync: |  | NOM | GREY | 2.0 | ✓ |
