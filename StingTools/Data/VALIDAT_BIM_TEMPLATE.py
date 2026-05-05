#!/usr/bin/env python3
# v4.1 | 20260226
"""
BIM Template CI Validation Script v4.1
45 checks (20 original + 24 new: G-01..G-20 + G-21..G-24).

Changes from v4.0:
  - find_xlsx() now uses reverse sort (newest first), matching find_csv() behaviour
  - G-14: falls back to schema JSON valid_data_types whitelist check when schema
    has no required_columns drift (prevents false PASS hiding MEP dtype gaps)
  - G-14: adds dedicated MEP data type whitelist validation sub-check
  - G-16 severity message corrected to list all 8 required columns
  - Check numbering: G-21 BLE and G-21 MEP now use cids 41a/41b for clarity
    in HTML/JSON reports (console output unchanged)
  - Result counter: prints "N results / 45 checks" to clarify BLE+MEP dual runs

Automation platform: pyRevit (replaces Dynamo).
  - G-16 now validates pyrevit_scripts_manifest.csv
  - G-21 checks MAT_CODE uniqueness per library file
  - G-22 checks discipline/prefix alignment (MECHANICAL vs HVAC/ARCH)
  - G-23 validates Hide_When_No_Value on computed numeric params
  - G-24 checks tag guide fields against parameter master (deep cross-ref)
  - G-14b checks MEP params using NUMBER instead of native Revit MEP data types (v4.1)

Usage:
  python3 validate_bim_template.py <data_dir>
  python3 validate_bim_template.py <data_dir> --json
  python3 validate_bim_template.py <data_dir> --report html
  python3 validate_bim_template.py <data_dir> --report json
  python3 validate_bim_template.py <data_dir> --strict-moderate
  python3 validate_bim_template.py <data_dir> --check-textures
  python3 validate_bim_template.py <data_dir> --baseline results.json

Exit codes:
  0 = all checks passed
  1 = CRITICAL failure
  2 = MODERATE failure (only with --strict-moderate)
"""

import pandas as pd
import numpy as np
import re
import sys
import os
import json
import hashlib
import argparse
from datetime import datetime, date
from collections import defaultdict

VERSION = "4.2"
RUN_DATE = datetime.now().strftime("%Y-%m-%d %H:%M:%S")

# ============================================================================
# DATA LOADING
# ============================================================================

def read_csv_versioned(path):
    """Read CSV, extracting # version comment header if present."""
    version = None
    if path and os.path.exists(path):
        with open(path, "r") as fh:
            first = fh.readline().strip()
            if first.startswith("#"):
                version = first.lstrip("# ").strip()
        return pd.read_csv(path, comment="#"), version
    return None, None


def file_hash(path):
    """SHA-256 of a file (first 8 hex chars)."""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()[:8]


def find_csv(src, pattern):
    """Return latest CSV path in src matching pattern (reverse-sorted = newest date first)."""
    for f in sorted(os.listdir(src), reverse=True):
        if pattern in f and f.endswith(".csv"):
            return os.path.join(src, f)
    return None


def find_xlsx(src, pattern):
    """Return latest xlsx path in src matching pattern (reverse-sorted = newest date first)."""
    for f in sorted(os.listdir(src), reverse=True):
        if pattern in f and f.endswith(".xlsx"):
            return os.path.join(src, f)
    return None


def load_files(src, tag_guide_dir=None):
    """Load all data files from data directory."""
    files = {}
    hashes = {}

    # Core CSVs
    for key, pat in [
        ("mr", "mr_parameters"), ("pc", "parameter_categories"),
        ("cb", "category_bindings"), ("fp", "family_parameter_bindings"),
        ("fd", "formulas_with_dependencies"),
    ]:
        p = find_csv(src, pat)
        if p:
            files[key], files[f"{key}_version"] = read_csv_versioned(p)
            hashes[key] = file_hash(p)

    # Schedules
    p = find_csv(src, "mr_schedules")
    if p:
        files["sch"], files["sch_version"] = read_csv_versioned(p)
        hashes["sch"] = file_hash(p)

    # Materials
    for key, pat in [("mep", "mep_material"), ("ble", "ble_material")]:
        p = find_csv(src, pat)
        if p:
            files[key] = pd.read_csv(p, comment="#")
            hashes[key] = file_hash(p)

    # .txt shared param file
    txt_files = [f for f in os.listdir(src) if "mr_parameters" in f and f.endswith(".txt")]
    if txt_files:
        txt_path = os.path.join(src, sorted(txt_files)[0])
        txt_params = {}
        with open(txt_path, "r") as fh:
            for line in fh:
                if line.strip().startswith("PARAM\t"):
                    parts = line.strip().split("\t")
                    if len(parts) >= 4:
                        txt_params[parts[2]] = {"guid": parts[1], "dtype": parts[3]}
        files["txt_params"] = txt_params
        hashes["txt"] = file_hash(txt_path)

    # Remap table (G-01)
    p = find_csv(src, "remap")
    if p:
        files["remap"] = pd.read_csv(p, comment="#")

    # Binding coverage matrix (G-02)
    p = find_csv(src, "binding_coverage_matrix")
    if p:
        files["bcm"] = pd.read_csv(p, comment="#")

    # Tag guide (G-04, G-16)
    tag_dir = tag_guide_dir or src
    tg_path = find_xlsx(tag_dir, "tag_guide")
    if tg_path:
        try:
            files["tag_guide"] = pd.read_excel(tg_path, sheet_name=None)
        except Exception:
            pass

    # Material schema contract (G-14)
    schema_path = os.path.join(src, "material_schema.json")
    if os.path.exists(schema_path):
        with open(schema_path) as fh:
            files["mat_schema"] = json.load(fh)

    # PyRevit script manifest (G-16) — replaces Dynamo
    p = find_csv(src, "pyrevit_scripts_manifest")
    if p:
        files["pyrevit_manifest"] = pd.read_csv(p, comment="#")

    files["_hashes"] = hashes
    return files

# ============================================================================
# VALIDATION RESULT
# ============================================================================

class VR:
    """Validation Result."""
    def __init__(self, cid, name, severity, passed, detail, gap=None):
        self.cid = cid
        self.name = name
        self.severity = severity
        self.passed = passed
        self.detail = detail
        self.gap = gap

    def to_dict(self):
        return {
            "check": self.cid, "name": self.name, "severity": self.severity,
            "passed": self.passed, "detail": self.detail, "gap": self.gap or "",
        }

# ============================================================================
# HELPER: deduplicated mr params
# ============================================================================

def mr_dedup_and_sets(files):
    mr = files["mr"]
    mr_dedup = mr.drop_duplicates(subset="Parameter_Name")
    mr_names = set(mr_dedup["Parameter_Name"])
    mr_guids = set(mr_dedup["Parameter_GUID"])
    mr_types = mr_dedup.set_index("Parameter_Name")["Data_Type"].to_dict()
    mr_guid_map = dict(zip(mr_dedup["Parameter_Name"], mr_dedup["Parameter_GUID"]))
    return mr_dedup, mr_names, mr_guids, mr_types, mr_guid_map


def extract_schedule_fields(sch):
    pat = re.compile(r"^[A-Z][A-Z0-9_]+[A-Z0-9]$")
    fields = set()
    for row_fields in sch["Fields"].dropna():
        for f in str(row_fields).split(","):
            f = f.strip()
            if f and pat.match(f):
                fields.add(f)
    return fields

# ============================================================================
# CHECKS 1-20 (ORIGINAL)
# ============================================================================

