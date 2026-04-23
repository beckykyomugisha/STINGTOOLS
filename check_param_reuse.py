#!/usr/bin/env python3
"""check_param_reuse.py — enforce the parameter reuse rule from
TIER_VARIATION_MATRIX.md / task brief Section 2 / Section 3.

For each candidate parameter proposed in the REPLACE sets, run two checks:
  Stage 1: exact NAME match against MR_PARAMETERS.txt.
  Stage 2: semantic DESCRIPTION match — every token of the semantic phrase
           must appear (any order, case-insensitive) in an existing param's
           description.

Emit PARAM_REUSE_REPORT.md at repo root with one row per candidate:
    Candidate | Verdict (NEW/REUSE) | Reused Name | Matched Description

Exit code 0 always (informational). Written output is authoritative.
"""

import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
MR_PARAMS = os.path.join(REPO_ROOT, 'StingTools', 'Data', 'MR_PARAMETERS.txt')
OUT_REPORT = os.path.join(REPO_ROOT, 'PARAM_REUSE_REPORT.md')

# Candidate list — one per proposed parameter.
# Includes Section 2 candidates marked inline as REUSE so the report covers
# every row that touches the T4..T10 replacement work.
# Fields: name, datatype, group_name, semantic_phrase, standard, note
CANDIDATES = [
    # --- STR_REBAR_T7 ---
    ('STR_BAR_MARK_TXT', 'TEXT', 'BLE_STRUCTURE',
     'bar mark identifier', 'BS 8666:2020',
     'Bar mark from bending schedule'),
    ('STR_BEND_SCHEDULE_REF_TXT', 'TEXT', 'BLE_STRUCTURE',
     'bar bending schedule reference', 'BS 8666:2020',
     'BBS document reference'),
    ('STR_CUTTING_LIST_REF_TXT', 'TEXT', 'BLE_STRUCTURE',
     'rebar cutting list reference', 'BS 8666:2020',
     'Cutting list drawing reference'),
    # --- STR_CONN_T7 ---
    ('STR_WELD_PROCEDURE_REF_TXT', 'TEXT', 'BLE_STRUCTURE',
     'welding procedure specification', 'BS EN ISO 15609-1:2019',
     'Welding procedure specification reference'),
    ('STR_BOLT_TORQUE_NM_NR', 'NUMBER', 'BLE_STRUCTURE',
     'bolt tightening torque', 'BS EN 1090-2:2018',
     'Bolt installation tightening torque (Nm)'),
    ('ASS_QC_INSPECTOR_TXT', None, None,
     'qc inspector name', None,
     'Default T7 row — reused across STR_CONN_T7 and PLM_PRESSURE_T7'),
    # --- HVC_REFRIG_T7 ---
    ('HVC_REFRIGERANT_CHARGE_KG_NR', 'NUMBER', 'HVC_SYSTEMS',
     'refrigerant charge mass kg', 'BS EN 378-2:2016',
     'Equipment refrigerant charge in kg'),
    ('HVC_FACTORY_FLASH_TEST_DATE_TXT', 'TEXT', 'HVC_SYSTEMS',
     'factory flash test date', 'BS EN 378-2:2016',
     'Factory flash test date'),
    ('HVC_FACTORY_QR_TXT', 'TEXT', 'HVC_SYSTEMS',
     'factory qr code label', 'ISO 19650-3:2020',
     'Factory QR code label reference'),
    # --- ELC_PANEL_T7 ---
    ('ELC_PANEL_SCHEDULE_REF_TXT', 'TEXT', 'ELC_PWR',
     'panel schedule reference', 'BS 7671:2018+A2:2022',
     'Panel schedule document reference'),
    ('ELC_FAT_CERT_REF_TXT', 'TEXT', 'ELC_PWR',
     'factory acceptance test certificate', 'IEC 61439-1:2020',
     'Factory Acceptance Test certificate reference'),
    ('ELC_FACTORY_QR_TXT', 'TEXT', 'ELC_PWR',
     'electrical factory qr code', 'ISO 19650-3:2020',
     'Electrical factory QR code label'),
    # --- PLM_PRESSURE_T7 ---
    ('PLM_PRESSURE_TEST_REF_TXT', 'TEXT', 'PLM_DRN',
     'hydrostatic pressure test certificate reference', 'BS EN 806-4:2010',
     'Hydrostatic pressure test certificate reference'),
    ('PLM_WELD_MAP_REF_TXT', 'TEXT', 'PLM_DRN',
     'pipe weld map drawing reference', 'BS EN ISO 15609-1:2019',
     'Pipe weld map drawing reference'),
    # --- ARC_DOOR_WIN_T7 ---
    ('ARC_FACTORY_ORDER_REF_TXT', 'TEXT', 'BLE_ELES',
     'joinery factory order reference', 'BS 8000-5:1990',
     'Factory order reference for joinery'),
    ('ARC_GLAZING_SPEC_TXT', 'TEXT', 'BLE_ELES',
     'glazing specification reference', 'BS 6262-1:2022',
     'Glazing specification reference'),
    ('ASS_WARRANTY_DURATION_PARTS_YRS', None, None,
     'warranty duration years', None,
     'Reused — ARC_DOOR_WIN_T7 third row'),
    # --- CBN_OPERATIONAL_T6 / CBN_REFRIG_T6 ---
    ('CBN_B6_KG_CO2E_YR', None, None,
     'carbon operational b6', None,
     'Reused — default T6 + CBN_OPERATIONAL/REFRIG'),
    ('CBN_A1_A3_KG_CO2E', None, None,
     'carbon product a1 a3', None,
     'Reused — default T6 + CBN_OPERATIONAL/REFRIG'),
    ('CBN_RUNTIME_HRS_YR_NR', 'NUMBER', 'PER_SUST',
     'equivalent annual operating hours', 'BS EN 16798-1:2019',
     'Equivalent annual operating hours'),
    ('CBN_REFRIGERANT_GWP_KG_CO2E', 'NUMBER', 'PER_SUST',
     'refrigerant global warming potential kgco2e', 'BS EN 378-2:2016',
     'Refrigerant GWP contribution (kgCO2e)'),
    # --- ASBUILT_REBAR_T9 ---
    ('ASBUILT_COVER_DEVIATION_MM', 'NUMBER', 'ASBUILT',
     'as-built concrete cover deviation', 'BS EN 13670:2009',
     'As-built concrete cover deviation (mm)'),
    ('ASBUILT_CAPTURE_DATE_TXT', None, None,
     'asbuilt capture date', None,
     'Reused — default T9 + ASBUILT_REBAR_T9'),
    ('HEALTH_SCORE_LAST_NR', None, None,
     'health score last', None,
     'Reused — default T9 + ASBUILT_REBAR_T9'),
]


