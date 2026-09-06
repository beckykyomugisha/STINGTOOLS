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
  * It refuses an NRM2 code that appears nowhere else in the map. A typo produces a
    section that silently collects nothing, which is the failure this file keeps
    finding; better to reject it and say so.
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
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MAP = ROOT / 'StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv'
SHEET = ROOT / 'docs/qs_review/nrm2_site_civil_review.csv'

# The divisions the map header flags as wanting a QS's eye. A parameter rather than a
# constant so the same tool serves a review of any other division later.
DEFAULT_DIVISIONS = ('31', '32', '33')

SHEET_COLS = ['ROW', 'Category', 'FamilyRegex', 'TypeRegex', 'Sys', 'Section', 'Title',
              'Current_Nrm2', 'Unit', 'QS_VERDICT', 'QS_NRM2', 'QS_NOTE']

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


def known_nrm2_codes(rows):
    """Every NRM2 code the map already uses. An answer outside this set is far more
    likely a typo than a section this project has never billed under."""
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
            for r in csv.DictReader(fh):
                existing[r.get('ROW', '')] = r

    kept = 0
    with io.open(SHEET, 'w', encoding='utf-8', newline='') as fh:
        w = csv.writer(fh)
        w.writerow(SHEET_COLS)
        for n, f in picked:
            prev = existing.get(str(n), {})
            verdict = (prev.get('QS_VERDICT') or '').strip()
            qs_nrm2 = (prev.get('QS_NRM2') or '').strip()
            note = (prev.get('QS_NOTE') or '').strip()
            if verdict:
                kept += 1
            w.writerow([n, f[0], f[1], f[2], f[3], f[4], f[5],
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
    known = known_nrm2_codes(rows)

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
            problems.append('row %s (%s / %s): NRM2 %r appears nowhere else in the map. If it is '
                            'genuinely a new work section, add it to a row that already bills under '
                            'it first, so a typo cannot create a section that collects nothing.'
                            % (rid, f[0], f[4], new))
            continue
        changes.append((i, f, new))

    for p in problems:
        print('REFUSED  ' + p)
    print()
    print('reviewed rows   : %d agreed, %d to change, %d refused, %d still unreviewed'
          % (agreed, len(changes), len(problems), unreviewed))

    if problems:
        print('\nNothing written -- fix the refusals above and re-run.')
        return 1
    if not changes:
        print('Nothing to apply.')
        return 0

    for i, f, new in changes:
        print('  %-26s %-12s %s -> %s' % (f[0], f[4], f[6].strip(), new))
    if dry_run:
        print('\n--dry-run: nothing written.')
        return 0

    for i, f, new in changes:
        parts = raw[i].split(',', 8)
        parts[6] = new
        raw[i] = ','.join(parts)
    io.open(MAP, 'w', encoding='utf-8', newline='\n').write('\n'.join(raw) + '\n')
    print('\napplied %d change(s) to %s' % (len(changes), MAP.relative_to(ROOT)))
    print('Re-run --export so the sheet shows the new current values.')
    return 0


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
