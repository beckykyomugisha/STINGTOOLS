#!/usr/bin/env python3
"""
fix_label_definitions.py
Rewrite LABEL_DEFINITIONS.json so every 'param' value that references
a native-typed param is changed to the corresponding _TXT mirror.

Rules:
- If param already ends with _TXT or _BOOL: leave alone
- If param ends with _DT: it's a date TEXT param, leave alone
- Otherwise: apply same suffix-replacement logic to get mirror name
  If mirror exists in MR_PARAMETERS.txt: use it
  Else: leave original (no mirror was generated — probably already TEXT)
"""
import json, shutil, re
from pathlib import Path

LABEL_DEF = Path('/home/user/STINGTOOLS/StingTools/Data/LABEL_DEFINITIONS.json')
MR_TXT = Path('/home/user/STINGTOOLS/StingTools/Data/MR_PARAMETERS.txt')

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

def mirror_name(name):
    if name.endswith('_TXT') or name.endswith('_BOOL') or name.endswith('_DT'):
        return name
    for suffix, replacement in SUFFIX_REPLACEMENTS:
        if name.endswith(suffix):
            candidate = name[:-len(suffix)] + replacement
            if candidate != name:
                return candidate
    return name + '_TXT'

def load_mr_param_names():
    names = set()
    for line in MR_TXT.read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('PARAM\t'):
            parts = line.split('\t')
            if len(parts) >= 3:
                names.add(parts[2])
    return names

def remap_params(obj, mr_names, stats):
    if isinstance(obj, dict):
        out = {}
        for k, v in obj.items():
            if k == 'param' and isinstance(v, str):
                new_v = mirror_name(v)
                if new_v != v:
                    if new_v in mr_names:
                        stats['remapped'] += 1
                        out[k] = new_v
                    else:
                        # Mirror doesn't exist — param is already TEXT or date
                        stats['no_mirror'] += 1
                        out[k] = v
                else:
                    stats['unchanged'] += 1
                    out[k] = v
            else:
                out[k] = remap_params(v, mr_names, stats)
        return out
    elif isinstance(obj, list):
        return [remap_params(i, mr_names, stats) for i in obj]
    return obj

def main():
    shutil.copy2(LABEL_DEF, LABEL_DEF.with_suffix('.json.bak'))
    mr_names = load_mr_param_names()
    print(f"MR params loaded: {len(mr_names)}")

    with open(LABEL_DEF, encoding='utf-8') as f:
        data = json.load(f)

    stats = {'remapped': 0, 'no_mirror': 0, 'unchanged': 0}
    data = remap_params(data, mr_names, stats)

    with open(LABEL_DEF, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2, ensure_ascii=False)

    print(f"Remapped: {stats['remapped']}, No mirror (left as-is): {stats['no_mirror']}, "
          f"Unchanged (already _TXT/_BOOL/_DT): {stats['unchanged']}")

if __name__ == '__main__':
    main()
