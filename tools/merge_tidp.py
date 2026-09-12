#!/usr/bin/env python3
"""Merge returned Task Information Delivery Plans into the project MIDP.

    python tools/merge_tidp.py returns/*.xlsx                # preview only
    python tools/merge_tidp.py returns/TIDP-A.xlsx --apply   # write the additions
    python tools/merge_tidp.py returns/*.xlsx --emit-python  # rows for midp_rows.py

WHY THIS EXISTS
Each appointed party completes the TIDP sheet and returns it; the Information
Manager merges the rows into the MIDP by hand. Nine consultants, re-baselined at
every stage, is enough repetition to go wrong quietly -- and the failure is
invisible. A dropped row does not appear anywhere as an error: the register is
simply missing a deliverable nobody is now tracking, and that surfaces at a gate.

PREVIEW BY DEFAULT
Nothing is written unless --apply is passed. The register is an issued document
and a merge is not reversible by eye once the workbook is saved, so the useful
default is the one that tells you what would happen. Preview is also stdlib-only
-- reading a .xlsx needs nothing installed -- so the safe operation always runs.
Writing needs openpyxl, and says so plainly if it is missing.

A Ref that already exists with DIFFERENT content is a conflict and is refused,
not merged. Two parties disagreeing about the same deliverable is a question for
a person; silently taking the newer file would let the second return overwrite
the first with nobody seeing it. --overwrite-conflicts is the explicit override.

Validation uses tools/midp_schema.py, the same definitions build_midp.py wrote
the drop-downs from. Restating them here would let the merge reject a value the
workbook itself had just offered the consultant -- or, worse, accept one it had
not.

--APPLY IS A WORKING COPY; --EMIT-PYTHON IS THE CLEAN PATH
The register is a GENERATED workbook. --apply edits the generated artefact, so
check_kut_documents.py immediately and correctly reports it as hand-edited since
generation, and the next regeneration silently drops the merged rows. That is
right for the Information Manager's working copy between issues and wrong before
an issue.

--emit-python closes the loop: it prints each accepted row as the row(...) call
that tools/midp_rows.py is made of, ready to paste in. Regenerate afterwards and
the register is generated, gate-clean, and carries the deliverable. The rows are
NOT written into midp_rows.py automatically -- placing a deliverable in the right
section of that file, next to the ones it belongs with, is an editorial judgement,
and a tool appending to the end of a curated file would degrade it every run.
"""
from __future__ import annotations

import argparse
import glob
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kut_docs_lib as K          # noqa: E402
import midp_schema as S           # noqa: E402

MIDP_FILE = "KUT_Master_Information_Delivery_Plan.xlsx"


class Row(dict):
    """One deliverable, keyed by column name, plus where it came from."""

    def __init__(self, values, source, line):
        super().__init__(values)
        self.source = source
        self.line = line

    def content(self):
        """The comparable payload: everything the returning party owns.

        Columns the Information Manager maintains are excluded, so a return that
        leaves them blank does not read as a conflict against a register that
        has them filled.
        """
        return {k: (v or "").strip() for k, v in self.items()
                if k not in S.IM_OWNED and (v or "").strip()}


def find_header(rows):
    """(row index, {column name: cell index}) for the register header.

    Located by searching for the key column rather than trusting a fixed
    address: the TIDP sheet carries a header block above its table and the MIDP
    sheet does not, and a consultant may well have inserted a row. A layout
    change should degrade to a clear error, not a silent column shift -- seven
    drop-downs in this very workbook once pointed at the wrong column and looked
    entirely correct.
    """
    for i, row in enumerate(rows):
        cells = [(c or "").strip() for c in row]
        if S.KEY_COL in cells and "Deliverable" in cells:
            return i, {name: j for j, name in enumerate(cells) if name}
    return None, None


