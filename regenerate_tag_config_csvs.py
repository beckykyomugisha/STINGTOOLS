#!/usr/bin/env python3
"""regenerate_tag_config_csvs.py — rewrite the T4..T10 block of every
Tag Family section in the four STING_TAG_CONFIG_v5_0_*.csv files.

Source of truth: TIER_VARIATION_MATRIX.md (same classification rules as
build_matrix.py) + Section 2 REPLACE sets (same as PerFamilyTierMap.cs).

Preserved verbatim: schema header comments, TAG STYLE PARAMETER CATALOG
block, every T1 / T2 / T3 row, the WARNING PARAMETERS block. Only the
T4..T10 rows (and the blank line separating from warnings) are rewritten.
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(REPO_ROOT, 'StingTools', 'Data')

CSVS = [
    os.path.join(DATA, 'STING_TAG_CONFIG_v5_0_ARCH.csv'),
    os.path.join(DATA, 'STING_TAG_CONFIG_v5_0_GEN.csv'),
    os.path.join(DATA, 'STING_TAG_CONFIG_v5_0_MEP.csv'),
    os.path.join(DATA, 'STING_TAG_CONFIG_v5_0_STR.csv'),
]

# --- Classification (mirror of build_matrix.py / build_map_cs.py) ---
ANNOTATION_NAMES = {
    'STING - Architectural Sheet Tag',
    'STING - MEP Sheet Tag',
    'STING - Structural Sheet Tag',
    'STING - Sheet Document Tag',
}
SPATIAL_NAMES = {
    'STING - Room Tag', 'STING - Areas Tag',
    'STING - Spaces Tag', 'STING - Zones Tag',
    'STING - Parts Tag', 'STING - Assemblies Tag',
    'STING - Point Loads Tag', 'STING - Line Loads Tag', 'STING - Area Loads Tag',
    'STING - Analytical Nodes Tag', 'STING - Analytical Panels Tag',
    'STING - Analytical Openings Tag', 'STING - Analytical Links Tag',
    'STING - Model Groups Tag', 'STING - RVT Links Tag', 'STING - Toposolid Links Tag',
    'STING - Property Lines Tag', 'STING - Property Line Segments Tag',
}
REPLACE_PLAN = {
    'STING - Structural Rebar Tag':               {'T7': 'STR_REBAR_T7',  'T9': 'ASBUILT_REBAR_T9'},
    'STING - Structural Rebar Couplers Tag':      {'T7': 'STR_REBAR_T7',  'T9': 'ASBUILT_REBAR_T9'},
    'STING - Structural Fabric Reinforcement Tag':{'T7': 'STR_REBAR_T7',  'T9': 'ASBUILT_REBAR_T9'},
    'STING - Structural Area Reinforcement Tag':  {'T7': 'STR_REBAR_T7',  'T9': 'ASBUILT_REBAR_T9'},
    'STING - Structural Path Reinforcement Tag':  {'T7': 'STR_REBAR_T7',  'T9': 'ASBUILT_REBAR_T9'},
    'STING - Structural Connection Tag':          {'T7': 'STR_CONN_T7'},
    'STING - Mechanical Equipment Tag':           {'T6': 'CBN_REFRIG_T6', 'T7': 'HVC_REFRIG_T7'},
    'STING - Mechanical Equipment Sets Tag':      {'T6': 'CBN_REFRIG_T6', 'T7': 'HVC_REFRIG_T7'},
    'STING - Electrical Equipment Tag':           {'T6': 'CBN_OPERATIONAL_T6', 'T7': 'ELC_PANEL_T7'},
    'STING - Plumbing Equipment Tag':             {'T7': 'PLM_PRESSURE_T7'},
    'STING - MEP Fabrication Pipework Tag':       {'T7': 'PLM_PRESSURE_T7'},
    'STING - Door Tag':                           {'T7': 'ARC_DOOR_WIN_T7'},
    'STING - Window Tag':                         {'T7': 'ARC_DOOR_WIN_T7'},
    'STING - Curtain Panel Tag':                  {'T7': 'ARC_DOOR_WIN_T7'},
    'STING - Lighting Device Tag':                {'T6': 'CBN_OPERATIONAL_T6'},
    'STING - Lighting Fixture Tag':               {'T6': 'CBN_OPERATIONAL_T6'},
    'STING - Mechanical Control Devices Tag':     {'T6': 'CBN_OPERATIONAL_T6'},
    'STING - Fire Protection Tag':                {'T6': 'CBN_OPERATIONAL_T6'},
    'STING - Audio Visual Devices Tag':           {'T6': 'CBN_OPERATIONAL_T6'},
}

def classify(name):
    if 'Internal Point Loads' in name or 'Internal Line Loads' in name or 'Internal Area Loads' in name:
        return 'SPATIAL'
    if 'Analytical Members' in name:
        return 'SPATIAL'
    if name in ANNOTATION_NAMES:
        return 'ANNOTATION'
    if name in SPATIAL_NAMES:
        return 'SPATIAL'
    return 'PHYSICAL'

def tier_states(name):
    klass = classify(name)
    if klass == 'ANNOTATION':
        base = {'T4':'OMIT','T5':'KEEP','T6':'OMIT','T7':'OMIT','T8':'OMIT','T9':'OMIT','T10':'OMIT'}
    elif klass == 'SPATIAL':
        base = {'T4':'KEEP','T5':'KEEP','T6':'KEEP','T7':'OMIT','T8':'KEEP','T9':'OMIT','T10':'KEEP'}
    else:
        base = {t:'KEEP' for t in ('T4','T5','T6','T7','T8','T9','T10')}
    for tier, setid in REPLACE_PLAN.get(name, {}).items():
        base[tier] = f'REPLACE:{setid}'
    return base

print("Script loaded (classifier)")

# --- Default and REPLACE row sets ---
# Row columns: (Tier, Parameter, Prefix, Suffix, Spc, Brk, Disc, Type, NameSuffix, FormulaParam, Style, Color, Size)
# Brk: '✓' or '' (empty)
# NameSuffix is the label name's descriptive portion; we emit "Show T{N} - {NameSuffix}"
# Formula wraps the parameter in "if(TAG_PARA_STATE_{N}_BOOL, PARAM, """")"
# Style/Color/Size are per-row per-tier defaults

