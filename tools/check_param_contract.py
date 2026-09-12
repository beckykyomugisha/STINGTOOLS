#!/usr/bin/env python3
"""Every bound shared parameter needs a producer and a consumer, or a reason it does not.

WHY THIS EXISTS
---------------
The same defect has been found by hand, on a delivered model, months late, six times
this quarter: a parameter is written and nothing reads it, or read and nothing writes
it, and nothing anywhere says so.

    MAT_CODE          written from a column->parameter table, read nowhere until #930
    MAT_COST_UGX/USD  written from the same table, read nowhere at all - 1,279 supplier
                      unit prices sitting on materials while the BOQ priced from 43
                      category rates
    BLE_APP-*         24 appearance columns written from the register, read nowhere
    RateProviders     "Pass C" wired end to end, sharing Pass B's keyspace, unable to fire

Producer and consumer were never introduced, and no gate noticed.

WHY IT IS DECLARATION-DRIVEN AND NOT DERIVED
--------------------------------------------
Three successive instruments tried to DERIVE the answer and all three were wrong:

    1. string literals only                -> 35 both / 140 write-only / 92 read-only
    2. + 470 ParamRegistry constants       -> 41 both / 148 write-only / 100 read-only
    3. ... and ASS_TAG_1_TXT STILL reports 13 reads and 0 writes

It is written by `ParamRegistry.WriteContainers(el, tokenValues, categoryName)`, which
takes an array of VALUES and resolves the target parameters internally. No parameter
name appears at the call site in any form a scan can follow.

A derived census will always be wrong here, in both directions, and a gate built on a
wrong census either blocks good work or waves bad work through. So this tool does NOT
publish a count as truth. It computes what it can see, compares the SET against a
checked-in baseline, and fails only when a parameter's situation CHANGES without anyone
recording why.

It also cannot see the readers that matter most for some parameters: a Revit schedule,
an IFC property set, a COBie sheet and a human reading the properties palette are all
real consumers, and none of them is C#. That is precisely why the baseline carries a
role and a reason per entry rather than a number.

USAGE
    python tools/check_param_contract.py           # report
    python tools/check_param_contract.py --check   # CI: non-zero when the set drifts
    python tools/check_param_contract.py --write   # rewrite the baseline (review the diff)
"""
import csv
import io
import json
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BASELINE = os.path.join(ROOT, 'docs', 'PARAM_CONTRACT_BASELINE.json')
BINDINGS = os.path.join(ROOT, 'StingTools', 'Data', 'CATEGORY_BINDINGS.csv')
REGISTRY = os.path.join(ROOT, 'StingTools', 'Core', 'ParamRegistry.cs')

WRITE = re.compile(r'\b(SetString|SetInt|SetDouble|SetIfEmpty|SetElementId)\s*\(')
READ = re.compile(r'\b(GetString|GetInt|GetDouble|GetElementId|LookupParameter'
                  r'|AsString|AsDouble|AsInteger|AsValueString)\b')
TABLE_ROW = re.compile(r'^\s*\(\s*Col\w+\s*,')

ROLES = ('pipeline', 'export', 'input', 'reference', 'unresolved')


def read_text(path):
    with io.open(path, encoding='utf-8', errors='replace') as fh:
        return fh.read()


def cs_files():
    out = subprocess.run(['git', 'ls-files', 'StingTools'], capture_output=True, cwd=ROOT)
    names = out.stdout.decode('utf-8', 'replace').split('\n')
    return [n for n in names if n.endswith('.cs') and '/obj/' not in n]


def bound_params():
    names = []
    with io.open(BINDINGS, encoding='utf-8-sig') as fh:
        for row in csv.reader(fh):
            if row and row[0] and not row[0].startswith('#') and row[0] != 'Parameter':
                names.append(row[0].strip())
    return sorted(set(n for n in names if n))


def constant_map():
    """ParamRegistry.FOO -> "ASS_FOO_TXT", so a write through a constant is visible."""
    text = read_text(REGISTRY)
    pairs = re.findall(r'public\s+const\s+string\s+(\w+)\s*=\s*"([A-Za-z0-9_\-]+)"', text)
    by_param = {}
    for const, param in pairs:
        by_param.setdefault(param, []).append(const)
    return by_param