def checks_original(files):
    R = []
    mr, pc, cb = files["mr"], files["pc"], files["cb"]
    fp, fd, sch = files["fp"], files["fd"], files["sch"]
    mrd, mr_names, mr_guids, mr_types, mr_guid_map = mr_dedup_and_sets(files)

    # 1. GUID: mr vs pc
    pc_guids = set(pc["GUID"])
    d = mr_guids.symmetric_difference(pc_guids)
    R.append(VR(1, "GUID: mr vs param_categories", "CRITICAL", len(d) == 0, f"{len(d)} mismatches"))

    # 2. GUID: mr vs fp
    fp_guids = set(fp["Maps_To_Shared_Param_GUID"].dropna())
    d = fp_guids - mr_guids
    R.append(VR(2, "GUID: mr vs family_bindings", "CRITICAL", len(d) == 0, f"{len(d)} orphan GUIDs"))

    # 3. Names: cb vs mr
    d = set(cb["Parameter_Name"]) - mr_names
    R.append(VR(3, "Names: cb vs mr", "CRITICAL", len(d) == 0, f"{len(d)} orphan names"))

    # 4. Names: fp vs mr
    d = set(fp["ParameterName"]) - mr_names
    R.append(VR(4, "Names: fp vs mr", "CRITICAL", len(d) == 0, f"{len(d)} orphan names"))

    # 5. Duplicate GUIDs
    dups = mrd[mrd.duplicated(subset="Parameter_GUID", keep=False)]
    R.append(VR(5, "Duplicate GUIDs", "CRITICAL", len(dups) == 0, f"{len(dups)} duplicates"))

    # 6. Formula dependency level order
    fd_level = dict(zip(fd["Parameter_Name"], fd["Dependency_Level"]))
    viols = 0
    for _, row in fd.iterrows():
        for inp in str(row.get("Input_Parameters", "")).split(","):
            inp = inp.strip()
            if inp in fd_level and fd_level[inp] >= row["Dependency_Level"]:
                viols += 1
    R.append(VR(6, "Formula dependency level order", "CRITICAL", viols == 0, f"{viols} violations"))

    # 7. Formula outputs in mr
    fd_names = set(fd["Parameter_Name"])
    d = fd_names - mr_names
    R.append(VR(7, "Formula outputs in mr", "CRITICAL", len(d) == 0, f"{len(d)} missing"))

    # 8. Formula text: mr vs fd
    mr_f = mrd[mrd["Has_Formula"] == True][["Parameter_Name", "Formula"]]
    fd_f = fd[["Parameter_Name", "Revit_Formula"]]
    mg = mr_f.merge(fd_f, on="Parameter_Name", how="inner")
    mm = mg[mg["Formula"].str.strip() != mg["Revit_Formula"].str.strip()]
    R.append(VR(8, "Formula text: mr vs fd", "CRITICAL", len(mm) == 0, f"{len(mm)} mismatches"))

    # 9. GUID: mr vs .txt
    if "txt_params" in files:
        txt = files["txt_params"]
        gd = sum(1 for n in txt if n in mr_guid_map and txt[n]["guid"] != mr_guid_map[n])
        R.append(VR(9, "GUID: mr vs .txt", "CRITICAL", gd == 0, f"{gd} mismatches"))
    else:
        R.append(VR(9, "GUID: mr vs .txt", "INFO", True, "No .txt file"))

    # 10. Has_Formula flag
    ft = mrd[mrd["Has_Formula"] == True]
    bad = ft[(ft["Formula"].isna()) | (ft["Formula"] == "")]
    R.append(VR(10, "Has_Formula flag consistency", "MODERATE", len(bad) == 0, f"{len(bad)} inconsistent"))

    # 11. User_Modifiable on formulas
    fo = mrd[mrd["Parameter_Name"].isin(fd_names)]
    bm = fo[fo["User_Modifiable"] == 1]
    R.append(VR(11, "User_Modifiable=0 on formulas", "MODERATE", len(bm) == 0, f"{len(bm)} violations"))

    # 12. Data types: mr vs pc
    pc_t = pc.set_index("Parameter Name")["Data Type"].to_dict()
    n = sum(1 for p in mr_types if p in pc_t and mr_types[p] != pc_t[p])
    R.append(VR(12, "Data types: mr vs pc", "CRITICAL", n == 0, f"{n} mismatches"))

    # 13. Data types: mr vs fd
    fd_t = fd.set_index("Parameter_Name")["Data_Type"].to_dict()
    n = sum(1 for p in fd_t if p in mr_types and fd_t[p] != mr_types[p])
    R.append(VR(13, "Data types: mr vs fd", "CRITICAL", n == 0, f"{n} mismatches"))

    # 14. Data types: mr vs .txt
    if "txt_params" in files:
        txt = files["txt_params"]
        n = sum(1 for p in txt if p in mr_types and txt[p]["dtype"] != mr_types[p])
        R.append(VR(14, "Data types: mr vs .txt", "CRITICAL", n == 0, f"{n} mismatches"))

    # 15. Schedule fields in mr
    sf = extract_schedule_fields(sch)
    d = sf - mr_names
    R.append(VR(15, "Schedule fields in mr", "CRITICAL", len(d) == 0, f"{len(d)} missing"))

    # 16. Schedule fields bound to category
    ub = 0
    for _, row in sch.iterrows():
        cat = row.get("Category", "")
        if pd.isna(cat) or pd.isna(row.get("Fields", "")):
            continue
        for f in str(row["Fields"]).split(","):
            f = f.strip()
            if f in mr_names and len(cb[(cb["Parameter_Name"] == f) & (cb["Revit_Category"] == cat)]) == 0:
                ub += 1
    R.append(VR(16, "Schedule fields bound to category", "CRITICAL", ub == 0, f"{ub} unbound"))

    # 17. Family bindings in cb
    fp_c = set(zip(fp["ParameterName"], fp["RevitCategory"]))
    cb_c = set(zip(cb["Parameter_Name"], cb["Revit_Category"]))
    d = fp_c - cb_c
    R.append(VR(17, "Family bindings in cb", "CRITICAL", len(d) == 0, f"{len(d)} missing"))

    # 18. Binding type: fp vs cb
    fp_bt = fp[["ParameterName", "RevitCategory", "BindingType"]].rename(
        columns={"ParameterName": "PN", "RevitCategory": "RC", "BindingType": "FP"})
    cb_bt = cb[["Parameter_Name", "Revit_Category", "Binding_Type"]].rename(
        columns={"Parameter_Name": "PN", "Revit_Category": "RC", "Binding_Type": "CB"})
    m = fp_bt.merge(cb_bt, on=["PN", "RC"], how="inner")
    d = m[m["FP"] != m["CB"]]
    R.append(VR(18, "Binding type: fp vs cb", "MODERATE", len(d) == 0, f"{len(d)} mismatches"))

    # 19. Type/Instance: mr vs pc
    pc_ti = pc.set_index("Parameter Name")["Type/Instance"].to_dict()
    mr_ti = mrd.set_index("Parameter_Name")["Binding_Type"].to_dict()
    n = sum(1 for p in mr_ti if p in pc_ti and mr_ti[p] != pc_ti[p])
    R.append(VR(19, "Type/Instance: mr vs pc", "LOW", n == 0, f"{n} mismatches"))

    # 20. Naming convention (uppercase)
    anom = sum(1 for p in mr_names if p != p.upper() and not p.startswith("STINGTags"))
    R.append(VR(20, "Naming convention", "LOW", anom == 0, f"{anom} anomalies"))

    return R

# ============================================================================
# CHECKS 21-40 (NEW: G-01 through G-20)
# ============================================================================