# Default T4..T10 rows — exact copy of the 21-row block in every current CSV.
DEFAULT_ROWS = {
    'T4': [
        ('COMM_STATE_TXT',       'Comm:', '',       0, '',  'Commissioning - State',      'BOLD',   'BLUE',   '2.0'),
        ('COMM_DATE_TXT',        'on',    '',       0, '',  'Commissioning - Date',       'BOLD',   'BLUE',   '2.0'),
        ('COMM_OPERATIVE_TXT',   'by',    '',       0, '✓', 'Commissioning - Operative',  'BOLD',   'BLUE',   '2.0'),
    ],
    'T5': [
        ('CST_UG_PRICE_UGX',     '',      'UGX',    0, '',  'Cost - UG Price',            'NOM',    'PURPLE', '2.0'),
        ('CST_INTL_PRICE_USD',   '/ ',    'USD',    0, '',  'Cost - Intl Price',          'NOM',    'PURPLE', '2.0'),
        ('CST_QUOTE_REF_TXT',    'Quote:', '',      0, '✓', 'Cost - Quote Ref',           'NOM',    'PURPLE', '2.0'),
    ],
    'T6': [
        ('CBN_A1_A3_KG_CO2E',    'A1-A3:', 'kgCO₂e',0, '',  'Carbon - Product A1-A3',     'ITALIC', 'GREEN',  '2.0'),
        ('CBN_A4_KG_CO2E',       '| A4:',  'kgCO₂e',0, '',  'Carbon - Transport A4',      'ITALIC', 'GREEN',  '2.0'),
        ('CBN_B6_KG_CO2E_YR',    '| B6:',  'kgCO₂e/yr',0,'✓','Carbon - Operational B6',   'ITALIC', 'GREEN',  '2.0'),
    ],
    'T7': [
        ('ASS_SPOOL_NR_TXT',      'Spool:','',      0, '',  'Fabrication - Spool No',     'BOLD',   'ORANGE', '2.0'),
        ('ASS_FAB_STATUS_TXT',    'Fab:',  '',      0, '',  'Fabrication - Status',       'BOLD',   'ORANGE', '2.0'),
        ('ASS_QC_INSPECTOR_TXT',  'QC:',   '',      0, '✓', 'Fabrication - QC Inspector', 'BOLD',   'ORANGE', '2.0'),
    ],
    'T8': [
        ('CLASH_TRIAGE_SEVERITY_NR',    'Sev:', '/5', 0, '',  'Clash - Triage Severity',    'BOLD', 'RED', '2.0'),
        ('CLASH_TRIAGE_CATEGORY_TXT',   '',     '',   0, '',  'Clash - Triage Category',    'BOLD', 'RED', '2.0'),
        ('CLASH_RESOLUTION_STATUS_TXT', 'Res:', '',   0, '✓', 'Clash - Resolution Status',  'BOLD', 'RED', '2.0'),
    ],
    'T9': [
        ('ASBUILT_DEVIATION_MM',       'Δ:',     'mm',  0, '',  'As-built - Deviation',    'ITALIC', 'GREY', '2.0'),
        ('ASBUILT_CAPTURE_DATE_TXT',   'on',     '',    0, '',  'As-built - Capture Date', 'ITALIC', 'GREY', '2.0'),
        ('HEALTH_SCORE_LAST_NR',       'Health:', '/100', 0, '✓', 'Health Score',          'ITALIC', 'GREY', '2.0'),
    ],
    'T10': [
        ('IFC_PSET_OVERRIDE_TXT', 'IFC:', '', 0, '',  'Compliance - IFC PSet Override', 'NOM', 'GREY', '2.0'),
        ('ACC_ISSUE_ID_TXT',      'ACC:', '', 0, '',  'Compliance - ACC Issue',          'NOM', 'GREY', '2.0'),
        ('ACC_SYNC_STATUS_TXT',   'Sync:','', 0, '✓', 'Compliance - ACC Sync Status',    'NOM', 'GREY', '2.0'),
    ],
}

