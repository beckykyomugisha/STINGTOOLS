#!/usr/bin/env python3
"""
fix_material_data.py — align the STINGTOOLS material library data.

Addresses gaps E-11, E-13, E-15 and the two class errors in E-3 from
GUIDES/STINGTOOLS_GAPS_KIBALE_REVIEW.md. Pure data: no code change.

WHAT IT FIXES
  1. BLE_APP-IDENTITY-CLASS  — 9 invalid values (Generic, Ceiling, Paint,
     Flooring, Plaster, Fabric, Carpet, Lining) normalised to the 12 that
     Revit understands AND that STING_CARBON_FACTORS_UG.json byMaterialClass
     actually keys on. This is the single biggest win: it removes
     "Supply and fix generic walls" from the bill and gives ~300 rows a real
     carbon class instead of the flat 200 default.
  2. Two outright class errors — LIGHTWEIGHT SCREED classed Metal (carbon
     12,200 vs 290 — 42x), HERRINGBONE BLOCK PAVING classed Wood.
  3. MAT_NAME — ALL-CAPS, and NNNxNNN dimension separator (no spaces),
     so the substring matching for carbon and waste behaves predictably.
  4. MAT_COST_UNIT_OF_MEASURE — NEW column. The library has 72 columns and
     not one of them says whether a cost is per m2, per m3 or per block.
     A rate without a unit is not a rate.
  5. Empty MAT_ELEMENT_TYPE (5 MEP rows).

WHAT IT DELIBERATELY DOES NOT DO
  - Guess. A row whose class cannot be resolved by an explicit rule keeps its
    current value and is listed in the report as UNRESOLVED.
  - Touch MAT_COST_UNIT_USD or MAT_COST_UNIT_UGX. The currency defect (E-1b)
    is a code fix, not a data fix.
  - Renumber or restructure MAT_ISO_19650_ID (E-14) — that needs a convention
    decision first.

USAGE
    python tools/fix_material_data.py                 # dry run, prints report
    python tools/fix_material_data.py --report out.md # dry run + write report
    python tools/fix_material_data.py --apply         # writes, after .bak

Every run is idempotent: applying twice changes nothing the second time.
"""

import argparse
import csv
import json
import os
import re
import shutil
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "StingTools", "Data")

FILES = ["BLE_MATERIALS.csv", "MEP_MATERIALS.csv"]
SCHEMA = "MATERIAL_SCHEMA.json"

# The only 12 values that are BOTH a sensible Revit MaterialClass AND a key in
# STING_CARBON_FACTORS_UG.json -> byMaterialClass. Anything else resolves no
# carbon factor and prints verbatim as the leading noun of the bill description.
VALID_CLASSES = {
    "Concrete", "Metal", "Wood", "Masonry", "Glass", "Plastic",
    "Insulation", "Gypsum", "Ceramic", "Stone", "Liquid", "Earth",
}

