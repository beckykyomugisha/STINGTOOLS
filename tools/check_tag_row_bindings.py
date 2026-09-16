#!/usr/bin/env python3
"""A tag row whose parameter is not bound to that category can never display anything.

WHY THIS EXISTS
===============
The STING room tag showed a blank where the room name belongs for as long as it has
existed. Not an error, not a warning — a gap. `ASS_DESCRIPTION_TXT` was row 3 of tier 2,
and DESC is populated from a loadable-family built-in that a spatial element does not
have, so the row rendered empty on every room in every project.

Looking for siblings found that this is not one bug. It is a CLASS:

  * a row bound to a parameter that has no binding row for that category at all
    (Rooms' Ht: / Occ: / Vent: / Esc Cap: — the params exist, nothing binds them)
  * a row bound to the WRONG member of a near-duplicate pair
    (Rooms' Floor Fin: read BLE_FLR_FINISH_TXT, the FLOOR's finish, while the room's own
     BLE_ROOM_FINISH_FLOOR_TXT sat bound to Rooms and populated)
  * a row carrying a project-level metric on an element tag
    (HEALTH_SCORE_LAST_TXT, bound to Project Information, in tier 9 of 130 categories)

Every one is invisible. An unbound parameter does not throw: Revit simply has no such
parameter on the element, the label renders empty, and the tag looks like a tag with
nothing to say. `ParamRegistry.cs:522` records the same shape one layer up — the binder
bound a title-block toggle nobody read and left the name every title block reads unbound,
so nine title blocks gated on a parameter that could never resolve.

WHAT THIS CHECKS
================
For every row of every tier of every category in LABEL_DEFINITIONS.json, the row's
parameter must be either

  * universal (bound <ALL> in RESOLVED_BINDINGS.csv), or
  * bound to that category in CATEGORY_BINDINGS.csv

CATEGORY_BINDINGS.csv is the file the binder actually reads
(SharedParamGuids.LoadPerParamCategoryBindings). RESOLVED_BINDINGS.csv is a derived
reference and is used here ONLY for the <ALL> set — an audit built on it alone reports a
different, wrong number, which is how the first draft of this tool came out at 1,021.

RATCHET, NOT A ZERO
===================
There are too many to fix at once, and several need a judgement about which parameter the
row SHOULD name — a judgement this tool cannot make. So it fails only when the count
RISES above the baseline. The baseline may shrink and never grow; a line added to make
this pass removes the only thing it does.

USAGE
    python tools/check_tag_row_bindings.py            # report
    python tools/check_tag_row_bindings.py --check    # CI: non-zero when the count rises
    python tools/check_tag_row_bindings.py --write    # rewrite the baseline (review it)
"""
import collections
import csv
import io
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LABELS = os.path.join(ROOT, 'StingTools', 'Data', 'LABEL_DEFINITIONS.json')
BOUND = os.path.join(ROOT, 'StingTools', 'Data', 'CATEGORY_BINDINGS.csv')
RESOLVED = os.path.join(ROOT, 'StingTools', 'Data', 'RESOLVED_BINDINGS.csv')
BASELINE = os.path.join(ROOT, 'docs', 'TAG_ROW_BINDING_BASELINE.json')


def load_universal():
    """Parameters bound to every category. Only the <ALL> set is taken from here."""
    out = set()
    if not os.path.exists(RESOLVED):
        return out
    for line in io.open(RESOLVED, encoding='utf-8-sig'):
        line = line.strip()
        if not line or line.startswith('#'):
            continue
        parts = line.split(',', 1)
        if len(parts) == 2 and parts[1].strip() == '<ALL>':
            out.add(parts[0].strip())
    return out