# REPLACE sets. Row tuple identical shape to DEFAULT_ROWS entries.
# Each set maps to a tier number (4..10).
REPLACE_SETS = {
    'STR_REBAR_T7': ('T7', [
        ('STR_BAR_MARK_TXT',            'Mark:','',  0, '',  'Rebar - Bar Mark',        'BOLD','ORANGE','2.0'),
        ('STR_BEND_SCHEDULE_REF_TXT',   'BBS:', '',  0, '',  'Rebar - BBS Ref',         'BOLD','ORANGE','2.0'),
        ('STR_CUTTING_LIST_REF_TXT',    'CL:',  '',  0, '✓', 'Rebar - Cutting List',    'BOLD','ORANGE','2.0'),
    ]),
    'STR_CONN_T7': ('T7', [
        ('STR_WELD_PROCEDURE_REF_TXT',  'WPS:', '',  0, '',  'Connection - WPS',        'BOLD','ORANGE','2.0'),
        ('STR_BOLT_TORQUE_NM_NR',       'Tq:',  'Nm',1, '',  'Connection - Bolt Torque','BOLD','ORANGE','2.0'),
        ('ASS_QC_INSPECTOR_TXT',        'QC:',  '',  0, '✓', 'Connection - QC Inspector','BOLD','ORANGE','2.0'),
    ]),
    'HVC_REFRIG_T7': ('T7', [
        ('HVC_REFRIGERANT_CHARGE_KG_NR','R-chg:','kg',1,'',  'Refrigerant Charge',      'BOLD','ORANGE','2.0'),
        ('HVC_FACTORY_FLASH_TEST_DATE_TXT','Flash:','',0,'', 'Flash Test Date',         'BOLD','ORANGE','2.0'),
        ('HVC_FACTORY_QR_TXT',          'QR:',  '',  0, '✓', 'Factory QR',              'BOLD','ORANGE','2.0'),
    ]),
    'ELC_PANEL_T7': ('T7', [
        ('ELC_PANEL_SCHEDULE_REF_TXT',  'PS:',  '',  0, '',  'Panel Schedule',          'BOLD','ORANGE','2.0'),
        ('ELC_FAT_CERT_REF_TXT',        'FAT:', '',  0, '',  'FAT Cert',                'BOLD','ORANGE','2.0'),
        ('ELC_FACTORY_QR_TXT',          'QR:',  '',  0, '✓', 'Elec Factory QR',         'BOLD','ORANGE','2.0'),
    ]),
    'PLM_PRESSURE_T7': ('T7', [
        ('PLM_PRESSURE_TEST_REF_TXT',   'HT:',  '',  0, '',  'Hydrostatic Test',        'BOLD','ORANGE','2.0'),
        ('PLM_WELD_MAP_REF_TXT',        'WM:',  '',  0, '',  'Pipe Weld Map',           'BOLD','ORANGE','2.0'),
        ('ASS_QC_INSPECTOR_TXT',        'QC:',  '',  0, '✓', 'Pipework QC',             'BOLD','ORANGE','2.0'),
    ]),
    'ARC_DOOR_WIN_T7': ('T7', [
        ('ARC_FACTORY_ORDER_REF_TXT',   'FO:',  '',  0, '',  'Factory Order',           'BOLD','ORANGE','2.0'),
        ('ARC_GLAZING_SPEC_TXT',        'Glz:', '',  0, '',  'Glazing Spec',            'BOLD','ORANGE','2.0'),
        ('ASS_WARRANTY_DURATION_PARTS_YRS','Warr:','yr',1,'✓','Warranty Parts',         'BOLD','ORANGE','2.0'),
    ]),
    'CBN_OPERATIONAL_T6': ('T6', [
        ('CBN_B6_KG_CO2E_YR',           'B6:',    'kgCO₂e/yr',0,'',  'Operational B6',  'ITALIC','GREEN','2.0'),
        ('CBN_A1_A3_KG_CO2E',           '| A1-A3:','kgCO₂e', 0,'',   'Product A1-A3',   'ITALIC','GREEN','2.0'),
        ('CBN_RUNTIME_HRS_YR_NR',       '| Run:', 'hr/yr',    1,'✓', 'Runtime hrs/yr',  'ITALIC','GREEN','2.0'),
    ]),
    'CBN_REFRIG_T6': ('T6', [
        ('CBN_B6_KG_CO2E_YR',           'B6:',    'kgCO₂e/yr',0,'',  'Operational B6',   'ITALIC','GREEN','2.0'),
        ('CBN_A1_A3_KG_CO2E',           '| A1-A3:','kgCO₂e', 0,'',   'Product A1-A3',    'ITALIC','GREEN','2.0'),
        ('CBN_RUNTIME_HRS_YR_NR',       '| Run:', 'hr/yr',    1,'',  'Runtime hrs/yr',   'ITALIC','GREEN','2.0'),
        ('CBN_REFRIGERANT_GWP_KG_CO2E', '| GWP:', 'kgCO₂e',   1,'✓', 'Refrigerant GWP',  'ITALIC','GREEN','2.0'),
    ]),
    'ASBUILT_REBAR_T9': ('T9', [
        ('ASBUILT_COVER_DEVIATION_MM',  'ΔCvr:',  'mm',   1, '',  'Cover Deviation',    'ITALIC','GREY','2.0'),
        ('ASBUILT_CAPTURE_DATE_TXT',    'on',     '',     0, '',  'Capture Date',       'ITALIC','GREY','2.0'),
        ('HEALTH_SCORE_LAST_NR',        'Health:','/100', 1, '✓', 'Health Score',       'ITALIC','GREY','2.0'),
    ]),
}
print("Row sets loaded:", len(DEFAULT_ROWS), "default tiers,", len(REPLACE_SETS), "replace sets")

