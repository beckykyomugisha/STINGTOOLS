#!/usr/bin/env python3
"""
Convert STING IFC enum + Pset templates → buildingSMART PSD XML.

buildingSMART tools (Solibri, BIM Vision, ArchiCAD IFC translator,
BlenderBIM PSD importer) consume the IFC4 PropertySetTemplate XML
format defined at
https://github.com/buildingSMART/IFC4.3-DocumentationGenerator/tree/main/psd_validate

Our internal format (StingPropertyEnumeration / StingPropertySetTemplate)
carries additional metadata that PSD doesn't (governance, lock,
cross-entity rules). This converter projects each artefact onto a PSD-
compatible XML, suitable for distribution to buildingSMART-compliant
tools.

USAGE

  # convert all enums + psets, output under shared/ifc/psd_out/
  python3 tools/converters/sting_to_psd.py

  # convert one file by path
  python3 tools/converters/sting_to_psd.py --in shared/ifc/enums/StingDisciplineCodes.xml --out /tmp/out.xml

  # dry-run, validate but don't write
  python3 tools/converters/sting_to_psd.py --dry-run

OUTPUTS

  Per-enum:  one PropertyEnumeration XML file
  Per-pset:  one PropertySetDef XML file

The output is suitable for embedding into IFC files via
ifcopenshell.api.pset (IfcPropertySetTemplate + IfcPropertyEnumeration
entities), or for upload to bSDD via the dictionary API.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path
from xml.etree import ElementTree as ET
from xml.dom import minidom

REPO_ROOT = Path(__file__).resolve().parents[2]
ENUMS_DIR = REPO_ROOT / "shared" / "ifc" / "enums"
PSETS_DIR = REPO_ROOT / "shared" / "ifc" / "psets"
DEFAULT_OUT_DIR = REPO_ROOT / "shared" / "ifc" / "psd_out"

ENUM_NS = "https://stingtools.io/schema/ifc/enums/v1"
PSET_NS = "https://stingtools.io/schema/ifc/psets/v1"

# buildingSMART PSD root namespace (per IFC4 PSD authoring conventions)
BSI_PSD_NS = "http://buildingSMART-tech.org/xml/psd/PSDS_IFC4.xsd"


# ----------------------------------------------------------------------
# enum -> PropertyEnumeration
# ----------------------------------------------------------------------

def _t(parent: ET.Element, tag: str, ns: str) -> ET.Element | None:
    return parent.find(f"{{{ns}}}{tag}")


def _text(parent: ET.Element, tag: str, ns: str, default: str = "") -> str:
    el = _t(parent, tag, ns)
    return el.text.strip() if el is not None and el.text else default


def convert_enum(src: Path) -> ET.Element:
    """STING enum XML → PSD-flavoured PropertyEnumeration."""
    root = ET.parse(src).getroot()
    if root.tag != f"{{{ENUM_NS}}}StingPropertyEnumeration":
        raise ValueError(f"{src}: root is not StingPropertyEnumeration")

    identity = _t(root, "Identity", ENUM_NS)
    name = _text(identity, "Name", ENUM_NS)
    definition = _text(identity, "Definition", ENUM_NS)
    ifd_guid = _text(identity, "IfdGuid", ENUM_NS)

    out = ET.Element("PropertyEnumeration")
    out.set("ifdguid", ifd_guid.replace("uuid:", "") if ifd_guid.startswith("uuid:") else ifd_guid)
    out.set("ifcversion", "4")
    ET.SubElement(out, "Name").text = name
    ET.SubElement(out, "Definition").text = definition

    values_el = ET.SubElement(out, "EnumerationValues")
    src_values = _t(root, "Values", ENUM_NS)
    if src_values is not None:
        for v in src_values.findall(f"{{{ENUM_NS}}}Value"):
            ev = ET.SubElement(values_el, "EnumerationValue")
            ET.SubElement(ev, "Value").text = v.attrib["code"]
            v_def = _text(v, "Definition", ENUM_NS)
            if v_def:
                ET.SubElement(ev, "Definition").text = v_def
            if v.attrib.get("display_form"):
                disp = ET.SubElement(ev, "Display")
                disp.text = v.attrib["display_form"]

    return out


# ----------------------------------------------------------------------
# pset -> PropertySetDef
# ----------------------------------------------------------------------

PSET_DATATYPE_MAP = {
    "IfcLabel":        "P_SINGLEVALUE",  # with TypePropertySingleValue + IfcLabel
    "IfcText":         "P_SINGLEVALUE",
    "IfcIdentifier":   "P_SINGLEVALUE",
    "IfcInteger":      "P_SINGLEVALUE",
    "IfcReal":         "P_SINGLEVALUE",
    "IfcBoolean":      "P_SINGLEVALUE",
    "IfcDate":         "P_SINGLEVALUE",
    "IfcDateTime":     "P_SINGLEVALUE",
}


def convert_pset(src: Path) -> ET.Element:
    """STING Pset XML → PSD-flavoured PropertySetDef."""
    root = ET.parse(src).getroot()
    if root.tag != f"{{{PSET_NS}}}StingPropertySetTemplate":
        raise ValueError(f"{src}: root is not StingPropertySetTemplate")

    identity = _t(root, "Identity", PSET_NS)
    name = _text(identity, "Name", PSET_NS)
    definition = _text(identity, "Definition", PSET_NS)
    ifd_guid = _text(identity, "IfdGuid", PSET_NS)
    template_type = _text(root, "TemplateType", PSET_NS, "PSET_TYPEDRIVENOVERRIDE")

    out = ET.Element("PropertySetDef")
    out.set("ifdguid", ifd_guid.replace("uuid:", "") if ifd_guid.startswith("uuid:") else ifd_guid)
    out.set("templatetype", template_type)
    out.set("ifcversion", "4")
    ET.SubElement(out, "Name").text = name
    ET.SubElement(out, "Definition").text = definition

    # Applicable entities
    app_el = _t(root, "Applicability", PSET_NS)
    applicable = ET.SubElement(out, "ApplicableClasses")
    if app_el is not None:
        for ent in app_el.findall(f"{{{PSET_NS}}}ApplicableEntity"):
            ET.SubElement(applicable, "ClassName").text = (ent.text or "").strip()

    # Properties
    props_el = _t(root, "Properties", PSET_NS)
    pdefs = ET.SubElement(out, "PropertyDefs")
    if props_el is not None:
        for p in props_el.findall(f"{{{PSET_NS}}}Property"):
            pdef = ET.SubElement(pdefs, "PropertyDef")
            pdef.set("ifdguid", "")  # PSD requires per-property GUIDs in production
            ET.SubElement(pdef, "Name").text = p.attrib["name"]
            p_def = _text(p, "Definition", PSET_NS)
            if p_def:
                ET.SubElement(pdef, "Definition").text = p_def
            pt = ET.SubElement(pdef, "PropertyType")
            psv = ET.SubElement(pt, "TypePropertySingleValue")
            dt_el = ET.SubElement(psv, "DataType")
            data_type = _text(p, "DataType", PSET_NS, "IfcLabel")
            dt_el.set("type", data_type)
            enum_ref = _text(p, "Enumeration", PSET_NS)
            if enum_ref:
                # PSD encodes enumerated properties differently — as P_ENUMERATEDVALUE
                # with EnumList. Project this when present.
                psv_parent = pt
                psv_parent.remove(psv)
                pev = ET.SubElement(psv_parent, "TypePropertyEnumeratedValue")
                el_ref = ET.SubElement(pev, "EnumList")
                el_ref.set("name", enum_ref)
    return out


# ----------------------------------------------------------------------
# pretty-print + write
# ----------------------------------------------------------------------

def prettify(element: ET.Element) -> str:
    raw = ET.tostring(element, encoding="utf-8")
    return minidom.parseString(raw).toprettyxml(indent="  ", encoding="utf-8").decode("utf-8")


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--in", dest="src", help="single source file (omit to convert all)")
    p.add_argument("--out", dest="dst", help="output file (when --in given) or out dir")
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--verbose", action="store_true")
    args = p.parse_args(argv)

    out_dir = Path(args.dst) if args.dst else DEFAULT_OUT_DIR
    if not args.dry_run:
        out_dir.mkdir(parents=True, exist_ok=True)

    files: list[tuple[str, Path]] = []
    if args.src:
        path = Path(args.src)
        root = ET.parse(path).getroot()
        if root.tag.endswith("StingPropertyEnumeration"):
            files.append(("enum", path))
        elif root.tag.endswith("StingPropertySetTemplate"):
            files.append(("pset", path))
        else:
            print(f"{path}: unknown root element {root.tag}", file=sys.stderr)
            return 1
    else:
        for p_ in sorted(ENUMS_DIR.glob("*.xml")):
            files.append(("enum", p_))
        for p_ in sorted(PSETS_DIR.glob("*.xml")):
            files.append(("pset", p_))

    converted = 0
    for kind, path in files:
        if kind == "enum":
            elem = convert_enum(path)
            out_name = f"PEnum_{path.stem}.xml"
        else:
            elem = convert_pset(path)
            out_name = f"{path.stem}.xml"
        out_str = prettify(elem)
        if args.dry_run:
            if args.verbose:
                print(f"--- {kind} {path.name} → {out_name} ({len(out_str)} bytes)")
        else:
            (out_dir / out_name).write_text(out_str, encoding="utf-8")
            if args.verbose:
                print(f"wrote {out_dir / out_name}")
        converted += 1

    print(f"converted {converted} file(s){' (dry-run)' if args.dry_run else f' to {out_dir}'}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
