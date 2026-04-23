# PARAM_REUSE_REPORT.md

Output of `check_param_reuse.py` — every candidate parameter from the per-family T4 to T10 REPLACE sets checked against `MR_PARAMETERS.txt` with both exact name match (Stage 1) and semantic description match (Stage 2, all tokens of semantic phrase present, case-insensitive).

MR_PARAMETERS.txt PARAM rows scanned: **2487**

| Candidate | Verdict | Reused Name | Matched Description | Semantic Phrase |
|---|---|---|---|---|
| `STR_BAR_MARK_TXT` | **NEW** | — | — | bar mark identifier |
| `STR_BEND_SCHEDULE_REF_TXT` | **NEW** | — | — | bar bending schedule reference |
| `STR_CUTTING_LIST_REF_TXT` | **NEW** | — | — | rebar cutting list reference |
| `STR_WELD_PROCEDURE_REF_TXT` | **NEW** | — | — | welding procedure specification |
| `STR_BOLT_TORQUE_NM_NR` | **NEW** | — | — | bolt tightening torque |
| `ASS_QC_INSPECTOR_TXT` | REUSE (exact) | `ASS_QC_INSPECTOR_TXT` | Name or ID of QC inspector who released the assembly | qc inspector name |
| `HVC_REFRIGERANT_CHARGE_KG_NR` | **NEW** | — | — | refrigerant charge mass kg |
| `HVC_FACTORY_FLASH_TEST_DATE_TXT` | **NEW** | — | — | factory flash test date |
| `HVC_FACTORY_QR_TXT` | **NEW** | — | — | factory qr code label |
| `ELC_PANEL_SCHEDULE_REF_TXT` | **NEW** | — | — | panel schedule reference |
| `ELC_FAT_CERT_REF_TXT` | **NEW** | — | — | factory acceptance test certificate |
| `ELC_FACTORY_QR_TXT` | **NEW** | — | — | electrical factory qr code |
| `PLM_PRESSURE_TEST_REF_TXT` | **NEW** | — | — | hydrostatic pressure test certificate reference |
| `PLM_WELD_MAP_REF_TXT` | **NEW** | — | — | pipe weld map drawing reference |
| `ARC_FACTORY_ORDER_REF_TXT` | **NEW** | — | — | joinery factory order reference |
| `ARC_GLAZING_SPEC_TXT` | **NEW** | — | — | glazing specification reference |
| `ASS_WARRANTY_DURATION_PARTS_YRS` | REUSE (exact) | `ASS_WARRANTY_DURATION_PARTS_YRS` | Warranty duration parts in years (COBie) | warranty duration years |
| `CBN_B6_KG_CO2E_YR` | REUSE (exact) | `CBN_B6_KG_CO2E_YR` | Operational carbon B6 energy annual kgCO2e/yr | carbon operational b6 |
| `CBN_A1_A3_KG_CO2E` | REUSE (exact) | `CBN_A1_A3_KG_CO2E` | Embodied carbon A1-A3 product stage (manufacturing) kgCO2e | carbon product a1 a3 |
| `CBN_RUNTIME_HRS_YR_NR` | **NEW** | — | — | equivalent annual operating hours |
| `CBN_REFRIGERANT_GWP_KG_CO2E` | **NEW** | — | — | refrigerant global warming potential kgco2e |
| `ASBUILT_COVER_DEVIATION_MM` | **NEW** | — | — | as-built concrete cover deviation |
| `ASBUILT_CAPTURE_DATE_TXT` | REUSE (exact) | `ASBUILT_CAPTURE_DATE_TXT` | ISO 8601 date when as-built data captured from Planscape mobile | asbuilt capture date |
| `HEALTH_SCORE_LAST_NR` | REUSE (exact) | `HEALTH_SCORE_LAST_NR` | Last model health score 0 to 100 | health score last |

## Summary

- Candidates checked: **24**
- REUSE verdicts: **6**
- NEW verdicts: **18**

## NEW parameters to add to MR_PARAMETERS.txt

| Name | Datatype | Group | Standard | Description note |
|---|---|---|---|---|
| `STR_BAR_MARK_TXT` | TEXT | BLE_STRUCTURE | BS 8666:2020 | Bar mark from bending schedule |
| `STR_BEND_SCHEDULE_REF_TXT` | TEXT | BLE_STRUCTURE | BS 8666:2020 | BBS document reference |
| `STR_CUTTING_LIST_REF_TXT` | TEXT | BLE_STRUCTURE | BS 8666:2020 | Cutting list drawing reference |
| `STR_WELD_PROCEDURE_REF_TXT` | TEXT | BLE_STRUCTURE | BS EN ISO 15609-1:2019 | Welding procedure specification reference |
| `STR_BOLT_TORQUE_NM_NR` | NUMBER | BLE_STRUCTURE | BS EN 1090-2:2018 | Bolt installation tightening torque (Nm) |
| `HVC_REFRIGERANT_CHARGE_KG_NR` | NUMBER | HVC_SYSTEMS | BS EN 378-2:2016 | Equipment refrigerant charge in kg |
| `HVC_FACTORY_FLASH_TEST_DATE_TXT` | TEXT | HVC_SYSTEMS | BS EN 378-2:2016 | Factory flash test date |
| `HVC_FACTORY_QR_TXT` | TEXT | HVC_SYSTEMS | ISO 19650-3:2020 | Factory QR code label reference |
| `ELC_PANEL_SCHEDULE_REF_TXT` | TEXT | ELC_PWR | BS 7671:2018+A2:2022 | Panel schedule document reference |
| `ELC_FAT_CERT_REF_TXT` | TEXT | ELC_PWR | IEC 61439-1:2020 | Factory Acceptance Test certificate reference |
| `ELC_FACTORY_QR_TXT` | TEXT | ELC_PWR | ISO 19650-3:2020 | Electrical factory QR code label |
| `PLM_PRESSURE_TEST_REF_TXT` | TEXT | PLM_DRN | BS EN 806-4:2010 | Hydrostatic pressure test certificate reference |
| `PLM_WELD_MAP_REF_TXT` | TEXT | PLM_DRN | BS EN ISO 15609-1:2019 | Pipe weld map drawing reference |
| `ARC_FACTORY_ORDER_REF_TXT` | TEXT | BLE_ELES | BS 8000-5:1990 | Factory order reference for joinery |
| `ARC_GLAZING_SPEC_TXT` | TEXT | BLE_ELES | BS 6262-1:2022 | Glazing specification reference |
| `CBN_RUNTIME_HRS_YR_NR` | NUMBER | PER_SUST | BS EN 16798-1:2019 | Equivalent annual operating hours |
| `CBN_REFRIGERANT_GWP_KG_CO2E` | NUMBER | PER_SUST | BS EN 378-2:2016 | Refrigerant GWP contribution (kgCO2e) |
| `ASBUILT_COVER_DEVIATION_MM` | NUMBER | ASBUILT | BS EN 13670:2009 | As-built concrete cover deviation (mm) |