def read_register(path: Path, sheet_name: str, source: str):
    """Every non-empty deliverable row on one sheet.

    The fallback used to take the FIRST sheet carrying a 'Ref' column. Once the
    workbook grew one Task Information Delivery Plan sheet per appointed party,
    the first such sheet became 'MIDP' -- so a consultant returning the whole
    workbook had their entire register read back as their own return, silently,
    and every row compared against itself. The fallback now looks only at plan
    sheets, and refuses to guess between two of them.
    """
    sheets = K.xlsx_sheets(path)
    rows = sheets.get(sheet_name)
    if rows is None:
        candidates = []
        for name, candidate in sheets.items():
            if name == S.MIDP_SHEET:
                continue                  # never mistake the register for a return
            idx, _cols = find_header(candidate)
            if idx is None:
                continue
            # A plan sheet with nothing under its header has not been filled in.
            if any(any((c or "").strip() for c in row) for row in candidate[idx + 1:]):
                candidates.append((name, candidate))
        # Prefer sheets named as delivery plans. Other sheets can carry a
        # 'Ref' / 'Deliverable' header without being one -- the change log does
        # -- so they are considered only when no plan sheet is present, which is
        # what happens when a consultant renames their tab.
        named = [c for c in candidates if c[0].upper().startswith(S.TIDP_SHEET)]
        if named:
            candidates = named
        if len(candidates) > 1:
            return None, ("contains %d delivery plan sheets (%s). Name the one to "
                          "merge with --sheet." % (len(candidates),
                                                   ", ".join(n for n, _ in candidates)))
        if candidates:
            sheet_name, rows = candidates[0][0], candidates[0][1]
    if rows is None:
        return None, "no sheet with a '%s' column" % S.KEY_COL

    hdr, cols = find_header(rows)
    if hdr is None:
        return None, "sheet %r has no '%s' / 'Deliverable' header row" % (sheet_name, S.KEY_COL)

    missing = [c for c in (S.KEY_COL, "Deliverable", "Stage") if c not in cols]
    if missing:
        return None, "sheet %r is missing column(s) %s" % (sheet_name, missing)

    out = []
    for n, row in enumerate(rows[hdr + 1:], start=hdr + 2):
        values = {name: (row[j].strip() if j < len(row) else "")
                  for name, j in cols.items()}
        if not values.get(S.KEY_COL) and not values.get("Deliverable"):
            continue                      # blank template row
        out.append(Row(values, source, n))
    return out, None


def validate(row: Row):
    """Values outside the permitted lists the workbook offered."""
    bad = []
    for column, permitted in S.LISTS.items():
        v = (row.get(column) or "").strip()
        if v and v not in permitted:
            bad.append("%s=%r is not a permitted value (expected one of: %s)"
                       % (column, v, ", ".join(permitted)))
    if not (row.get(S.KEY_COL) or "").strip():
        bad.append("no %s -- the register is matched on it, so the row cannot be placed"
                   % S.KEY_COL)
    return bad


def classify(register, incoming):
    """(new, identical, conflicts, invalid) across every returned row."""
    by_ref = {(r.get(S.KEY_COL) or "").strip(): r for r in register}
    new, identical, conflicts, invalid = [], [], [], []
    seen = {}

    for row in incoming:
        problems = validate(row)
        if problems:
            invalid.append((row, problems))
            continue

        ref = (row.get(S.KEY_COL) or "").strip()

        # Two returns claiming the same Ref is a conflict even before the
        # register is consulted -- otherwise whichever file was listed last on
        # the command line would silently win.
        if ref in seen and seen[ref].content() != row.content():
            conflicts.append((row, seen[ref], "another return"))
            continue
        seen[ref] = row

        existing = by_ref.get(ref)
        if existing is None:
            new.append(row)
        elif existing.content() == row.content():
            identical.append(row)
        else:
            conflicts.append((row, existing, "the register"))
    return new, identical, conflicts, invalid


def describe_difference(a: Row, b: Row):
    keys = sorted(set(a.content()) | set(b.content()))
    out = []
    for k in keys:
        va, vb = a.content().get(k, ""), b.content().get(k, "")
        if va != vb:
            out.append("      %-16s returned %r vs %r" % (k, va, vb))
    return out


def apply_merge(midp: Path, rows, verbose: bool):
    """Append rows to the register, preserving the sheet's formatting."""
    try:
        from openpyxl import load_workbook
    except ImportError:
        print("\n--apply needs openpyxl (preview does not):\n"
              "    python -m pip install openpyxl", file=sys.stderr)
        return 1

    wb = load_workbook(midp)
    ws = wb[S.MIDP_SHEET]

    header = {}
    for j in range(1, ws.max_column + 1):
        name = ws.cell(row=S.MIDP_HEADER_ROW, column=j).value
        if name:
            header[str(name).strip()] = j

    last = ws.max_row
    while last > S.MIDP_HEADER_ROW and not ws.cell(row=last, column=header[S.KEY_COL]).value:
        last -= 1

    from copy import copy
    for offset, row in enumerate(rows, start=1):
        target = last + offset
        for name, j in header.items():
            src = ws.cell(row=last, column=j)
            cell = ws.cell(row=target, column=j)
            # Carry the register's own formatting down rather than leaving the
            # merged rows visually distinct from the rest of the schedule.
            cell.font, cell.border = copy(src.font), copy(src.border)
            cell.alignment, cell.number_format = copy(src.alignment), src.number_format

            if name == "Variance (days)":
                # A formula, not a value. Taking a literal from a return would
                # replace the calculation with a number that stops updating.
                cell.value = ('=IF(AND(L{0}<>"",M{0}<>""),M{0}-L{0},"")'.format(target))
            else:
                cell.value = row.get(name) or None

    end = last + len(rows)
    ws.auto_filter.ref = "A%d:R%d" % (S.MIDP_HEADER_ROW, end)
    wb.save(midp)
    if verbose:
        print("  extended the register to row %d and reset the filter range" % end)
    return 0


