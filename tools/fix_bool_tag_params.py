#!/usr/bin/env python3
"""
fix_bool_tag_params.py

1. Parse MR_PARAMETERS.txt for YESNO-typed parameters.
2. Parse FORMULAS_WITH_DEPENDENCIES.csv for TEXT-output (tag label) formulas.
3. Any YESNO param referenced as an input in those TEXT formulas gets its
   DATATYPE rewritten to TEXT in MR_PARAMETERS.txt.
4. Writes changed_params.csv at the repo root with one row per changed param.

Usage:
    python tools/fix_bool_tag_params.py
"""

import csv
import re
from pathlib import Path

REPO_ROOT     = Path(__file__).resolve().parent.parent
DATA_DIR      = REPO_ROOT / "StingTools" / "Data"
PARAMS_FILE   = DATA_DIR / "MR_PARAMETERS.txt"
FORMULAS_FILE = DATA_DIR / "FORMULAS_WITH_DEPENDENCIES.csv"
REPORT_FILE   = REPO_ROOT / "changed_params.csv"


# ---------------------------------------------------------------------------
# Parsing
# ---------------------------------------------------------------------------

def load_params_file(path: Path):
    """
    Read MR_PARAMETERS.txt preserving exact content.
    Returns (lines: list[str], line_sep: str, params: dict[name -> dict])
    params dict: { name: {line_idx: int, parts: list[str]} }
    """
    raw = path.read_bytes().decode("utf-8")
    line_sep = "\r\n" if "\r\n" in raw else "\n"
    lines = raw.split(line_sep)

    params: dict = {}
    for idx, line in enumerate(lines):
        if not line.startswith("PARAM\t"):
            continue
        parts = line.split("\t")
        # PARAM <GUID> <NAME> <DATATYPE> <DATACATEGORY> <GROUP> ...
        if len(parts) < 4:
            continue
        name = parts[2]
        params[name] = {"line_idx": idx, "parts": list(parts)}

    return lines, line_sep, params


def collect_yesno(params: dict) -> dict:
    """Return subset of params whose DATATYPE is YESNO or BOOLEAN."""
    return {
        name: data
        for name, data in params.items()
        if data["parts"][3].strip().upper() in ("YESNO", "BOOLEAN")
    }


def load_tag_formulas(path: Path) -> list:
    """
    Parse FORMULAS_WITH_DEPENDENCIES.csv; return rows where Data_Type == TEXT.
    Those are the tag label formulas – text strings shown in Revit tag families.
    Columns: Discipline(0) Parameter_Name(1) Data_Type(2) Revit_Formula(3)
             Description(4) Input_Parameters(5) ...
    """
    raw = path.read_text(encoding="utf-8")
    non_comment = [ln for ln in raw.splitlines() if not ln.startswith("#")]
    reader = csv.reader(non_comment)

    tag_rows = []
    header = None
    for row in reader:
        if header is None:
            header = row
            continue
        if len(row) < 6:
            continue
        if row[2].strip().upper() == "TEXT":
            tag_rows.append(row)

    return tag_rows


# ---------------------------------------------------------------------------
# Matching
# ---------------------------------------------------------------------------

def find_bool_inputs(yesno_names: set, tag_rows: list) -> dict:
    """
    Return {param_name: [formula_output_params, ...]} for every YESNO param
    that appears as an explicit input (column 5) or word-boundary hit in
    the Revit_Formula (column 3) of any TEXT formula row.
    """
    found: dict = {}

    for row in tag_rows:
        out_param   = row[1].strip()           # the TEXT output parameter name
        formula_txt = row[3].strip()           # Revit_Formula expression
        inputs_str  = row[5].strip()           # comma-separated Input_Parameters

        explicit = {p.strip() for p in inputs_str.split(",") if p.strip()}

        for yn in yesno_names:
            if not yn:
                continue
            in_explicit = yn in explicit
            in_formula  = bool(re.search(r"\b" + re.escape(yn) + r"\b", formula_txt))
            if in_explicit or in_formula:
                found.setdefault(yn, []).append(out_param)

    return found


# ---------------------------------------------------------------------------
# Write-back
# ---------------------------------------------------------------------------

def rewrite_params_file(path: Path, lines: list, line_sep: str,
                        yesno: dict, matched: dict) -> None:
    """Patch DATATYPE field to TEXT for each matched param, write file."""
    for name in matched:
        data = yesno[name]
        data["parts"][3] = "TEXT"
        lines[data["line_idx"]] = "\t".join(data["parts"])

    path.write_bytes(line_sep.join(lines).encode("utf-8"))


def write_report(path: Path, report_rows: list) -> None:
    """Write CSV report: parameter_name, old_type, new_type, referenced_in_formula."""
    with open(path, "w", newline="", encoding="utf-8") as fh:
        writer = csv.writer(fh)
        writer.writerow(["parameter_name", "old_type", "new_type", "referenced_in_formula"])
        writer.writerows(report_rows)


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

def main() -> None:
    # 1. Load parameter file
    lines, line_sep, all_params = load_params_file(PARAMS_FILE)

    # 2. Filter to YESNO-typed parameters
    yesno = collect_yesno(all_params)

    # 3. Load TEXT-output (tag label) formulas
    tag_formulas = load_tag_formulas(FORMULAS_FILE)

    # 4. Find which YESNO params appear inside tag label formulas
    matched = find_bool_inputs(set(yesno.keys()), tag_formulas)

    # 5. Capture report data before mutating parts list
    report_rows = [
        (name, "YESNO", "TEXT", "; ".join(matched[name]))
        for name in sorted(matched)
    ]

    # 6. Rewrite MR_PARAMETERS.txt (only if there are changes)
    if matched:
        rewrite_params_file(PARAMS_FILE, lines, line_sep, yesno, matched)

    # 7. Write report regardless (empty report = no matches)
    write_report(REPORT_FILE, report_rows)

    print(f"Changed {len(matched)} parameter(s). Report: {REPORT_FILE}")


if __name__ == "__main__":
    main()