# Ordered rules, FIRST MATCH WINS, matched against MAT_CATEGORY then MAT_NAME.
# Each carries the reasoning so the change is reviewable rather than magic.
CLASS_RULES = [
    # -- explicit corrections first (E-3) -------------------------------------
    (r"^SCREED-LIGHTWEIGHT|LIGHTWEIGHT SCREED", "Concrete",
     "cement-bound screed wrongly classed Metal -> carbon 12,200 vs 290"),
    (r"BLOCK-PAVING|BLOCK PAVING|HERRINGBONE", "Concrete",
     "concrete block paving wrongly classed Wood"),

    # -- APPLIED WET, and SOFT FURNISHING, must precede the substrate rules ----
    # PAINT-MASONRY is paint, not masonry. CARPET TILE is carpet, not ceramic.
    # These two were mis-resolving until the rules were reordered.
    (r"PAINT|COATING|SEALER|PRIMER|VARNISH|LACQUER|STAIN\b|"
     r"MEMBRANE-LIQUID|WATERPROOF-LIQUID", "Liquid",
     "applied wet, measured by volume"),
    (r"CARPET|FABRIC|TEXTILE|UNDERLAY", "Plastic",
     "synthetic textile floor covering"),

    # -- cementitious ---------------------------------------------------------
    (r"RENDER|SCREED|MORTAR|GROUT|CONCRETE|CEMENT|TERRAZZO|PRECAST", "Concrete",
     "cementitious"),

    # -- masonry --------------------------------------------------------------
    (r"\bBLOCK\b|BRICK|MASONRY|WALL ASSEMBLY|WALL-CORE|WALL-STRUCT", "Masonry",
     "masonry unit or masonry assembly"),

    # -- gypsum ---------------------------------------------------------------
    (r"GYPSUM|PLASTERBOARD|DRYWALL|PLASTER", "Gypsum",
     "gypsum-based board or plaster"),

    # -- ceramic --------------------------------------------------------------
    (r"CERAMIC|PORCELAIN|CLAY-TILE|CLAY TILE|QUARRY|MOSAIC|SUBWAY|ZELLIGE|"
     r"HEXAGON|\bTILES?\b", "Ceramic",
     "fired ceramic"),

    # -- stone (before ceramic: QUARTZITE TILES is stone, not fired clay) ------
    (r"STONE|GRANITE|MARBLE|SLATE|LIMESTONE|QUARTZ|TRAVERTINE|BASALT|"
     r"HARDCORE|AGGREGATE|BALLAST", "Stone",
     "natural stone or crushed stone"),

    # -- earth ----------------------------------------------------------------
    (r"MURRAM|LATERITE|SOIL|EARTH|CLAY-FILL", "Earth",
     "won earth / lateritic fill"),

    # -- timber ---------------------------------------------------------------
    (r"TIMBER|WOOD|PLY|MDF|CHIPBOARD|OSB|BAMBOO|PARQUET|THATCH|MAKUTI", "Wood",
     "timber or plant-fibre"),

    # -- metal ----------------------------------------------------------------
    (r"METAL|STEEL|ALUMIN|ZINC|COPPER|BRASS|GALV|IRON SHEET|TRAY|CONDUIT-STEEL|"
     r"SOCKET|SWITCH|LIGHTING|LUMINAIRE|DUCT-GALV|PURLIN", "Metal",
     "metal product or predominantly metal fitting"),

    # -- glass ----------------------------------------------------------------
    (r"GLASS|GLAZ|MIRROR", "Glass", "glass"),

    # -- insulation -----------------------------------------------------------
    (r"INSULAT|MINERAL WOOL|ROCKWOOL|ACOUSTIC|MF-TILE|CEILING-SPECIALTY|"
     r"FIBREGLASS|FIBERGLASS|PIR\b|PUR\b|EPS\b|XPS\b|SPRAY FOAM|"
     r"CALCIUM SILICATE|CLOUD PANEL", "Insulation",
     "thermal or acoustic insulation, or a mineral board"),

    # -- thin membranes and sheet goods ---------------------------------------
    (r"VAPOR BARRIER|VAPOUR BARRIER|HOUSE WRAP|\bDPM\b|\bDPC\b|RADON|"
     r"ICE & WATER|LINOLEUM|ANTI-STATIC|GRASSCLOTH|WALLCOVERING|WALLPAPER|"
     r"\bSILK\b", "Plastic",
     "sheet membrane or applied covering"),

    # -- adhesives ------------------------------------------------------------
    (r"ADHESIVE|MASTIC|SEALANT", "Liquid", "applied wet"),

    # -- plastic (last: many products mention a polymer in passing) ------------
    (r"PVC|VINYL|ACRYLIC|EPOXY|LAMINATE|RUBBER|POLY|PLASTIC|"
     r"NYLON|BITUMEN|FELT|MEMBRANE|GEOTEXT", "Plastic",
     "polymer"),
]

