#!/usr/bin/env python3
"""
Generate a Revit shared-parameter file fragment from STING Pset XML.

Revit's shared-parameter file format is a tab-separated text file with
a fixed header. STING's existing MR_PARAMETERS.txt at v5.7 contains
~2,555 hand-authored entries; this converter emits a *new* fragment
covering only the properties declared in Pset_Sting* templates — the
manually-curated entries continue to live in the StingTools/Data/
folder.

USAGE

  # convert every Pset to MR_PARAMETERS-style fragment
  python3 tools/converters/sting_to_revit_params.py
      --out shared/ifc/revit_out/MR_PARAMETERS_Pset_fragment.txt

  # convert a single Pset
  python3 tools/converters/sting_to_revit_params.py
      --in shared/ifc/psets/Pset_StingTags.xml

  # CI gate: exit 1 if the committed fragment is not what the Pset XML
  # (and MR_PARAMETERS.txt) generate, or a property disagrees with
  # MR_PARAMETERS.txt on type
  python3 tools/converters/sting_to_revit_params.py --check

OUTPUT FORMAT

Revit shared-parameter file format (per Autodesk Revit Help):

    # This is a Revit shared parameter file.
    # Do not edit manually.
    *META  VERSION MINVERSION
    META   2       1
    *GROUP ID      NAME
    GROUP  101     PSet_StingTags
    *PARAM GUID    NAME            DATATYPE    DATACATEGORY    GROUP   VISIBLE DESCRIPTION    USERMODIFIABLE
    PARAM  <guid>  Discipline      TEXT        ...             101     1       ...            1

DataType mappings:
    IfcLabel        -> TEXT
    IfcText         -> TEXT
    IfcIdentifier   -> TEXT
    IfcInteger      -> INTEGER
    IfcReal         -> NUMBER
    IfcBoolean      -> YESNO
    IfcDate         -> TEXT
    IfcDateTime     -> TEXT

CAVEAT

Revit shared parameters require **stable GUIDs**. STING's Pset XML
carries IfdGuids per Pset but not per property. This script generates
deterministic per-property GUIDs by hashing (PsetName + PropName) so
that re-running produces the same GUIDs. Once Pset XML grows per-
property GUIDs, this converter switches to using those directly.

ONE GUID PER NAME (DSCH-38 follow-up)

Some templates name their properties after existing STING shared parameters
(Pset_StingCostManagement's CST_INSTALL_HRS, Pset_StingHVACExecution's HVC_*).
Revit keys a shared parameter on its GUID, so a fragment that minted a second
GUID for CST_INSTALL_HRS would define a different parameter with the same
name as the one MR_PARAMETERS.txt binds. Such a property takes the GUID and
data type from StingTools/Data/MR_PARAMETERS.txt (read only - the reference,
as tools/check_shared_param_files.py treats it). When the template's data type
maps to a different Revit type, --check fails and names it: one of the two
is wrong and a person decides which.
"""

from __future__ import annotations

import argparse
import hashlib
import sys
import uuid
from pathlib import Path
from xml.etree import ElementTree as ET

REPO_ROOT = Path(__file__).resolve().parents[2]
PSETS_DIR = REPO_ROOT / "shared" / "ifc" / "psets"
DEFAULT_OUT = REPO_ROOT / "shared" / "ifc" / "revit_out" / "MR_PARAMETERS_Pset_fragment.txt"
PSET_NS = "https://stingtools.io/schema/ifc/psets/v1"
REFERENCE = REPO_ROOT / "StingTools" / "Data" / "MR_PARAMETERS.txt"
# (template's Revit type, MR_PARAMETERS type) pairs that hold the same value: an
# IfcInteger property may be stored in a NUMBER parameter (HVC_PEAK_HOUR).
COMPATIBLE = {("INTEGER", "NUMBER")}

DATATYPE_MAP = {
    "IfcLabel":      "TEXT",
    "IfcText":       "TEXT",
    "IfcIdentifier": "TEXT",
    "IfcInteger":    "INTEGER",
    "IfcReal":       "NUMBER",
    "IfcBoolean":    "YESNO",
    "IfcDate":       "TEXT",
    "IfcDateTime":   "TEXT",
}


def _t(parent: ET.Element, tag: str) -> ET.Element | None:
    return parent.find(f"{{{PSET_NS}}}{tag}")


def _text(parent: ET.Element, tag: str, default: str = "") -> str:
    el = _t(parent, tag)
    return el.text.strip() if el is not None and el.text else default


def deterministic_guid(pset_name: str, prop_name: str) -> str:
    """Hash-derived UUID v5-style identifier. Stable across runs."""
    namespace = uuid.UUID("a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a")  # Planscape docs namespace from CLAUDE.md
    return str(uuid.uuid5(namespace, f"{pset_name}.{prop_name}"))


def emit_group(group_id: int, group_name: str) -> str:
    return f"GROUP\t{group_id}\t{group_name}"


