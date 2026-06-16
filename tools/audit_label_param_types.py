#!/usr/bin/env python3
"""
audit_label_param_types.py — canonical data-type alignment gate for STING tags.

The rule (STING v5.3+, see LABEL_DEFINITIONS.json `datatype_note` and the
`calculated_value_templates._comment`):

  Every parameter that a Revit tag-family label / Calculated Value plugs into
  the `if(GATE, PARAM, "")` template MUST be declared TEXT in MR_PARAMETERS.txt,
  because Revit label formulas cannot return a non-text value from a TEXT
  Calculated Value, nor use a YESNO parameter as the condition of if(...).

  Concretely, a parameter must be TEXT when it is:
    1. referenced by a `category_labels` tier (tier_1..N) or `warnings` entry, or
       a `paragraph_container`, in LABEL_DEFINITIONS.json;
    2. referenced by a `calculated_value_templates` gate or a
       `tag_style_parameters` gate list (paragraph_gates / tag7_section_gates /
       warning_visibility); or
    3. used as an input to any TEXT-output formula in
       FORMULAS_WITH_DEPENDENCIES.csv.

  Numeric/measurement parameters (carbon, cost, radiation, …) stay numeric so
  they remain usable for calculation; labels must reference their `*_DISP_TXT`
  display mirror (a TEXT `format(<src>)` parameter) instead of the numeric
  source. Pure-flag `_BOOL` parameters that are NOT referenced by any label or
  formula stay YESNO.

This script is READ-ONLY. It prints a summary and exits non-zero if any
misalignment is found, so it can run as a CI / pre-merge gate. It does NOT
mutate any file — fixing is a deliberate edit (flip a flag to TEXT, or repoint
a label to the `*_DISP_TXT` mirror).
"""

import csv
import io
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DATA = REPO / "StingTools" / "Data"
TXT = DATA / "MR_PARAMETERS.txt"
FORMULAS = DATA / "FORMULAS_WITH_DEPENDENCIES.csv"
LABELS = DATA / "LABEL_DEFINITIONS.json"

NAME_RE = re.compile(r"[A-Z][A-Z0-9_]{2,}")


def load_param_types() -> dict:
    types = {}
    for line in TXT.read_text(encoding="utf-8").splitlines():
        if not line.startswith("PARAM\t"):
            continue
        parts = line.split("\t")
        if len(parts) < 4:
            continue
        types[parts[2]] = parts[3].strip().upper()
    return types


def collect_formula_text_inputs(types: dict):
    """YESNO/BOOLEAN params used inside a TEXT-output formula, and TEXT-formula
    output params whose own declared type isn't TEXT."""
    raw = [l for l in FORMULAS.read_text(encoding="utf-8").splitlines()
           if not l.startswith("#")]
    rdr = csv.DictReader(io.StringIO("\n".join(raw)))
    bad_inputs = {}          # input -> example output
    bad_outputs = []         # (out, mr_type)
    for r in rdr:
        out = (r.get("Parameter_Name") or "").strip()
        dtype = (r.get("Data_Type") or "").strip().upper()
        if dtype != "TEXT" or not out:
            continue
        if out in types and types[out] != "TEXT":
            bad_outputs.append((out, types[out]))
        inputs = {x.strip() for x in (r.get("Input_Parameters") or "").split(",") if x.strip()}
        for tok in NAME_RE.findall(r.get("Revit_Formula") or ""):
            if tok in types:
                inputs.add(tok)
        for inp in inputs:
            if inp == out:
                continue
            if types.get(inp) in ("YESNO", "BOOLEAN"):
                bad_inputs.setdefault(inp, out)
    return bad_inputs, bad_outputs


def collect_label_refs() -> dict:
    """param -> set(sources) for everything a tag label/calculated-value uses."""
    d = json.loads(LABELS.read_text(encoding="utf-8"))
    refs = {}

    def note(p, src):
        if p and p not in ("TXT", "BOOL", "NA"):
            refs.setdefault(p, set()).add(src)

    for k, v in d.get("calculated_value_templates", {}).items():
        if isinstance(v, dict):
            for tok in NAME_RE.findall(v.get("formula", "")):
                note(tok, f"cv:{k}")

    for cat, cfg in d.get("category_labels", {}).items():
        if not isinstance(cfg, dict):
            continue
        for key, val in cfg.items():
            if (key.startswith("tier_") or key == "warnings") and isinstance(val, list):
                for entry in val:
                    if isinstance(entry, dict):
                        note(entry.get("parameter") or entry.get("param"), f"{cat}.{key}")
            elif key == "paragraph_container" and isinstance(val, str):
                note(val, f"{cat}.paragraph_container")

    tsp = d.get("tag_style_parameters", {})
    for gkey in ("paragraph_gates", "tag7_section_gates", "warning_visibility"):
        g = tsp.get(gkey)
        if g is not None:
            for tok in NAME_RE.findall(json.dumps(g)):
                note(tok, f"tsp:{gkey}")
    return refs


def main() -> int:
    types = load_param_types()
    print(f"MR_PARAMETERS.txt: {len(types)} params")

    bad_inputs, bad_outputs = collect_formula_text_inputs(types)
    label_refs = collect_label_refs()

    label_not_text = sorted(
        (p, types[p], sorted(s)[:3])
        for p, s in label_refs.items()
        if p in types and types[p] != "TEXT"
    )
    label_missing = sorted(p for p in label_refs if p not in types)

    print(f"\n[1] TEXT-formula output params not typed TEXT: {len(bad_outputs)}")
    for o, t in bad_outputs:
        print(f"    {o}: MR={t}")
    print(f"\n[2] YESNO params used as inputs inside TEXT formulas: {len(bad_inputs)}")
    for i, o in sorted(bad_inputs.items()):
        print(f"    {i} -> e.g. {o}")
    print(f"\n[3] Label-referenced params not typed TEXT: {len(label_not_text)}")
    for p, t, s in label_not_text:
        hint = (f"  (repoint label to {p}_DISP_TXT)"
                if t in ("NUMBER", "INTEGER", "LENGTH", "AREA", "VOLUME")
                else "  (flip to TEXT)")
        print(f"    {p}: MR={t}{hint}  e.g. {s}")
    print(f"\n[4] Label-referenced names missing from MR_PARAMETERS.txt: {len(label_missing)}")
    for p in label_missing:
        print(f"    {p}")

    total = len(bad_outputs) + len(bad_inputs) + len(label_not_text) + len(label_missing)
    print(f"\n{'ALIGNED — no data-type mismatches' if total == 0 else f'{total} MISMATCH(ES) — fix before merge'}")
    return 0 if total == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
