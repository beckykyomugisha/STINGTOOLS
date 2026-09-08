# -*- coding: utf-8 -*-
"""Derive the machine-checkable parts of the KUT owner-standards overlay.

WHAT IS DERIVED, AND WHY ONLY THESE
Two rules in project-templates/KUT/_BIM_COORD/owner_standards.json restate the
naming convention, and a restatement drifts:

  sheet-kut-number-pattern.pattern   the container-name regex
  discipline-code-valid.values       the asset discipline codes

Both are already stated in tools/kut_naming.py, which is the single source the
BEP and the Document Control Standard render from. Anything a drafter reads and
anything a gate enforces therefore comes from one table.

Every other field -- descriptions, sources, severities, the Fohlio rule -- is
hand-authored and is copied through untouched. This script edits two values in
place; it does not author the file. That is the same discipline
build_kut_lod_overlay.py applies to the LOD matrix, and for the same reason: a
generator that rewrites a whole hand-authored document loses the parts a human
put there on purpose.

WHY A GENERATED ALTERNATION BEATS A CHARACTER CLASS
The hand-written pattern used [A-Z0-9]{2} for Volume, Level and Type. That is
ISO-shaped but not ISO: it accepts volume 47 and type QQ, neither of which
exists, so an invalid container name passed validation and reached a transmittal
looking checked. An enumerated alternation rejects them, and because it is
generated it cannot fall out of step with the documents that publish the same
lists.

TWO TRAPS THIS ENCODES
1. Level 01 means "first floor, AND UPWARD IN SEQUENCE". Enumerating the six
   literal level codes would reject 02, 03, 04 -- every floor above the first in
   a building that has them. Numeric levels are therefore a \\d{2} class, and the
   non-numeric codes are enumerated beside it.
2. Alternations are ordered. Role codes are emitted longest-first so a two-letter
   code can never be shadowed by a one-letter prefix of itself. No shipped pair
   collides today (FP and LV start with letters that are not roles), which is
   exactly why it would be missed the day one does.

WHAT IS NOT DERIVED: THE ORIGINATOR
It stays [A-Z0-9]{3}. The originator register is Symbion's to issue and has not
been (ROADMAP KUT-OPEN-1); enumerating a guess would reject the real codes the
day they arrive. The LENGTH comes from kut_naming.ORIGINATOR_LENGTH, so the one
part that is decided is still single-sourced.

Usage
    python tools/build_kut_owner_standards.py            # write
    python tools/build_kut_owner_standards.py --check    # verify only; CI-gate ready

Stdlib only. Run from the repository root.
"""
from __future__ import annotations

import io
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kut_naming as N  # noqa: E402

OVERLAY = 'project-templates/KUT/_BIM_COORD/owner_standards.json'

PATTERN_RULE_ID = 'sheet-kut-number-pattern'
DISCIPLINE_RULE_ID = 'discipline-code-valid'


def _alt(codes):
    """An ordered alternation, longest code first.

    Regex alternation is first-match-wins, so a one-letter code listed before a
    two-letter code that starts with it would match the first letter and then
    fail on the separator. Sorting by length removes the class of bug rather
    than the instance.
    """
    return '|'.join(sorted(set(codes), key=lambda c: (-len(c), c)))


def volume_group():
    """The numbering value of each volume. A closed list: the temple site has six
    buildings plus site-wide and all-volumes, and a seventh volume is a project
    decision, not a typo to be waved through."""
    return '(?:%s)' % _alt(n for n, _c, _v, _a in N.VOLUMES)


def level_group():
    """Non-numeric level codes enumerated; numeric floors as a two-digit class.

    See trap 1 in the module docstring -- LEVELS lists 01 as "first floor, and
    upward in sequence", so 02 and above are authorised by the same row.
    """
    literals = [c for c, _m in N.LEVELS if not c.isdigit()]
    return '(?:%s|\\d{2})' % _alt(literals)


def type_group():
    return '(?:%s)' % _alt(N.TYPE_CODES)


def role_group():
    """Container role codes: the element disciplines plus the container-only Z.

    ALL_ROLES, not ROLES: Z appears in a container name and never on an element,
    and a pattern built from ROLES alone would reject every federated and
    multi-discipline container the same convention requires.
    """
    return '(?:%s)' % _alt(c for c, _m in N.ALL_ROLES)


def build_pattern():
    return '^%s-[A-Z0-9]{%d}-%s-%s-%s-%s-\\d{4}$' % (
        N.PROJECT_CODE,
        N.ORIGINATOR_LENGTH,
        volume_group(),
        level_group(),
        type_group(),
        role_group(),
    )


def build_discipline_values():
    """Asset discipline codes: ROLES, deliberately WITHOUT Z.

    BEP 4.2.2 keeps container fields and asset-identifier fields apart. Z is a
    container role only, so listing it here would authorise an element stamped
    with a discipline that does not exist.
    """
    return [c for c, _m in N.ROLES]


# ── self-checks on the generated pattern ────────────────────────────────────

def _sample(volume='01', level='GF', type_code='M3', role='A', originator='SMB', number='0001'):
    return '%s-%s-%s-%s-%s-%s-%s' % (
        N.PROJECT_CODE, originator, volume, level, type_code, role, number)


