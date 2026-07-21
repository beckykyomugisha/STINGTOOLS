#!/usr/bin/env python3
"""
transform_mr_params.py
Transform MR_PARAMETERS.txt:
  1. All _BOOL params that are TEXT → YESNO
  2. ~192 native-typed params from TEXT to correct Revit type (idempotent)
  3. Add _TXT mirror params after each transformed native-typed param
     (mirrors use TEXT type, same GROUP, VISIBLE=1, USERMODIFIABLE=1)
  4. Write back in-place (backup first)
"""

import uuid, re, shutil, os, sys
from pathlib import Path

# ── UUIDv5 namespace ──────────────────────────────────────────────────────────
NS = uuid.UUID('7f9f5e3a-a7c0-b2e4-4d91-4a557c5e3a00')

def make_guid(name: str) -> str:
    return str(uuid.uuid5(NS, name))

# ── Target native types ───────────────────────────────────────────────────────
TARGET_TYPES = {
    # AREA
    'CST_CALC_AREA_M2':                      'AREA',
    'ASS_ROOM_AREA_SQ_M':                    'AREA',
    'BLE_ELE_AREA_SQ_M':                     'AREA',
    'BLE_FLR_AREA_SQ_M':                     'AREA',
    'BLE_ROOF_AREA_SQ_M':                    'AREA',
    'BLE_WINDOW_AREA_SQ_M':                  'AREA',
    'FLS_SFTY_COVERAGE_AREA_SQ_M':           'AREA',
    'HVC_DUCT_AREA_SQ_M':                    'AREA',
    # CURRENCY
    'ASS_CST_TOTAL_UGX_NR':                  'CURRENCY',
    'ASS_CST_UNIT_PRICE_UGX_NR':             'CURRENCY',
    'FLS_DETECTION_COST_UGX':                'CURRENCY',
    'PER_REPLACEMENT_COST_UGX':              'CURRENCY',
    # INTEGER
    'COM_C_DEV_BACNET_INSTANCE_INT':         'INTEGER',
    'ASS_PMT_CERT_NO_NR':                    'INTEGER',
    'CLASH_TRIAGE_SEVERITY_NR':              'INTEGER',
    'ELC_CIRCUIT_POLES_NR':                  'INTEGER',
    'CLN_MRI_ZONE_INT':                      'INTEGER',
    'ASS_DESIGN_OCCUPANCY_INT':              'INTEGER',
    # LENGTH
    'ASS_ELEVATION_M':                       'LENGTH',
    'BLE_CEILING_GRID_SZ_MM':               'LENGTH',
    'BLE_CEILING_HEIGHT_MM':                'LENGTH',
    'BLE_DOOR_HEIGHT_MM':                   'LENGTH',
    'BLE_DOOR_WIDTH_MM':                    'LENGTH',
    'BLE_FLR_THICKNESS_MM':                 'LENGTH',
    'BLE_HEADROOM_MM':                      'LENGTH',
    'BLE_RAMP_LENGTH_MM':                   'LENGTH',
    'BLE_RAMP_WIDTH_MM':                    'LENGTH',
    'BLE_ROOF_THICKNESS_MM':                'LENGTH',
    'BLE_STAIR_GOING_MM':                   'LENGTH',
    'BLE_STAIR_HEADROOM_MM':               'LENGTH',
    'BLE_STAIR_RISE_MM':                   'LENGTH',
    'BLE_STAIR_WIDTH_MM':                  'LENGTH',
    'BLE_STRUCT_CAMBER_MM':                'LENGTH',
    'BLE_STRUCT_SPAN_MM':                  'LENGTH',
    'BLE_WALL_THICKNESS_MM':               'LENGTH',
    'BLE_WINDOW_HEIGHT_MM':                'LENGTH',
    'BLE_WINDOW_WIDTH_MM':                 'LENGTH',
    'CST_CALC_LENGTH_M':                   'LENGTH',
    'ELC_CBT_SUPPORT_SPACING_MM':          'LENGTH',
    'ELC_CDT_BEND_RADIUS_MM':              'LENGTH',
    'ELC_CDT_SUPPORT_SPACING_MM':          'LENGTH',
    'ELC_CDT_SZ_MM':                       'LENGTH',
    'ELC_CTR_WIDTH_MM':                    'LENGTH',
    'ELC_LPS_MESH_SIZE_M':                 'LENGTH',
    'ELC_LPS_ROLLING_SPHERE_RADIUS_M':     'LENGTH',
    'ELC_LPS_SEPARATION_DISTANCE_MM':      'LENGTH',
    'HVC_DCT_SUPPORTS_SPACING_MM':         'LENGTH',
    'HVC_PIPE_LENGTH_M':                   'LENGTH',
    'MNT_HGT_MM':                          'LENGTH',
    'PLM_PPE_INS_THK_MM':                  'LENGTH',
    'PLM_PPE_LENGTH_M':                    'LENGTH',
    'PLM_PPE_SUPPORTS_SPACING_MM':         'LENGTH',
    'PLM_PPE_SZ_MM':                       'LENGTH',
    'SLV_SZ_MM':                           'LENGTH',
    'STR_BRACE_LENGTH_MM':                 'LENGTH',
    'STR_COL_HEIGHT_MM':                   'LENGTH',
    'STR_COL_SIZE_MM':                     'LENGTH',
    'STR_FDN_COVER_MM':                    'LENGTH',
    'STR_FDN_DEPTH_MM':                    'LENGTH',
    'STR_FDN_SETTLEMENT_MM':               'LENGTH',
    'STR_FDN_SIZE_MM':                     'LENGTH',
    # NUMBER (includes already-correct ones for idempotency)
    'ASBUILT_DEVIATION_MM':                'NUMBER',
    'ASS_CRITICALITY_RATING_NR':           'NUMBER',
    'ASS_CST_FX_TO_BASE_NR':               'NUMBER',
    'ASS_CST_QUANTITY_NR':                 'NUMBER',
    'ASS_CST_UNIT_RATE_NR':                'NUMBER',
    'ASS_DESIGN_LTG_LVL_LUX_NR':          'NUMBER',
    'ASS_EXPECTED_LIFE_YEARS_YRS':         'NUMBER',
    'ASS_MAINTENANCE_FREQUENCY_MONTHS':    'NUMBER',
    'ASS_PMT_PCT_COMPLETE_NR':             'NUMBER',
    'ASS_VAR_VALUATION_NR':                'NUMBER',
    'ASS_WEIGHT_KG_NR':                    'NUMBER',
    'BLE_CEILING_NOISE_REDUCTION_COEFFICIENT_NRC_NR': 'NUMBER',
    'BLE_FLOOR_LOAD_KN_M2':               'NUMBER',
    'BLE_FLR_LD_CAP_KPA':                 'NUMBER',
    'BLE_RAMP_SLOPE_PCT':                  'NUMBER',
    'BLE_ROOF_SLOPE_DEG':                  'NUMBER',
    'BLE_ROOM_FIRE_ESCAPE_CAPACITY_NR':    'NUMBER',
    'BLE_ROOM_OCCUPANCY_NR':               'NUMBER',
    'BLE_ROOM_VENTILATION_RATE_LPS':       'NUMBER',
    'BLE_STRUCT_FDN_BEARING_KN_M2':        'NUMBER',
    'BLE_STRUCT_LD_CAP_KN_NR':             'NUMBER',
    'BLE_WALL_SOUND_TRANSMISSION_CLASS_RATING_NR': 'NUMBER',
    'BLE_WINDOW_SOLAR_HEAT_GAIN_COEFFICIENT_NR':   'NUMBER',
    'BLE_WINDOW_U_VALUE_W_M_2K_NR':        'NUMBER',
    'CBN_A1_A3_KG_CO2E':                   'NUMBER',
    'CBN_A4_KG_CO2E':                      'NUMBER',
    'CBN_B6_KG_CO2E_YR':                   'NUMBER',
    'CLN_BARI_DESIGN_KG_NR':               'NUMBER',
    'COM_C_PWR_SUPPLY_V':                  'NUMBER',
    'CST_CALC_INTL_PRICE_USD':             'NUMBER',
    'CST_CALC_UG_PRICE_UGX':               'NUMBER',
    'CST_DELIVERY_LEAD_TIME_DAYS':         'NUMBER',
    'CST_FIX_LUMEN_OUTPUT_LM':             'NUMBER',
    'CST_INTL_PRICE_USD':                  'NUMBER',
    'CST_S_REI_WEIGHT_KG':                 'NUMBER',
    'CST_UG_PRICE_UGX':                    'NUMBER',
    'ELC_CBL_FEEDER_SZ_MM2':               'NUMBER',
    'ELC_CBL_NUM_OF_CORES_NR':             'NUMBER',
    'ELC_CDT_CBL_FILL_PCT':                'NUMBER',
    'ELC_CKT_CUR_A':                       'NUMBER',
    'ELC_CKT_NR':                          'NUMBER',
    'ELC_CKT_PHASE_COUNT_NR':              'NUMBER',
    'ELC_CTR_FILL_PCT':                    'NUMBER',
    'ELC_LPS_AIR_TERMINAL_COUNT_NR':       'NUMBER',
    'ELC_LPS_CONDUCTOR_CROSS_SECT_MM2':    'NUMBER',
    'ELC_LPS_DOWN_CONDUCTOR_COUNT_NR':     'NUMBER',
    'ELC_LPS_EARTH_ELECTRODE_COUNT_NR':    'NUMBER',
    'ELC_LPS_EARTH_RESISTANCE_OHM':        'NUMBER',
    'ELC_LPS_INSPECTION_INTERVAL_MONTHS':  'NUMBER',
    'ELC_LPS_PROJECT_NG_OVERRIDE_NR':      'NUMBER',
    'ELC_LPS_PROTECTION_ANGLE_DEG':        'NUMBER',
    'ELC_PNL_CONNECTED_LOAD_KW':           'NUMBER',
    'ELC_PNL_MAIN_BRK_A':                  'NUMBER',
    'ELC_PNL_NUM_OF_WAYS_NR':              'NUMBER',
    'ELC_PNL_PHS_COUNT_NR':                'NUMBER',
    'ELC_PNL_RATED_KW':                    'NUMBER',
    'ELC_PNL_SHORT_CIRCUIT_RATING_KA':     'NUMBER',
    'ELC_PNL_VLT_V':                       'NUMBER',
    'ELC_PWR_KW':                          'NUMBER',
    'ELC_SPARE_WAYS_NR':                   'NUMBER',
    'ELC_VLT_DROP_PCT':                    'NUMBER',
    'ELC_VLT_PRIMARY_RATING_V':            'NUMBER',
    'ELC_VOLTAGE_V':                       'NUMBER',
    'FAB_HANGER_LOAD_KN':                  'NUMBER',
    'FLS_ALARM_CIRCUIT_COUNT_NR':          'NUMBER',
    'FLS_DETECTOR_COUNT_NR':               'NUMBER',
    'FLS_PROT_FLS_RESISTANCE_RATING_MINUTES_MIN': 'NUMBER',
    'FLS_SFTY_FLW_RATE_LPM_NR':            'NUMBER',
    'FLS_SFTY_HEADS_OPERATING_NR':         'NUMBER',
    'FLS_SFTY_K_FACTOR_NR':                'NUMBER',
    'FLS_SFTY_TEMP_RATING_C':              'NUMBER',
    'HEALTH_SCORE_LAST_NR':                'NUMBER',
    'HVC_AIR_CHANGES_PER_HR':              'NUMBER',
    'HVC_CAP_KW':                          'NUMBER',
    'HVC_CAPACITY_KW':                     'NUMBER',
    'HVC_DCT_FLW_CFM':                     'NUMBER',
    'HVC_DCT_SOUNDLVL_DB':                 'NUMBER',
    'HVC_DUCT_FLOWRATE_M3H':               'NUMBER',
    'HVC_EFF_RATIO_NR':                    'NUMBER',
    'HVC_NOISE_LEVEL_DB':                  'NUMBER',
    'HVC_PIPE_PRESSURE_KPA':               'NUMBER',
    'HVC_PWR_KW':                          'NUMBER',
    'HVC_REFRIGERANT_KG_NR':               'NUMBER',
    'HVC_VEL_MPS':                         'NUMBER',
    'ICT_TEL_OUTLET_QTY_NR':               'NUMBER',
    'LTG_CLR_RENDERING_INDEX_NR':          'NUMBER',
    'LTG_CLR_TEMP_K':                      'NUMBER',
    'LTG_DESIGN_ILLUMINANCE_LUX':          'NUMBER',
    'LTG_EFFICACY_LM_W':                   'NUMBER',
    'LTG_FIX_LMP_WATTAGE_W':               'NUMBER',
    'PER_EMBODIED_ENERGY_MJ':              'NUMBER',
    'PER_EXPECTED_LIFE_YEARS':             'NUMBER',
    'PER_FIRE_RATING_HR':                  'NUMBER',
    'PER_RECYCLABILITY_PCT':               'NUMBER',
    'PER_SUST_EMBODIED_CARBON_KG':         'NUMBER',
    'PER_SUST_RECYCLED_CONTENT_PCT':       'NUMBER',
    'PER_SUST_WTR_RATING_NR':              'NUMBER',
    'PER_THERM_R_VALUE_M2K_W':             'NUMBER',
    'PER_THERM_U_VALUE_W_M2K':             'NUMBER',
    'PLM_ACC_CONNECTION_SZ_NR':            'NUMBER',
    'PLM_PPE_FLW_LPS':                     'NUMBER',
    'PLM_PPE_PSR_RATING_BAR':              'NUMBER',
    'PLM_PSR_KPA':                         'NUMBER',
    'PLM_VEL_MPS':                         'NUMBER',
    'PLM_VLV_CV_NR':                       'NUMBER',
    'PLM_VNT_SZ_NR':                       'NUMBER',
    'RAD_LEAD_MM_NR':                      'NUMBER',
    'RGL_KCCA_CERT_NR':                    'NUMBER',
    'RGL_NWSC_CERT_NR':                    'NUMBER',
    'STR_COL_MOMENT_CAPACITY_KNM':         'NUMBER',
    'STR_FDN_BEARING_KN_M2':               'NUMBER',
    'STR_LOAD_AXIAL_KN':                   'NUMBER',
    # Phase 187 HVAC params
    'HVC_PEAK_SENS_W':                     'NUMBER',
    'HVC_PEAK_LAT_W':                      'NUMBER',
    'HVC_PEAK_HOUR':                       'NUMBER',
    'HVC_OA_LS':                           'NUMBER',
    'HVC_FLOW_LS':                         'NUMBER',
}

