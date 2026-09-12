"""Exercise the QS review round trip, including every refusal path.

The map is restored afterwards: this proves the tool works, it does not author a
single NRM2 code. A tool whose --apply has only ever been read is not a round trip.
"""
import csv, io, os, shutil, subprocess, sys

MAP = 'StingTools/Data/STING_CSI_MASTERFORMAT_MAP.csv'
SHEET = 'docs/qs_review/nrm2_site_civil_review.csv'
PY = sys.executable


def run(*args):
    r = subprocess.run([PY, 'tools/qs_nrm2_review.py'] + list(args),
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    return r.returncode, (r.stdout or '') + (r.stderr or '')


def sheet_rows():
    with io.open(SHEET, encoding='utf-8-sig', newline='') as fh:
        return list(csv.DictReader(fh))


def write_sheet(rows):
    with io.open(SHEET, 'w', encoding='utf-8', newline='') as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0].keys()))
        w.writeheader()
        w.writerows(rows)


def map_nrm2(row_id):
    raw = io.open(MAP, encoding='utf-8-sig').read().splitlines()
    n = 0
    header_seen = False
    for line in raw:
        if not line.strip() or line.lstrip().startswith('#'):
            continue
        if not header_seen:
            header_seen = True
            continue
        n += 1
        if n == int(row_id):
            return line.split(',', 8)[6].strip()
    return None


shutil.copyfile(MAP, MAP + '.bak')
shutil.copyfile(SHEET, SHEET + '.bak')
fails = 0
try:
    rows = sheet_rows()
    target = rows[0]
    rid, before = target['ROW'], target['Current_Nrm2']
    print('target row %s  %s / %s  currently NRM2 %s'
          % (rid, target['Category'], target['Section'], before))
    print()

    def check(label, cond, detail=''):
        global fails
        print('%-6s %s %s' % ('PASS' if cond else 'FAIL', label, detail))
        if not cond:
            fails += 1

    # 1. An untouched sheet applies nothing.
    rc, out = run('--apply')
    check('untouched sheet applies nothing', rc == 0 and 'Nothing to apply' in out)
    # KUT-10 - DERIVED, not hardcoded. This read '36 still unreviewed' and broke the
    # moment the map gained a division-31 row, which says nothing about whether the
    # round trip works. What matters is that the tool reports EVERY row on the sheet as
    # unreviewed, so the count comes from the sheet the tool just wrote.
    check('  ... and reports them unreviewed',
          ('%d still unreviewed' % len(rows)) in out,
          '(%d rows on the sheet)' % len(rows))

    # 2. "ok" changes nothing.
    rows = sheet_rows(); rows[0]['QS_VERDICT'] = 'ok'; write_sheet(rows)
    rc, out = run('--apply')
    check('an "ok" verdict changes nothing', rc == 0 and map_nrm2(rid) == before)

    # 3. change with an empty QS_NRM2 is refused.
    rows = sheet_rows(); rows[0]['QS_VERDICT'] = 'change'; rows[0]['QS_NRM2'] = ''; write_sheet(rows)
    rc, out = run('--apply')
    check('change with no code is refused', rc == 1 and 'QS_NRM2 is empty' in out)
    check('  ... and writes nothing', map_nrm2(rid) == before)

    # 4. an NRM2 code that exists nowhere in the map is refused (typo guard).
    rows = sheet_rows(); rows[0]['QS_NRM2'] = '444'; write_sheet(rows)
    rc, out = run('--apply')
    check('an unknown NRM2 code is refused', rc == 1 and 'appears nowhere else' in out)
    check('  ... and writes nothing', map_nrm2(rid) == before)

    # 5. a stale sheet (category no longer matches) is refused.
    rows = sheet_rows(); rows[0]['QS_NRM2'] = '15'; rows[0]['Category'] = 'Not A Real Category'
    write_sheet(rows)
    rc, out = run('--apply')
    check('a stale sheet is refused', rc == 1 and 're-export' in out)
    check('  ... and writes nothing', map_nrm2(rid) == before)

    # 6. a nonsense verdict is refused rather than guessed.
    rows = sheet_rows(); rows[0]['Category'] = target['Category']; rows[0]['QS_VERDICT'] = 'maybe?'
    write_sheet(rows)
    rc, out = run('--apply')
    check('an unrecognised verdict is refused', rc == 1 and 'neither an agreement nor a change' in out)

    # 7. --dry-run reports the change and writes nothing.
    rows = sheet_rows(); rows[0]['QS_VERDICT'] = 'change'; rows[0]['QS_NRM2'] = '15'
    write_sheet(rows)
    rc, out = run('--apply', '--dry-run')
    check('--dry-run reports but does not write', rc == 0 and 'nothing written' in out and map_nrm2(rid) == before)

    # 8. the real thing.
    rc, out = run('--apply')
    check('a valid verdict IS applied', rc == 0 and map_nrm2(rid) == '15',
          '(map now %s)' % map_nrm2(rid))

    # 9. idempotent: re-export then re-apply changes nothing more.
    rc, out = run('--export')
    check('re-export carries the verdict over', rc == 0 and 'verdicts carried over  : 1' in out)
    rc, out = run('--apply')
    check('applying the same sheet twice is a no-op', rc == 0 and map_nrm2(rid) == '15'
          and ('Nothing to apply' in out or 'to change' in out))

    # 10. the ONLY row that moved is the target.
    orig = io.open(MAP + '.bak', encoding='utf-8-sig').read().splitlines()
    now = io.open(MAP, encoding='utf-8-sig').read().splitlines()
    moved = [i for i, (a, b) in enumerate(zip(orig, now)) if a != b]
    check('exactly one line changed', len(orig) == len(now) and len(moved) == 1,
          '(%d line(s))' % len(moved))
finally:
    shutil.copyfile(MAP + '.bak', MAP); os.remove(MAP + '.bak')
    shutil.copyfile(SHEET + '.bak', SHEET); os.remove(SHEET + '.bak')
    print('\nrestored the map and the sheet -- no NRM2 code was authored by this run')

print('\n%s' % ('ALL CHECKS PASSED' if not fails else '%d CHECK(S) FAILED' % fails))
sys.exit(1 if fails else 0)