# Cost unit inference. FIRST MATCH WINS, on MAT_ELEMENT_TYPE then MAT_CATEGORY
# then MAT_NAME. Conservative: anything unmatched is left blank and reported,
# because a wrong unit is worse than a missing one.
UOM_RULES = [
    (r"\bBLOCK\b|BRICK", "each", "sold and priced per unit"),
    (r"CONCRETE|SCREED|MORTAR|RENDER-VOLUME|HARDCORE|MURRAM|AGGREGATE|SAND",
     "m3", "measured by volume"),
    (r"TIMBER|PURLIN|SKIRTING|BATTEN|TRIM|CORNICE|PIPE|CONDUIT|TRAY|CABLE|"
     r"DUCT|SECTION|BEAM|RAIL", "m", "measured by length"),
    (r"REBAR|REINFORCEMENT|STEEL-SECTION|MESH", "kg", "measured by mass"),
    (r"PAINT|SEALER|PRIMER|VARNISH", "L", "supplied by volume, applied by area"),
    (r"ADHESIVE|GROUT|PUTTY", "kg", "supplied by mass"),
    (r"SOCKET|SWITCH|LIGHTING|LUMINAIRE|FITTING|VALVE|DAMPER|OUTLET|SENSOR|"
     r"DETECTOR|SOUNDER|PANEL-BOARD", "each", "discrete item"),
    (r"CEILING|FLOOR|WALL|ROOF|TILE|CARPET|VINYL|MEMBRANE|INSULAT|PLASTER|"
     r"GYPSUM|GLASS|RENDER|CLADDING|SHEET", "m2", "measured by area"),
]

UOM_COL = "MAT_COST_UNIT_OF_MEASURE"


def norm_name(name: str) -> str:
    """ALL-CAPS, NNNxNNN dimensions, single-spaced."""
    s = name.upper()
    # 600 X 1200 -> 600X1200   (also 600 x1200, 600x 1200)
    s = re.sub(r"(?<=\d)\s*[X×]\s*(?=\d)", "X", s)
    s = re.sub(r"\s+", " ", s).strip()
    return s


def resolve_class(current, category, name, element_type):
    """Return (new_class, reason) or (None, None) when no rule fires."""
    if current in VALID_CLASSES:
        # Still run the two explicit corrections over already-'valid' values.
        hay = f"{category} {name}".upper()
        for pattern, cls, why in CLASS_RULES[:2]:
            if re.search(pattern, hay):
                return (cls, why) if cls != current else (None, None)
        return None, None
    hay = f"{category} {name} {element_type}".upper()
    for pattern, cls, why in CLASS_RULES:
        if re.search(pattern, hay):
            return cls, why
    return None, None


def resolve_uom(category, name, element_type):
    hay = f"{element_type} {category} {name}".upper()
    for pattern, uom, why in UOM_RULES:
        if re.search(pattern, hay):
            return uom, why
    return None, None


def load_csv(path):
    with open(path, encoding="utf-8-sig", errors="replace", newline="") as f:
        rows = list(csv.reader(f))
    if len(rows) < 3:
        raise SystemExit(f"{path}: expected a comment line, a header and data")
    return rows[0], rows[1], rows[2:]