def load_bindings():
    """param -> {category display name}. The file the binder reads."""
    out = collections.defaultdict(set)
    for raw in io.open(BOUND, encoding='utf-8-sig'):
        if not raw.strip() or raw.startswith('#'):
            continue
        try:
            cols = next(csv.reader([raw.rstrip('\n')]))
        except Exception:
            continue
        if len(cols) < 2:
            continue
        param, cat = cols[0].strip(), cols[1].strip()
        if not param or not cat or param.lower() == 'parameter_name':
            continue
        out[param].add(cat)
    return out


def audit():
    labels = json.load(io.open(LABELS, encoding='utf-8-sig'))
    cats = labels.get('category_labels') or {}
    universal = load_universal()
    bound = load_bindings()

    if len(cats) < 100 or len(bound) < 100:
        raise SystemExit(
            'Parsed %d categories and %d bound parameters. That is too few for the '
            'assertion to mean anything — it would pass on nothing, which is the failure '
            'mode this tool exists to catch.' % (len(cats), len(bound)))

    findings = collections.defaultdict(list)
    total_rows = 0
    for cat, v in cats.items():
        if not isinstance(v, dict):
            continue
        for tier, rows in v.items():
            if not tier.startswith('tier_') or not isinstance(rows, list):
                continue
            for r in rows:
                p = (r or {}).get('param', '')
                if not p:
                    continue
                total_rows += 1
                if p in universal:
                    continue
                cs = bound.get(p)
                if cs is None:
                    findings[cat].append((tier, p, 'no binding row at all'))
                elif cat not in cs:
                    findings[cat].append(
                        (tier, p, 'bound to ' + '|'.join(sorted(cs))[:48]))
    return findings, total_rows, len(cats)


def main():
    check = '--check' in sys.argv
    write = '--write' in sys.argv

    findings, total_rows, n_cats = audit()
    dead = sum(len(v) for v in findings.values())

    print('Tag row bindings')
    print('  categories                  : %d' % n_cats)
    print('  tag rows inspected          : %d' % total_rows)
    print('  rows that can never display : %d  (%.1f%%)'
          % (dead, 100.0 * dead / max(total_rows, 1)))
    print('  categories affected         : %d' % len(findings))

    by_param = collections.Counter()
    for rows in findings.values():
        for _, p, _why in rows:
            by_param[p] += 1
    print('\n  worst parameters (row count across categories):')
    for p, n in by_param.most_common(10):
        print('    %-46s %d' % (p, n))

    if write:
        io.open(BASELINE, 'w', encoding='utf-8', newline='').write(
            json.dumps({'_comment': 'Tag rows whose parameter is not bound to that '
                                    'category. May shrink, never grow. See '
                                    'tools/check_tag_row_bindings.py.',
                        'dead_rows': dead,
                        'categories_affected': len(findings)},
                       indent=2) + '\n')
        print('\nBaseline written: %d dead rows.' % dead)
        return 0

    if not os.path.exists(BASELINE):
        print('\nNo baseline at %s — run with --write.' % BASELINE)
        return 1 if check else 0

    base = json.load(io.open(BASELINE, encoding='utf-8-sig'))
    allowed = base.get('dead_rows', 0)

    print('\n  baseline                    : %d' % allowed)
    if dead > allowed:
        print('\nTAG ROW BINDING GATE FAILED — %d new dead row(s).' % (dead - allowed))
        print('A tag row whose parameter is not bound to its category renders EMPTY.')
        print('It does not throw and it is not logged; the tag simply has less to say.')
        print('Either bind the parameter to that category in CATEGORY_BINDINGS.csv, or')
        print('point the row at the parameter that IS bound (check for a near-duplicate:')
        print('BLE_FLR_FINISH_TXT vs BLE_ROOM_FINISH_FLOOR_TXT was exactly that).')
        return 1 if check else 0

    if dead < allowed:
        print('\n%d row(s) fixed since the baseline. Re-run with --write to lock it in;'
              % (allowed - dead))
        print('this file may shrink, never grow.')
        return 0

    print('\nTag row binding gate OK — no new dead rows.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
