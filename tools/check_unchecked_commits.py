#!/usr/bin/env python3
"""check_unchecked_commits.py - no Revit Transaction may discard its Commit() status (ELEC-32).

Transaction.Commit() returns RolledBack when a failure handler, or the user cancelling
Revit's error dialog, undoes the work. Code that ignores the status goes on to report
counts for work that is not in the model. Every commit must either go through
StingTools.Core.StingTx (Commit / TryCommit) or read the returned TransactionStatus.

A site is flagged when a variable declared as a Transaction (local, field or parameter;
not SubTransaction / TransactionGroup) is committed as a bare statement:
    tx.Commit();           else tx.Commit();          { tx.Commit(); }
Reading the status is fine:
    var st = tx.Commit();   if (tx.Commit() != TransactionStatus.Committed) ...

Usage:  python tools/check_unchecked_commits.py            # list and exit 1 if any
        python tools/check_unchecked_commits.py --check    # same (CI)
"""
import os
import re
import sys

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
SRC = os.path.join(ROOT, 'StingTools')
ALLOWED = {os.path.normpath('StingTools/Core/StingTx.cs')}

DECL = re.compile(r'\bTransaction\s+(\w+)\s*[=;,)]|\bvar\s+(\w+)\s*=\s*new\s+Transaction\s*\(')
BARE = re.compile(r'(?:^|[;{}]|\belse\b|\)\s*)\s*(\w+)\.Commit\(\)\s*;')


def strip_comment(line):
    i = line.find('//')
    return line if i < 0 else line[:i]


def scan():
    hits = []
    for d, dirs, files in os.walk(SRC):
        dirs[:] = [x for x in dirs if x not in ('bin', 'obj')]
        for f in files:
            if not f.endswith('.cs'):
                continue
            path = os.path.join(d, f)
            rel = os.path.normpath(os.path.relpath(path, ROOT))
            if rel in ALLOWED:
                continue
            with open(path, encoding='utf-8', errors='replace') as fh:
                lines = fh.read().split('\n')
            code = [strip_comment(l) for l in lines]
            txvars = set()
            for l in code:
                for m in DECL.finditer(l):
                    txvars.add(m.group(1) or m.group(2))
            if not txvars:
                continue
            for i, l in enumerate(code):
                for m in BARE.finditer(l):
                    if m.group(1) in txvars:
                        hits.append(f"{rel.replace(os.sep, '/')}:{i + 1}: {lines[i].strip()}")
    return hits


def main():
    hits = scan()
    if hits:
        print(f"{len(hits)} Transaction.Commit() call(s) discard the status "
              "(use StingTx.Commit / StingTx.TryCommit, or read the TransactionStatus):")
        for h in hits:
            print("  " + h)
        return 1
    print("No Transaction.Commit() discards its status.")
    return 0


if __name__ == '__main__':
    sys.exit(main())
