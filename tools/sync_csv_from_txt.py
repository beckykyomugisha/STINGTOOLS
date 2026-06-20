#!/usr/bin/env python3
"""
sync_csv_from_txt.py
Sync MR_PARAMETERS.csv from the transformed MR_PARAMETERS.txt:
  1. Update Data_Type for all params whose type changed in TXT
  2. Add rows for new _TXT mirror params
"""
import shutil
from pathlib import Path

TXT = Path('/home/user/STINGTOOLS/StingTools/Data/MR_PARAMETERS.txt')
CSV = Path('/home/user/STINGTOOLS/StingTools/Data/MR_PARAMETERS.csv')

REVIT_TYPE_MAP = {
    'TEXT': 'TEXT', 'YESNO': 'YESNO', 'INTEGER': 'INTEGER',
    'NUMBER': 'NUMBER', 'LENGTH': 'LENGTH', 'AREA': 'AREA',
    'VOLUME': 'VOLUME', 'CURRENCY': 'CURRENCY',
}

GROUP_NAMES = {}

def load_txt():
    """Parse TXT → {name: (guid, type, group_id, description, user_mod)}"""
    params = {}
    for line in TXT.read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('GROUP\t'):
            parts = line.split('\t')
            if len(parts) >= 3:
                GROUP_NAMES[parts[1]] = parts[2]
        if not line.startswith('PARAM\t'):
            continue
        parts = line.split('\t')
        while len(parts) < 9:
            parts.append('')
        name = parts[2]
        params[name] = {
            'guid': parts[1],
            'type': parts[3],
            'group_id': parts[5],
            'description': parts[7],
            'user_mod': parts[8].rstrip(),
        }
    return params

def load_csv():
    lines = CSV.read_text(encoding='utf-8-sig').splitlines(keepends=True)
    comment_lines = [l for l in lines if l.startswith('#')]
    data_lines = [l for l in lines if not l.startswith('#')]
    return comment_lines, data_lines

def main():
    txt_params = load_txt()
    comment_lines, data_lines = load_csv()

    # Header
    header = data_lines[0].rstrip('\n').split(',')
    col = {h: i for i, h in enumerate(header)}

    # Parse existing CSV rows (skip header)
    existing_by_name = {}
    rows = []
    for line in data_lines[1:]:
        if not line.strip():
            rows.append(('blank', line))
            continue
        parts = line.rstrip('\n').split(',')
        while len(parts) < len(header):
            parts.append('')
        name = parts[col['Parameter_Name']]
        existing_by_name[name] = len(rows)
        rows.append(('data', parts))

    type_fixes = 0; added = 0
    # Update existing rows
    for _, parts in rows:
        if _ != 'data':
            continue
        name = parts[col['Parameter_Name']]
        if name in txt_params:
            txt_type = txt_params[name]['type']
            csv_type = parts[col['Data_Type']]
            if csv_type != txt_type:
                parts[col['Data_Type']] = txt_type
                type_fixes += 1

    # Add missing mirror params
    new_rows = []
    for name, info in txt_params.items():
        if name not in existing_by_name:
            group_name = GROUP_NAMES.get(info['group_id'], info['group_id'])
            new_row = ['Generic Models', name, info['guid'], info['type'],
                       group_name, 'Instance', info['description'],
                       'False', '', '', 'MULTI', info['user_mod'], '0']
            new_rows.append(new_row)
            added += 1

    # Build output
    shutil.copy2(CSV, CSV.with_suffix('.csv.bak'))
    out_lines = list(comment_lines)
    out_lines.append(','.join(header) + '\n')
    for kind, row in rows:
        if kind == 'blank':
            continue  # drop blank lines
        out_lines.append(','.join(row) + '\n')
    for row in new_rows:
        out_lines.append(','.join(row) + '\n')

    # Update version header
    new_header = f'# v6.8 | 20260620 | +{added} mirror params, {type_fixes} type fixes — Phase 188 native-type + _TXT mirror sync\n'
    out_lines.insert(0, new_header)

    CSV.write_text(''.join(out_lines), encoding='utf-8')
    print(f"Type fixes: {type_fixes}, New mirror rows added: {added}")
    print(f"Total CSV rows now: {len([l for l in out_lines if not l.startswith('#') and l.strip() and not l.startswith('Revit')])}")

if __name__ == '__main__':
    main()
