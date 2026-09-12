#!/usr/bin/env python3
"""QS review sheet for the NRM2 work sections in STING_CSI_MASTERFORMAT_MAP.csv.

WHY THIS EXISTS
The Nrm2 column decides which NRM2 work section a priced BOQ line is billed under.
The division 31 / 32 / 33 rows -- site, civil and external utilities -- carry the code
implied by their CSI division and this file's existing convention. That is a strict
improvement on the category-keyword fallback they had before (which put "Generic
Models", "Topography" and "Pads" in a bucket), but it is not a quantity surveyor's
judgement, and nobody should simulate one: a wrong code here moves money into the wrong
section of an issued tender, quietly, because the number is plausible either way.

So this does not decide anything. It makes the review cheap:

    python tools/qs_nrm2_review.py --export       # write the review sheet
    ...  a QS fills QS_VERDICT and, where it differs, QS_NRM2  ...
    python tools/qs_nrm2_review.py --apply        # fold the answers back in

The point of the round trip is that the QS's answer lands as DATA rather than as a
message somebody has to re-key -- re-keying is where a review's findings get lost or
transcribed wrong.

WHAT --apply WILL AND WILL NOT DO
  * It writes ONLY rows whose QS_VERDICT says change, and only when QS_NRM2 differs.
  * It refuses a code that GuessSectionName (BOQCostManager.cs) does not name. That
    function turns the integer into the heading printed on the bill, so a code it
    does not know prints the raw Revit category and the section silently collects
    nothing. The test used to be "does another row already use it", which made a
    defined-but-unused section unwritable -- codes 3 and 31 were correct, defined,
    and unreachable, because the only way to satisfy the refusal was the apply it
    was blocking.
  * It applies the rows it CAN and names the rows it cannot, exiting non-zero. It
    used to write nothing at all if any row refused, which meant a review holding
    even one unanswerable row could never land any of its answers.
  * It matches on the row id AND re-checks the category and section still agree, so a
    stale sheet fails loudly instead of writing to whatever row moved into that slot.
  * It is idempotent: applying the same reviewed sheet twice changes nothing the
    second time.

Nrm2 is per ROW, not per section. Section 03 30 00 is NRM2 5 for a concrete floor and
14 for a concrete wall; that is why the sheet carries the full rule (category, family
regex, type regex, sys) and not just the section number.
"""
import argparse
import csv
import io
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MAP = ROOT / 'StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv'
# The work-section vocabulary. GuessSectionName here turns the Nrm2 integer into the
# heading printed on the bill, so it decides what codes exist -- see vocabulary().
SECTION_NAMES = ROOT / 'StingTools/BOQ/BOQCostManager.cs'
SHEET = ROOT / 'docs/qs_review/nrm2_site_civil_review.csv'

# The divisions the map header flags as wanting a QS's eye. A parameter rather than a
# constant so the same tool serves a review of any other division later.
DEFAULT_DIVISIONS = ('31', '32', '33')

SHEET_COLS = ['ROW', 'Category', 'FamilyRegex', 'TypeRegex', 'Sys', 'Section', 'Title',
              'Qualifier', 'Current_Nrm2', 'Unit', 'QS_VERDICT', 'QS_NRM2', 'QS_NOTE']

VERDICT_OK = {'ok', 'agree', 'correct', 'y', 'yes'}
VERDICT_CHANGE = {'change', 'wrong', 'no', 'n', 'amend'}


def read_map():
    """Return (raw_lines, header_index, rows) where rows are (line_index, fields)."""
    raw = io.open(MAP, encoding='utf-8-sig').read().splitlines()
    header_i = None
    rows = []
    for i, line in enumerate(raw):
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        if header_i is None:
            header_i = i
            continue
        rows.append((i, line.split(',', 8)))
    if header_i is None:
        sys.exit('no header row found in %s' % MAP)
    return raw, header_i, rows


def division(section):
    s = (section or '').strip()
    return s.split(' ', 1)[0] if s else ''


def rule_identity(f):
    """What makes a map row the row it is: the rule, not its position.

    --export used to carry verdicts over by ROW NUMBER. Row numbers are positions,
    so inserting a rule mid-file silently re-attached every later note to a
    different rule -- a QS's reasoning about kerbs quietly becoming their reasoning
    about turf, with nothing to notice it. (--apply's staleness check would have
    caught the mismatch afterwards, but only after --export had already scrambled
    the sheet.) Identity survives insertion, deletion and reordering; the row number
    is still written to the sheet, because --apply needs a handle to address, but it
    is no longer what carries the answers.

    Six rules in the map are duplicates on these fields, distinguished only by their
    material/phase qualifier -- 111 and 112 are both `Structural Foundations /
    (?i)pile / 31 62 00 / Driven Piles`, one qualified `precast`. Keying on the
    fields alone collapses each pair and hands both rows the last one's note, which
    is the same corruption in a smaller place. The occurrence ordinal separates them
    and is stable under insertion anywhere else in the file.
    """
    return tuple((f[i] or '').strip() if len(f) > i else '' for i in range(6))


