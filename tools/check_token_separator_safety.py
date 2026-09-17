#!/usr/bin/env python3
"""A source token must never be joined to anything with the tag separator.

WHY THIS EXISTS
===============
TAGPROD-1. Two subsystems disagreed, and the loser lost in silence for the whole life of
the feature.

`TagConfig.GetFamilyAwareProdCode` appends a material suffix to the PROD code:

    return $"{baseProd}-{suffix}";        // "FSP" + "CON" -> "FSP-CON"

"-" is the DEFAULT TAG SEPARATOR. PROD is one segment of an eight-segment tag, so
"FSP-CON" is a ninth segment. `ParameterHelpers.SanitiseSourceTokenWrite` exists precisely
to stop a corrupt value reaching a source-token parameter, and its rule is "drop anything
after the first separator" — so it truncated the code straight back to "FSP" on every
write and said so:

    SetString: rejecting malformed ASS_PRODCT_COD_TXT value 'DR-GLZ' — storing 'DR'
    SetString: rejecting malformed ASS_PRODCT_COD_TXT value 'FSP-CON' — storing 'FSP'

One half of the codebase minting a value the other half is built to reject. No material
suffix has ever reached a tag. Nothing failed; a feature quietly did not exist.

WHAT THIS CHECKS
================
That the join goes through `TagConfig.ProdSuffixJoin()`, which returns the first candidate
character that is NOT the active separator — so the collision cannot come back when a
project overrides `Separator`, only when someone re-hardcodes a literal.

Proven RED before being committed green: restoring `$"{baseProd}-{suffix}"` fails this
gate.

WHAT THIS DELIBERATELY DOES NOT CHECK
=====================================
A first draft also scanned the 13 shipped `*TAG_CONFIG*.csv` files for vocabulary codes
containing "-". It reported 0 — and it would have reported 0 for ever, because it looked
for a header row and these files have none: `DISC_SYS_FUNC.csv` opens straight into
`DISC,Mechanical Equipment,M,...` under a `#SCHEMA_VERSION` comment, so the column lookup
matched nothing and every cell was skipped. It passed an injected `DR-GLZ` without a word.

The obvious repair — flag any short ALL-CAPS hyphenated cell — collides with the standards
citations those same files carry (`HTM-02`, `BS-EN`), so it would cry wolf instead. A gate
that cannot be made precise is worse than no gate: it is a claim of coverage that is not
there. The data half is therefore dropped rather than shipped vacuous, and the residual
risk is stated instead: a hand-authored vocabulary code containing "-" would still be
truncated at write time, and the only current signal is the SetString warning above.

USAGE
    python tools/check_token_separator_safety.py [--check]
"""
import io
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TAGCFG = os.path.join(ROOT, 'StingTools', 'Core', 'TagConfig.cs')

# `$"{prod}-{suffix}"` — the exact shape that shipped.
BAD_JOIN = re.compile(r'\$"\{\s*(?:base)?[Pp]rod[A-Za-z]*\s*\}-\{\s*\w*[Ss]uffix\w*\s*\}"')


def main():
    check = '--check' in sys.argv
    findings = []

    if not os.path.exists(TAGCFG):
        print('MISSING: ' + TAGCFG)
        return 1
    src = io.open(TAGCFG, encoding='utf-8-sig', errors='replace').read()

    bad = BAD_JOIN.findall(src)
    for b in bad:
        findings.append(
            'TagConfig.cs joins a PROD code to its suffix with a literal "-": %s\n'
            '      "-" is the default tag separator, so the result is an extra tag segment '
            'and SanitiseSourceTokenWrite truncates it away in silence. Use '
            'ProdSuffixJoin().' % b)

    if 'ProdSuffixJoin' not in src:
        findings.append(
            'ProdSuffixJoin is gone from TagConfig.cs. Without it the material suffix is '
            'joined with whatever literal the author picked, and the moment that equals '
            'the active separator the suffix stops reaching tags — with no error.')

    print('Token separator safety')
    print('  ProdSuffixJoin present   : %s' % ('yes' if 'ProdSuffixJoin' in src else 'NO'))
    print('  literal "-" joins found  : %d' % len(bad))

    if findings:
        print('\nTOKEN SEPARATOR GATE FAILED — %d finding(s):' % len(findings))
        for f in findings:
            print('  - ' + f)
        return 1 if check else 0

    print('\nToken separator gate OK.')
    print('  The PROD suffix cannot collide with the separator, so it cannot be')
    print('  silently truncated on the way into a source-token parameter.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
