#!/usr/bin/env python3
"""A per-element value bound as a TYPE parameter cannot be written per element.

WHY THIS EXISTS
===============
Found in Revit on 2026-09-16, on one door, in about ninety seconds of looking.

A newly placed door tagged as `A-BLD1-Z01-L01-ARC-FIT-DR-` — seven tokens and a trailing
separator where the sequence number belongs. `ASS_SEQ_NUM_TXT` on that door read `-215`,
greyed out in the Properties palette. Greyed means TYPE-scoped, and a type-scoped parameter
is not reachable through `element.get_Parameter()`. So:

    ParameterHelpers.SetString(el, ParamRegistry.SEQ, seq, overwrite: true);   // TagConfig.cs

returned `false` in silence, SEQ was never written, and the tag assembled with an empty
final segment. No exception, no log line, a green build, and a drawing with a broken tag.

It is also wrong on its own terms. A sequence number identifies ONE element. Bound to the
type, every door of `750 x 2000mm` shares a single SEQ and the number cannot do the job it
exists for. The same shape is visible in `ASS_TAG_6_TXT = "- BLD1"` and `ASS_TAG_2_TXT =
"A-DR"` on the same element.

THE RULE, AND WHY IT IS DERIVED RATHER THAN LISTED
==================================================
    If the code writes a parameter PER ELEMENT, that parameter must be bound Instance.

That is not a matter of taste, so it does not need a hand-written list — and a hand-written
list is exactly what failed before. `check_map_target_types.py` shipped naming four helpers
and passed while four others stayed dead; its docstring now says so. A list only ever
checks what its author already knew about.

So the target set is read out of the source: every `ParamRegistry.X` or ALL_CAPS literal
appearing as an argument to a `Map*` / `Write*` / `Set*` call anywhere in the plugin — all
1,656 .cs files, not just the two where the defect happened to surface. Sources do not
match — they are `BuiltInParameter.*` or MixedCase names like
"Fire Rating". A parameter that becomes a write target tomorrow is covered tomorrow.

WHAT THIS DOES NOT CLAIM
========================
It does not say every Type binding is wrong. 17,762 of the 19,614 rows are Type-bound and
most are correct: a door's width genuinely belongs to the door type. This flags only the
intersection — bound Type AND written per element — which is the set that can only ever
fail or stamp one element's value across every sibling.

⚠️  MIGRATION. Editing this CSV does not re-bind an existing project. The binder reads it
when it runs; a model bound by an earlier run keeps its old scope until re-bound. The
project that produced this finding already disagrees with the shipped CSV in both
directions — `ASS_TAG_1_TXT` is Type here and Instance there, `BLE_DOOR_HEAD_HEIGHT_MM` has
no row here at all and is populated there.

USAGE
    python tools/check_binding_scope.py            # report
    python tools/check_binding_scope.py --check    # CI: non-zero when the count rises
    python tools/check_binding_scope.py --fix      # flip the offending rows to Instance
    python tools/check_binding_scope.py --write    # rewrite the baseline (review it)
"""
import collections
import io
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BIND = os.path.join(ROOT, 'StingTools', 'Data', 'CATEGORY_BINDINGS.csv')
REG = os.path.join(ROOT, 'StingTools', 'Data', 'PARAMETER_REGISTRY.json')
BASELINE = os.path.join(ROOT, 'docs', 'BINDING_SCOPE_BASELINE.json')
def source_files():
    """Every plugin .cs file, excluding build output.

    The first version of this gate read TWO files — ParameterHelpers.cs and TagConfig.cs —
    because that is where the defect was found. Widening it to the tree found **188 more
    rows across 41 parameters** in engines it had never looked at: ELC_VLT_DROP_PCT and
    ELC_CBL_SZ_MM (AutoUpsizeWiresCommand), ELC_CKT_CUR_A and HVC_DUCT_FLOWRATE_M3H
    (MepCrossStampOrchestrator), MNT_WARRANTY_EXPIRY_TXT (IoTMaintenanceCommands),
    PLM_RECIRC_PUMP_DUTY_LPM (RecircLoopBalancer), and more.

    That is the third time in one session that an instrument here was narrower than the
    defect it was built for: a hardcoded helper list in Phase 293, a key resolver that read
    only JSON earlier in this phase, and this. Scanning everything costs a second.
    """
    out = []
    for dirpath, dirnames, filenames in os.walk(os.path.join(ROOT, 'StingTools')):
        dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
        for fn in filenames:
            if fn.endswith('.cs'):
                out.append(os.path.join(dirpath, fn))
    return out

CALL = re.compile(
    r'\b(?:Map[A-Za-z]*|Write(?:Mapped|Quantity)|'
    r'Set(?:String|Int|Double|IfEmpty|IfEmptyInt))\s*\(([^;]{0,400}?)\)\s*[;,]', re.S)
TOKEN = re.compile(r'ParamRegistry\.([A-Z0-9_]+)|"([A-Z][A-Z0-9_]{3,})"')


