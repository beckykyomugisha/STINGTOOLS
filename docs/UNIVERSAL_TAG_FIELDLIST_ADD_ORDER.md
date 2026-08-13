# Universal Tag — Edit Label field-list add order (74 params, 13 groups)

Fast group-by-group checklist for adding every master parameter to the tag's **Edit Label field
list**, without opening the Excel. Because the loaded shared-parameter file is
`UNIVERSAL_TAG_MASTER_PARAMS.txt` (trimmed to ONLY the 74 master params), **every parameter in
every group is needed — add them all.**

## Workflow (repeat per group)
1. Edit Label → **Add parameter** (▸) → **Select…** → in **Shared Parameters**, pick the group in
   the **Parameter group** dropdown.
2. Click a parameter → **OK** → **OK**. It lands in the field list. Tick it below.
3. Re-open Add parameter and repeat for the next param in the same group (the dialog **resets to
   the first group each time** — re-pick the group).
4. When a group is fully ticked, move to the next group. Done when all 13 groups = 74 ticks.

Notes: no multi-select (one param per OK); this only puts them in the **field list** — you build
the label rows afterwards (guide Part 2); do **not** close Edit Label until the rows are built.

---

## 1. ASS_MNG  (25)
- [ ] ASS_CRITICALITY_RATING_NR
- [ ] ASS_CST_INSTALL_UGX_NR
- [ ] ASS_DESCRIPTION_TXT
- [ ] ASS_DESIGN_OPTION_TXT
- [ ] ASS_EXPECTED_LIFE_YEARS_YRS
- [ ] ASS_FAB_STATUS_TXT
- [ ] ASS_INSTALL_DATE_TXT
- [ ] ASS_ITEM_CODE_TXT
- [ ] ASS_KEYNOTE_TXT
- [ ] ASS_LVL_COD_TXT
- [ ] ASS_MANUFACTURER_TXT
- [ ] ASS_MODEL_NR_TXT
- [ ] ASS_MODEL_REF_TXT
- [ ] ASS_QC_INSPECTOR_TXT
- [ ] ASS_SPOOL_NR_TXT
- [ ] ASS_STATUS_TXT
- [ ] ASS_SYSTEMS_TXT
- [ ] ASS_TAG_1_TXT
- [ ] ASS_TAG_2_TXT
- [ ] ASS_TRACE_SEQ_NR_DISP_TXT
- [ ] ASS_UNICLASS_2015_TXT
- [ ] ASS_WARRANTY_LABOR_TXT
- [ ] ASS_WARRANTY_PARTS_TXT
- [ ] ASS_WARRANTY_TXT
- [ ] ASS_ZONE_TXT

## 2. CST_PROC  (18)
- [ ] ASS_CST_AS_OF_DT
- [ ] ASS_CST_CURRENCY_TXT
- [ ] ASS_CST_FX_DATE_DT
- [ ] ASS_CST_FX_TO_BASE_NR_DISP_TXT
- [ ] ASS_CST_STALE_BOOL
- [ ] ASS_CST_STALE_REASON_TXT
- [ ] ASS_CST_UNIT_RATE_NR_DISP_TXT
- [ ] ASS_PMT_CERT_DATE_DT
- [ ] ASS_PMT_CERT_NO_NR_DISP_TXT
- [ ] ASS_PMT_LAST_VALUED_DT
- [ ] ASS_PMT_PCT_COMPLETE_NR_DISP_TXT
- [ ] ASS_VAR_INSTRUCTION_DT
- [ ] ASS_VAR_NO_TXT
- [ ] ASS_VAR_VALUATION_NR_DISP_TXT
- [ ] CST_INSTALL_HRS_DISP_TXT
- [ ] CST_INTL_PRICE_USD_DISP_TXT
- [ ] CST_QUOTE_REF_TXT
- [ ] CST_UG_PRICE_UGX_DISP_TXT

## 3. ELC_PWR  (2)
- [ ] ASS_CAPACITY_TXT
- [ ] ASS_POWER_RATING_TXT

## 4. HVC_SYSTEMS  (2)
- [ ] ASS_FLOW_RATE_TXT
- [ ] MEC_SYS_TXT

## 5. PER_SUST  (3)
- [ ] CBN_A1_A3_KG_CO2E_DISP_TXT
- [ ] CBN_A4_KG_CO2E_DISP_TXT
- [ ] CBN_B6_KG_CO2E_YR_DISP_TXT

## 6. RGL_CMPL  (1)
- [ ] RGL_STD_TXT

## 7. STINGTags_ISO19650  (11)   ← the tier gates + status-gate ints (needed by the formulas)
- [ ] STING_GATE_DATA_STATUS_INT
- [ ] STING_GATE_QA_STATUS_INT
- [ ] TAG_PARA_STATE_2_BOOL
- [ ] TAG_PARA_STATE_4_BOOL
- [ ] TAG_PARA_STATE_5_BOOL
- [ ] TAG_PARA_STATE_6_BOOL
- [ ] TAG_PARA_STATE_7_BOOL
- [ ] TAG_PARA_STATE_8_BOOL
- [ ] TAG_PARA_STATE_9_BOOL
- [ ] TAG_PARA_STATE_10_BOOL
- [ ] TAG_WARN_VISIBLE_BOOL

## 8. CLASH_COORDINATION  (3)
- [ ] CLASH_RESOLUTION_STATUS_TXT
- [ ] CLASH_TRIAGE_CATEGORY_TXT
- [ ] CLASH_TRIAGE_SEVERITY_NR_DISP_TXT

## 9. ACC_SYNC  (2)
- [ ] ACC_ISSUE_ID_TXT
- [ ] ACC_SYNC_STATUS_TXT

## 10. IFC_EXCH  (1)
- [ ] IFC_PSET_OVERRIDE_TXT

## 11. HEALTH_METRICS  (1)
- [ ] HEALTH_SCORE_LAST_NR_DISP_TXT

## 12. ASBUILT  (2)
- [ ] ASBUILT_CAPTURE_DATE_TXT
- [ ] ASBUILT_DEVIATION_MM_DISP_TXT

## 13. COMMISSIONING  (3)
- [ ] COMM_DATE_TXT
- [ ] COMM_OPERATIVE_TXT
- [ ] COMM_STATE_TXT

---

**TOTAL: 74 params across 13 groups.** When all boxes are ticked, the field list is complete —
proceed to build the 65 label rows (guide Part 2).
