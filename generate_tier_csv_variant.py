#!/usr/bin/env python3
"""generate_tier_csv_variant.py — emit preset-populated CSV variants of the
four STING_TAG_CONFIG_v5_0_*.csv files. The existing CSVs already hold the
Handover preset in their T4..T10 rows; this script generates DesignConstruction
(or any other preset from PARAGRAPH_PRESETS.json) as sibling _<preset>.csv
files so the tag-family authoring script can pick a mode per project.

Usage:
    python3 generate_tier_csv_variant.py <preset_key>

Produces:
    StingTools/Data/STING_TAG_CONFIG_v5_0_ARCH_<preset_key>.csv
    StingTools/Data/STING_TAG_CONFIG_v5_0_GEN_<preset_key>.csv
    StingTools/Data/STING_TAG_CONFIG_v5_0_MEP_<preset_key>.csv
    StingTools/Data/STING_TAG_CONFIG_v5_0_STR_<preset_key>.csv
"""
import json
import os
import re
import sys

REPO_ROOT = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(REPO_ROOT, 'StingTools', 'Data')
PRESETS_JSON = os.path.join(DATA, 'PARAGRAPH_PRESETS.json')

SRC = [
    'STING_TAG_CONFIG_v5_0_ARCH.csv',
    'STING_TAG_CONFIG_v5_0_GEN.csv',
    'STING_TAG_CONFIG_v5_0_MEP.csv',
    'STING_TAG_CONFIG_v5_0_STR.csv',
]

TIER_ORDER = ('T4', 'T5', 'T6', 'T7', 'T8', 'T9', 'T10')
ROW_RE = re.compile(r'^\d+,T(?:4|5|6|7|8|9|10),')
FAM_RE = re.compile(r'^Tag Family #\d+:\s', re.M)


def csv_field(v):
    """Quote CSV field if it contains comma, quote, or newline."""
    s = '' if v is None else str(v)
    if '"' in s or ',' in s or '\n' in s:
        return '"' + s.replace('"', '""') + '"'
    return s


def tiers_present_in_block(lines):
    """Scan a family's original rows and return which T4..T10 tiers already
    have rows. We only emit preset rows for those tiers so Annotation/Spatial
    families (which omit most data tiers) keep their reduced structure."""
    present = set()
    for ln in lines:
        m = re.match(r'^\d+,(T(?:4|5|6|7|8|9|10)),', ln)
        if m:
            present.add(m.group(1))
    return present


def build_preset_rows(preset, tiers_present):
    """Emit preset T4..T10 rows for a family, seq 1..N. Skips tiers the
    family did not carry in the source CSV."""
    out = []
    seq = 0
    tiers = preset.get('tiers', {})
    for tier in TIER_ORDER:
        if tier not in tiers_present:
            continue
        label = tiers.get(tier, {}).get('label', '')
        for r in tiers.get(tier, {}).get('rows', []):
            if not r.get('enabled', True):
                continue
            seq += 1
            tier_num = tier[1:]
            param   = r.get('parameter', '')
            prefix  = r.get('prefix', '')
            suffix  = r.get('suffix', '')
            brk     = '✓' if r.get('break') else ''
            style   = r.get('style', 'NOM')
            color   = r.get('color', 'GREY')
            size    = r.get('size', 2.0)
            name    = f'Show {tier} - {label} - {param}' if label else f'Show {tier} - {param}'
            formula = f'if(TAG_PARA_STATE_{tier_num}_BOOL, {param}, "")'
            fields = [
                str(seq), tier, param, prefix, suffix, '0', brk,
                'Common', 'Text', name, formula,
                style, color, str(size), 'None', 'None',
            ]
            out.append(','.join(csv_field(x) for x in fields))
    return out


def regen_csv(src_path, dst_path, preset, preset_key):
    with open(src_path, 'r', encoding='utf-8') as f:
        text = f.read()

    # Split into header block + per-family blocks. Keep header+catalog verbatim.
    family_starts = [m.start() for m in FAM_RE.finditer(text)]
    if not family_starts:
        print(f'  [SKIP] {src_path} — no families')
        return 0

    out_parts = [text[:family_starts[0]]]
    total_rows = 0

    for i, start in enumerate(family_starts):
        end = family_starts[i + 1] if i + 1 < len(family_starts) else len(text)
        block = text[start:end]
        lines = block.split('\n')

        # Mirror the per-family tier pattern of the original (Handover) CSV.
        # Annotation/Spatial families omit most data tiers; physical-default
        # families carry the full 21 rows.
        present = tiers_present_in_block(lines)
        preset_rows = build_preset_rows(preset, present)
        total_rows += len(preset_rows)

        new_lines = []
        inserted = False
        for ln in lines:
            if ROW_RE.match(ln):
                if not inserted:
                    new_lines.extend(preset_rows)
                    inserted = True
                continue  # drop existing T4-T10 row
            new_lines.append(ln)

        # Families that had zero T4-T10 rows stay empty — do NOT inject.
        out_parts.append('\n'.join(new_lines))

    out_text = ''.join(out_parts)
    # Bump schema header to note the variant
    out_text = re.sub(
        r'^(#SCHEMA_VERSION=[^\n]*?)(\n)',
        lambda m: (
            m.group(1)
            + (f',VARIANT={preset_key}' if f'VARIANT={preset_key}' not in m.group(1) else '')
            + m.group(2)
        ),
        out_text, count=1, flags=re.M,
    )

    with open(dst_path, 'w', encoding='utf-8') as f:
        f.write(out_text)
    return total_rows


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    preset_key = sys.argv[1]
    with open(PRESETS_JSON, 'r', encoding='utf-8') as f:
        presets = json.load(f)
    if preset_key not in presets['presets']:
        raise SystemExit(f'Preset "{preset_key}" not in PARAGRAPH_PRESETS.json. '
                         f'Available: {list(presets["presets"])}')
    preset = presets['presets'][preset_key]

    print(f'Preset: {preset_key} ({preset.get("display_name", "")})')
    for name in SRC:
        src = os.path.join(DATA, name)
        if not os.path.exists(src):
            print(f'  [SKIP] {src} — not found')
            continue
        stem, ext = os.path.splitext(name)
        dst = os.path.join(DATA, f'{stem}_{preset_key}{ext}')
        n = regen_csv(src, dst, preset, preset_key)
        print(f'  {name:40s}  ->  {os.path.basename(dst):55s}  ({n} preset rows/family)')


if __name__ == '__main__':
    main()
