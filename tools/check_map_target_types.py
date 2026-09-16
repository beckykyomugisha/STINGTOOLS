#!/usr/bin/env python3
"""A Map* helper must write through WriteMapped, or typed targets silently drop the value.

WHY THIS EXISTS
===============
`NativeParamMapper`'s Map* helpers read a Revit built-in, format it, and write it on. Every
one of them used to end at `SetIfEmpty` -> `SetString`, and `SetString` returns FALSE the
moment the target is not String storage:

    if (p.StorageType != StorageType.String) return false;

28 of the 57 resolvable map targets are declared LENGTH / AREA / NUMBER / CURRENCY in
MR_PARAMETERS.txt. Those 28 writes did nothing, returned 0, logged nothing, and threw
nothing. Among them:

    BLE_DOOR_WIDTH_MM / _HEIGHT_MM      a door tag showed no width and no height
    BLE_WINDOW_WIDTH_MM / _HEIGHT_MM    likewise for windows
    ASS_ROOM_AREA_SQ_M                  a room tag showed no area
    BLE_WALL_THICKNESS_MM, BLE_STAIR_*, PLM_PPE_*, ELC_*, HVC_* ...

And because the `_TXT` display mirror the tag LABEL reads is only written by `SetDouble`,
the mirror was never written either. Two dead parameters per mapping, from one type
mismatch, with no symptom other than a blank line on a drawing.

`WriteMapped` fixes it by branching on the TARGET's storage type, and it carries BOTH the
raw internal value (feet — what a Double target must store) and the formatted display
string (millimetres — what the mirror and any String target must carry). Passing the
converted number to a LENGTH parameter would store 900 FEET for a 900 mm door, so the two
are not interchangeable and the helper takes both.

WHAT THIS CHECKS
================
That every Map* helper still routes through `WriteMapped`, and that none of them has been
"simplified" back to a direct `SetIfEmptyInt`. A unit test cannot catch this: the helpers
need a live Revit `Parameter` to exercise, and the failure is a silent false return, not
an exception. The only cheap instrument is the call site itself.

USAGE
    python tools/check_map_target_types.py [--check]
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'StingTools', 'Core', 'ParameterHelpers.cs')

# Helpers that write a mapped value onto a target parameter. Each must reach WriteMapped.
HELPERS = ['MapDimension', 'MapLookup', 'MapBuiltIn', 'MapStringParam']


def body_of(src, name):
    """Crude but adequate: from the helper's signature to the next helper signature."""
    m = re.search(r'private static int ' + name + r'\s*\(', src)
    if not m:
        return None
    rest = src[m.end():]
    nxt = re.search(r'\n        (?:private|public|internal) static ', rest)
    return rest[:nxt.start()] if nxt else rest


def main():
    check = '--check' in sys.argv
    findings = []

    if not os.path.exists(SRC):
        print('MISSING: ' + SRC)
        return 1
    src = io.open(SRC, encoding='utf-8-sig', errors='replace').read()

    if 'private static int WriteMapped(' not in src:
        findings.append(
            'WriteMapped is gone from ParameterHelpers.cs. Without it every Map* helper '
            'writes a string into whatever the target is, and 28 typed targets drop the '
            'value in silence.')

    if 'displayText' not in src:
        findings.append(
            'SetDouble no longer takes displayText. The _TXT mirror then publishes the '
            'STORED value, which for a LENGTH parameter is decimal feet under a '
            'millimetre label — a wrong number is worse than a blank one.')

    for h in HELPERS:
        body = body_of(src, h)
        if body is None:
            findings.append('%s not found — renamed or removed; this gate no longer '
                            'covers it.' % h)
            continue
        if 'WriteMapped(' not in body:
            findings.append(
                '%s does not call WriteMapped. It writes through SetString, so every '
                'LENGTH / AREA / NUMBER / CURRENCY target it names silently keeps its '
                'old value and the _TXT mirror the tag reads stays empty.' % h)

    print('Map target types')
    print('  helpers checked            : %d' % len(HELPERS))
    print('  WriteMapped call sites     : %d' % src.count('WriteMapped('))

    if findings:
        print('\nMAP TARGET TYPE GATE FAILED — %d finding(s):' % len(findings))
        for f in findings:
            print('  - ' + f)
        print('\nThe symptom of this defect is a blank line on a drawing. It does not')
        print('throw, it does not log, and the build stays green.')
        return 1 if check else 0

    print('\nMap target type gate OK.')
    print('  Every Map* helper routes through WriteMapped, so a typed target gets the')
    print('  number and the _TXT mirror gets the display string.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
