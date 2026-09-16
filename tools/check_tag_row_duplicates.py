#!/usr/bin/env python3
"""The same parameter twice on one tag renders the same value twice on the drawing.

WHY THIS EXISTS
===============
Two shapes, both found by sweeping after the binding work, and both invisible in the data
until you ask the right question:

  WITHIN ONE TIER — the door tag carried BLE_DOOR_WIDTH_MM twice, once prefixed "W:" and
  once prefixed "Clear:". One value, two labels, and the second label meant something
  else entirely: clear width is the Approved Document M / BS 8300 dimension, and the row
  published the leaf width under it. A row that is confidently wrong is worse than a blank.

  ACROSS TWO TIERS THAT DISPLAY TOGETHER — 291 rows repeated a tier-2 parameter in
  tier 3. Eight of the twelve presentation modes show tiers 2 and 3 together, so those
  tags printed Description, Manufacturer, Model and ASS_TAG_2 twice, one line under the
  other, on ~75 categories.

Neither throws. Neither is visible in a diff of the JSON. They show up as a drawing that
looks slightly wrong in a way nobody can name.

WHAT THIS CHECKS
================
For every category in LABEL_DEFINITIONS.json:

  1. no parameter appears twice inside one tier
  2. no parameter appears in two tiers that any presentation_mode shows TOGETHER

The second check reads presentation_modes rather than assuming which tiers co-display, so
adding a mode that shows tiers 2 and 5 together automatically extends the check to that
pair. A duplicate across tiers that are NEVER shown together is legitimate — the same
value can appear in a Compact summary and an Audit detail — and is not reported.

USAGE
    python tools/check_tag_row_duplicates.py [--check]
"""
import collections
import io
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LABELS = os.path.join(ROOT, 'StingTools', 'Data', 'LABEL_DEFINITIONS.json')


def main():
    check = '--check' in sys.argv
    d = json.load(io.open(LABELS, encoding='utf-8-sig'))
    cats = d.get('category_labels') or {}
    modes = d.get('presentation_modes') or {}

    if len(cats) < 100 or not modes:
        print('Parsed %d categories and %d presentation modes — too few for this '
              'assertion to mean anything.' % (len(cats), len(modes)))
        return 1

    # Tier pairs that at least one mode shows together.
    co_displayed = set()
    for cfg in modes.values():
        on = sorted(int(k.replace('state_', ''))
                    for k, v in cfg.items() if k.startswith('state_') and v is True)
        for i in range(len(on)):
            for j in range(i + 1, len(on)):
                co_displayed.add((on[i], on[j]))

    within = []
    across = []
    rows_seen = 0

    for cat, v in cats.items():
        if not isinstance(v, dict):
            continue
        tiers = {}
        for t, rows in v.items():
            if not t.startswith('tier_') or not isinstance(rows, list):
                continue
            n = int(t.split('_')[1])
            params = [r.get('param') for r in rows if r and r.get('param')]
            rows_seen += len(params)
            tiers[n] = params

            counts = collections.Counter(params)
            for p, c in counts.items():
                if c > 1:
                    within.append((cat, t, p, c))

        for lo in sorted(tiers):
            for hi in sorted(tiers):
                if hi <= lo or (lo, hi) not in co_displayed:
                    continue
                shared = set(tiers[lo]) & set(tiers[hi])
                for p in sorted(shared):
                    across.append((cat, lo, hi, p))

    print('Tag row duplicates')
    print('  categories                        : %d' % len(cats))
    print('  tag rows inspected                : %d' % rows_seen)
    print('  co-displayed tier pairs           : %d' % len(co_displayed))
    print('  same parameter twice in one tier  : %d' % len(within))
    print('  same parameter in two co-displayed tiers : %d' % len(across))

    for cat, t, p, c in within[:10]:
        print('      WITHIN  %-24s %-8s %-38s x%d' % (cat, t, p, c))
    for cat, lo, hi, p in across[:10]:
        print('      ACROSS  %-24s tier_%d+tier_%d %-34s' % (cat, lo, hi, p))

    total = len(within) + len(across)
    if total:
        print('\nTAG ROW DUPLICATE GATE FAILED — %d duplicate row(s).' % total)
        print('The drawing prints the value twice. Drop the copy in the HIGHER tier —')
        print('every mode that shows the higher tier also shows the lower one, so nothing')
        print('is lost. If the two labels were meant to show DIFFERENT things, the second')
        print('one needs its own parameter: that is how the door "Clear:" row came to')
        print('publish leaf width under an accessibility label.')
        return 1 if check else 0

    print('\nTag row duplicate gate OK — no tag prints the same value twice.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
