#!/usr/bin/env python3
"""One row per id in docs/ROADMAP.md.

WHY
The established rule is that a row is closed by EDITING it, never by adding a
second row for the same id. When that slips, the file contradicts itself: a
CLOSED row saying the work is done sits beside a stale OPEN row telling a reader
to wait for it, and nothing says which is current. A reader who checks a row and
finds it lying stops trusting the file, which is worse than the row being absent.

Four such pairs were removed in an earlier pass; four more had appeared by
2026-09-09, including:

  SB-5    one row listing SB-5a as open, one marking it DONE Phase 225
  DEP-6a  THREE rows -- two "DONE" (both true, successive pieces of one job) and
          one open row asserting the test could not be written
  DEP-6b  the same row twice, the longer containing the shorter verbatim
  DEP-7   two DIFFERENT subjects sharing an id, which is a mis-numbering rather
          than a duplicate; the second is now DEP-15

Recurrence is the point: this is not a one-off tidy-up, so it is gated.

WHAT COUNTS AS A ROW
A markdown table row whose first cell is an id -- LETTERS-DIGITS, optionally with
a trailing letter (DEP-6a). Leading emphasis or strikethrough is tolerated,
because a closed row is written `| ~~SB-5~~ |` in some sections and `| SB-5 |` in
others, and a reader treats both as the same id.

Usage:  python tools/check_roadmap_ids.py
"""
from __future__ import annotations

import collections
import io
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ROADMAP = ROOT / "docs" / "ROADMAP.md"

ROW = re.compile(r"^\s*\|\s*[~*`]*\s*([A-Za-z][A-Za-z0-9]*-[0-9]+[a-z]?)\s*[~*`]*\s*\|")


def main() -> int:
    if not ROADMAP.exists():
        sys.stderr.write("docs/ROADMAP.md does not exist\n")
        return 2

    rows = collections.defaultdict(list)
    for i, line in enumerate(
            io.open(ROADMAP, encoding="utf-8", errors="replace"), start=1):
        m = ROW.match(line.rstrip("\n"))
        if m:
            rows[m.group(1)].append(i)

    total = sum(len(v) for v in rows.values())

    # INSTRUMENT CHECK. A pattern that stops matching finds no duplicates and
    # reports a clean file, which is the failure mode that lets this recur --
    # a silent pass is indistinguishable from a real one.
    if total < 50:
        sys.stderr.write(
            "FAILED: only %d id-shaped rows parsed from docs/ROADMAP.md. The "
            "reader is wrong, not the file -- a broken pattern reports NO "
            "duplicates, which passes.\n" % total)
        return 2

    dupes = {k: v for k, v in rows.items() if len(v) > 1}

    print("Roadmap id gate")
    print("  rows with an id      : %d" % total)
    print("  distinct ids         : %d" % len(rows))
    print("  ids used more than once: %d" % len(dupes))
    print()

    if dupes:
        print("FAIL: %d id(s) appear on more than one row:" % len(dupes))
        for k in sorted(dupes):
            print("    %-12s lines %s" % (k, ", ".join(str(n) for n in dupes[k])))
        print()
        print("  A row is closed by EDITING it, never by adding a second row.")
        print("  Where two share an id: keep the CLOSED variant -- a row is only")
        print("  ever closed by the work that closed it, so the closed one is the")
        print("  newer fact. Where they are two DIFFERENT items, one is")
        print("  mis-numbered and needs the next free id, not a merge.")
        return 1

    print("Roadmap id gate OK.")
    print("  Every id names exactly one row.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