def process(path, apply_changes, log):
    comment, header, data = load_csv(path)
    idx = {name: i for i, name in enumerate(header)}
    for req in ("MAT_NAME", "MAT_CATEGORY", "MAT_ELEMENT_TYPE",
                "BLE_APP-IDENTITY-CLASS"):
        if req not in idx:
            raise SystemExit(f"{path}: missing required column {req}")

    has_uom = UOM_COL in idx
    uom_i = idx[UOM_COL] if has_uom else len(header)

    stats = {
        "rows": 0, "class_changed": 0, "class_unresolved": [],
        "name_changed": 0, "uom_set": 0, "uom_unresolved": [],
        "et_filled": 0,
    }
    class_moves = Counter()
    name_examples, class_examples, uom_counts = [], [], Counter()

    out = []
    for r in data:
        if len(r) < len(header):
            r = r + [""] * (len(header) - len(r))
        stats["rows"] += 1

        name = r[idx["MAT_NAME"]]
        cat = r[idx["MAT_CATEGORY"]]
        et = r[idx["MAT_ELEMENT_TYPE"]]
        cls = r[idx["BLE_APP-IDENTITY-CLASS"]].strip()

        # 5 -- empty MAT_ELEMENT_TYPE (derive from the ISO id's 2nd segment)
        if not et.strip() and "MAT_ISO_19650_ID" in idx:
            parts = r[idx["MAT_ISO_19650_ID"]].split("-")
            if len(parts) >= 2:
                et = f"{parts[0]}-{parts[1]}"
                r[idx["MAT_ELEMENT_TYPE"]] = et
                stats["et_filled"] += 1

        # 3 -- name normalisation
        new_name = norm_name(name)
        if new_name != name:
            if len(name_examples) < 8:
                name_examples.append((name, new_name))
            r[idx["MAT_NAME"]] = new_name
            stats["name_changed"] += 1
            name = new_name

        # 1 + 2 -- identity class
        new_cls, why = resolve_class(cls, cat, name, et)
        if new_cls:
            class_moves[(cls or "(empty)", new_cls)] += 1
            if len(class_examples) < 10:
                class_examples.append((name, cls, new_cls, why))
            r[idx["BLE_APP-IDENTITY-CLASS"]] = new_cls
            stats["class_changed"] += 1
        elif cls not in VALID_CLASSES:
            stats["class_unresolved"].append((name, cls, cat))

        # 4 -- cost unit of measure
        while len(r) <= uom_i:
            r.append("")
        if not r[uom_i].strip():
            uom, _ = resolve_uom(cat, name, et)
            if uom:
                r[uom_i] = uom
                uom_counts[uom] += 1
                stats["uom_set"] += 1
            else:
                stats["uom_unresolved"].append((name, cat))
        out.append(r)

    new_header = header if has_uom else header + [UOM_COL]

    log.append(f"\n## {os.path.basename(path)} — {stats['rows']} rows\n")
    log.append(f"- identity class changed: **{stats['class_changed']}**")
    log.append(f"- identity class still unresolved: **{len(stats['class_unresolved'])}**")
    log.append(f"- names normalised: **{stats['name_changed']}**")
    log.append(f"- `{UOM_COL}` populated: **{stats['uom_set']}**"
               + ("" if has_uom else "  _(column added)_"))
    log.append(f"- cost unit unresolved: **{len(stats['uom_unresolved'])}**")
    log.append(f"- empty MAT_ELEMENT_TYPE filled: **{stats['et_filled']}**")

    if class_moves:
        log.append("\n**Class moves**\n")
        log.append("| from | to | rows |")
        log.append("|---|---|---|")
        for (a, b), n in class_moves.most_common():
            log.append(f"| {a} | {b} | {n} |")

    if class_examples:
        log.append("\n**Sample class decisions**\n")
        log.append("| material | from | to | why |")
        log.append("|---|---|---|---|")
        for nm, a, b, why in class_examples:
            log.append(f"| {nm[:44]} | {a} | {b} | {why} |")

    if name_examples:
        log.append("\n**Sample name normalisations**\n")
        log.append("| before | after |")
        log.append("|---|---|")
        for a, b in name_examples:
            log.append(f"| `{a}` | `{b}` |")

    if uom_counts:
        log.append("\n**Cost units inferred**: "
                   + ", ".join(f"`{k}` {v}" for k, v in uom_counts.most_common()))

    if stats["class_unresolved"]:
        log.append("\n**UNRESOLVED classes — left untouched, decide by hand**\n")
        log.append("| material | current class | category |")
        log.append("|---|---|---|")
        for nm, c, ct in stats["class_unresolved"][:25]:
            log.append(f"| {nm[:44]} | {c} | {ct[:30]} |")
        if len(stats["class_unresolved"]) > 25:
            log.append(f"| _… {len(stats['class_unresolved']) - 25} more_ | | |")

    if stats["uom_unresolved"]:
        log.append(f"\n**UNRESOLVED cost units — left blank** "
                   f"({len(stats['uom_unresolved'])}). First 15:\n")
        log.append("| material | category |")
        log.append("|---|---|")
        for nm, ct in stats["uom_unresolved"][:15]:
            log.append(f"| {nm[:44]} | {ct[:34]} |")

    if apply_changes:
        shutil.copy2(path, path + ".bak")
        with open(path, "w", encoding="utf-8", newline="") as f:
            w = csv.writer(f, lineterminator="\n")
            w.writerow(comment)
            w.writerow(new_header)
            w.writerows(out)
        log.append(f"\n_Written. Backup at `{os.path.basename(path)}.bak`._")

    return stats, (not has_uom)