def registry_keys():
    """key -> parameter name, so `ParamRegistry.SEQ` resolves to ASS_SEQ_NUM_TXT."""
    out = {}
    reg = json.load(io.open(REG, encoding='utf-8-sig'))
    for _, items in (reg.get('extended_params') or {}).items():
        for it in items:
            if it.get('key') and it.get('param_name'):
                out[it['key']] = it['param_name']
    for k, v in reg.items():
        if isinstance(v, str) and k.isupper():
            out.setdefault(k, v)

    # The eight ISO tokens resolve through ParamRegistry.TokenParamName(slot), so the
    # key -> name map for SEQ/DISC/LVL/... lives in source_tokens, not extended_params.
    for t in (reg.get('source_tokens') or []):
        if isinstance(t, dict) and t.get('key') and t.get('param_name'):
            out.setdefault(t['key'], t['param_name'])
    for grp in (reg.get('container_groups') or []):
        if isinstance(grp, dict) and grp.get('key') and grp.get('param_name'):
            out.setdefault(grp['key'], grp['param_name'])

    # ...and TAG1 / STATUS / PARA_STATE_n are C# property initialisers, not JSON at all.
    # Missing this half is not hypothetical: the first run of this gate flipped 740 rows
    # and left ASS_SEQ_NUM_TXT and ASS_TAG_1_TXT — the two parameters whose Type binding
    # produced the broken tag that started all of this — untouched, because neither key
    # resolved. A derivation is only as wide as the places it looks.
    reg_cs = os.path.join(ROOT, 'StingTools', 'Core', 'ParamRegistry.cs')
    if os.path.exists(reg_cs):
        src = io.open(reg_cs, encoding='utf-8-sig', errors='replace').read()
        for m in re.finditer(
                r'static string ([A-Z][A-Z0-9_]*)\s*(?:\{[^}]*\}\s*)?=\s*"([A-Z][A-Z0-9_]{3,})"',
                src):
            out.setdefault(m.group(1), m.group(2))
    return out


def write_targets():
    key2name = registry_keys()
    src = ''
    for f in source_files():
        try:
            src += io.open(f, encoding='utf-8-sig', errors='replace').read()
        except Exception:
            continue

    targets = set()
    for m in CALL.finditer(src):
        for t in TOKEN.finditer(m.group(1)):
            name = key2name.get(t.group(1)) if t.group(1) else t.group(2)
            if name:
                targets.add(name)

    if len(targets) < 40:
        raise SystemExit(
            'Derived only %d write targets. That is too few for the assertion to mean '
            'anything — it would pass on almost nothing, which is the failure mode this '
            'tool exists to catch.' % len(targets))
    return targets


def audit():
    targets = write_targets()
    rows = io.open(BIND, encoding='utf-8-sig').read().split('\n')

    offend = collections.Counter()
    total_type = 0
    for raw in rows:
        if not raw.strip() or raw.startswith('#'):
            continue
        c = raw.split(',')
        if len(c) < 3:
            continue
        if c[2].strip() == 'Type':
            total_type += 1
            if c[0].strip() in targets:
                offend[c[0].strip()] += 1
    return targets, offend, total_type, rows


def fix(rows, targets):
    out, n = [], 0
    for raw in rows:
        if not raw.strip() or raw.startswith('#'):
            out.append(raw)
            continue
        c = raw.split(',')
        if len(c) >= 3 and c[2].strip() == 'Type' and c[0].strip() in targets:
            c[2] = 'Instance'
            out.append(','.join(c))
            n += 1
        else:
            out.append(raw)
    return out, n


def main():
    check = '--check' in sys.argv
    do_fix = '--fix' in sys.argv
    write = '--write' in sys.argv

    targets, offend, total_type, rows = audit()
    bad = sum(offend.values())

    print('Binding scope')
    print('  per-element write targets derived : %d' % len(targets))
    print('  Type-bound rows total             : %d' % total_type)
    print('  ...written per element (WRONG)    : %d across %d params'
          % (bad, len(offend)))
    for p, n in offend.most_common(12):
        print('    %-46s %d' % (p, n))

    if do_fix:
        out, n = fix(rows, targets)
        hdr = ('# BINDSCOPE-1 | 20260916 | %d rows Type -> Instance. A parameter the tagging '
               'pipeline writes per element cannot be Type-bound: the write is unreachable '
               'through the instance and returns false in silence (this is why a new door '
               'tagged A-BLD1-Z01-L01-ARC-FIT-DR- with no SEQ), and where it does land it '
               'stamps one element value across every sibling of that type. Derived by '
               'tools/check_binding_scope.py, not hand-listed.' % n)
        io.open(BIND, 'w', encoding='utf-8-sig', newline='').write(
            hdr + '\n' + '\n'.join(out))
        print('\nRewrote %d rows to Instance.' % n)
        print('NOTE: this does not re-bind an existing project. Re-run the binder there.')
        return 0

    if write:
        io.open(BASELINE, 'w', encoding='utf-8', newline='').write(
            json.dumps({'_comment': 'Rows bound Type that the pipeline writes per element. '
                                    'May shrink, never grow. See '
                                    'tools/check_binding_scope.py.',
                        'bad_rows': bad,
                        'params_affected': len(offend)}, indent=2) + '\n')
        print('\nBaseline written: %d bad rows.' % bad)
        return 0

    if not os.path.exists(BASELINE):
        print('\nNo baseline at %s — run with --write.' % BASELINE)
        return 1 if check else 0

    allowed = json.load(io.open(BASELINE, encoding='utf-8-sig')).get('bad_rows', 0)
    print('\n  baseline                          : %d' % allowed)

    if bad > allowed:
        print('\nBINDING SCOPE GATE FAILED — %d new row(s).' % (bad - allowed))
        print('A Type-bound parameter is not reachable through element.get_Parameter(),')
        print('so the per-element write returns false and logs nothing. Where it does')
        print('land, it writes one element\'s value onto every instance of that type.')
        print('Run with --fix, or stop writing that parameter per element.')
        return 1 if check else 0

    if bad < allowed:
        print('\n%d row(s) fixed since the baseline. Re-run with --write to lock it in.'
              % (allowed - bad))
        return 0

    print('\nBinding scope gate OK.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
