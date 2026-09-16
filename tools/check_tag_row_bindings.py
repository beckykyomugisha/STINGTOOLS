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

SCHEDULES ARE THE SAME SURFACE
==============================
The universal-tag plan moves discipline data OFF the tag and INTO per-category schedules
(SCHEDULE_SPEC_all_disciplines.json, read by ScheduleDisciplineTagExpanderCommand). That
does not escape this defect, it RELOCATES it: a schedule column whose parameter is not
bound to the category is an empty column instead of an empty label line. 117 of the 583
schedule-field parameters were in exactly that state. So this gate checks both surfaces —
otherwise the pivot to schedules would start with 117 blank columns.

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
SCHED = os.path.join(ROOT, 'StingTools', 'Data', 'SCHEDULE_SPEC_all_disciplines.json')
LABELS_FOR_CATS = LABELS


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


def schedule_audit(universal, bound):
    """Schedule columns whose parameter is not bound to the category the schedule is for.

    Category resolution mirrors ScheduleDisciplineTagExpanderCommand: index
    category_labels by the plural display name, the family_name suffix and any
    csv_family_alias, then accept an UNAMBIGUOUS prefix hit for the spec's truncated
    family names. A family that does not resolve is REPORTED, not guessed at — binding a
    parameter to the wrong category is worse than leaving the column empty.
    """
    if not os.path.exists(SCHED):
        return [], ['SCHEDULE_SPEC_all_disciplines.json not found.']

    spec = json.load(io.open(SCHED, encoding='utf-8-sig'))
    labels = (json.load(io.open(LABELS_FOR_CATS, encoding='utf-8-sig'))
              .get('category_labels') or {})

    def strip_family(fam):
        if not fam:
            return None
        f = fam.strip()
        if f.upper().startswith('STING - '):
            f = f[8:]
        if f.upper().endswith(' TAG'):
            f = f[:-4]
        return f.strip()

    fam_to_cat = {}
    for cat, v in labels.items():
        if not isinstance(v, dict):
            continue
        for key in (cat, strip_family(v.get('family_name'))):
            if key:
                fam_to_cat.setdefault(key, cat)
        alias = v.get('csv_family_alias')
        for a in ([alias] if isinstance(alias, str) else (alias or [])):
            if isinstance(a, str):
                fam_to_cat.setdefault(a, cat)

    def resolve(fam):
        if not fam:
            return None
        if fam in fam_to_cat:
            return fam_to_cat[fam]
        hits = {v for k, v in fam_to_cat.items() if k.startswith(fam)}
        return next(iter(hits)) if len(hits) == 1 else None

    KEYS = ('col1_tag', 'col2_desc', 'sheet_columns', 'full_columns')
    dead, unresolved = [], []
    for _, v in spec.items():
        if not isinstance(v, dict):
            continue
        cat = resolve(v.get('family'))
        if cat is None:
            unresolved.append(v.get('family'))
            continue
        cols = set()
        for kk in KEYS:
            val = v.get(kk)
            if isinstance(val, str):
                cols.add(val)
            elif isinstance(val, list):
                cols.update(x for x in val if isinstance(x, str))
        for c in sorted(cols):
            if not (c.isupper() and '_' in c) or c in universal:
                continue
            if cat not in bound.get(c, set()):
                dead.append((cat, c))
    return dead, unresolved


def main():
    check = '--check' in sys.argv
    write = '--write' in sys.argv

    findings, total_rows, n_cats = audit()
    dead = sum(len(v) for v in findings.values())

    sched_dead, sched_unresolved = schedule_audit(load_universal(), load_bindings())
    dead += len(sched_dead)

    print('Tag row bindings')
    print('  categories                  : %d' % n_cats)
    print('  tag rows inspected          : %d' % total_rows)
    print('  rows that can never display : %d  (%.1f%%)'
          % (dead, 100.0 * dead / max(total_rows, 1)))
    print('  categories affected         : %d' % len(findings))
    print('  schedule columns that can never fill : %d' % len(sched_dead))
    for cat, c in sched_dead[:10]:
        print('      %-28s %s' % (cat, c))
    if sched_unresolved:
        print('  spec families that resolve to no category : %d (reported, not guessed)'
              % len(sched_unresolved))
        for f in sched_unresolved[:8]:
            print('      %s' % f)

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