def verify(pattern):
    """Prove the generated pattern accepts what the convention authorises and
    rejects what it does not. Returns a list of failure strings.

    A generated regex that was never exercised is a generated regex nobody has
    read: the alternation ordering and the numeric-level class are both silent
    when wrong, and both would only surface on a real sheet months from now.
    """
    rx = re.compile(pattern)
    bad = []

    def accepts(name, why):
        if not rx.match(name):
            bad.append('REJECTS %s (%s)' % (name, why))

    def rejects(name, why):
        if rx.match(name):
            bad.append('ACCEPTS %s (%s)' % (name, why))

    # Every authorised value of every field, one field at a time.
    for num, _c, vname, _a in N.VOLUMES:
        accepts(_sample(volume=num), 'volume %s = %s' % (num, vname))
    for code, meaning in N.LEVELS:
        accepts(_sample(level=code), 'level %s = %s' % (code, meaning))
    for code, meaning in N.TYPES:
        accepts(_sample(type_code=code), 'type %s = %s' % (code, meaning))
    for code, meaning in N.ALL_ROLES:
        accepts(_sample(role=code), 'role %s = %s' % (code, meaning))

    # Trap 1: 01 means first floor AND UPWARD.
    for lvl in ('02', '03', '12'):
        accepts(_sample(level=lvl), 'LEVELS row "01 first floor, and upward in sequence"')

    # The example the issued documents publish, in its machine form. build_bep.py
    # renders both a spaced display form and this one; only this one is a
    # container name, and it must satisfy the rule that validates container names.
    accepts(N.example_name().replace(' ', ''), 'the example published in the BEP')

    # What the character-class pattern used to wave through.
    rejects(_sample(volume='47'), 'volume 47 does not exist')
    rejects(_sample(type_code='QQ'), 'type QQ does not exist')
    rejects(_sample(level='Q1'), 'level Q1 does not exist')
    # Q was a non-existent role until the project adopted the BS EN ISO 19650-2 UK NA
    # Table NA.3 set, in which Q is Quantity Surveyor. The negative case has to move to
    # a letter the standard genuinely does not use, or this check passes by being wrong.
    rejects(_sample(role='V'), 'role V is not in the standard set')
    rejects(_sample(number='001'), 'Number is four digits')
    rejects(_sample(number='00001'), 'Number is four digits')
    rejects(_sample(originator='SM'), 'originator is %d characters' % N.ORIGINATOR_LENGTH)
    rejects(_sample() + '-S2', 'suitability is not part of the container name')
    rejects(N.example_name(), 'the SPACED form is presentation, not a container name')

    return bad


# ── the overlay ─────────────────────────────────────────────────────────────

def load(path):
    with io.open(path, encoding='utf-8') as fh:
        return json.load(fh)


def apply_to(doc, pattern, values):
    """Set the two derived fields. Returns a list of (rule id, field, old, new)."""
    changes = []
    for rule in doc.get('rules', []):
        if rule.get('id') == PATTERN_RULE_ID and rule.get('pattern') != pattern:
            changes.append((PATTERN_RULE_ID, 'pattern', rule.get('pattern'), pattern))
            rule['pattern'] = pattern
        if rule.get('id') == DISCIPLINE_RULE_ID and rule.get('values') != values:
            changes.append((DISCIPLINE_RULE_ID, 'values', rule.get('values'), values))
            rule['values'] = values
    return changes


def main(argv):
    check_only = '--check' in argv

    pattern = build_pattern()
    values = build_discipline_values()

    failures = verify(pattern)
    if failures:
        sys.stderr.write('generated pattern failed its own checks:\n  %s\n'
                         % '\n  '.join(failures))
        return 2

    if not os.path.exists(OVERLAY):
        sys.stderr.write('%s not found -- run from the repository root\n' % OVERLAY)
        return 2

    doc = load(OVERLAY)
    ids = {r.get('id') for r in doc.get('rules', [])}
    for required in (PATTERN_RULE_ID, DISCIPLINE_RULE_ID):
        if required not in ids:
            sys.stderr.write('%s has no rule "%s" -- this script edits, it does not author\n'
                             % (OVERLAY, required))
            return 2

    changes = apply_to(doc, pattern, values)

    if check_only:
        if changes:
            sys.stderr.write('%s is STALE against tools/kut_naming.py:\n' % OVERLAY)
            for rid, field, old, new in changes:
                sys.stderr.write('  %s.%s\n    on disk: %s\n    derived: %s\n'
                                 % (rid, field, old, new))
            sys.stderr.write('\nRun: python tools/build_kut_owner_standards.py\n')
            return 1
        print('owner_standards.json is current against tools/kut_naming.py.')
        print('  pattern : %s' % pattern)
        print('  %d self-checks passed on the generated pattern' % _check_count())
        return 0

    with io.open(OVERLAY, 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(doc, fh, indent=2, ensure_ascii=False)
        fh.write('\n')

    if changes:
        print('updated %s' % OVERLAY)
        for rid, field, old, new in changes:
            print('  %s.%s' % (rid, field))
            print('    was: %s' % old)
            print('    now: %s' % new)
    else:
        print('%s already current; rewrote formatting only.' % OVERLAY)
    print('  %d self-checks passed on the generated pattern' % _check_count())
    return 0


def _check_count():
    """How many accept/reject assertions verify() makes -- reported so a shrinking
    check set is visible rather than quiet."""
    return (len(N.VOLUMES) + len(N.LEVELS) + len(N.TYPES) + len(N.ALL_ROLES)
            + 3 + 1 + 9)


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