def emit_param(prop_guid: str, prop_name: str, datatype: str, group_id: int, description: str) -> str:
    # Sanitise description: no tabs / newlines
    clean = " ".join(description.split())
    return f"PARAM\t{prop_guid}\t{prop_name}\tTEXT\t\t{group_id}\t1\t{clean}\t1" if datatype == "TEXT" else \
           f"PARAM\t{prop_guid}\t{prop_name}\t{datatype}\t\t{group_id}\t1\t{clean}\t1"


def load_reference(path: Path = REFERENCE) -> dict[str, tuple[str, str]]:
    """name -> (GUID, Revit type) from the project shared-parameter file."""
    ref: dict[str, tuple[str, str]] = {}
    if not path.exists():
        return ref
    raw = path.read_bytes()
    text = raw.decode("utf-16") if raw[:2] in (b"\xff\xfe", b"\xfe\xff") else raw.decode("utf-8-sig", errors="replace")
    for line in text.splitlines():
        c = line.split("\t")
        if c[0] == "PARAM" and len(c) > 3:
            ref.setdefault(c[2], (c[1], c[3]))
    return ref


def convert(pset_path: Path, group_id: int, reference: dict | None = None,
            conflicts: list | None = None) -> tuple[list[str], str]:
    """Convert one Pset XML to a list of Revit lines + group name. A property
    named like a parameter in ``reference`` reuses that parameter's GUID and
    type; a type disagreement is appended to ``conflicts``."""
    root = ET.parse(pset_path).getroot()
    if root.tag != f"{{{PSET_NS}}}StingPropertySetTemplate":
        raise ValueError(f"{pset_path}: root is not StingPropertySetTemplate")

    identity = _t(root, "Identity")
    pset_name = _text(identity, "Name")
    properties_el = _t(root, "Properties")
    if properties_el is None:
        return [], pset_name

    lines: list[str] = []
    for p in properties_el.findall(f"{{{PSET_NS}}}Property"):
        prop_name = p.attrib["name"]
        prop_guid = deterministic_guid(pset_name, prop_name)
        datatype = DATATYPE_MAP.get(_text(p, "DataType"), "TEXT")
        if reference and prop_name in reference:
            ref_guid, ref_type = reference[prop_name]
            if ref_type != datatype and (datatype, ref_type) not in COMPATIBLE and conflicts is not None:
                conflicts.append(f"{pset_name}.{prop_name}: template {_text(p, 'DataType') or 'IfcLabel'} -> "
                                 f"{datatype}, but MR_PARAMETERS.txt declares {ref_type}")
            prop_guid, datatype = ref_guid, ref_type
        description = _text(p, "Definition")
        lines.append(emit_param(prop_guid, prop_name, datatype, group_id, description))
    return lines, pset_name


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--in", dest="src", help="single Pset XML (omit to convert all)")
    p.add_argument("--out", default=str(DEFAULT_OUT))
    p.add_argument("--verbose", action="store_true")
    p.add_argument("--check", action="store_true",
                   help="exit 1 if --out is stale or a property disagrees with MR_PARAMETERS.txt")
    args = p.parse_args(argv)
    reference = load_reference()
    conflicts: list[str] = []

    out_path = Path(args.out)

    psets: list[Path]
    if args.src:
        psets = [Path(args.src)]
    else:
        psets = sorted(PSETS_DIR.glob("*.xml"), key=lambda p: p.name)  # by name: Path order is case-insensitive on Windows only

    if not psets:
        print(f"no Pset files found", file=sys.stderr)
        return 1

    output: list[str] = []
    output.append("# This is a Revit shared parameter file.")
    output.append("# Generated by tools/converters/sting_to_revit_params.py")
    output.append("# Do not edit manually — re-run the converter when the Pset XML changes.")
    output.append("*META\tVERSION\tMINVERSION")
    output.append("META\t2\t1")
    output.append("*GROUP\tID\tNAME")

    group_lines: list[str] = []
    param_lines: list[str] = []
    next_group_id = 200  # leave 1-199 for hand-curated existing groups

    for pset_path in psets:
        params, name = convert(pset_path, next_group_id, reference, conflicts)
        group_lines.append(emit_group(next_group_id, name))
        param_lines.extend(params)
        next_group_id += 1
        if args.verbose:
            print(f"  {pset_path.name}: {len(params)} params -> group {next_group_id-1}")

    output.extend(group_lines)
    output.append("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE")
    output.extend(param_lines)

    text = "\n".join(output) + "\n"
    if conflicts:
        print("Pset properties disagree with MR_PARAMETERS.txt on type (fix one side):", file=sys.stderr)
        for c in conflicts:
            print("  " + c, file=sys.stderr)
    if args.check:
        current = out_path.read_text(encoding="utf-8") if out_path.exists() else ""
        if current.replace("\r\n", "\n") != text:
            print(f"{out_path} is stale - run python tools/converters/sting_to_revit_params.py", file=sys.stderr)
            return 1
        if conflicts:
            return 1
        print(f"{out_path} is current ({len(group_lines)} groups, {len(param_lines)} params)")
        return 0
    out_path.parent.mkdir(parents=True, exist_ok=True)
    with open(out_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    print(f"wrote {out_path} ({len(group_lines)} groups, {len(param_lines)} params)")
    return 1 if conflicts else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