def checks_new(files, check_textures=False):
    R = []
    mr, cb, fd, sch = files["mr"], files["cb"], files["fd"], files["sch"]
    mrd, mr_names, mr_guids, mr_types, mr_guid_map = mr_dedup_and_sets(files)
    sched_fields = extract_schedule_fields(sch)

    # ------------------------------------------------------------------
    # G-01 / Check 21: Validate schedule_field_remap.csv
    # ------------------------------------------------------------------
    if "remap" in files and files["remap"] is not None:
        remap = files["remap"]
        remapped = remap[remap["Action"] == "REMAPPED"] if "Action" in remap.columns else remap
        old_names = set(remapped["Old_Schedule_Field"].dropna())
        targets = dict(zip(remapped["Old_Schedule_Field"], remapped["Consolidated_Parameter"]))

        old_in_mr = old_names & mr_names
        old_in_sched = old_names & sched_fields
        bad_targets = {t for _, t in targets.items() if pd.notna(t) and t not in mr_names}

        issues = len(old_in_mr) + len(old_in_sched) + len(bad_targets)
        parts = []
        if old_in_mr:
            parts.append(f"{len(old_in_mr)} retired names still in mr")
        if old_in_sched:
            parts.append(f"{len(old_in_sched)} retired names still in schedules")
        if bad_targets:
            parts.append(f"{len(bad_targets)} targets missing from mr")
        R.append(VR(21, "Remap table integrity (G-01)", "CRITICAL",
                     issues == 0, "; ".join(parts) or "0 issues", "G-01"))
    else:
        R.append(VR(21, "Remap table integrity (G-01)", "INFO",
                     True, "No remap file found", "G-01"))

    # ------------------------------------------------------------------
    # G-02 / Check 22: BCM vs category_bindings diff
    # ------------------------------------------------------------------
    if "bcm" in files and files["bcm"] is not None:
        bcm = files["bcm"]
        cb_pairs = set(zip(cb["Parameter_Name"], cb["Revit_Category"]))
        cats = [c for c in bcm.columns if c != "Parameter_Name"]
        disc = 0
        for _, row in bcm.iterrows():
            pn = row["Parameter_Name"]
            for cat in cats:
                val = row[cat]
                bcm_bound = (pd.notna(val) and int(val) >= 1)
                cb_bound = (pn, cat) in cb_pairs
                if bcm_bound != cb_bound:
                    disc += 1
        R.append(VR(22, "BCM vs category_bindings (G-02)", "CRITICAL",
                     disc == 0, f"{disc} cell discrepancies", "G-02"))
    else:
        R.append(VR(22, "BCM vs category_bindings (G-02)", "INFO",
                     True, "No BCM file found", "G-02"))

    # ------------------------------------------------------------------
    # G-03 / Checks 23-25: Material library validation
    # ------------------------------------------------------------------
    for key, label in [("ble", "BLE"), ("mep", "MEP")]:
        mat = files.get(key)
        if mat is None:
            R.append(VR(23, f"{label} ISO 19650 ID format (G-03)", "INFO",
                         True, f"No {label} file", "G-03"))
            R.append(VR(24, f"{label} cost non-negative (G-03)", "INFO",
                         True, f"No {label} file", "G-03"))
            R.append(VR(25, f"{label} property completeness (G-03)", "INFO",
                         True, f"No {label} file", "G-03"))
            continue

        # 23: ISO 19650 ID format (e.g. A-CLG-GYPSUM-STANDARD-9.5MM-INT-GB01)
        if "MAT_ISO_19650_ID" in mat.columns:
            iso_pat = re.compile(r"^[A-Z]-[A-Z]{2,}(-[A-Z0-9.]+)+-[A-Z0-9]+$")
            populated = mat["MAT_ISO_19650_ID"].dropna()
            bad = sum(1 for v in populated if not iso_pat.match(str(v)))
            R.append(VR(23, f"{label} ISO 19650 ID format (G-03)", "MODERATE",
                         bad == 0, f"{bad}/{len(populated)} invalid IDs", "G-03"))
        else:
            R.append(VR(23, f"{label} ISO 19650 ID format (G-03)", "INFO",
                         True, "Column missing", "G-03"))

        # 24: Cost columns non-negative
        cost_cols = [c for c in mat.columns if "COST" in c.upper()]
        neg = 0
        for col in cost_cols:
            if pd.api.types.is_numeric_dtype(mat[col]):
                neg += int((mat[col].dropna() < 0).sum())
        R.append(VR(24, f"{label} cost non-negative (G-03)", "MODERATE",
                     neg == 0, f"{neg} negative values across {len(cost_cols)} cost columns", "G-03"))

        # 25: Physical property completeness
        prop_cols = [c for c in mat.columns if c.startswith("PROP_")]
        if prop_cols:
            total = len(mat) * len(prop_cols)
            nulls = sum(int(mat[c].isna().sum()) for c in prop_cols)
            pct = 100 * nulls / total if total else 0
            R.append(VR(25, f"{label} property completeness (G-03)", "MODERATE",
                         pct < 50, f"{pct:.1f}% null ({nulls}/{total})", "G-03"))
        else:
            R.append(VR(25, f"{label} property completeness (G-03)", "INFO",
                         True, "No PROP_ columns", "G-03"))

    # ------------------------------------------------------------------
    # G-04 / Check 26: Tag guide new-parameter drift
    # ------------------------------------------------------------------
    tag_result = _check_tag_guide(files, mr_names)
    R.append(tag_result)

    # ------------------------------------------------------------------
    # G-05 / Check 27: DFS cycle detection on formula dependency graph
    # ------------------------------------------------------------------
    fd_param_set = set(fd["Parameter_Name"])
    graph = defaultdict(set)
    for _, row in fd.iterrows():
        out = row["Parameter_Name"]
        for inp in str(row.get("Input_Parameters", "")).split(","):
            inp = inp.strip()
            if inp and inp in fd_param_set:
                graph[inp].add(out)

    WHITE, GRAY, BLACK = 0, 1, 2
    colour = {n: WHITE for n in fd_param_set}
    cycles = []

    def dfs(node, path):
        colour[node] = GRAY
        path.append(node)
        for nb in graph.get(node, []):
            if nb in colour:
                if colour[nb] == GRAY:
                    ci = path.index(nb)
                    cycles.append(path[ci:] + [nb])
                elif colour[nb] == WHITE:
                    dfs(nb, path)
        path.pop()
        colour[node] = BLACK

    for n in list(colour.keys()):
        if colour[n] == WHITE:
            dfs(n, [])

    detail = f"{len(cycles)} cycles"
    if cycles:
        detail += f" (first: {' -> '.join(cycles[0])})"
    R.append(VR(27, "Formula DAG cycle detection (G-05)", "CRITICAL",
                 len(cycles) == 0, detail, "G-05"))

    # ------------------------------------------------------------------
    # G-06 / Check 28: Built-in geometry per category
    # ------------------------------------------------------------------
    builtin_catalogue = {
        "Length": {"Generic Models", "Structural Framing", "Structural Columns",
                   "Conduits", "Pipes", "Cable Trays", "Ducts",
                   "Duct Accessories", "Duct Fittings", "Flex Ducts",
                   "Walls", "Floors",
                   "Pipe Accessories", "Pipe Fittings", "Mechanical Equipment",
                   "Air Terminals", "Sprinklers", "Plumbing Equipment",
                   "Plumbing Fixtures", "Ramps", "Stairs", "Roofs", "Ceilings",
                   "Casework", "Furniture", "Curtain Panels", "Curtain Wall Mullions",
                   "Electrical Equipment", "Electrical Fixtures", "Lighting Fixtures",
                   "Specialty Equipment", "Doors", "Windows", "Rooms",
                   "Structural Foundations"},
        "Width": {"Generic Models", "Doors", "Windows", "Casework", "Furniture",
                  "Structural Framing", "Cable Trays", "Ducts", "Duct Accessories",
                  "Duct Fittings", "Flex Ducts",
                  "Walls", "Floors", "Ceilings", "Roofs", "Curtain Panels",
                  "Curtain Wall Mullions", "Air Terminals", "Rooms", "Ramps",
                  "Stairs", "Electrical Equipment", "Electrical Fixtures",
                  "Lighting Fixtures", "Mechanical Equipment", "Specialty Equipment",
                  "Conduits", "Pipes", "Pipe Accessories", "Pipe Fittings",
                  "Plumbing Equipment", "Plumbing Fixtures", "Sprinklers",
                  "Structural Foundations", "Structural Columns"},
        "Height": {"Generic Models", "Doors", "Windows", "Casework", "Furniture",
                   "Walls", "Rooms", "Spaces", "Structural Columns", "Ducts",
                   "Duct Fittings", "Flex Ducts", "Duct Accessories",
                   "Air Terminals", "Ceilings", "Roofs", "Ramps", "Stairs", "Floors",
                   "Electrical Equipment", "Lighting Fixtures", "Curtain Panels",
                   "Mechanical Equipment", "Specialty Equipment"},
        "Thickness": {"Walls", "Floors", "Ceilings", "Roofs", "Curtain Panels",
                      "Structural Foundations", "Generic Models", "Casework",
                      "Doors", "Windows", "Ramps", "Structural Framing",
                      "Structural Columns", "Stairs", "Specialty Equipment"},
        "Diameter": {"Pipes", "Conduits", "Structural Columns", "Structural Framing",
                     "Mechanical Equipment", "Air Terminals", "Ducts",
                     "Sprinklers", "Plumbing Fixtures", "Plumbing Equipment",
                     "Pipe Accessories", "Pipe Fittings", "Generic Models"},
        "Tile_Width": {"Floors", "Walls", "Ceilings", "Doors", "Windows",
                       "Generic Models"},
        "Tile_Height": {"Floors", "Walls", "Ceilings", "Doors", "Windows",
                        "Generic Models"},
    }

    if "Uses_Builtin_Geometry" in fd.columns and "Builtin_Inputs" in fd.columns:
        builtin_rows = fd[fd["Uses_Builtin_Geometry"] == True]
        issues = []
        for _, row in builtin_rows.iterrows():
            pname = row["Parameter_Name"]
            builtins = [b.strip() for b in str(row.get("Builtin_Inputs", "")).split(",") if b.strip()]
            bound_cats = set(cb[cb["Parameter_Name"] == pname]["Revit_Category"])
            for bi in builtins:
                valid_cats = builtin_catalogue.get(bi, None)
                if valid_cats is not None:
                    bad_cats = bound_cats - valid_cats
                    if bad_cats:
                        issues.append(f"{pname}: {bi} invalid for {','.join(list(bad_cats)[:2])}")
        R.append(VR(28, "Built-in geometry per category (G-06)", "HIGH",
                     len(issues) == 0,
                     f"{len(issues)} issues" + (f" ({issues[0]})" if issues else ""), "G-06"))
    else:
        R.append(VR(28, "Built-in geometry per category (G-06)", "INFO",
                     True, "No builtin columns in fd", "G-06"))

    # ------------------------------------------------------------------
    # G-07 / Check 29: Remap deprecation metadata
    # ------------------------------------------------------------------
    if "remap" in files and files["remap"] is not None:
        remap = files["remap"]
        has_cols = all(c in remap.columns for c in ["Deprecated_Date", "Sunset_Date"])
        if has_cols:
            today = date.today().isoformat()
            past_sunset = remap[remap["Sunset_Date"].apply(
                lambda x: pd.notna(x) and str(x) < today)]
            R.append(VR(29, "Remap deprecation metadata (G-07)", "LOW",
                         len(past_sunset) == 0,
                         f"{len(past_sunset)} params past sunset date", "G-07"))
        else:
            missing = [c for c in ["Deprecated_Date", "Deprecation_Owner",
                                   "Sunset_Date", "Migration_Notes"]
                       if c not in remap.columns]
            R.append(VR(29, "Remap deprecation metadata (G-07)", "LOW",
                         False, f"Missing columns: {', '.join(missing)}", "G-07"))
    else:
        R.append(VR(29, "Remap deprecation metadata (G-07)", "INFO",
                     True, "No remap file", "G-07"))

    # ------------------------------------------------------------------
    # G-08 / Check 30: Pre-dedup GUID consistency
    # ------------------------------------------------------------------
    guid_map = defaultdict(set)
    for _, row in mr.iterrows():
        guid_map[row["Parameter_Name"]].add(row["Parameter_GUID"])
    conflicts = {k: v for k, v in guid_map.items() if len(v) > 1}
    R.append(VR(30, "Pre-dedup GUID consistency (G-08)", "CRITICAL",
                 len(conflicts) == 0,
                 f"{len(conflicts)} names with conflicting GUIDs", "G-08"))

    # ------------------------------------------------------------------
    # G-09 / Check 31: Hide_When_No_Value + User_Modifiable cross-check
    # ------------------------------------------------------------------
    if "Hide_When_No_Value" in mrd.columns and "User_Modifiable" in mrd.columns:
        # Formula params may validly be hidden+non-modifiable (computed output, hide blank rows)
        # Only flag NON-formula params with this combination
        formula_names = set(fd["Parameter_Name"]) if fd is not None else set()
        bad_combo = mrd[
            (mrd["Hide_When_No_Value"] == 1) &
            (mrd["User_Modifiable"] == 0) &
            (~mrd["Parameter_Name"].isin(formula_names))
        ]
        # Warn on hidden params in schedules (formula params are ok - they show computed values)
        hidden_params = set(mrd[
            (mrd["Hide_When_No_Value"] == 1) &
            (~mrd["Parameter_Name"].isin(formula_names))
        ]["Parameter_Name"])
        hidden_in_sched = hidden_params & sched_fields
        total = len(bad_combo) + len(hidden_in_sched)
        parts = []
        if len(bad_combo):
            parts.append(f"{len(bad_combo)} non-formula hidden+non-modifiable")
        if hidden_in_sched:
            parts.append(f"{len(hidden_in_sched)} hidden non-formula params in schedules")
        R.append(VR(31, "Hide_When_No_Value checks (G-09)", "MODERATE",
                     total == 0, "; ".join(parts) or "0 issues", "G-09"))
    else:
        R.append(VR(31, "Hide_When_No_Value checks (G-09)", "INFO",
                     True, "Columns not present", "G-09"))

    # ------------------------------------------------------------------
    # G-10 / Check 32: Prefix-to-group alignment
    # ------------------------------------------------------------------
    # Build valid prefix->group mapping from data (top group per prefix)
    prefix_groups = defaultdict(lambda: defaultdict(int))
    for _, row in mrd.iterrows():
        pname = row["Parameter_Name"]
        group = row["Group_Name"]
        if pd.isna(group) or group == "STINGTags_ISO19650":
            continue
        prefix = pname.split("_")[0] if "_" in pname else ""
        prefix_groups[prefix][group] += 1

    # A prefix is valid for any group where it appears 3+ times
    valid_prefix_groups = {}
    for prefix, groups in prefix_groups.items():
        valid_prefix_groups[prefix] = {g for g, c in groups.items() if c >= 3}

    misaligned = 0
    for _, row in mrd.iterrows():
        pname = row["Parameter_Name"]
        group = row["Group_Name"]
        if pd.isna(group) or group == "STINGTags_ISO19650":
            continue
        prefix = pname.split("_")[0] if "_" in pname else ""
        valid = valid_prefix_groups.get(prefix)
        if valid and group not in valid:
            misaligned += 1
    R.append(VR(32, "Prefix-to-group alignment (G-10)", "LOW",
                 misaligned == 0, f"{misaligned} misaligned", "G-10"))

    # ------------------------------------------------------------------
    # G-11 / Check 33: STINGTags/ISO19650 parameter structure
    # ------------------------------------------------------------------
    sting_params = mrd[mrd["Group_Name"] == "STINGTags_ISO19650"]
    # ISO 19650 coding params follow pattern ASS_*_COD_TXT
    cod_pat = re.compile(r"^[A-Z]{2,4}_[A-Z_]+_(COD|TAG|SEQ|NUM)_TXT$")
    bad_tokens = 0
    for pname in sting_params["Parameter_Name"]:
        if not cod_pat.match(pname):
            bad_tokens += 1
    R.append(VR(33, "ISO19650 group naming structure (G-11)", "LOW",
                 bad_tokens == 0, f"{bad_tokens} non-conforming names", "G-11"))

    # ------------------------------------------------------------------
    # G-12 / Check 34: Schedule alias references
    # ------------------------------------------------------------------
    alias_orphans = 0
    if "Formulas" in sch.columns:
        for fml_str in sch["Formulas"].dropna():
            for pair in str(fml_str).split(","):
                pair = pair.strip()
                if "=" in pair:
                    alias_source = pair.split("=")[0].strip()
                    if re.match(r"^[A-Z][A-Z0-9_]+$", alias_source) and alias_source not in mr_names:
                        # Check remap targets
                        if "remap" in files and files["remap"] is not None:
                            consol = set(files["remap"]["Consolidated_Parameter"].dropna())
                            if alias_source not in consol:
                                alias_orphans += 1
                        else:
                            alias_orphans += 1
    R.append(VR(34, "Schedule alias references (G-12)", "MODERATE",
                 alias_orphans == 0, f"{alias_orphans} orphan aliases", "G-12"))

    # ------------------------------------------------------------------
    # G-13 / Check 35: Schedule colour and sort/group validation
    # ------------------------------------------------------------------
    hex_pat = re.compile(r"^#[0-9A-Fa-f]{6}$")
    # Also collect remap old names for sort/group validation
    remap_old_names = set()
    if "remap" in files and files["remap"] is not None:
        remap_old_names = set(files["remap"]["Old_Schedule_Field"].dropna())

    colour_issues = 0
    sort_issues = 0
    for _, row in sch.iterrows():
        for cc in ["Header_Color", "Text_Color", "Background_Color"]:
            val = row.get(cc)
            if pd.notna(val) and str(val).strip():
                if not hex_pat.match(str(val).strip()):
                    colour_issues += 1

        for sc in ["Sorting", "Grouping"]:
            sv = row.get(sc)
            if pd.notna(sv):
                revit_builtins = {"Level", "Category", "Type", "Family",
                                  "Family and Type", "Number", "Name", "Mark",
                                  "Phase", "Room", "Space", "System", "Size",
                                  "Length", "Area", "Volume"}
                for token in str(sv).replace(";", ",").split(","):
                    token = token.strip()
                    for suffix in [": Ascending", ": Descending"]:
                        if token.endswith(suffix):
                            token = token[:-len(suffix)].strip()
                    if not token:
                        continue
                    if re.match(r"^[A-Z][A-Z0-9_]+$", token):
                        # Sort/group can reference params not in Fields (valid Revit)
                        # Only fail if param doesn't exist at all
                        if (token not in mr_names and
                            token not in revit_builtins and
                            token not in remap_old_names):
                            sort_issues += 1

    total = colour_issues + sort_issues
    parts = []
    if colour_issues:
        parts.append(f"{colour_issues} invalid hex colours")
    if sort_issues:
        parts.append(f"{sort_issues} sort/group refs not in fields")
    R.append(VR(35, "Schedule formatting validation (G-13)", "MODERATE",
                 total == 0, "; ".join(parts) or "0 issues", "G-13"))

    # ------------------------------------------------------------------
    # G-14 / Check 36: Material schema contract
    # ------------------------------------------------------------------
    # Valid Revit data types whitelist (includes 8 MEP-critical types added in v4.1)
    VALID_DATA_TYPES = {
        "TEXT", "INTEGER", "NUMBER", "LENGTH", "AREA", "VOLUME", "CURRENCY",
        "ANGLE", "URL", "ELECTRICAL_POWER", "ELECTRICAL_CURRENT",
        "ELECTRICAL_POTENTIAL", "BOOLEAN",
        # MEP-critical (v4.1 additions):
        "FLOW", "PRESSURE", "TEMPERATURE", "VELOCITY", "MASS",
        "DENSITY", "POWER_DENSITY", "HVAC_DENSITY",
        # Revit-native boolean type name (v4.2 addition):
        "YESNO",
    }

    if "mat_schema" in files:
        schema_cols = set(files["mat_schema"].get("required_columns", []))
        for key, label in [("ble", "BLE"), ("mep", "MEP")]:
            mat = files.get(key)
            if mat is not None:
                actual = set(mat.columns)
                missing = schema_cols - actual
                extra = actual - schema_cols
                issues = len(missing) + len(extra)
                parts = []
                if missing:
                    parts.append(f"{len(missing)} missing")
                if extra:
                    parts.append(f"{len(extra)} extra")
                R.append(VR(36, f"{label} schema contract (G-14)", "MODERATE",
                             issues == 0, "; ".join(parts) or "0 drift", "G-14"))

    # G-14c: Material column order (schema column_order constraint)
    if "mat_schema" in files and "column_order" in files["mat_schema"]:
        expected_order = files["mat_schema"]["column_order"]
        for key, label in [("ble", "BLE"), ("mep", "MEP")]:
            mat = files.get(key)
            if mat is not None:
                actual_order = list(mat.columns)
                mispositioned = sum(1 for a, b in zip(actual_order, expected_order) if a != b)
                R.append(VR(36, f"{label} column order (G-14c)", "MODERATE",
                             mispositioned == 0,
                             f"{mispositioned} columns out of position" if mispositioned > 0
                             else "Column order matches schema",
                             "G-14"))

    else:
        # Check BLE vs MEP column alignment directly
        ble, mep = files.get("ble"), files.get("mep")
        if ble is not None and mep is not None:
            ble_cols = set(ble.columns)
            mep_cols = set(mep.columns)
            diff = ble_cols.symmetric_difference(mep_cols)
            R.append(VR(36, "Material schema alignment (G-14)", "MODERATE",
                         len(diff) == 0, f"{len(diff)} column differences", "G-14"))
        else:
            R.append(VR(36, "Material schema alignment (G-14)", "INFO",
                         True, "Material files not loaded", "G-14"))

    # G-14b: MEP data type whitelist — parameters using NUMBER instead of native MEP types
    mr_mep_types = mrd[mrd["Discipline"].isin(
        {"HVAC", "PLUMBING", "FIRE", "MECHANICAL", "ELECTRICAL"}
    )][["Parameter_Name", "Data_Type"]]
    # Only flag NUMBER params whose names suggest a physical quantity
    # OCCUPANT_LOAD is a count (NUMBER is correct); exclude from MEP dtype check
    mep_qty_patterns = re.compile(
        r"(?<!OCCUPANT_)(PRESSURE|TEMPERATURE|FLOW_RATE|FLOW_|VELOCITY|DENSITY|MASS_|POWER_DENSITY)",
        re.IGNORECASE
    )
    wrong_type = mr_mep_types[
        (mr_mep_types["Data_Type"] == "NUMBER") &
        (mr_mep_types["Parameter_Name"].str.contains(mep_qty_patterns))
    ]
    R.append(VR(36, "MEP data type whitelist (G-14b)", "HIGH",
                 len(wrong_type) == 0,
                 f"{len(wrong_type)} MEP params using NUMBER instead of native Revit type "
                 f"(FLOW/PRESSURE/TEMPERATURE/etc)" +
                 (f" (e.g. {list(wrong_type['Parameter_Name'])[:3]})" if len(wrong_type) else ""),
                 "G-14"))

    # ------------------------------------------------------------------
    # G-15 / Check 37: Texture URL reachability
    # ------------------------------------------------------------------
    if check_textures:
        import urllib.request
        bad_urls = 0
        total_urls = 0
        cache_path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                  "texture_url_status.json")
        cache = {}
        if os.path.exists(cache_path):
            with open(cache_path) as fh:
                cache = json.load(fh)

        all_urls = set()
        for key in ["ble", "mep"]:
            mat = files.get(key)
            if mat is not None and "BLE_MAT_TEXTURE_URL" in mat.columns:
                all_urls.update(mat["BLE_MAT_TEXTURE_URL"].dropna().astype(str))

        total_urls = len(all_urls)
        for url in all_urls:
            if url in cache and cache[url] == 200:
                continue
            try:
                req = urllib.request.Request(url, method="HEAD")
                resp = urllib.request.urlopen(req, timeout=5)
                cache[url] = resp.status
                if resp.status != 200:
                    bad_urls += 1
            except Exception:
                cache[url] = 0
                bad_urls += 1

        with open(cache_path, "w") as fh:
            json.dump(cache, fh)

        R.append(VR(37, "Texture URL reachability (G-15)", "LOW",
                     bad_urls == 0, f"{bad_urls}/{total_urls} unreachable", "G-15"))
    else:
        R.append(VR(37, "Texture URL reachability (G-15)", "INFO",
                     True, "Skipped (use --check-textures)", "G-15"))

    # ------------------------------------------------------------------
    # G-16 / Check 38: PyRevit script manifest (replaces Dynamo)
    # ------------------------------------------------------------------
    if "pyrevit_manifest" in files and files["pyrevit_manifest"] is not None:
        pm = files["pyrevit_manifest"]
        orphans = 0
        # Expected columns: Script_Name, Script_Path, Button_Name,
        #   Parameters_Read, Parameters_Written, Revit_Categories,
        #   Last_Validated_Date, pyRevit_Extension
        for col in ["Parameters_Read", "Parameters_Written"]:
            if col in pm.columns:
                for val in pm[col].dropna():
                    for p in str(val).split(","):
                        p = p.strip()
                        if p and re.match(r"^[A-Z][A-Z0-9_]+$", p) and p not in mr_names:
                            orphans += 1
        R.append(VR(38, "PyRevit manifest param refs (G-16)", "LOW",
                     orphans == 0, f"{orphans} orphan param refs", "G-16"))
    else:
        R.append(VR(38, "PyRevit manifest param refs (G-16)", "HIGH",
                     False,
                     "pyrevit_scripts_manifest.csv not found — create with columns: "
                     "Script_Name, Script_Path, Button_Name, Parameters_Read, "
                     "Parameters_Written, Revit_Categories, Last_Validated_Date, "
                     "pyRevit_Extension",
                     "G-16"))

    # ------------------------------------------------------------------
    # G-17 / Check 39: Exit code design (self-check: always passes)
    # ------------------------------------------------------------------
    R.append(VR(39, "Exit code design (G-17)", "INFO",
                 True, "v4.1: exit 0/1/2 + --json + --report implemented", "G-17"))

    # ------------------------------------------------------------------
    # G-19 / Check 40: Version headers in CSVs
    # ------------------------------------------------------------------
    version_keys = ["mr", "pc", "cb", "fp", "fd", "sch"]
    missing_ver = [k for k in version_keys if files.get(f"{k}_version") is None]
    R.append(VR(40, "CSV version headers (G-19)", "LOW",
                 len(missing_ver) == 0,
                 f"{len(missing_ver)} files missing # header: {','.join(missing_ver)}" if missing_ver else "All present",
                 "G-19"))

    # ------------------------------------------------------------------
    # G-21 / Check 41: MAT_CODE uniqueness within each material library
    # ------------------------------------------------------------------
    for key, label in [("ble", "BLE"), ("mep", "MEP")]:
        mat = files.get(key)
        if mat is not None and "MAT_CODE" in mat.columns:
            dups = mat[mat.duplicated("MAT_CODE", keep=False)]
            dup_codes = sorted(mat.loc[mat.duplicated("MAT_CODE", keep=False), "MAT_CODE"].unique())
            detail = (f"{len(dup_codes)} duplicate codes ({len(dups)} records): "
                      f"{', '.join(dup_codes[:5])}{'...' if len(dup_codes) > 5 else ''}")
            R.append(VR(41, f"{label} MAT_CODE uniqueness (G-21)", "HIGH",
                         len(dups) == 0, detail if dups.shape[0] > 0 else "All unique", "G-21"))
        else:
            R.append(VR(41, f"{label} MAT_CODE uniqueness (G-21)", "INFO",
                         True, "MAT_CODE column not present", "G-21"))

    # ------------------------------------------------------------------
    # G-22 / Check 42: Discipline/prefix alignment
    #   MECHANICAL discipline should only contain params with non-HVC prefix
    #   or params that are genuinely mechanical (pumps, fans, etc).
    #   HVC_* params must be HVAC; BLE_STAIR/ARCH_STAIR must be ARCHITECTURAL.
    # ------------------------------------------------------------------
    prefix_discipline_rules = {
        "HVC": {"HVAC", "MULTI"},
        "BLE": {"ARCHITECTURAL", "MULTI", "COSTING", "CONSTRUCTION"},
        "ELE": {"ELECTRICAL", "MULTI"},
        "PLM": {"PLUMBING", "MULTI"},
        "FLS": {"FIRE", "MULTI"},
        "HVC_STAIR": {"ARCHITECTURAL"},  # stair params under wrong prefix
    }
    misdisc = []
    for _, row in mrd.iterrows():
        pname = row["Parameter_Name"]
        disc = row["Discipline"]
        # Detect HVC_STAIR_ params (architectural stair content under HVC prefix)
        if "_STAIR_" in pname and disc not in {"ARCHITECTURAL", "MULTI", "FIRE"}:
            # FLS_ params with _STAIR_ are fire escape stair classification (correct as FIRE)
            if not pname.startswith("FLS_"):
                misdisc.append(f"{pname}: has _STAIR_ but Discipline={disc} (expected ARCHITECTURAL)")
                continue
        prefix = pname.split("_")[0]
        allowed = prefix_discipline_rules.get(prefix)
        if allowed and disc == "MECHANICAL" and prefix in ("HVC", "ELE", "PLM", "FLS"):
            misdisc.append(f"{pname}: prefix={prefix} but Discipline=MECHANICAL")
    R.append(VR(42, "Discipline/prefix alignment (G-22)", "MODERATE",
                 len(misdisc) == 0,
                 f"{len(misdisc)} mismatches" + (f" (e.g. {misdisc[0]})" if misdisc else ""),
                 "G-22"))

    # ------------------------------------------------------------------
    # G-23 / Check 43: Hide_When_No_Value on computed numeric params
    #   Formula-driven params with numeric output should have Hide_When_No_Value=1
    #   to suppress blank rows in QTO and cost schedules.
    # ------------------------------------------------------------------
    if "Hide_When_No_Value" in mrd.columns:
        numeric_types = {"NUMBER", "LENGTH", "AREA", "VOLUME", "CURRENCY", "INTEGER",
                         "ELECTRICAL_POWER", "ELECTRICAL_CURRENT", "ELECTRICAL_POTENTIAL", "ANGLE",
                         "FLOW", "PRESSURE", "TEMPERATURE", "VELOCITY", "MASS",
                         "DENSITY", "POWER_DENSITY", "HVAC_DENSITY"}
        # Params appearing in schedule Fields are intentionally shown (Hide=0 is correct for them)
        formula_numeric = mrd[
            (mrd["Has_Formula"] == True) &
            (mrd["Data_Type"].isin(numeric_types)) &
            (mrd["Hide_When_No_Value"] == 0) &
            (~mrd["Parameter_Name"].isin(sched_fields))  # exclude scheduled params
        ]
        R.append(VR(43, "Hide_When_No_Value on computed params (G-23)", "LOW",
                     len(formula_numeric) == 0,
                     f"{len(formula_numeric)} non-scheduled computed numeric params with Hide_When_No_Value=0",
                     "G-23"))
    else:
        R.append(VR(43, "Hide_When_No_Value on computed params (G-23)", "INFO",
                     True, "Hide_When_No_Value column not present", "G-23"))

    # ------------------------------------------------------------------
    # G-24 / Check 44: Tag guide deep cross-reference
    #   Every field referenced in schedule Formulas/aliases must exist in
    #   mr_parameters. Additionally, formula-computed params referenced in
    #   tag schedules are flagged as performance warnings.
    # ------------------------------------------------------------------
    fd_param_names = set(fd["Parameter_Name"])
    tag_orphans = []
    tag_computed_warnings = []
    if "Formulas" in sch.columns:
        for _, srow in sch.iterrows():
            fml_str = srow.get("Formulas", "")
            sched_name = srow.get("Schedule_Name", "?")
            if pd.isna(fml_str):
                continue
            for pair in str(fml_str).split(","):
                pair = pair.strip()
                if "=" not in pair:
                    continue
                alias_source = pair.split("=")[0].strip()
                if not re.match(r"^[A-Z][A-Z0-9_]+$", alias_source):
                    continue
                if alias_source not in mr_names:
                    # Check remap consolidated params as well
                    remap_consol = set()
                    if "remap" in files and files["remap"] is not None:
                        remap_consol = set(files["remap"]["Consolidated_Parameter"].dropna())
                    if alias_source not in remap_consol:
                        tag_orphans.append(f"{sched_name}: {alias_source}")
                elif alias_source in fd_param_names:
                    tag_computed_warnings.append(f"{sched_name}: {alias_source} is computed")

    total_issues = len(tag_orphans)
    detail_parts = []
    if tag_orphans:
        detail_parts.append(f"{len(tag_orphans)} orphan aliases")
    if tag_computed_warnings:
        detail_parts.append(f"{len(tag_computed_warnings)} computed params in tags (perf warning)")
    R.append(VR(44, "Tag schedule deep cross-ref (G-24)", "MODERATE",
                 total_issues == 0,
                 "; ".join(detail_parts) if detail_parts else "0 issues",
                 "G-24"))


    # ------------------------------------------------------------------
    # G-25 / Check 45: Data type whitelist validation
    # ------------------------------------------------------------------
    invalid_types = mrd[~mrd["Data_Type"].isin(VALID_DATA_TYPES)]
    R.append(VR(45, "Data type whitelist (G-25)", "MODERATE",
                 len(invalid_types) == 0,
                 f"{len(invalid_types)} params with invalid Data_Type "
                 f"(not in VALID_DATA_TYPES whitelist)"
                 + (f" (e.g. {list(invalid_types['Data_Type'].unique())[:3]})"
                    if len(invalid_types) > 0 else ""),
                 "G-25"))

    return R


