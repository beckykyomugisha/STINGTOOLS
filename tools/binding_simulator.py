#!/usr/bin/env python3
"""Offline binding simulator — COMMITTED, so its baseline is reproducible.

Until now the 'offline simulator' was a DESCRIBED METHOD, re-implemented per
session, so its numbers could not be compared across runs. The 3,018 bound / 374
skipped / 0 conflicts figure came from a runtime Revit session and is SUPERSEDED
and NON-REPRODUCIBLE here: it counted runtime CategorySet resolution, this counts
binding-data rows. They are different metrics and must not be compared.

This models the binder's decision for every declared parameter:

  bound    -> resolves to a non-empty CategorySet
  skipped  -> resolves to no categories (nothing to bind to)
  conflict -> same NAME declared twice with different GUID, or same GUID reused
              for two different names (the case LoadSharedParams step 3b pre-skips)

Absolute totals depend on runtime category availability, so the number that matters
is the DELTA between two runs of this same script.
"""
import os, sys, json, collections

import os as _os
REPO = _os.path.dirname(_os.path.dirname(_os.path.abspath(__file__)))
ROOT = _os.path.join(REPO, "StingTools")
DATA = os.path.join(ROOT, "Data")

def p(fn): return os.path.join(DATA, fn)

# --- declared params ---
declared = {}          # name -> (guid, group)
guid_owner = {}        # guid -> name
name_dupes = collections.Counter()
conflicts = []
for line in open(p("MR_PARAMETERS.txt"), encoding="utf-8", errors="replace"):
    f = line.rstrip("\n").split("\t")
    if len(f) < 6 or f[0] != "PARAM":
        continue
    guid, name, group = f[1], f[2], f[5]
    name_dupes[name] += 1
    if name in declared and declared[name][0] != guid:
        conflicts.append(f"NAME {name} declared with two GUIDs")
    if guid in guid_owner and guid_owner[guid] != name:
        conflicts.append(f"GUID {guid} reused: {guid_owner[guid]} / {name}")
    declared[name] = (guid, group)
    guid_owner[guid] = name

# --- binder inputs ---
cat_rows = collections.defaultdict(set)
for line in open(p("CATEGORY_BINDINGS.csv"), encoding="utf-8", errors="replace"):
    line = line.strip()
    if not line or line.startswith("#") or line.startswith("Parameter_Name"):
        continue
    c = line.split(",")
    if len(c) >= 2:
        cat_rows[c[0].strip()].add(c[1].strip())

res_rows = {}
for line in open(p("RESOLVED_BINDINGS.csv"), encoding="utf-8", errors="replace"):
    line = line.strip()
    if not line or line.startswith("#"):
        continue
    c = line.split(",", 1)
    if len(c) == 2:
        res_rows[c[0].strip()] = c[1].strip()

fam_rows = collections.defaultdict(set)
for line in open(p("FAMILY_PARAMETER_BINDINGS.csv"), encoding="utf-8", errors="replace"):
    c = line.split(",")
    if len(c) > 6:
        fam_rows[c[1].strip()].add(c[6].strip())

bound = skipped = 0
skipped_names = []
for name in sorted(declared):
    has = bool(cat_rows.get(name)) or bool(res_rows.get(name)) or bool(fam_rows.get(name))
    if has:
        bound += 1
    else:
        skipped += 1
        skipped_names.append(name)

print(f"declared params : {len(declared)}")
print(f"  bound         : {bound}")
print(f"  skipped       : {skipped}")
print(f"  conflicts     : {len(conflicts)}")
for c in conflicts[:10]:
    print("     ", c)
if "-v" in sys.argv:
    print("\n-- skipped --")
    for n in skipped_names:
        print("   ", n)
