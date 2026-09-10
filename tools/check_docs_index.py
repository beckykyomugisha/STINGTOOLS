#!/usr/bin/env python3
"""Every docs/*.md must have an entry in docs/INDEX.md.

WHY
CLAUDE.md calls docs/INDEX.md the authoritative table of contents for docs/ and
marks populating it a P0. It was not authoritative: 175 markdown files sat in
docs/ and 41 of them were named nowhere in the index. A missing entry is not a
cosmetic gap -- the index is where a reader learns whether a document is current
(the file's own header rarely says), so an unindexed document is indistinguishable
from one nobody has written.

WHAT COUNTS AS AN ENTRY
The EXACT filename, appearing anywhere in INDEX.md. Deliberately not a glob:
the index carried `HEALTHCARE_*_PROMPT.md` covering seven real files, which reads
as coverage and is not -- you cannot click it, and it does not say which of the
seven is current. A summary is not an index entry.

Deliberately not markdown-link syntax either. INDEX.md lists many files in prose
and in `·`-separated runs, so an extraction that only recognised `[x](y.md)`
reported 155 of 175 missing. A measurement that finds almost everything broken is
a broken measurement.

THE BASELINE ONLY SHRINKS
It is empty. Every docs/*.md is indexed as of 2026-09-09 -- the 41 that were not
are listed under "Unclassified" in INDEX.md, which states plainly that nobody has
yet decided whether each is current or superseded. That judgement needs a human
and is visible as outstanding rather than invisible as absent.

A file may be baselined here only if it genuinely should not be indexed. The gate
also fails on a baseline entry that is now indexed, or that names a file that no
longer exists, so the list cannot rot into a claim about a tree that has moved.

Usage:  python tools/check_docs_index.py
"""
from __future__ import annotations

import io
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
INDEX = DOCS / "INDEX.md"
BASELINE = Path(__file__).resolve().parent / "docs_index_baseline.txt"


def load_baseline() -> dict:
    out = {}
    if not BASELINE.exists():
        return out
    for line in io.open(BASELINE, encoding="utf-8"):
        line = line.rstrip("\n")
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        name, _, note = line.partition("\t")
        out[name.strip()] = note.strip()
    return out


def main() -> int:
    if not INDEX.exists():
        sys.stderr.write("docs/INDEX.md does not exist\n")
        return 2

    index_text = io.open(INDEX, encoding="utf-8", errors="replace").read()
    files = sorted(p.name for p in DOCS.glob("*.md") if p.name != "INDEX.md")

    # INSTRUMENT CHECK. A reader that finds nothing reports the whole tree as
    # unindexed, which reads exactly like a finding. INDEX.md is a large curated
    # file; if it parses to almost nothing, this gate is wrong, not the docs.
    if len(files) < 50:
        sys.stderr.write(
            "FAILED: only %d markdown files found under docs/ -- the reader is "
            "wrong, not the tree.\n" % len(files))
        return 2
    if len(index_text) < 2000:
        sys.stderr.write(
            "FAILED: docs/INDEX.md is only %d bytes. Refusing to report the tree "
            "as unindexed against an index that did not load.\n" % len(index_text))
        return 2

    baseline = load_baseline()
    missing = [f for f in files if f not in index_text]
    unexpected = [f for f in missing if f not in baseline]

    rc = 0
    print("Docs index gate")
    print("  markdown files under docs/ : %d" % len(files))
    print("  indexed in INDEX.md        : %d" % (len(files) - len(missing)))
    print("  not indexed                : %d" % len(missing))
    print("  of those, baselined        : %d" % (len(missing) - len(unexpected)))
    print()

    if unexpected:
        rc = 1
        print("FAIL: %d file(s) under docs/ are named nowhere in docs/INDEX.md:"
              % len(unexpected))
        for f in unexpected:
            print("    " + f)
        print()
        print("  Add each to docs/INDEX.md. If you do not yet know whether it is")
        print("  current or superseded, list it under 'Unclassified' -- an entry")
        print("  saying 'nobody has triaged this' is worth more than no entry,")
        print("  because absence reads as 'no such document'.")
        print()

    stale_indexed = sorted(f for f in baseline if f in index_text)
    if stale_indexed:
        rc = 1
        print("FAIL: %d baselined file(s) ARE indexed now. Delete their lines from"
              % len(stale_indexed))
        print("      tools/docs_index_baseline.txt -- the baseline only shrinks:")
        for f in stale_indexed:
            print("    " + f)
        print()

    gone = sorted(f for f in baseline if not (DOCS / f).exists())
    if gone:
        rc = 1
        print("FAIL: %d baselined file(s) no longer exist under docs/. Delete their"
              % len(gone))
        print("      lines -- a baseline entry for a file that is gone is a claim")
        print("      about a tree that has moved:")
        for f in gone:
            print("    " + f)
        print()

    if rc == 0:
        print("Docs index gate OK.")
        print("  Every markdown file under docs/ has an entry in docs/INDEX.md.")
        if baseline:
            print("  Deliberately unindexed (baselined): %d" % len(baseline))
    return rc


if __name__ == "__main__":
    sys.exit(main())