# Date params — already TEXT, NO mirror needed
DATE_PARAMS = {
    'ASS_CST_AS_OF_DT', 'ASS_CST_FX_DATE_DT', 'ASS_PMT_CERT_DATE_DT',
    'ASS_PMT_LAST_VALUED_DT', 'ASS_VAR_INSTRUCTION_DT', 'HVC_SIZE_MODIFIED_DT',
}

# Suffix replacement for mirror name generation
SUFFIX_REPLACEMENTS = [
    ('_LM_W', '_TXT'), ('_SQ_M', '_TXT'), ('_CU_M', '_TXT'),
    ('_MM2', '_TXT'), ('_M2K_W', '_TXT'), ('_W_M2K', '_TXT'),
    ('_KN_M2', '_TXT'), ('_INT', '_TXT'), ('_NR', '_TXT'),
    ('_MM', '_TXT'), ('_M2', '_TXT'), ('_KW', '_TXT'),
    ('_KPA', '_TXT'), ('_KNM', '_TXT'), ('_KA', '_TXT'),
    ('_KN', '_TXT'), ('_LPS', '_TXT'), ('_LPM', '_TXT'),
    ('_LPM_NR', '_TXT'), ('_MPS', '_TXT'), ('_LUX', '_TXT'),
    ('_OHM', '_TXT'), ('_DEG', '_TXT'), ('_YRS', '_TXT'),
    ('_USD', '_TXT'), ('_UGX', '_TXT'), ('_LM', '_TXT'),
    ('_MJ', '_TXT'), ('_DB', '_TXT'), ('_CFM', '_TXT'),
    ('_PCT', '_TXT'), ('_KG', '_TXT'), ('_BAR', '_TXT'),
    ('_HR', '_TXT'), ('_CO2E', '_TXT'), ('_CO2E_YR', '_TXT'),
    ('_W', '_TXT'), ('_V', '_TXT'), ('_A', '_TXT'),
    ('_M3H', '_TXT'), ('_M', '_TXT'), ('_K', '_TXT'),
    ('_LS', '_TXT'), ('_C', '_TXT'),
]

