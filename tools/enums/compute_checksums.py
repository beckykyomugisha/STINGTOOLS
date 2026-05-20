#!/usr/bin/env python3
"""
Compute SHA-256 corporate-lock checksums for STING IFC artefacts and emit
their manifest indexes.

Handles two file kinds in two folders:

  shared/ifc/enums/    — StingPropertyEnumeration files
  shared/ifc/psets/    — StingPropertySetTemplate files

Each produces its own _manifest.json.

------------------------------------------------------------------------
Canonical JSON form — enums (must match shared/ifc/enums/_README.md):

    {
      "name": "<EnumName>",
      "version": "<SemVer>",
      "values": [
        { "code": "<code>",
          "sentinel": <bool>,
          "since": "<SemVer>",
          "deprecated": <bool>,
          "display_form": "<str>",
          "replaced_by": "<code>" },
        ...
      ]
    }

  - Values sorted alphabetically by `code` (UTF-8 code-point order).
  - Only `code`, `sentinel`, `since`, `deprecated`, `display_form`,
    `replaced_by` are included — definitions / governance metadata are
    NOT part of the checksum (description edits don't flip the lock).
  - `deprecated` defaults to False; `display_form` and `replaced_by`
    omitted if absent.
  - JSON: sort_keys=True, separators=(",", ":"), no trailing newline.

------------------------------------------------------------------------
Canonical JSON form — psets (shared/ifc/psets/_README.md):

    {
      "name": "<PsetName>",
      "version": "<SemVer>",
      "templateType": "<TemplateType>",
      "applicability": [<sorted IFC entity strings>],
      "properties": [
        { "name": "<PropName>",
          "cardinality": "<required|optional|prohibited>",
          "datatype": "<IfcLabel|IfcText|...>",
          "enumeration": "<EnumName or null>",
          "appliesTo": [<sorted IFC entities>],
          "since": "<SemVer>" },
        ...
      ],
      "validationRules": [
        { "id": "<RuleId>",
          "activeFrom": "<Stage>" },
        ...
      ]
    }

  - Properties sorted alphabetically by `name`.
  - Applicability + appliesTo + validation rules sorted alphabetically.
  - Only structural metadata is in the lock — definitions / remediation
    text / notes are excluded.
  - JSON: sort_keys=True, separators=(",", ":"), no trailing newline.

------------------------------------------------------------------------
Usage:
    python3 tools/enums/compute_checksums.py
        --update     rewrite each XML's <Sha256> with computed value
        --manifest   rewrite the two _manifest.json files
        --check      exit 1 if any stored hash mismatches computed hash
        --verbose    print per-file detail
        --kind {enums,psets,all}   limit scope

Defaults: --update --manifest --verbose --kind all
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from xml.etree import ElementTree as ET

ENUM_NS = "https://stingtools.io/schema/ifc/enums/v1"
PSET_NS = "https://stingtools.io/schema/ifc/psets/v1"
ENUM_NSMAP = {"s": ENUM_NS}
PSET_NSMAP = {"s": PSET_NS}

REPO_ROOT = Path(__file__).resolve().parents[2]
ENUMS_DIR = REPO_ROOT / "shared" / "ifc" / "enums"
PSETS_DIR = REPO_ROOT / "shared" / "ifc" / "psets"
ENUMS_MANIFEST = ENUMS_DIR / "_manifest.json"
PSETS_MANIFEST = PSETS_DIR / "_manifest.json"


# ----------------------------------------------------------------------
# generic helpers
# ----------------------------------------------------------------------

def _t(parent: ET.Element, tag: str, ns: str) -> ET.Element | None:
    return parent.find(f"{{{ns}}}{tag}")


def _text(parent: ET.Element, tag: str, ns: str, default: str = "") -> str:
    el = _t(parent, tag, ns)
    return el.text.strip() if el is not None and el.text else default


def _texts(parent: ET.Element, tag: str, ns: str) -> list[str]:
    return [
        (e.text or "").strip()
        for e in parent.findall(f"{{{ns}}}{tag}")
        if e.text and e.text.strip()
    ]


def sha256_hex(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


def canonical_dump(payload: dict) -> str:
    return json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


# ----------------------------------------------------------------------
# enum walker
# ----------------------------------------------------------------------

def canonical_enum_values(enum_root: ET.Element) -> list[dict]:
    values_el = _t(enum_root, "Values", ENUM_NS)
    if values_el is None:
        return []
    out = []
    for v in values_el.findall(f"{{{ENUM_NS}}}Value"):
        entry: dict = {
            "code": v.attrib["code"],
            "sentinel": v.attrib.get("sentinel", "false").lower() == "true",
            "since": _text(v, "SinceVersion", ENUM_NS, "0.0.0"),
            "deprecated": v.attrib.get("deprecated", "false").lower() == "true",
        }
        if "display_form" in v.attrib:
            entry["display_form"] = v.attrib["display_form"]
        if "replaced_by" in v.attrib:
            entry["replaced_by"] = v.attrib["replaced_by"]
        out.append(entry)
    out.sort(key=lambda e: e["code"])
    return out


def canonical_enum_json(enum_root: ET.Element) -> str:
    identity = _t(enum_root, "Identity", ENUM_NS)
    name = _text(identity, "Name", ENUM_NS) if identity is not None else ""
    version = enum_root.attrib.get("version", "0.0.0")
    return canonical_dump({
        "name": name,
        "version": version,
        "values": canonical_enum_values(enum_root),
    })


def parse_enum(xml_path: Path) -> dict:
    tree = ET.parse(xml_path)
    root = tree.getroot()
    if root.tag != f"{{{ENUM_NS}}}StingPropertyEnumeration":
        raise ValueError(f"{xml_path.name}: root is not StingPropertyEnumeration")
    identity = _t(root, "Identity", ENUM_NS)
    governance = _t(root, "Governance", ENUM_NS)
    ifc_mapping = _t(root, "IfcMapping", ENUM_NS)
    values_el = _t(root, "Values", ENUM_NS)
    return {
        "path": xml_path,
        "tree": tree,
        "root": root,
        "kind": "enum",
        "ns": ENUM_NS,
        "name": _text(identity, "Name", ENUM_NS),
        "definition": _text(identity, "Definition", ENUM_NS),
        "ifd_guid": _text(identity, "IfdGuid", ENUM_NS),
        "bsdd_iri": _text(identity, "BsddIri", ENUM_NS),
        "scope": _text(governance, "Scope", ENUM_NS),
        "origin": _text(governance, "Origin", ENUM_NS),
        "since_version": _text(governance, "SinceVersion", ENUM_NS),
        "standards_basis": _text(governance, "StandardsBasis", ENUM_NS),
        "version": root.attrib.get("version", "0.0.0"),
        "schema_version": root.attrib.get("schema_version", "1"),
        "primary_ifc_type": _text(ifc_mapping, "PrimaryType", ENUM_NS) if ifc_mapping is not None else "",
        "applicable_ifc_versions": _text(ifc_mapping, "ApplicableIfcVersions", ENUM_NS) if ifc_mapping is not None else "",
        "value_count": len(values_el.findall(f"{{{ENUM_NS}}}Value")) if values_el is not None else 0,
        "has_corporate_lock": _t(root, "CorporateLock", ENUM_NS) is not None,
        "canonical_json": canonical_enum_json(root),
    }


# ----------------------------------------------------------------------
# pset walker
# ----------------------------------------------------------------------

def canonical_pset_properties(pset_root: ET.Element) -> list[dict]:
    props_el = _t(pset_root, "Properties", PSET_NS)
    if props_el is None:
        return []
    out = []
    for p in props_el.findall(f"{{{PSET_NS}}}Property"):
        applies_to = _text(p, "AppliesTo", PSET_NS, "")
        out.append({
            "name": p.attrib["name"],
            "cardinality": p.attrib.get("cardinality", "optional"),
            "datatype": _text(p, "DataType", PSET_NS),
            "enumeration": _text(p, "Enumeration", PSET_NS) or None,
            "appliesTo": sorted(applies_to.split()),
            "since": _text(p, "SinceVersion", PSET_NS, "0.0.0"),
        })
    out.sort(key=lambda e: e["name"])
    return out


def canonical_pset_validation_rules(pset_root: ET.Element) -> list[dict]:
    rules_el = _t(pset_root, "CrossEntityValidationRules", PSET_NS)
    if rules_el is None:
        return []
    out = []
    for r in rules_el.findall(f"{{{PSET_NS}}}Rule"):
        out.append({
            "id": r.attrib["id"],
            "activeFrom": _text(r, "ActiveFrom", PSET_NS, ""),
        })
    out.sort(key=lambda e: e["id"])
    return out


def canonical_pset_json(pset_root: ET.Element) -> str:
    identity = _t(pset_root, "Identity", PSET_NS)
    name = _text(identity, "Name", PSET_NS) if identity is not None else ""
    version = pset_root.attrib.get("version", "0.0.0")
    template_type = _text(pset_root, "TemplateType", PSET_NS, "PSET_TYPEDRIVENOVERRIDE")
    applicability_el = _t(pset_root, "Applicability", PSET_NS)
    applicability = sorted(_texts(applicability_el, "ApplicableEntity", PSET_NS)) if applicability_el is not None else []
    return canonical_dump({
        "name": name,
        "version": version,
        "templateType": template_type,
        "applicability": applicability,
        "properties": canonical_pset_properties(pset_root),
        "validationRules": canonical_pset_validation_rules(pset_root),
    })


def parse_pset(xml_path: Path) -> dict:
    tree = ET.parse(xml_path)
    root = tree.getroot()
    if root.tag != f"{{{PSET_NS}}}StingPropertySetTemplate":
        raise ValueError(f"{xml_path.name}: root is not StingPropertySetTemplate")
    identity = _t(root, "Identity", PSET_NS)
    governance = _t(root, "Governance", PSET_NS)
    applicability_el = _t(root, "Applicability", PSET_NS)
    props_el = _t(root, "Properties", PSET_NS)
    rules_el = _t(root, "CrossEntityValidationRules", PSET_NS)
    return {
        "path": xml_path,
        "tree": tree,
        "root": root,
        "kind": "pset",
        "ns": PSET_NS,
        "name": _text(identity, "Name", PSET_NS),
        "definition": _text(identity, "Definition", PSET_NS),
        "ifd_guid": _text(identity, "IfdGuid", PSET_NS),
        "scope": _text(governance, "Scope", PSET_NS),
        "origin": _text(governance, "Origin", PSET_NS),
        "since_version": _text(governance, "SinceVersion", PSET_NS),
        "standards_basis": _text(governance, "StandardsBasis", PSET_NS),
        "version": root.attrib.get("version", "0.0.0"),
        "schema_version": root.attrib.get("schema_version", "1"),
        "template_type": _text(root, "TemplateType", PSET_NS, "PSET_TYPEDRIVENOVERRIDE"),
        "applicability": _texts(applicability_el, "ApplicableEntity", PSET_NS) if applicability_el is not None else [],
        "property_count": len(props_el.findall(f"{{{PSET_NS}}}Property")) if props_el is not None else 0,
        "validation_rule_count": len(rules_el.findall(f"{{{PSET_NS}}}Rule")) if rules_el is not None else 0,
        "has_corporate_lock": _t(root, "CorporateLock", PSET_NS) is not None,
        "canonical_json": canonical_pset_json(root),
    }


# ----------------------------------------------------------------------
# shared writeback + manifest
# ----------------------------------------------------------------------

def stored_sha256(root: ET.Element, ns: str) -> str | None:
    lock = _t(root, "CorporateLock", ns)
    if lock is None:
        return None
    sha = _t(lock, "Sha256", ns)
    return sha.text.strip() if sha is not None and sha.text else None


def write_back_sha256(tree: ET.ElementTree, root: ET.Element, ns: str, new_sha: str, path: Path) -> None:
    lock = _t(root, "CorporateLock", ns)
    if lock is None:
        return
    sha_el = _t(lock, "Sha256", ns)
    if sha_el is None:
        return
    sha_el.text = new_sha
    ET.register_namespace("", ns)
    raw = ET.tostring(root, encoding="utf-8", xml_declaration=False).decode("utf-8")
    out = '<?xml version="1.0" encoding="UTF-8"?>\n' + raw + ("\n" if not raw.endswith("\n") else "")
    path.write_text(out, encoding="utf-8")


def process_kind(
    label: str,
    directory: Path,
    manifest_path: Path,
    parser,
    expected_root_marker: str,
    args,
) -> tuple[int, list[dict]]:
    """Returns (drift_count, manifest_entries)."""
    ET.register_namespace("", parser.__globals__.get(expected_root_marker, ""))
    xml_files = sorted(p for p in directory.glob("*.xml") if not p.name.startswith("_"))
    if not xml_files:
        print(f"[{label}] no files found in {directory}", file=sys.stderr)
        return 0, []
    print(f"\n=== {label} ({len(xml_files)} file(s)) ===")
    drift = 0
    entries: list[dict] = []
    for path in xml_files:
        meta = parser(path)
        computed = sha256_hex(meta["canonical_json"])
        existing = stored_sha256(meta["root"], meta["ns"])
        status = "n/a"
        if meta["scope"] == "corporate":
            if existing == computed:
                status = "ok"
            elif existing and existing.startswith("UNCOMPUTED_"):
                status = "first_compute"
            else:
                status = "drift"
                drift += 1
        if args.verbose:
            extra = f"values={meta.get('value_count', '?')}" if meta["kind"] == "enum" else f"props={meta.get('property_count', '?')} rules={meta.get('validation_rule_count', '?')}"
            print(f"  {meta['name']:<34} scope={meta['scope']:<17} {extra:<22} status={status} sha={computed[:12]}")
        if args.update and meta["scope"] == "corporate":
            write_back_sha256(meta["tree"], meta["root"], meta["ns"], computed, path)
        # build manifest entry
        if meta["kind"] == "enum":
            entries.append({
                "name": meta["name"],
                "file": path.name,
                "kind": "enum",
                "version": meta["version"],
                "schema_version": int(meta["schema_version"]),
                "scope": meta["scope"],
                "origin": meta["origin"],
                "since_version": meta["since_version"],
                "ifd_guid": meta["ifd_guid"],
                "bsdd_iri": meta["bsdd_iri"] or None,
                "primary_ifc_type": meta["primary_ifc_type"],
                "applicable_ifc_versions": meta["applicable_ifc_versions"].split() if meta["applicable_ifc_versions"] else [],
                "standards_basis": meta["standards_basis"],
                "value_count": meta["value_count"],
                "sha256": computed,
                "canonical_json_length": len(meta["canonical_json"]),
            })
        else:
            entries.append({
                "name": meta["name"],
                "file": path.name,
                "kind": "pset",
                "version": meta["version"],
                "schema_version": int(meta["schema_version"]),
                "scope": meta["scope"],
                "origin": meta["origin"],
                "since_version": meta["since_version"],
                "ifd_guid": meta["ifd_guid"],
                "standards_basis": meta["standards_basis"],
                "template_type": meta["template_type"],
                "applicability": meta["applicability"],
                "property_count": meta["property_count"],
                "validation_rule_count": meta["validation_rule_count"],
                "sha256": computed,
                "canonical_json_length": len(meta["canonical_json"]),
            })

    if args.manifest:
        if label == "enums":
            manifest = {
                "schema_version": 2,
                "format": "STING IFC PropertyEnumeration manifest",
                "generated_by": "tools/enums/compute_checksums.py",
                "generated_for_repo_relative_path": "shared/ifc/enums/",
                "total_enums": len(entries),
                "by_tier": {
                    "tier_1_essentials": 12,
                    "tier_2_drawing": 7,
                    "tier_3_workflow": 8,
                    "tier_4_engineering": 14,
                    "tier_5_healthcare": 11,
                },
                "enums": entries,
            }
        else:
            manifest = {
                "schema_version": 1,
                "format": "STING IFC PropertySetTemplate manifest",
                "generated_by": "tools/enums/compute_checksums.py",
                "generated_for_repo_relative_path": "shared/ifc/psets/",
                "total_psets": len(entries),
                "psets": entries,
            }
        manifest_path.write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )
        if args.verbose:
            print(f"  manifest written: {manifest_path.relative_to(REPO_ROOT)}")

    return drift, entries


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--update", action="store_true")
    p.add_argument("--manifest", action="store_true")
    p.add_argument("--check", action="store_true")
    p.add_argument("--verbose", action="store_true")
    p.add_argument("--kind", choices=("enums", "psets", "all"), default="all")
    args = p.parse_args(argv)

    if not any([args.update, args.manifest, args.check]):
        args.update = args.manifest = args.verbose = True

    total_drift = 0
    if args.kind in ("enums", "all"):
        d, _ = process_kind("enums", ENUMS_DIR, ENUMS_MANIFEST, parse_enum, "ENUM_NS", args)
        total_drift += d
    if args.kind in ("psets", "all"):
        d, _ = process_kind("psets", PSETS_DIR, PSETS_MANIFEST, parse_pset, "PSET_NS", args)
        total_drift += d

    if args.check and total_drift > 0:
        print(f"\nDRIFT: {total_drift} artefact(s) have stored hashes that do not match computed.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