def load_params(path):
    """Return list of (name, description) tuples from MR_PARAMETERS.txt."""
    params = []
    with open(path, 'r', encoding='utf-8') as fh:
        for line in fh:
            if not line.startswith('PARAM\t'):
                continue
            parts = line.rstrip('\n').split('\t')
            # PARAM | GUID | NAME | DATATYPE | DATACATEGORY | GROUP | VISIBLE | DESCRIPTION | USERMODIFIABLE
            if len(parts) < 8:
                continue
            params.append({'name': parts[2], 'desc': parts[7]})
    return params


def name_exists(candidate, params):
    for p in params:
        if p['name'] == candidate:
            return p
    return None


def semantic_match(phrase, params):
    tokens = [t.lower() for t in re.split(r'[\s\-]+', phrase) if t]
    hits = []
    for p in params:
        desc = p['desc'].lower()
        if all(t in desc for t in tokens):
            hits.append(p)
    return hits


def main():
    if not os.path.exists(MR_PARAMS):
        print(f'ERROR: {MR_PARAMS} not found', file=sys.stderr)
        return 1
    params = load_params(MR_PARAMS)
    print(f'Loaded {len(params)} PARAM rows from MR_PARAMETERS.txt')

    lines = []
    lines.append('# PARAM_REUSE_REPORT.md')
    lines.append('')
    lines.append('Output of `check_param_reuse.py` — every candidate parameter from the '
                 'per-family T4 to T10 REPLACE sets checked against `MR_PARAMETERS.txt` '
                 'with both exact name match (Stage 1) and semantic description match '
                 '(Stage 2, all tokens of semantic phrase present, case-insensitive).')
    lines.append('')
    lines.append(f'MR_PARAMETERS.txt PARAM rows scanned: **{len(params)}**')
    lines.append('')
    lines.append('| Candidate | Verdict | Reused Name | Matched Description | Semantic Phrase |')
    lines.append('|---|---|---|---|---|')

    new_params = []
    reused = []
    for (cand, dt, grp, phrase, std, note) in CANDIDATES:
        exact = name_exists(cand, params)
        if exact is not None:
            lines.append(f'| `{cand}` | REUSE (exact) | `{exact["name"]}` | {exact["desc"]} | {phrase} |')
            reused.append(cand)
            continue
        hits = semantic_match(phrase, params)
        if hits:
            # Prefer shortest description (most specific match) then alphabetical
            hits.sort(key=lambda p: (len(p['desc']), p['name']))
            h = hits[0]
            lines.append(f'| `{cand}` | REUSE (semantic) | `{h["name"]}` | {h["desc"]} | {phrase} |')
            reused.append(cand)
            continue
        lines.append(f'| `{cand}` | **NEW** | — | — | {phrase} |')
        new_params.append((cand, dt, grp, phrase, std, note))

    lines.append('')
    lines.append(f'## Summary')
    lines.append('')
    lines.append(f'- Candidates checked: **{len(CANDIDATES)}**')
    lines.append(f'- REUSE verdicts: **{len(reused)}**')
    lines.append(f'- NEW verdicts: **{len(new_params)}**')
    lines.append('')
    lines.append('## NEW parameters to add to MR_PARAMETERS.txt')
    lines.append('')
    lines.append('| Name | Datatype | Group | Standard | Description note |')
    lines.append('|---|---|---|---|---|')
    for (cand, dt, grp, phrase, std, note) in new_params:
        lines.append(f'| `{cand}` | {dt} | {grp} | {std} | {note} |')
    lines.append('')

    with open(OUT_REPORT, 'w', encoding='utf-8') as fh:
        fh.write('\n'.join(lines) + '\n')
    print(f'Wrote {OUT_REPORT}: {len(CANDIDATES)} candidates, '
          f'{len(new_params)} NEW, {len(reused)} REUSE')
    return 0


if __name__ == '__main__':
    sys.exit(main())
