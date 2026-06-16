#!/usr/bin/env python3
"""
fix_bool_param_types.py

All parameters whose NAME ends with _BOOL must have DATATYPE = YESNO.
In MR_PARAMETERS.txt the DATATYPE is in column 3 (0-indexed, tab-delimited).
In MR_PARAMETERS.csv  the DATATYPE is in a column named 'Data Type'.

This script fixes both files in-place and prints a summary.
"""

import csv
import re
from pathlib import Path

REPO_ROOT   = Path(__file__).resolve().parent.parent
DATA_DIR    = REPO_ROOT / "StingTools" / "Data"
PARAMS_TXT  = DATA_DIR / "MR_PARAMETERS.txt"
PARAMS_CSV  = DATA_DIR / "MR_PARAMETERS.csv"


def fix_txt(path: Path) -> int:
    raw      = path.read_bytes().decode("utf-8")
    line_sep = "\r\n" if "\r\n" in raw else "\n"
    lines    = raw.split(line_sep)
    changed  = 0

    for idx, line in enumerate(lines):
        if not line.startswith("PARAM\t"):
            continue
        parts = line.split("\t")
        if len(parts) < 4:
            continue
        name     = parts[2]
        datatype = parts[3].strip().upper()
        if name.endswith("_BOOL") and datatype not in ("YESNO", "BOOLEAN"):
            parts[3] = "YESNO"
            lines[idx] = "\t".join(parts)
            changed += 1

    path.write_bytes(line_sep.join(lines).encode("utf-8"))
    return changed


def fix_csv(path: Path) -> int:
    raw      = path.read_bytes().decode("utf-8")
    line_sep = "\r\n" if "\r\n" in raw else "\n"
    lines    = raw.split(line_sep)
    changed  = 0

    # Find header row to locate columns
    header_idx = None
    for i, line in enumerate(lines):
        if line.startswith("#"):
            continue
        header_idx = i
        break

    if header_idx is None:
        print(f"  No header found in {path.name}")
        return 0

    # Use csv.reader on just the header to get column positions
    import io
    header_parts = next(csv.reader(io.StringIO(lines[header_idx])))
    try:
        name_col = header_parts.index("Name")
        type_col = header_parts.index("Data Type")
    except ValueError:
        # Fallback: Name is col 1, Data Type is col 3 (standard Revit SP CSV)
        name_col = 1
        type_col = 3

    for idx in range(header_idx + 1, len(lines)):
        line = lines[idx]
        if not line.strip() or line.startswith("#"):
            continue

        row = next(csv.reader(io.StringIO(line)))
        if len(row) <= max(name_col, type_col):
            continue
        if not row[name_col].endswith("_BOOL"):
            continue
        if row[type_col].strip().upper() in ("YESNO", "BOOLEAN"):
            continue

        # Rebuild the line with corrected Data Type
        row[type_col] = "YESNO"
        out = io.StringIO()
        w   = csv.writer(out)
        w.writerow(row)
        lines[idx] = out.getvalue().rstrip("\r\n")
        changed += 1

    path.write_bytes(line_sep.join(lines).encode("utf-8"))
    return changed


if __name__ == "__main__":
    n_txt = fix_txt(PARAMS_TXT)
    n_csv = fix_csv(PARAMS_CSV)
    print(f"MR_PARAMETERS.txt : fixed {n_txt} _BOOL parameters → YESNO")
    print(f"MR_PARAMETERS.csv : fixed {n_csv} _BOOL parameters → YESNO")