def make_mirror_name(name: str) -> str:
    """Generate the _TXT mirror name for a native-typed param."""
    # If name already ends _TXT, it's its own mirror
    if name.endswith('_TXT'):
        return name
    # Try suffix replacements (longest first to avoid partial matches)
    for suffix, replacement in SUFFIX_REPLACEMENTS:
        if name.endswith(suffix):
            candidate = name[:-len(suffix)] + replacement
            # Avoid creating a name identical to the source
            if candidate != name:
                return candidate
    # Fallback: append _TXT
    return name + '_TXT'


def parse_param_line(line: str):
    """Parse a PARAM line into fields list. Returns None if not a PARAM line."""
    if not line.startswith('PARAM\t'):
        return None
    parts = line.rstrip('\n').split('\t')
    # Expected: PARAM guid name datatype datacategory group visible description usermodifiable
    while len(parts) < 9:
        parts.append('')
    return parts


def build_param_line(parts: list) -> str:
    return '\t'.join(parts[:9]) + '\n'


def main():
    # Path was hardcoded to the original author's Linux sandbox. Parameterised so
    # the analysis is reproducible on any checkout:
    #   python tools/transform_mr_params.py --dry-run
    import argparse
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--file', default='StingTools/Data/MR_PARAMETERS.txt')
    ap.add_argument('--dry-run', action='store_true',
                    help='report what would change; write nothing')
    args = ap.parse_args()

    src = Path(args.file)
    backup = src.with_suffix('.txt.bak')

    if not src.exists():
        print(f"ERROR: {src} not found", file=sys.stderr)
        sys.exit(1)

    # Backup (skipped on a dry run — a dry run must not touch the tree)
    if not args.dry_run:
        shutil.copy2(src, backup)
        print(f"Backup: {backup}")

    lines = src.read_text(encoding='utf-8-sig').splitlines(keepends=True)

    # ── First pass: collect all existing param names and their GUIDs ──────────
    existing_names = {}   # name → guid
    existing_guids = {}   # guid → name
    for line in lines:
        p = parse_param_line(line)
        if p:
            existing_names[p[2]] = p[1]
            existing_guids[p[1]] = p[2]

    print(f"Existing params: {len(existing_names)}")

    # ── Transformation counters ───────────────────────────────────────────────
    bool_fixed = 0
    native_fixed = 0
    mirrors_added = 0
    already_correct = 0

    # ── Second pass: transform ────────────────────────────────────────────────
    out_lines = []
    for line in lines:
        p = parse_param_line(line)
        if not p:
            out_lines.append(line)
            continue

        name = p[2]
        current_type = p[3]

        # ── Rule 1: Any _BOOL param that is TEXT → YESNO ─────────────────────
        if name.endswith('_BOOL') and current_type == 'TEXT':
            p[3] = 'YESNO'
            bool_fixed += 1
            out_lines.append(build_param_line(p))
            continue

        # ── Rule 2 + 3: Native-typed params ──────────────────────────────────
        if name in TARGET_TYPES:
            target = TARGET_TYPES[name]
            if current_type != target:
                p[3] = target
                native_fixed += 1
            else:
                already_correct += 1

            out_lines.append(build_param_line(p))

            # Inject _TXT mirror after this line
            mirror_name = make_mirror_name(name)
            if mirror_name == name:
                # Can't make a distinct mirror name — skip
                continue

            if mirror_name in existing_names:
                # Mirror already exists — don't duplicate
                continue

            # Build mirror PARAM line
            mirror_guid = make_guid(mirror_name)
            # Use same group as original
            group_id = p[5]
            mirror_parts = [
                'PARAM',
                mirror_guid,
                mirror_name,
                'TEXT',
                '',          # datacategory
                group_id,
                '1',         # visible
                f'{name} display mirror [auto-generated]',
                '1',         # usermodifiable
            ]
            out_lines.append(build_param_line(mirror_parts))
            # Register so we don't add it again if same name appears twice
            existing_names[mirror_name] = mirror_guid
            mirrors_added += 1
            continue

        # ── Passthrough ───────────────────────────────────────────────────────
        out_lines.append(build_param_line(p))

    # Write output
    if args.dry_run:
        print("[dry-run] nothing written")
    else:
        src.write_text(''.join(out_lines), encoding='utf-8')

    print(f"\nTransformation complete:")
    print(f"  _BOOL TEXT→YESNO fixes : {bool_fixed}")
    print(f"  Native type fixes       : {native_fixed}")
    print(f"  Already correct (skip)  : {already_correct}")
    print(f"  _TXT mirrors added      : {mirrors_added}")
    print(f"  Total output lines      : {len(out_lines)}")
    print(f"\nWritten: {src}" if not args.dry_run else "\n(dry run — no file written)")


if __name__ == '__main__':
    main()
