#!/usr/bin/env python3
"""A sentinel must never double as a real vocabulary code.

WHY THIS EXISTS
===============
GENPH-1, found 2026-09-16 by auditing the tag vocabulary category by category.

`TagConfig._placeholders` is `{ "XX", "ZZ", "GEN", "0000" }` — the values that mean "this
segment was never resolved". `TagHasPlaceholders` looks for them delimited inside a tag,
and `TagIsComplete` fails any tag that contains one.

"GEN" was also a REAL code, in three places at once:

    GetDiscDefaultSysCode:  case "G": return "GEN";      // a real sys code
    SysMap:                 { "GEN", [12 categories] }   // a real sys key
    ProdMap:                { "Generic Models", "GEN" }  // a real product code

So every element in those twelve categories produced a tag containing `-GEN-`, and that tag
could never be judged complete. Nothing errored. The consequences were all silent:

  * `skipComplete` never skipped them, so every run re-derived and rewrote the tag, churning
    the audit trail (`ASS_TAG_PREV_TXT`, `ASS_TAG_MODIFIED_DT`) on every pass
  * the O(1) idempotency guard could never fire for them
  * `ComplianceScan` counted them non-compliant for ever — the dashboard could not reach
    100% no matter what anyone did in the model

The fix keeps the sentinel meaning exactly one thing: "GNL" is the real general sys/func
code, "GM" the Generic Models product code, and "GEN" now only ever means unresolved.

These maps are overridable from `project_config.json`, so a project that prefers different
letters can set them — what it may not do is reuse a sentinel.

WHAT THIS CHECKS
================
  1. No value in DiscMap / SysMap / ProdMap / FuncMap equals a placeholder sentinel.
  2. Every category in DiscMap has a ProdMap entry (otherwise PROD falls back to the
     sentinel and the tag is incomplete for that whole category).
  3. Product codes are code-shaped: non-empty, uppercase, no whitespace, <= 8 chars, and
     free of the tag separator (which would make the code an extra tag segment — TAGPROD-1).

Proven RED before GREEN: it reported `Generic Models -> GEN` on the tree as it stood, and 0
after the rename.

USAGE
    python tools/check_tag_vocabulary.py [--check]
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULTS = os.path.join(ROOT, 'StingTools', 'Core', 'TagConfig.Defaults.cs')
TAGCFG = os.path.join(ROOT, 'StingTools', 'Core', 'TagConfig.cs')

PAIR = re.compile(r'\{\s*"([^"]+)"\s*,\s*"([^"]*)"\s*\}')
LIST = re.compile(r'\{\s*"([^"]+)"\s*,\s*new\s+List<string>\s*\{([^}]*)\}\s*\}')


def block(src, name):
    m = re.search(r'Default' + name + r'\(\)\s*\{(.*?)\n        \}', src, re.S)
    return m.group(1) if m else ''


def placeholders(src):
    m = re.search(r'_placeholders\s*=\s*new\s+HashSet<string>\s*\{([^}]*)\}', src)
    if not m:
        return set()
    return set(x.strip().strip('"') for x in m.group(1).split(',') if x.strip())


def main():
    check = '--check' in sys.argv
    findings = []

    dsrc = io.open(DEFAULTS, encoding='utf-8-sig', errors='replace').read()
    tsrc = io.open(TAGCFG, encoding='utf-8-sig', errors='replace').read()

    ph = placeholders(tsrc)
    if not ph:
        raise SystemExit('Could not parse _placeholders from TagConfig.cs — this gate '
                         'would pass on anything, which is the failure it exists to catch.')

    disc = dict(PAIR.findall(block(dsrc, 'DiscMap')))
    prod = dict(PAIR.findall(block(dsrc, 'ProdMap')))
    func = dict(PAIR.findall(block(dsrc, 'FuncMap')))
    sysm = dict((k, v) for k, v in LIST.findall(block(dsrc, 'SysMap')))

    if len(disc) < 50 or len(prod) < 50:
        raise SystemExit('Parsed %d disc / %d prod entries — too few to assert anything.'
                         % (len(disc), len(prod)))

    # 1. sentinel reuse
    for label, values in (('ProdMap', prod.values()),
                          ('FuncMap', func.values()),
                          ('SysMap key', sysm.keys()),
                          ('DiscMap', disc.values())):
        for v in values:
            if v in ph:
                findings.append(
                    '%s uses "%s", which is a PLACEHOLDER sentinel. Every tag carrying it '
                    'fails TagIsComplete for ever: never skipped, re-written every run, and '
                    'permanently non-compliant in ComplianceScan.' % (label, v))

    for m in re.finditer(r'case\s+"[A-Z]{1,3}":\s*return\s+"([A-Z0-9]+)";', tsrc):
        if m.group(1) in ph:
            findings.append(
                'GetDiscDefaultSysCode returns the sentinel "%s" as a real sys code. Use a '
                'distinct code; the sentinel must only ever mean "unresolved".' % m.group(1))

    # 2. every tagged category needs a product code
    missing = sorted(set(disc) - set(prod))
    for c in missing[:20]:
        findings.append(
            'Category "%s" is tagged (DiscMap) but has no ProdMap entry, so PROD falls back '
            'to the sentinel and every tag in that category is incomplete.' % c)

    # 3. code shape
    for cat, code in sorted(prod.items()):
        why = []
        if not code:
            why.append('empty')
        if '-' in code:
            why.append('contains the tag separator (TAGPROD-1)')
        if code != code.upper():
            why.append('not uppercase')
        if ' ' in code:
            why.append('whitespace')
        if len(code) > 8:
            why.append('longer than 8 characters')
        if why:
            findings.append('Product code "%s" for "%s": %s' % (code, cat, '; '.join(why)))

    print('Tag vocabulary')
    print('  placeholders (sentinels) : %s' % ', '.join(sorted(ph)))
    print('  DiscMap %d  ProdMap %d  SysMap %d  FuncMap %d'
          % (len(disc), len(prod), len(sysm), len(func)))
    print('  categories with no PROD  : %d' % len(missing))

    if findings:
        print('\nTAG VOCABULARY GATE FAILED — %d finding(s):' % len(findings))
        for f in findings:
            print('  - ' + f)
        return 1 if check else 0

    print('\nTag vocabulary gate OK.')
    print('  No sentinel doubles as a real code, so "unresolved" means only that.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