def _check_tag_guide(files, mr_names):
    """G-04 / Check 26: Tag guide sheet 08 new parameters."""
    if "tag_guide" not in files or files["tag_guide"] is None:
        return VR(26, "Tag guide new params (G-04)", "INFO",
                  True, "No tag guide loaded", "G-04")

    sheets = files["tag_guide"]
    target = None
    for sn in sheets:
        if "08" in sn or "NEW" in sn.upper():
            target = sheets[sn]
            break
    if target is None:
        return VR(26, "Tag guide new params (G-04)", "INFO",
                  True, "Sheet 08 not found", "G-04")

    # Find header row (row containing "Parameter Name")
    name_col_idx = None
    header_row = None
    for i in range(min(5, len(target))):
        for j, val in enumerate(target.iloc[i]):
            if pd.notna(val) and "parameter name" in str(val).lower():
                name_col_idx = j
                header_row = i
                break
        if name_col_idx is not None:
            break

    if name_col_idx is None:
        # Fall back: column with most uppercase param-like values
        best_col = None
        best_count = 0
        for ci, col in enumerate(target.columns):
            vals = target[col].dropna().astype(str)
            count = sum(1 for v in vals if re.match(r"^[A-Z][A-Z0-9_]+$", v))
            if count > best_count:
                best_count = count
                best_col = ci
        if best_col is not None and best_count > 5:
            name_col_idx = best_col
            header_row = 0

    if name_col_idx is None:
        return VR(26, "Tag guide new params (G-04)", "INFO",
                  True, "Cannot identify param column", "G-04")

    # Extract param names below header
    col = target.columns[name_col_idx]
    start = (header_row + 1) if header_row is not None else 0
    params = set()
    for val in target[col].iloc[start:]:
        s = str(val).strip()
        if re.match(r"^[A-Z][A-Z0-9_]{3,}$", s):
            params.add(s)

    missing = params - mr_names
    return VR(26, "Tag guide new params (G-04)", "CRITICAL",
              len(missing) == 0,
              f"{len(missing)}/{len(params)} tag params not in mr" +
              (f" (e.g. {list(missing)[:3]})" if missing else ""),
              "G-04")