def rule_identity_from_sheet(r):
    return tuple((r.get(k) or '').strip()
                 for k in ('Category', 'FamilyRegex', 'TypeRegex', 'Sys', 'Section', 'Title'))


def keyed(identity, counter):
    """identity + how many rows with this identity came before it."""
    counter[identity] = counter.get(identity, -1) + 1
    return identity + (counter[identity],)


def qualifier(f):
    """The material / phase columns. They are what distinguishes an otherwise
    duplicate rule, so the sheet must show them or the reviewer sees two identical
    rows and no way to tell which is which."""
    return ' '.join(p for p in ((f[8] or '').strip() if len(f) > 8 else '',
                                (f[9] or '').strip() if len(f) > 9 else '') if p)


def vocabulary():
    """Every work-section code this project HAS, parsed from the authority.

    `GuessSectionName` in BOQCostManager.cs turns the integer into the heading a
    reader sees on the bill, so it -- not the map -- decides what a real section is.
    A code it does not know prints as the raw Revit category instead.

    This check used to be "does the code appear on another row of the map", which
    made a defined-but-unused section unwritable: the refusal told you to "add it to
    a row that already bills under it first", and the only way to do that was the
    apply this refusal was blocking. Codes 3 (Groundworks) and 31 (Drainage below
    ground) sat in that trap -- correct, defined, and unreachable.

    Usage is still the wrong test in the other direction too: a code becoming unused
    (the last row carrying it gets re-classified) would silently make it unwritable
    again.

    Parsing C# from Python is a real coupling, so it fails LOUDLY. If the function
    is renamed or restructured this exits rather than falling back to the old
    usage-based set -- a quiet fallback here would re-introduce the trap and look
    like the tool working.
    """
    if not SECTION_NAMES.exists():
        sys.exit('cannot find %s -- the work-section vocabulary lives in its '
                 'GuessSectionName' % SECTION_NAMES)
    src = io.open(SECTION_NAMES, encoding='utf-8', errors='replace').read()
    block = re.search(r'GuessSectionName\s*\([^)]*\)\s*\{(.*?)\n        \}', src, re.S)
    if not block:
        sys.exit('could not find GuessSectionName in %s. It is the source of truth for\n'
                 'what a work-section code means; if it moved or was renamed, update\n'
                 'SECTION_NAMES / this parse rather than guessing from map usage.'
                 % SECTION_NAMES.relative_to(ROOT))
    codes = dict(re.findall(r'case\s+"(\d+)"\s*:\s*return\s+"([^"]+)"\s*;', block.group(1)))
    if not codes:
        sys.exit('GuessSectionName parsed but yielded no `case "N": return "..."` codes '
                 'in %s -- the switch shape changed.' % SECTION_NAMES.relative_to(ROOT))
    return codes


def codes_used(rows):
    """Every code the map actually carries. Only used to report the reverse defect:
    a code billed on a row that the vocabulary does not name prints as the raw
    category, so the bill silently loses its heading."""
    return {f[6].strip() for _i, f in rows if len(f) >= 7 and f[6].strip()}


