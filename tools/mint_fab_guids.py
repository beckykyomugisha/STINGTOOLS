#!/usr/bin/env python3
"""
Regenerate the 46 UUIDv5 GUIDs used by StingTools/Core/Fabrication/
FabricationParamsV4.cs and StingTools/Data/Parameters/STING_PARAMS_V4.txt.

All v4 fabrication / LPS / cost parameters hash their shared-parameter
name under a single fixed namespace:

    STING_FAB_NS = 7f9f5e3a-a7c0-b2e4-4d91-4a557c5e3a00
    guid(name)   = uuid5(STING_FAB_NS, name)

As long as the namespace and parameter name are stable, the GUID is
stable — which means rebuilding the plugin does not change binding
on family-library round-trips.

Usage:
    python3 tools/mint_fab_guids.py                     # print all
    python3 tools/mint_fab_guids.py --check             # verify .txt rows
    python3 tools/mint_fab_guids.py --name ASS_WELD_COUNT_NR
"""
import sys, uuid, argparse, pathlib, re

STING_FAB_NS = uuid.UUID('7f9f5e3a-a7c0-b2e4-4d91-4a557c5e3a00')
ROOT = pathlib.Path(__file__).resolve().parent.parent
TXT  = ROOT / 'StingTools' / 'Data' / 'Parameters' / 'STING_PARAMS_V4.txt'

def guid_for(name: str) -> uuid.UUID:
    return uuid.uuid5(STING_FAB_NS, name)

def parse_txt_rows():
    rows = []
    for line in TXT.read_text().splitlines():
        if not line.startswith('PARAM\t'): continue
        parts = line.split('\t')
        if len(parts) < 3: continue
        rows.append((parts[1].strip(), parts[2].strip()))
    return rows

def check():
    mismatches = 0
    for guid_str, name in parse_txt_rows():
        if name.startswith('ASS_PLACE_') or name.startswith('ELC_CPC_') \
           or name.startswith('PLM_PPE_') or name == 'PLM_SLOPE_PCT':
            # Section 3 rows use their own project-authored GUIDs —
            # not part of the 46 UUIDv5 set.
            continue
        expected = str(guid_for(name))
        if guid_str.lower() != expected.lower():
            print(f"MISMATCH: {name}\n  .txt:      {guid_str}\n  expected:  {expected}")
            mismatches += 1
    if mismatches == 0:
        print(f"OK — all v4 fabrication GUIDs match uuid5(STING_FAB_NS, name).")
    return mismatches

def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--check', action='store_true',
                    help='verify the .txt file against the hash')
    ap.add_argument('--name',
                    help='print GUID for a single parameter name')
    args = ap.parse_args()
    if args.name:
        print(f"{args.name} → {guid_for(args.name)}")
        return 0
    if args.check:
        return check()
    # Default: print all rows.
    for _, name in parse_txt_rows():
        print(f"{name}\t{guid_for(name)}")
    return 0

if __name__ == '__main__':
    sys.exit(main())
