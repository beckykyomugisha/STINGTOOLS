#!/usr/bin/env python3
"""
Build shared/ifc/mappings/BUILDINGSMART_PSET_INDEX.json — the buildingSMART
property-set and quantity-set names, with their property names, for IFC4 and
IFC4X3 (DSCH-38).

Why: the Pset_ and Qto_ prefixes are reserved for sets the IFC specification
declares (IFC 4.3 IfcPropertySet, docs/schemas/core/IfcKernel/Entities/
IfcPropertySet.md in github.com/buildingSMART/IFC4.3.x-development). A row of
shared/ifc/mappings/STING_IFC_PSET_MAPPING.json that names a Pset_* / Qto_*
property buildingSMART does not define writes into a reserved namespace and is
read by nothing. The plugin and server tests check every such row against this
index, so a wrong target fails the build instead of shipping.

Source: the PSD templates bundled with ifcopenshell (ifcopenshell.util.pset),
which are compiled from buildingSMART's published PSD XML. The IFC4 set names
match buildingSMART's reference_schemas/psd_IFC4_ADD2_TC1/ one for one (checked
2026-10-02).

Usage:
    python tools/enums/bsi_pset_index.py            # rewrite the index
    python tools/enums/bsi_pset_index.py --check    # exit 1 if it is stale
Needs ifcopenshell (pip install ifcopenshell); --check fails, never passes, when
it is missing.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "shared" / "ifc" / "mappings" / "BUILDINGSMART_PSET_INDEX.json"
SCHEMAS = ("IFC4", "IFC4X3")


def build() -> str:
    try:
        import ifcopenshell
        import ifcopenshell.util.pset as pset
    except ImportError:
        print("bsi_pset_index: ifcopenshell is not installed (pip install ifcopenshell) - cannot build or check",
              file=sys.stderr)
        sys.exit(1)
    rows = []
    for schema in SCHEMAS:
        found = {}
        for f in pset.get_template(schema).templates:
            for t in f.by_type("IfcPropertySetTemplate"):
                if t.Name and t.Name.startswith(("Pset_", "Qto_")):
                    found[t.Name] = sorted({p.Name for p in (t.HasPropertyTemplates or [])})
        for name in sorted(found):
            rows.append({"schema": schema, "name": name, "properties": found[name]})
    head = {
        "_source": ("buildingSMART IFC4 / IFC4X3 property and quantity set templates (PSD), as bundled with "
                    f"ifcopenshell {ifcopenshell.version}. IFC4 set names match buildingSMART "
                    "reference_schemas/psd_IFC4_ADD2_TC1 in github.com/buildingSMART/IFC4.3.x-development."),
        "_generator": "tools/enums/bsi_pset_index.py - do not edit by hand.",
        "_rule": ("Pset_ / Qto_ are reserved for sets the IFC specification declares (IFC 4.3 IfcPropertySet). "
                  "Every Pset_* / Qto_* row of STING_IFC_PSET_MAPPING.json must name a property listed here for "
                  "both IFC4 and IFC4X3; anything else goes in a STING-owned Pset_Sting* set (shared/ifc/psets)."),
    }
    lines = ["{"]
    for k, v in head.items():
        lines.append(f"  {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)},")
    lines.append('  "sets": [')
    lines.append(",\n".join("    " + json.dumps(r, ensure_ascii=False) for r in rows))
    lines.append("  ]")
    lines.append("}")
    return "\n".join(lines) + "\n"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    text = build()
    if args.check:
        cur = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if cur != text:
            print(f"{OUT.relative_to(REPO).as_posix()} is stale - run python tools/enums/bsi_pset_index.py", file=sys.stderr)
            return 1
        print(f"{OUT.relative_to(REPO).as_posix()} is current")
        return 0
    OUT.write_text(text, encoding="utf-8", newline="\n")
    print(f"wrote {OUT.relative_to(REPO).as_posix()} ({text.count(chr(10))} lines)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