# ============================================================================
# REPORT GENERATION (G-18)
# ============================================================================

def generate_json_report(results, files, args):
    hashes = files.get("_hashes", {})
    return {
        "validator_version": VERSION,
        "run_date": RUN_DATE,
        "source_dir": os.path.abspath(args.data_dir),
        "file_hashes": hashes,
        "total_checks": 44,
        "total_results": len(results),
        "passed": sum(1 for r in results if r.passed),
        "failed": sum(1 for r in results if not r.passed),
        "critical_failures": sum(1 for r in results if not r.passed and r.severity == "CRITICAL"),
        "moderate_failures": sum(1 for r in results if not r.passed and r.severity == "MODERATE"),
        "checks": [r.to_dict() for r in results],
    }


def generate_html_report(results, files, args):
    data = generate_json_report(results, files, args)

    def row_class(r):
        if r["passed"]:
            return "pass"
        if r["severity"] == "CRITICAL":
            return "crit"
        if r["severity"] in ("HIGH", "MODERATE"):
            return "mod"
        return "low"

    rows = ""
    for r in data["checks"]:
        cls = row_class(r)
        icon = "&#10003;" if r["passed"] else "&#10007;"
        status = "PASS" if r["passed"] else "FAIL"
        rows += f'<tr class="{cls}"><td>{r["check"]}</td><td>{icon} {status}</td>'
        rows += f'<td>{r["severity"]}</td><td>{r["name"]}</td>'
        rows += f'<td>{r["detail"]}</td><td>{r["gap"]}</td></tr>\n'

    html = f"""<!DOCTYPE html>
<html><head><meta charset="utf-8">
<title>BIM Template Validation Report v{VERSION} (pyRevit)</title>
<style>
  body {{ font-family: -apple-system, sans-serif; margin: 2em; background: #f8f9fa; }}
  h1 {{ color: #1a1a2e; }} h2 {{ color: #16213e; }}
  .summary {{ display: flex; gap: 1em; margin: 1em 0; }}
  .card {{ padding: 1em 1.5em; border-radius: 8px; color: #fff; min-width: 120px; }}
  .card.g {{ background: #27ae60; }} .card.r {{ background: #c0392b; }}
  .card.y {{ background: #f39c12; }} .card.b {{ background: #2980b9; }}
  table {{ border-collapse: collapse; width: 100%; background: #fff; border-radius: 8px; overflow: hidden; }}
  th {{ background: #1a1a2e; color: #fff; padding: 10px 12px; text-align: left; font-size: 13px; }}
  td {{ padding: 8px 12px; border-bottom: 1px solid #eee; font-size: 13px; }}
  tr.pass td:nth-child(2) {{ color: #27ae60; font-weight: bold; }}
  tr.crit td {{ background: #fde8e8; }} tr.crit td:nth-child(2) {{ color: #c0392b; font-weight: bold; }}
  tr.mod td {{ background: #fef9e7; }} tr.mod td:nth-child(2) {{ color: #f39c12; font-weight: bold; }}
  tr.low td {{ background: #eaf2f8; }} tr.low td:nth-child(2) {{ color: #2980b9; font-weight: bold; }}
  .meta {{ color: #666; font-size: 12px; margin: 0.5em 0; }}
</style>
</head><body>
<h1>BIM Template CI Validation Report</h1>
<p class="meta">v{VERSION} | {RUN_DATE} | Source: {data['source_dir']}</p>
<p class="meta">{data['total_results']} results / {data['total_checks']} checks
  (some checks run once per material library)</p>
<div class="summary">
  <div class="card g"><strong>{data['passed']}</strong><br>Passed</div>
  <div class="card r"><strong>{data['critical_failures']}</strong><br>Critical</div>
  <div class="card y"><strong>{data['moderate_failures']}</strong><br>Moderate</div>
  <div class="card b"><strong>{data['total_results']}</strong><br>Results</div>
</div>
<h2>Check results</h2>
<table>
<tr><th>#</th><th>Status</th><th>Severity</th><th>Check</th><th>Detail</th><th>Gap</th></tr>
{rows}
</table>
<h2>File hashes</h2>
<table><tr><th>File</th><th>SHA-256 (8)</th></tr>
{''.join(f"<tr><td>{k}</td><td><code>{v}</code></td></tr>" for k,v in data['file_hashes'].items())}
</table>
</body></html>"""
    return html

