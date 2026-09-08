#!/usr/bin/env python3
"""
sync_csv_from_txt.py
Sync MR_PARAMETERS.csv from the transformed MR_PARAMETERS.txt:
  1. Update Data_Type for all params whose type changed in TXT
  2. Update Group_Name for all params whose group changed in TXT
  3. Add rows for new _TXT mirror params

PARAM-1. Step 2 did not exist, and that is the whole defect: the script corrected
Data_Type on an existing row but never Group_Name, so a parameter whose group moved
in the .txt kept the old name in the .csv forever. Twenty-eight rows had drifted.

The .txt is authoritative. On a PARAM line the GROUP field is a group ID, and the
names live in a separate GROUP table (*GROUP ID NAME -> GROUP\t1\tASS_MNG), so the
id must be resolved before anything is compared. Comparing the raw id to the CSV's
Group_Name makes all 3,598 rows look wrong -- which is a broken instrument, not a
finding, and is exactly the false positive this script is here to stop producing.
"""
import io
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


class UndeclaredGroup(Exception):
    """A PARAM line references a GROUP id the GROUP table does not declare."""


def group_name(group_id, param_name):
    """Resolve a group ID to its name, or FAIL.

    The old new-row path did ``GROUP_NAMES.get(id, id)`` -- on an undeclared id it
    silently wrote the NUMBER into the Group_Name column, producing a row that looks
    populated and names a group that does not exist. #758 shipped two parameters in an
    undeclared GROUP 38 exactly that way. A missing group is a data error in the .txt
    and has to be fixed there; guessing is how one wrong fact becomes two files' worth.
    """
    if group_id in GROUP_NAMES:
        return GROUP_NAMES[group_id]
    raise UndeclaredGroup(
        "MR_PARAMETERS.txt: parameter %r references GROUP id %r, which the GROUP table "
        "does not declare. Add a GROUP row for it in the .txt -- this script will not "
        "invent a name, and writing the id into Group_Name would create a group that "
        "does not exist." % (param_name, group_id))

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

    type_fixes = 0; group_fixes = 0; added = 0
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

            # PARAM-1. The .txt is authoritative for the group too. Group names are
            # bare identifiers (BLE_ELES, RGL_CMPL) so this needs no CSV quoting --
            # which matters, because existing rows are deliberately NOT re-encoded
            # (see the note further down); rejoining them must reproduce the original
            # bytes or the script stops being idempotent.
            txt_group = group_name(txt_params[name]['group_id'], name)
            if parts[col['Group_Name']] != txt_group:
                parts[col['Group_Name']] = txt_group
                group_fixes += 1

    # Add missing mirror params
    new_rows = []
    for name, info in txt_params.items():
        if name not in existing_by_name:
            new_row = ['Generic Models', name, info['guid'], info['type'],
                       group_name(info['group_id'], name), 'Instance', info['description'],
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
    # PARAM-1: NO CLOCK IN THE HEADER. The stamp was _dt.date.today(), so the file
    # changed on any day the generator ran even when nothing about its input had
    # moved. That makes 'regenerate and diff' fail spuriously the day after any
    # commit, and a gate that cries wolf is a gate people learn to ignore. It is also
    # the rule this block already states just above: the header says what the FILE
    # HOLDS, not what this run did -- and a date is what the run did. Git records when
    # the file changed, and does it better than a stamp the generator overwrites.
    total = len([l for l in out_lines
                 if not l.startswith('#') and l.strip() and not l.startswith('Revit')])
    new_header = (f'# v6.8 | {total} parameter rows'
                  f' — Phase 188 native-type + _TXT mirror sync\n')
    out_lines = [l for l in out_lines if not l.startswith('# v6.8 |')]
    out_lines.insert(0, new_header)

    # Pin to LF, the way param_binding_resolver.py does. Path.write_text goes
    # through text mode, which translates \n to \r\n on Windows -- so the same
    # script produced different BYTES on different machines, and the CI gate that
    # regenerates on Linux and diffs would fail on line endings alone with the real
    # change invisible underneath. .gitattributes pins the checkout; this pins the
    # writer, which is the root-cause half.
    with io.open(CSV, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(''.join(out_lines))
    print(f"Type fixes: {type_fixes}, Group fixes: {group_fixes}, New mirror rows added: {added}")
    print(f"Total CSV rows now: {len([l for l in out_lines if not l.startswith('#') and l.strip() and not l.startswith('Revit')])}")

if __name__ == '__main__':
    main()