def classify():
    by_param = constant_map()
    sources = {}
    for f in cs_files():
        if f.endswith('ParamRegistry.cs'):
            continue          # the declaration site is not a use site
        sources[f] = read_text(os.path.join(ROOT, f))

    result = {}
    for p in bound_params():
        needles = ['"%s"' % p] + ['ParamRegistry.%s' % c for c in by_param.get(p, [])]
        writes = reads = 0
        for text in sources.values():
            for needle in needles:
                if needle not in text:
                    continue
                for m in re.finditer(re.escape(needle), text):
                    ls = text.rfind('\n', 0, m.start()) + 1
                    le = text.find('\n', m.end())
                    line = text[ls:le if le > 0 else len(text)]
                    ctx = text[max(0, m.start() - 100):m.end() + 40]
                    if WRITE.search(ctx) or TABLE_ROW.match(line):
                        writes += 1
                    elif READ.search(ctx):
                        reads += 1
        if writes and reads:
            state = 'both'
        elif writes:
            state = 'write_only'
        elif reads:
            state = 'read_only'
        else:
            state = 'no_cs_site'
        result[p] = state
    return result


def load_baseline():
    if not os.path.exists(BASELINE):
        return None
    return json.loads(read_text(BASELINE))


def main():
    check = '--check' in sys.argv
    write = '--write' in sys.argv

    state = classify()
    counts = {}
    for s in state.values():
        counts[s] = counts.get(s, 0) + 1

    print('bound parameters        : %d' % len(state))
    print('  written AND read      : %d' % counts.get('both', 0))
    print('  WRITTEN, NEVER READ   : %d' % counts.get('write_only', 0))
    print('  READ, NEVER WRITTEN   : %d' % counts.get('read_only', 0))
    print('  no C# site at all     : %d' % counts.get('no_cs_site', 0))
    print()
    print('These counts are NOT a defect tally. A Revit schedule, an IFC property set and')
    print('a human reading the properties palette are all real consumers this cannot see.')
    print('What is gated is the SET changing, not the numbers.')

    tracked = {p: s for p, s in state.items() if s in ('write_only', 'read_only')}

    if write:
        old = (load_baseline() or {}).get('params', {})
        params = {}
        for p in sorted(tracked):
            prev = old.get(p, {})
            params[p] = {
                'state': tracked[p],
                'role': prev.get('role', 'unresolved'),
                'reason': prev.get('reason', ''),
            }
        doc = {
            'schemaVersion': '1.0',
            '_comment': [
                'One entry per bound parameter this scan can see written but not read, or',
                'read but not written. Generated by tools/check_param_contract.py --write.',
                '',
                'role:',
                '  pipeline  - StingTools writes it and StingTools reads it. Both must exist.',
                '  export    - StingTools writes it; an IFC pset, COBie sheet or Revit schedule',
                '              consumes it. Name the consumer in reason.',
                '  input     - a human fills it in; StingTools reads it. No writer expected.',
                '  reference - carried for a reader outside this codebase, or for the model',
                '              author to see. No code path expected either way.',
                '  unresolved- nobody has decided yet. Allowed in the baseline, never for a',
                '              parameter added after this gate landed.',
                '',
                'Adding a parameter to this file is not a fix. It is a decision, recorded.',
            ],
            'params': params,
        }
        with io.open(BASELINE, 'w', encoding='utf-8', newline='\n') as fh:
            json.dump(doc, fh, indent=2, ensure_ascii=False)
            fh.write('\n')
        print()
        print('wrote %s with %d entries' % (os.path.relpath(BASELINE, ROOT), len(params)))
        return 0

    base = load_baseline()
    if base is None:
        print()
        print('No baseline yet. Run with --write.')
        return 1 if check else 0

    known = base.get('params', {})
    new = sorted(p for p in tracked if p not in known)
    gone = sorted(p for p in known if p not in tracked)
    moved = sorted(p for p in tracked if p in known
                   and known[p].get('state') != tracked[p])

    unresolved = sorted(p for p, v in known.items() if v.get('role') == 'unresolved')
    print()
    print('baseline entries        : %d  (%d still unresolved)' % (len(known), len(unresolved)))

    if not (new or gone or moved):
        print('Parameter contract OK - the set matches the baseline.')
        return 0

    print()
    if new:
        print('NEW - a parameter now has a producer with no consumer, or the reverse:')
        for p in new:
            print('  %-44s %s' % (p, tracked[p]))
        print('  Decide what it is for, then record it: python tools/check_param_contract.py --write')
    if moved:
        print('CHANGED state:')
        for p in moved:
            print('  %-44s %s -> %s' % (p, known[p].get('state'), tracked[p]))
    if gone:
        print('RESOLVED - no longer one-sided, remove from the baseline:')
        for p in gone:
            print('  %-44s was %s' % (p, known[p].get('state')))

    return 1 if check else 0


if __name__ == '__main__':
    sys.exit(main())