# The row() signature in tools/midp_rows.py, in order. Stated once here because
# the emitted call is positional up to `tidp`; a mismatch would produce a call
# that runs and puts every value in the wrong field.
#
# Left of the mapping: the register COLUMN name a returned TIDP uses.
# Right: the row() parameter it feeds. Columns absent from row() are absent on
# purpose -- 'Originator' is written blank by the builder, 'RAG' is left for the
# Information Manager, and the three date columns are formulas derived from the
# one appointment date, so a literal from a return would replace a calculation
# with a number that stops updating.
_EMIT_ORDER = [
    ("Ref", "ref"), ("Discipline", "disc"), ("Volume", "vol"),
    ("Deliverable", "deliv"), ("Type", "typ"), ("Type code", "iso"),
    ("Stage", "stage"), ("LOD", "lod"), ("Format", "fmt"),
    ("Suitability", "suit"), ("CDE State", "state"),
    ("Month from", "mf"), ("Month to", "mt"),
    ("Responsible", "resp"), ("TIDP ref", "tidp"),
]

_INT_FIELDS = {"mf", "mt"}


def _constant_names():
    """{value: CONSTANT_NAME} for the module-level strings in midp_rows.

    So a Stage emits as `DA` rather than `'2.1 Deliverable A'`, matching the 122
    rows already in that file. Read from the module rather than restated: a
    second copy of the stage names is a second thing to drift, and this file
    already refuses to restate the validation lists for the same reason.
    """
    import midp_rows as D
    out = {}
    for name, value in vars(D).items():
        if name.startswith("_") or not isinstance(value, str):
            continue
        if not name.isupper() and name not in ("IM",):
            continue
        out.setdefault(value, name)
    return out


def _new_source():
    """The `source` value a newly merged row should carry.

    NOT the literal "New". build_midp.py maps every source through SRC_LABEL to
    a Change-log heading, and an unmapped value is a KeyError that stops the
    build -- so an invented source emits rows that look right and break the next
    regeneration. Derived from the rows already in midp_rows.py: the newest
    "New ..." source in the file, which is the issue this merge belongs to.
    """
    import midp_rows as D
    news = sorted({r["source"] for r in D.R
                   if str(r.get("source", "")).startswith("New ")})
    if not news:
        raise RuntimeError(
            'no "New ..." source found in tools/midp_rows.py, so there is nothing '
            'to copy. build_midp.py rejects a source SRC_LABEL does not map, so '
            'this cannot be guessed.')
    return news[-1]