def csv_quote(s):
    """Wrap in double quotes if it contains comma or quote; escape inner quotes."""
    if s is None:
        return ''
    s = str(s)
    if ',' in s or '"' in s or '\n' in s:
        return '"' + s.replace('"', '""') + '"'
    return s

def emit_row(row_idx, tier, row_tuple):
    """Emit a single CSV row in the exact shape of the existing CSVs."""
    param, prefix, suffix, spc, brk, name_suffix, style, color, size = row_tuple
    tier_num = int(tier[1:])
    name = f"Show T{tier_num} - {name_suffix}"
    formula = f'if(TAG_PARA_STATE_{tier_num}_BOOL, {param}, "")'
    cols = [
        str(row_idx),
        tier,
        param,
        prefix,
        suffix,
        str(spc),
        brk,
        'Common',
        'Text',
        name,
        formula,
        style,
        color,
        size,
        'None',
        'None',
    ]
    return ','.join(csv_quote(c) for c in cols)

def build_tier_rows(family_name):
    """Return list of CSV-formatted strings for this family's T4..T10 block."""
    states = tier_states(family_name)
    rows = []
    idx = 1
    for tier in ('T4', 'T5', 'T6', 'T7', 'T8', 'T9', 'T10'):
        state = states[tier]
        if state == 'OMIT':
            continue
        if state == 'KEEP':
            for row_tuple in DEFAULT_ROWS[tier]:
                rows.append(emit_row(idx, tier, row_tuple))
                idx += 1
        elif state.startswith('REPLACE:'):
            set_id = state.split(':', 1)[1]
            set_tier, set_rows = REPLACE_SETS[set_id]
            assert set_tier == tier, f"{set_id} mismatched tier"
            for row_tuple in set_rows:
                rows.append(emit_row(idx, tier, row_tuple))
                idx += 1
    return rows