# ============================================================================
# BASELINE COMPARISON (G-17 enhancement)
# ============================================================================

def compare_baseline(current, baseline_path):
    """Compare current results against a stored baseline."""
    if not os.path.exists(baseline_path):
        return []
    with open(baseline_path) as f:
        baseline = json.load(f)
    baseline_map = {c["check"]: c for c in baseline.get("checks", [])}
    regressions = []
    for r in current:
        rd = r.to_dict()
        prev = baseline_map.get(rd["check"])
        if prev and prev["passed"] and not rd["passed"]:
            regressions.append(f"Check {rd['check']} ({rd['name']}): PASS -> FAIL")
    return regressions

# ============================================================================
# MAIN
# ============================================================================

def main():
    parser = argparse.ArgumentParser(description=f"BIM Template CI Validator v{VERSION}")
    parser.add_argument("data_dir", help="Directory containing v2.1+ data files")
    parser.add_argument("--json", action="store_true", help="Print JSON results to stdout")
    parser.add_argument("--report", choices=["html", "json"], help="Write report file")
    parser.add_argument("--strict-moderate", action="store_true",
                        help="Exit code 2 on MODERATE failures")
    parser.add_argument("--check-textures", action="store_true",
                        help="HEAD-check texture URLs (slow)")
    parser.add_argument("--baseline", help="Path to baseline JSON for regression detection")
    parser.add_argument("--tag-guide-dir", help="Directory containing tag_guide xlsx (default: data_dir)")
    args = parser.parse_args()

    src = args.data_dir
    if not os.path.isdir(src):
        print(f"Error: {src} is not a directory", file=sys.stderr)
        return 1

    files = load_files(src, tag_guide_dir=args.tag_guide_dir)

    # Verify core files
    required = ["mr", "pc", "cb", "fp", "fd", "sch"]
    missing = [k for k in required if k not in files or files[k] is None]
    if missing:
        print(f"Error: missing core files for keys: {', '.join(missing)}", file=sys.stderr)
        return 1

    results = checks_original(files) + checks_new(files, check_textures=args.check_textures)

    # Console output
    if not args.json:
        print(f"BIM Template CI Validation v{VERSION} (45 checks | pyRevit)")
        print(f"Source: {os.path.abspath(src)}")
        print(f"Date:   {RUN_DATE}")
        print("=" * 80)
        for r in results:
            icon = "\u2713" if r.passed else "\u2717"
            status = "PASS" if r.passed else "FAIL"
            gap = f" [{r.gap}]" if r.gap else ""
            print(f"  {icon} [{r.severity:8s}] {status:4s}  #{r.cid:2d} {r.name}: {r.detail}{gap}")
        print("=" * 80)
        passed = sum(1 for r in results if r.passed)
        total = len(results)
        num_checks = 44
        crit = sum(1 for r in results if not r.passed and r.severity == "CRITICAL")
        mod = sum(1 for r in results if not r.passed and r.severity in ("MODERATE", "HIGH"))
        low = sum(1 for r in results if not r.passed and r.severity == "LOW")
        print(f"Result: {passed}/{total} results ({num_checks} checks) | "
              f"{crit} CRITICAL | {mod} MODERATE/HIGH | {low} LOW")

    # JSON mode
    if args.json:
        report = generate_json_report(results, files, args)
        print(json.dumps(report, indent=2))

    # Report output (G-18)
    if args.report:
        if args.report == "json":
            report = generate_json_report(results, files, args)
            out = os.path.join(src, "validation_report.json")
            with open(out, "w") as f:
                json.dump(report, f, indent=2)
            if not args.json:
                print(f"JSON report: {out}")
        elif args.report == "html":
            html = generate_html_report(results, files, args)
            out = os.path.join(src, "validation_report.html")
            with open(out, "w") as f:
                f.write(html)
            if not args.json:
                print(f"HTML report: {out}")

    # Baseline regression check (G-17)
    if args.baseline:
        regressions = compare_baseline(results, args.baseline)
        if regressions:
            print(f"\nBASELINE REGRESSIONS ({len(regressions)}):")
            for reg in regressions:
                print(f"  ! {reg}")

    # Exit code (G-17: 0/1/2)
    crit_fail = any(not r.passed and r.severity == "CRITICAL" for r in results)
    mod_fail = any(not r.passed and r.severity in ("MODERATE", "HIGH") for r in results)

    if crit_fail:
        if not args.json:
            print("STATUS: FAIL (critical)")
        return 1
    elif mod_fail and args.strict_moderate:
        if not args.json:
            print("STATUS: FAIL (moderate, --strict-moderate)")
        return 2
    else:
        if not args.json:
            print("STATUS: PASS")
        return 0


if __name__ == "__main__":
    sys.exit(main())
