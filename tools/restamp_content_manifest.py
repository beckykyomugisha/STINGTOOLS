#!/usr/bin/env python3
"""
Re-stamp STING_CONTENT_MANIFEST.json's tagFamilies checksums against a library.

WHY THIS EXISTS
---------------
The manifest's 206 artefact checksums describe whichever tag library was
canonical when they were stamped. If the canonical library changes, every
family reports drift on load until the manifest is re-stamped — correct
behaviour from ContentManifest's checksum verification, but 206 warnings.

This is deliberately a SEPARATE, EXPLICIT step and not something a build does.
Re-stamping declares "this library is now the baseline". Doing it automatically
would mean the manifest always agreed with whatever happened to be on disk,
which is the same as having no checksum at all.

USAGE
  python3 tools/restamp_content_manifest.py --check <library-dir>
      Report match / differ / missing. Writes nothing. Exit 1 if anything differs.

  python3 tools/restamp_content_manifest.py --apply <library-dir>
      Rewrite the checksum field of every tagFamilies entry whose familyFile is
      present in <library-dir>. Leaves absent files alone and says so.

A .bak is written next to the manifest before any change.
"""

import hashlib
import io
import json
import os
import shutil
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
MANIFEST = os.path.join(REPO, "StingTools", "Data", "STING_CONTENT_MANIFEST.json")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for block in iter(lambda: fh.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def main():
    args = [a for a in sys.argv[1:]]
    mode = None
    for m in ("--check", "--apply"):
        if m in args:
            mode = m
            args.remove(m)
    if mode is None or len(args) != 1:
        print(__doc__)
        return 2

    library = args[0]
    if not os.path.isdir(library):
        print(f"ERROR: not a directory: {library}")
        return 2

    with io.open(MANIFEST, encoding="utf-8-sig") as fh:
        doc = json.load(fh)

    entries = doc.get("tagFamilies", [])
    match = differ = missing = 0
    changed = []

    for e in entries:
        fn = e.get("familyFile")
        if not fn:
            continue
        p = os.path.join(library, fn)
        if not os.path.isfile(p):
            missing += 1
            continue
        actual = sha256(p)
        if actual == e.get("checksum"):
            match += 1
        else:
            differ += 1
            changed.append((fn, e.get("checksum"), actual))
            if mode == "--apply":
                e["checksum"] = actual

    print(f"library : {library}")
    print(f"entries : {len(entries)}")
    print(f"  match   {match}")
    print(f"  differ  {differ}")
    print(f"  absent  {missing}")

    if mode == "--check":
        for fn, old, new in changed[:10]:
            o = (old or "")[:12]
            print(f"    {fn}  manifest {o}... on disk {new[:12]}...")
        if len(changed) > 10:
            print(f"    ... and {len(changed) - 10} more")
        return 1 if (differ or missing) else 0

    if differ == 0:
        print("\nNothing to do — the manifest already describes this library.")
        return 0

    shutil.copy2(MANIFEST, MANIFEST + ".bak")

    # Rewrite the checksum VALUES in place, as text. Re-serialising with
    # json.dump is semantically identical and practically much worse: this file
    # is written by tools/content/gen_content_manifest.ps1, whose ConvertTo-Json
    # uses a different style ("key":  "value", array indentation), so a dump
    # here reformats all 39k lines. Measured 2026-09-22: a 206-checksum change
    # produced a 5,350-line diff, and the two tools would then reformat it back
    # and forth on every run - churn that makes a real edit unreviewable and
    # hides it among thousands of cosmetic ones.
    #
    # The substitution is anchored per old value and applied once each, so a
    # checksum appearing twice cannot be over-written, and anything that fails
    # to match is reported rather than silently skipped.
    text = io.open(MANIFEST, encoding="utf-8").read()
    written, unmatched = 0, []
    for fn, old, new in changed:
        if not old:
            unmatched.append(f"{fn} (no previous checksum to replace)")
            continue
        if text.count(old) != 1:
            unmatched.append(f"{fn} (old checksum appears {text.count(old)} times)")
            continue
        text = text.replace(old, new, 1)
        written += 1

    io.open(MANIFEST, "w", encoding="utf-8", newline="").write(text)
    print(f"\nRe-stamped {written} checksum(s) in place. Backup: {os.path.basename(MANIFEST)}.bak")
    if unmatched:
        print(f"WARNING: {len(unmatched)} checksum(s) could NOT be replaced as text and are "
              "UNCHANGED - re-run --check, and fix these by hand:")
        for u in unmatched[:10]:
            print("    " + u)
    if missing:
        print(f"WARNING: {missing} entry/entries have no file in this library and were "
              "left untouched — their checksums still describe the previous baseline.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