def regen_csv(path):
    """Rewrite T4..T10 block per-family in this CSV file, preserve everything else."""
    with open(path, 'r', encoding='utf-8') as f:
        text = f.read()

    # The content splits into: (header block through style catalog) + per-family blocks
    # Each family block: "Tag Family #N: NAME\n<next line>\n#,Tier,...header\n<rows>\n<warning block>\n"
    # We identify T4..T10 rows as those starting with "^\d+,T[4-9]|^\d+,T10"
    fam_pat = re.compile(r'^(Tag Family #\d+: [^\n]+\n)', re.M)
    matches = list(fam_pat.finditer(text))
    if not matches:
        print(f'  [SKIP] {path} — no families')
        return

    out = []
    out.append(text[:matches[0].start()])  # header block + style catalog up to first family

    for i, m in enumerate(matches):
        block_start = m.start()
        block_end = matches[i+1].start() if i+1 < len(matches) else len(text)
        block = text[block_start:block_end]

        # Parse family name
        fam_m = re.match(r'Tag Family #\d+: (.+?)\n', block)
        fam_name = fam_m.group(1).strip() if fam_m else ''

        # Split the block into lines, keeping line endings consistent
        lines = block.split('\n')

        # Scan lines: before T4 row → keep; T4..T10 rows → drop; warning block + anything after → keep
        tier4_10_pat = re.compile(r'^\d+,T(4|5|6|7|8|9|10),')
        out_lines = []
        dropped_any = False
        pending_rows = build_tier_rows(fam_name)

        # We need to insert pending_rows at the exact first spot we see a T4..T10 row
        inserted = False
        for ln in lines:
            if tier4_10_pat.match(ln):
                if not inserted:
                    out_lines.extend(pending_rows)
                    inserted = True
                dropped_any = True
                continue  # drop old row
            out_lines.append(ln)

        # If the family had no existing T4..T10 rows (unlikely — current CSVs all have 21),
        # insert new rows just before the warning block marker.
        if not inserted and pending_rows:
            warn_idx = None
            for j, ln in enumerate(out_lines):
                if ln.startswith('⚠ WARNING PARAMETERS'):
                    warn_idx = j
                    break
            if warn_idx is not None:
                out_lines = out_lines[:warn_idx] + pending_rows + out_lines[warn_idx:]

        out.append('\n'.join(out_lines))

    with open(path, 'w', encoding='utf-8') as f:
        f.write(''.join(out))
    print(f'  [OK]   {path}')

def main():
    for p in CSVS:
        print(f'Regenerating {os.path.basename(p)}')
        regen_csv(p)

if __name__ == '__main__':
    main()
