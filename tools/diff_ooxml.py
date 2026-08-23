#!/usr/bin/env python3
"""Report which OPC parts differ between a working-tree file and HEAD.

Diagnostic only. `git diff` on a .docx or .xlsx says "Binary files differ",
which is true and useless: it cannot say whether the content moved or only the
packaging did. This unpacks both sides and names the parts, so a CI failure
carries its own cause instead of needing a local reproduction.
"""
from __future__ import annotations

import difflib
import io
import subprocess
import sys
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import kut_docs_lib as K  # noqa: E402


def head_bytes(rel: str):
    r = subprocess.run(["git", "show", "HEAD:" + rel], capture_output=True)
    return r.stdout if r.returncode == 0 else None


def main() -> int:
    for name in K.ISSUED:
        path = Path(name)
        if not path.exists():
            print("%s: missing" % name)
            continue
        old = head_bytes(name)
        if old is None:
            print("%s: not in HEAD" % name)
            continue
        if old == path.read_bytes():
            print("%s: identical" % name)
            continue

        zn = zipfile.ZipFile(path)
        zo = zipfile.ZipFile(io.BytesIO(old))
        names_n, names_o = set(zn.namelist()), set(zo.namelist())
        print("\n%s" % name)
        only = sorted(names_n ^ names_o)
        if only:
            print("  parts added/removed: %s" % only)
        for part in sorted(names_n & names_o):
            a, b = zn.read(part), zo.read(part)
            if a == b:
                continue
            print("  CONTENT DIFFERS: %s (%d bytes now, %d in HEAD)" % (part, len(a), len(b)))
            try:
                sa = a.decode("utf-8").split("><")
                sb = b.decode("utf-8").split("><")
            except UnicodeDecodeError:
                continue
            shown = 0
            for line in difflib.unified_diff(sb, sa, lineterm="", n=0):
                if line.startswith(("---", "+++", "@@")):
                    continue
                print("      %s" % line[:240])
                shown += 1
                if shown >= 8:
                    print("      ... (truncated)")
                    break
        # Packaging-only differences are worth naming explicitly: they mean the
        # document content is identical and only the zip metadata moved.
        meta = [n for n in sorted(names_n & names_o)
                if zn.getinfo(n).date_time != zo.getinfo(n).date_time]
        if meta:
            print("  zip entry timestamps differ on %d part(s), e.g. %s: %s vs %s"
                  % (len(meta), meta[0], zn.getinfo(meta[0]).date_time,
                     zo.getinfo(meta[0]).date_time))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