def emit_python(rows, verbose: bool) -> int:
    """Print each accepted row as a midp_rows.row(...) call."""
    try:
        consts = _constant_names()
        source = _new_source()
    except Exception as exc:
        print("cannot read tools/midp_rows.py: %s" % exc, file=sys.stderr)
        return 1

    def lit(field, value):
        text = (value or "").strip()
        if field in _INT_FIELDS:
            try:
                return str(int(float(text)))
            except (TypeError, ValueError):
                # A month that is not a number is a data fault, not something to
                # paper over with 0 -- 0 means "mobilisation" and would read as a
                # real answer. Emit it as a string so Python refuses it loudly if
                # anyone pastes the line unedited.
                return repr(text)
        if text in consts:
            return consts[text]
        return repr(text)

    print("")
    print("# " + "-" * 68)
    print("# Paste into tools/midp_rows.py, in the section these deliverables")
    print("# belong to, then regenerate:  python tools/build_midp.py")
    print("#")
    print("# Placement is deliberately yours: appending to the end of a curated")
    print("# file would work and would degrade it a little every time.")
    print("# " + "-" * 68)
    for r in rows:
        args = ", ".join(lit(field, r.get(col)) for col, field in _EMIT_ORDER)
        change = "Merged from %s line %d" % (r.source, r.line)
        notes = (r.get("Notes") or "").strip()
        print("row(%s, 2, %r, %r, %r, %r, %r, %r)"
              % (args, "N", "N", "N", source, change, notes))
    print("")
    print("# %d row(s), sourced %r to match the current issue. The defaults after"
          % (len(rows), source))
    print("# `tidp` are revclass=2 and N/N/N for critical / as-built / O&M: a")
    print("# returned TIDP does not carry those, so they are the conservative")
    print("# choice and want a look before you paste.")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("returns", nargs="+", help="returned TIDP workbook(s); globs accepted")
    ap.add_argument("--midp", default=None, help="the register to merge into")
    ap.add_argument("--apply", action="store_true",
                    help="write the additions (default is preview only)")
    ap.add_argument("--emit-python", action="store_true",
                    help="print the new rows as midp_rows.py row(...) calls, so the "
                         "register can be REGENERATED with them rather than hand-edited")
    ap.add_argument("--overwrite-conflicts", action="store_true",
                    help="also replace register rows whose content differs")
    ap.add_argument("--sheet", default=None,
                    help="the delivery plan sheet to read, e.g. TIDP-M. Needed only "
                         "when a returned workbook carries more than one.")
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    root = Path(__file__).resolve().parent.parent
    midp = Path(args.midp) if args.midp else root / MIDP_FILE
    if not midp.exists():
        print("register not found: %s" % midp, file=sys.stderr)
        return 1

    paths = []
    for pattern in args.returns:
        hits = [Path(p) for p in glob.glob(pattern)]
        if not hits:
            print("no file matches %r" % pattern, file=sys.stderr)
            return 1
        paths.extend(hits)

    register, err = read_register(midp, S.MIDP_SHEET, midp.name)
    if err:
        print("cannot read the register: %s" % err, file=sys.stderr)
        return 1

    incoming, failed = [], []
    for p in paths:
        rows, err = read_register(p, args.sheet or S.TIDP_SHEET, p.name)
        if err:
            failed.append((p, err))
            continue
        incoming.extend(rows)

    new, identical, conflicts, invalid = classify(register, incoming)

    print("MIDP merge preview" if not args.apply else "MIDP merge")
    print("  register            : %s (%d deliverables)" % (midp.name, len(register)))
    print("  returns read        : %d file(s), %d row(s)" % (len(paths) - len(failed), len(incoming)))
    print("  would be added      : %d" % len(new))
    print("  already identical   : %d" % len(identical))
    print("  conflicts           : %d" % len(conflicts))
    print("  rejected as invalid : %d" % len(invalid))

    if failed:
        print("\nUNREADABLE")
        for p, err in failed:
            print("  %s: %s" % (p.name, err))

    if new:
        print("\nWOULD ADD")
        for r in new:
            print("  %-9s %-46s %-20s [%s line %d]"
                  % (r.get(S.KEY_COL), (r.get("Deliverable") or "")[:46],
                     r.get("Stage") or "", r.source, r.line))

    if identical and args.verbose:
        print("\nALREADY IN THE REGISTER, UNCHANGED")
        for r in identical:
            print("  %-9s %s" % (r.get(S.KEY_COL), (r.get("Deliverable") or "")[:60]))

    if conflicts:
        print("\nCONFLICTS -- not merged" +
              (" (--overwrite-conflicts given, see below)" if args.overwrite_conflicts else ""))
        for row, other, against in conflicts:
            print("  %-9s [%s line %d] differs from %s"
                  % (row.get(S.KEY_COL), row.source, row.line, against))
            for line in describe_difference(row, other):
                print(line)

    if invalid:
        print("\nREJECTED -- a value outside the lists the workbook offers")
        for row, problems in invalid:
            print("  %-9s [%s line %d]" % (row.get(S.KEY_COL) or "(no ref)", row.source, row.line))
            for p in problems:
                print("      %s" % p)

    if args.emit_python:
        if not new:
            print("\nNothing new to emit.")
        else:
            emit_python(new, args.verbose)

    if not args.apply:
        if args.emit_python:
            return 1 if (invalid or failed) else 0
        print("\nNothing was written. Re-run with --apply to add the %d new row(s)." % len(new))
        print("Or --emit-python to print them as midp_rows.py rows, which is the path")
        print("that survives regeneration.")
        if conflicts:
            print("Conflicts stay refused unless --overwrite-conflicts is also given: two")
            print("parties disagreeing about one deliverable is a question for a person.")
        return 1 if (invalid or failed) else 0

    to_write = list(new)
    if args.overwrite_conflicts and conflicts:
        print("\n--overwrite-conflicts: %d conflicting row(s) are NOT appended -- a Ref that"
              % len(conflicts))
        print("already exists is corrected in place, which this tool does not do. Edit the")
        print("register row directly, or agree the change with the returning party first.")

    if not to_write:
        print("\nNothing to add.")
        return 1 if (invalid or failed) else 0

    rc = apply_merge(midp, to_write, args.verbose)
    if rc:
        return rc
    print("\nAdded %d deliverable(s) to %s." % (len(to_write), midp.name))
    print("The register is a generated document: this edit is a MANUAL change to it,")
    print("so tools/check_kut_documents.py will now report it as edited since generation.")
    print("Re-run with --emit-python to get these rows as midp_rows.py row(...) calls,")
    print("paste them in, and regenerate: that register is gate-clean and survives the")
    print("next build. This edited copy does not.")
    return 1 if (invalid or failed) else 0


if __name__ == "__main__":
    raise SystemExit(main())