def cmd_export(divisions):
    _raw, _h, rows = read_map()
    picked = [(n, f) for n, (_i, f) in enumerate(rows, start=1)
              if len(f) >= 7 and division(f[4]) in divisions]
    if not picked:
        sys.exit('no rows matched divisions %s' % ', '.join(divisions))

    SHEET.parent.mkdir(parents=True, exist_ok=True)
    existing = {}
    if SHEET.exists():
        # Preserve any verdicts already filled in, so re-exporting after the map
        # changes does not throw away a review in progress.
        with io.open(SHEET, encoding='utf-8-sig', newline='') as fh:
            seen_prev = {}
            for r in csv.DictReader(fh):
                existing[keyed(rule_identity_from_sheet(r), seen_prev)] = r

    kept = 0
    with io.open(SHEET, 'w', encoding='utf-8', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(SHEET_COLS)
        seen_now = {}
        for n, f in picked:
            prev = existing.get(keyed(rule_identity(f), seen_now), {})
            verdict = (prev.get('QS_VERDICT') or '').strip()
            qs_nrm2 = (prev.get('QS_NRM2') or '').strip()
            note = (prev.get('QS_NOTE') or '').strip()
            if verdict:
                kept += 1
            w.writerow([n, f[0], f[1], f[2], f[3], f[4], f[5], qualifier(f),
                        f[6].strip(), (f[7].strip() if len(f) >= 8 else ''),
                        verdict, qs_nrm2, note])

    print('wrote %s' % SHEET.relative_to(ROOT))
    print('  rows for review        : %d  (divisions %s)' % (len(picked), ', '.join(divisions)))
    if kept:
        print('  verdicts carried over  : %d' % kept)
    print()
    print('Fill QS_VERDICT with "ok" or "change". For "change", put the correct work')
    print('section in QS_NRM2. QS_NOTE is free text and is never read back into the map.')
    print('Then: python tools/qs_nrm2_review.py --apply')
    return 0


def cmd_apply(dry_run):
    if not SHEET.exists():
        sys.exit('no review sheet at %s -- run --export first' % SHEET.relative_to(ROOT))

    raw, _h, rows = read_map()
    by_n = {n: (i, f) for n, (i, f) in enumerate(rows, start=1)}
    known = vocabulary()
    orphaned = sorted(codes_used(rows) - set(known), key=lambda c: (len(c), c))

    with io.open(SHEET, encoding='utf-8-sig', newline='') as fh:
        review = list(csv.DictReader(fh))

    changes, problems, unreviewed, agreed = [], [], 0, 0
    for r in review:
        rid = (r.get('ROW') or '').strip()
        verdict = (r.get('QS_VERDICT') or '').strip().lower()
        if not verdict:
            unreviewed += 1
            continue
        if verdict in VERDICT_OK:
            agreed += 1
            continue
        if verdict not in VERDICT_CHANGE:
            problems.append('row %s: QS_VERDICT %r is neither an agreement nor a change' % (rid, r.get('QS_VERDICT')))
            continue

        if not rid.isdigit() or int(rid) not in by_n:
            problems.append('row %s: no such row in the map -- re-export, the sheet is stale' % rid)
            continue
        i, f = by_n[int(rid)]

        # A stale sheet must fail loudly rather than write to whatever row moved here.
        if f[0].strip() != (r.get('Category') or '').strip() or f[4].strip() != (r.get('Section') or '').strip():
            problems.append('row %s: sheet says %s / %s but the map now has %s / %s -- re-export'
                            % (rid, r.get('Category'), r.get('Section'), f[0], f[4]))
            continue

        new = (r.get('QS_NRM2') or '').strip()
        if not new:
            problems.append('row %s (%s / %s): marked for change but QS_NRM2 is empty' % (rid, f[0], f[4]))
            continue
        if new == f[6].strip():
            agreed += 1   # marked change, gave the same code back
            continue
        if new not in known:
            problems.append('row %s (%s / %s): %r is not a work section this project has. '
                            'GuessSectionName in %s knows %s. A code it does not know prints '
                            'the raw category instead of a heading, so the section silently '
                            'collects nothing -- add it there first if it is genuinely new.'
                            % (rid, f[0], f[4], new, SECTION_NAMES.name,
                               ', '.join(sorted(known, key=int))))
            continue
        changes.append((i, f, new))

    for p in problems:
        print('REFUSED  ' + p)
    if orphaned:
        print('WARNING  the map bills under %s, which GuessSectionName does not name -- '
              'those rows print the raw Revit category instead of a section heading.'
              % ', '.join(orphaned))
    print()
    print('reviewed rows   : %d agreed, %d to change, %d refused, %d still unreviewed'
          % (agreed, len(changes), len(problems), unreviewed))

    if not changes:
        print('\nNothing to apply.' if not problems
              else '\nNothing written -- every row for change was refused.')
        return 1 if problems else 0

    for i, f, new in changes:
        print('  %-26s %-12s %s -> %s' % (f[0], f[4], f[6].strip(), new))
    if dry_run:
        print('\n--dry-run: nothing written.')
        return 1 if problems else 0

    for i, f, new in changes:
        parts = raw[i].split(',', 8)
        parts[6] = new
        raw[i] = ','.join(parts)
    io.open(MAP, 'w', encoding='utf-8', newline='\n').write('\n'.join(raw) + '\n')
    print('\napplied %d change(s) to %s' % (len(changes), MAP.relative_to(ROOT)))

    # A refused row is a row still carrying its ORIGINAL code, which is exactly the
    # code the review says is wrong. Previously any refusal blocked the whole apply,
    # which made the tool unusable on a real review: a review normally has rows that
    # cannot be answered yet, and holding the answerable ones hostage to them meant
    # nothing ever landed. Rows are independent -- there is no cross-row invariant to
    # protect -- so the answerable ones apply and the rest are named. The non-zero
    # exit keeps a partial apply from reading as a finished one.
    if problems:
        print('%d row(s) were REFUSED and still carry the code the review rejects. '
              'They are listed above.' % len(problems))
    print('Re-run --export so the sheet shows the new current values.')
    return 1 if problems else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument('--export', action='store_true', help='write the review sheet')
    g.add_argument('--apply', action='store_true', help='fold reviewed verdicts back into the map')
    ap.add_argument('--dry-run', action='store_true', help='with --apply, report without writing')
    ap.add_argument('--divisions', default=','.join(DEFAULT_DIVISIONS),
                    help='CSI divisions to review (default: %(default)s)')
    a = ap.parse_args()

    divisions = tuple(d.strip() for d in a.divisions.split(',') if d.strip())
    return cmd_export(divisions) if a.export else cmd_apply(a.dry_run)


if __name__ == '__main__':
    sys.exit(main())