def update_schema(apply_changes, log):
    """Keep MATERIAL_SCHEMA.json honest: it already declares 70 for 72 real
    columns (gap E-8). Add the two carbon columns AND the new UOM column."""
    path = os.path.join(DATA, SCHEMA)
    if not os.path.exists(path):
        log.append(f"\n_{SCHEMA} not found — skipped._")
        return
    with open(path, encoding="utf-8") as f:
        schema = json.load(f)

    order = schema.get("column_order", [])
    missing = [c for c in ("PROP_CARBON_FOSSIL_KG_M3",
                           "PROP_CARBON_BIOGENIC_KG_M3",
                           UOM_COL) if c not in order]
    if not missing:
        log.append(f"\n_{SCHEMA} already current._")
        return

    log.append(f"\n## {SCHEMA}\n")
    log.append(f"- declared columns before: **{len(order)}** (real files carry 72)")
    log.append(f"- adding: {', '.join('`' + m + '`' for m in missing)}")

    if apply_changes:
        shutil.copy2(path, path + ".bak")
        order.extend(missing)
        schema["column_order"] = order
        if "required_columns" in schema:
            schema["required_columns"] = [c for c in order]
        schema.setdefault("column_types", {})
        for m in missing:
            schema["column_types"][m] = "string" if m == UOM_COL else "float"
        with open(path, "w", encoding="utf-8") as f:
            json.dump(schema, f, indent=2, ensure_ascii=False)
            f.write("\n")
        log.append(f"- declared columns after: **{len(order)}**")
        log.append(f"\n_Written. Backup at `{SCHEMA}.bak`._")


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--apply", action="store_true",
                    help="write the files (default is a dry run)")
    ap.add_argument("--report", metavar="PATH", help="write the report to a file")
    args = ap.parse_args()

    log = ["# Material data alignment — "
           + ("APPLIED" if args.apply else "DRY RUN (nothing written)")]

    total_class = total_name = total_uom = 0
    added_col = False
    for fn in FILES:
        path = os.path.join(DATA, fn)
        if not os.path.exists(path):
            log.append(f"\n_{fn} not found — skipped._")
            continue
        st, added = process(path, args.apply, log)
        total_class += st["class_changed"]
        total_name += st["name_changed"]
        total_uom += st["uom_set"]
        added_col = added_col or added

    if added_col or args.apply:
        update_schema(args.apply, log)

    log.insert(1, f"\n**Totals — {total_class} class fixes, {total_name} name "
                  f"normalisations, {total_uom} cost units inferred.**\n")
    if not args.apply:
        log.append("\n---\n\nDry run. Re-run with `--apply` to write "
                   "(`.bak` backups are made automatically).")

    text = "\n".join(log)
    print(text)
    if args.report:
        with open(args.report, "w", encoding="utf-8") as f:
            f.write(text + "\n")
        print(f"\n[report written to {args.report}]", file=sys.stderr)


if __name__ == "__main__":
    main()
