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


def convert(pset_path: Path, group_id: int) -> tuple[list[str], str]:
    """Convert one Pset XML to a list of Revit lines + group name."""
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
        description = _text(p, "Definition")
        lines.append(emit_param(prop_guid, prop_name, datatype, group_id, description))
    return lines, pset_name


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--in", dest="src", help="single Pset XML (omit to convert all)")
    p.add_argument("--out", default=str(DEFAULT_OUT))
    p.add_argument("--verbose", action="store_true")
    args = p.parse_args(argv)

    out_path = Path(args.out)
    out_path.parent.mkdir(parents=True, exist_ok=True)

    psets: list[Path]
    if args.src:
        psets = [Path(args.src)]
    else:
        psets = sorted(PSETS_DIR.glob("*.xml"))

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
        params, name = convert(pset_path, next_group_id)
        group_lines.append(emit_group(next_group_id, name))
        param_lines.extend(params)
        next_group_id += 1
        if args.verbose:
            print(f"  {pset_path.name}: {len(params)} params -> group {next_group_id-1}")

    output.extend(group_lines)
    output.append("*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE")
    output.extend(param_lines)

    out_path.write_text("\n".join(output) + "\n", encoding="utf-8")
    print(f"wrote {out_path} ({len(group_lines)} groups, {len(param_lines)} params)")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
