#!/usr/bin/env python3
"""Remove the 43 placeholder WARN_* duplicates from MR_PARAMETERS.txt."""
import re
import sys
from collections import defaultdict
from pathlib import Path

PATH = Path('MR_PARAMETERS.txt')
BACKUP = Path('MR_PARAMETERS.txt.predeupe.bak')
PLACEHOLDER_PREFIXES = ('a1b2c3d4-', 'a2b3c4d5-', 'b3c4d5e6-')

raw = PATH.read_bytes()
bom = b''
if raw.startswith(b'\xef\xbb\xbf'):
    bom = b'\xef\xbb\xbf'
    raw = raw[3:]
text = raw.decode('utf-8')
use_crlf = '\r\n' in text
BACKUP.write_bytes(bom + text.encode('utf-8'))
lines = text.split('\r\n') if use_crlf else text.split('\n')

param_index = defaultdict(list)
for i, ln in enumerate(lines):
    if ln.startswith('PARAM\t'):
        parts = ln.split('\t')
        if len(parts) >= 6:
            param_index[parts[2]].append((i, parts))

indices_to_remove = set()
removed_count = 0
errors = []
for name, entries in param_index.items():
    if len(entries) < 2:
        continue
    placeholders = []
    proper = []
    for idx, parts in entries:
        guid_lower = parts[1].lower()
        group_id = parts[5]
        is_placeholder = any(guid_lower.startswith(p) for p in PLACEHOLDER_PREFIXES) and group_id == '17'
        if is_placeholder:
            placeholders.append((idx, parts))
        else:
            proper.append((idx, parts))
    if len(placeholders) != 1 or len(proper) != 1:
        errors.append((name, len(placeholders), len(proper)))
        continue
    indices_to_remove.add(placeholders[0][0])
    removed_count += 1

if errors:
    print('ERROR: duplicates that do not match the expected pattern:')
    for name, p, r in errors:
        print(f'  {name}: {p} placeholders, {r} proper')
    sys.exit(1)
if removed_count != 43:
    print(f'ERROR: expected to remove 43 rows but found {removed_count}.')
    sys.exit(1)

out_lines = [ln for i, ln in enumerate(lines) if i not in indices_to_remove]
new_text = ('\r\n' if use_crlf else '\n').join(out_lines)
if text.endswith('\r\n' if use_crlf else '\n') and not new_text.endswith('\r\n' if use_crlf else '\n'):
    new_text += '\r\n' if use_crlf else '\n'

result_params = [ln for ln in out_lines if ln.startswith('PARAM\t')]
result_names = [ln.split('\t')[2] for ln in result_params if len(ln.split('\t')) >= 3]
result_duplicates = {n for n in result_names if result_names.count(n) > 1}
if result_duplicates:
    print(f'ERROR: {len(result_duplicates)} duplicates remain:')
    for n in sorted(result_duplicates)[:10]:
        print(f'  {n}')
    sys.exit(1)
expected = 3256 - 43
if len(result_params) != expected:
    print(f'ERROR: expected {expected} PARAM rows, got {len(result_params)}')
    sys.exit(1)

GUID_RE = re.compile(r'^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$', re.I)
malformed = [ln.split('\t')[2] for ln in result_params if not GUID_RE.match(ln.split('\t')[1])]
if malformed:
    print(f'ERROR: malformed GUIDs: {malformed[:5]}')
    sys.exit(1)

group_ids = {ln.split('\t')[1] for ln in out_lines if ln.startswith('GROUP\t')}
orphan = [ln.split('\t')[2] for ln in result_params if ln.split('\t')[5] not in group_ids]
if orphan:
    print(f'ERROR: orphan group refs: {orphan[:5]}')
    sys.exit(1)

PATH.write_bytes(bom + new_text.encode('utf-8'))
print(f'Removed {removed_count} placeholder PARAM rows')
print(f'Result: {len(result_params)} unique PARAM rows, {len(group_ids)} GROUPs, 0 duplicates')
print(f'Backup saved to {BACKUP}')
