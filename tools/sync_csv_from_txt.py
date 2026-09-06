#!/usr/bin/env python3
"""
sync_csv_from_txt.py
Sync MR_PARAMETERS.csv from the transformed MR_PARAMETERS.txt:
  1. Update Data_Type for all params whose type changed in TXT
  2. Add rows for new _TXT mirror params
"""
import datetime as _dt
import shutil
import pathlib
from pathlib import Path

# Repo root, discovered from this file's location. The original of this script
# ran in a sandbox and hard-coded /home/user/STINGTOOLS/..., so it could not be
# run anywhere else -- which is why the data it generates sat unregenerated on a
# branch for three months while main's copies of the same files moved on.
ROOT = pathlib.Path(__file__).resolve().parent.parent


TXT = ROOT / 'StingTools/Data/MR_PARAMETERS.txt'
CSV = ROOT / 'StingTools/Data/MR_PARAMETERS.csv'

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
    # New rows are assembled from .txt FIELDS, which have never been through a CSV
    # encoder. A description containing a comma -- "…, e.g. Pr_30" -- written with a
    # bare join produces one field too many and shifts every column after Description
    # one to the left, dropping the last off the end. The file still parses; only the
    # column-count gate can see it.
    #
    # Existing rows above are deliberately NOT re-encoded: they are read as raw lines
    # and split, so their text still carries any quotes it already had, and rejoining
    # reproduces the original bytes. That identity is what makes this script
    # idempotent, and quoting them again would wrap quoted text in a second layer.
    def _csv_field(f):
        f = '' if f is None else str(f)
        if any(c in f for c in (',', '"', '\n', '\r')):
            return '"' + f.replace('"', '""') + '"'
        return f

    for row in new_rows:
        out_lines.append(','.join(_csv_field(c) for c in row) + '\n')

    # Update the version header. REPLACE it, never prepend: the original
    # inserted unconditionally, so a second run stacked a second header reading
    # "+0 mirror params, 0 type fixes" on top of the first, and the script that
    # describes itself as idempotent was not. The date comes from the clock
    # rather than a literal -- the one it carried was three months stale by the
    # time this was next run.
    # It states what the FILE holds, not what this run did. A header carrying
    # the run's delta reads "+5 mirror params" on the first run and "+0" on the
    # second, so the file changes on every regeneration and the script still is
    # not idempotent -- replacing the header instead of stacking it fixed only
    # half of that. What the run did is printed to the console, where a figure
    # that varies per run belongs.
    stamp = _dt.date.today().strftime('%Y%m%d')
    total = len([l for l in out_lines
                 if not l.startswith('#') and l.strip() and not l.startswith('Revit')])
    new_header = (f'# v6.8 | {stamp} | {total} parameter rows'
                  f' — Phase 188 native-type + _TXT mirror sync\n')
    out_lines = [l for l in out_lines if not l.startswith('# v6.8 |')]
    out_lines.insert(0, new_header)

    CSV.write_text(''.join(out_lines), encoding='utf-8')
    print(f"Type fixes: {type_fixes}, New mirror rows added: {added}")
    print(f"Total CSV rows now: {len([l for l in out_lines if not l.startswith('#') and l.strip() and not l.startswith('Revit')])}")

if __name__ == '__main__':
    main()
