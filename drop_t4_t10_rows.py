#!/usr/bin/env python3
"""drop_t4_t10_rows.py — strip every T4..T10 label row from the 4
STING_TAG_CONFIG_v5_0_*.csv files. T4..T10 content moves to the
narrative layer (ASS_TAG_7_TXT) composed by ApplyParagraphPresetCommand
from PARAGRAPH_PRESETS.json; the tag family itself only carries T1..T3
label rows plus the warning block.

Preserved verbatim:
  - Header comments / schema banner
  - TAG STYLE PARAMETER CATALOG block
  - Per-family "Tag Family #N:" banner + column header row
  - Every T1 / T2 / T3 row
  - The ⚠ WARNING PARAMETERS block
  - Trailing blank lines

Removed:
  - Any line whose CSV columns start with "<N>,T<4..10>,"
  - "T4 — …" / "T5 — …" / "…" sub-banner lines immediately above those rows
"""
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(REPO_ROOT, 'StingTools', 'Data')

CSVS = [
    'STING_TAG_CONFIG_v5_0_ARCH.csv',
    'STING_TAG_CONFIG_v5_0_GEN.csv',
    'STING_TAG_CONFIG_v5_0_MEP.csv',
    'STING_TAG_CONFIG_v5_0_STR.csv',
]

ROW_RE    = re.compile(r'^\d+,T(?:4|5|6|7|8|9|10),')
BANNER_RE = re.compile(r'^T(?:4|5|6|7|8|9|10)\s+—')


def strip_csv(path):
    with open(path, 'r', encoding='utf-8') as f:
        lines = f.readlines()

    out, dropped = [], 0
    for ln in lines:
        if ROW_RE.match(ln) or BANNER_RE.match(ln):
            dropped += 1
            continue
        out.append(ln)

    # Bump schema note — append ADDS=narrative_only_T4_T10 if not present
    for i, ln in enumerate(out[:5]):
        if ln.startswith('#SCHEMA_VERSION=') and 'narrative_only_T4_T10' not in ln:
            out[i] = ln.rstrip('\n') + ',UPDATE=narrative_only_T4_T10\n'
            break

    with open(path, 'w', encoding='utf-8') as f:
        f.writelines(out)
    return len(lines), len(out), dropped


def main():
    grand_total_dropped = 0
    for name in CSVS:
        path = os.path.join(DATA, name)
        if not os.path.exists(path):
            print(f'  [SKIP] {path} — not found')
            continue
        before, after, dropped = strip_csv(path)
        grand_total_dropped += dropped
        print(f'  {name:40s}  {before:>5} -> {after:>5} lines  (-{dropped})')
    print(f'Total T4-T10 lines dropped: {grand_total_dropped}')


if __name__ == '__main__':
    main()
