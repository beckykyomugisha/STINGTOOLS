#!/usr/bin/env python3
"""
Compute SHA-256 corporate-lock checksums for every STING enum XML and
emit the manifest.json that indexes the lot.

Canonical JSON form (must match the rule documented in
shared/ifc/enums/_README.md):

    {
      "name": "<EnumName>",
      "version": "<SemVer>",
      "values": [
        { "code": "<code>", "sentinel": <bool>, "since": "<SemVer>",
          "deprecated": <bool>, "replaced_by": "<code>" },
        ...
      ]
    }

  - Values are sorted alphabetically by `code` (UTF-8 code-point order).
  - Only `code`, `sentinel`, `since`, `deprecated`, `replaced_by` are
    included — definitions / governance metadata are NOT part of the
    checksum (description edits don't flip the lock).
  - `deprecated` defaults to False; `replaced_by` is omitted if absent.
  - JSON: sort_keys=True, separators=(",", ":"), no trailing newline.

Usage:
    python3 tools/enums/compute_checksums.py
        --update     rewrite each XML's <Sha256> with the computed value
        --manifest   rewrite shared/ifc/enums/_manifest.json
        --check      exit 1 if any stored hash mismatches computed hash
        --verbose    print per-file detail

Defaults: --update --manifest --verbose
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from xml.etree import ElementTree as ET

NS = "https://stingtools.io/schema/ifc/enums/v1"
NSMAP = {"sting": NS}
REPO_ROOT = Path(__file__).resolve().parents[2]
ENUMS_DIR = REPO_ROOT / "shared" / "ifc" / "enums"
MANIFEST_PATH = ENUMS_DIR / "_manifest.json"


def _t(parent: ET.Element, tag: str) -> ET.Element | None:
    """Helper: find direct child by tag in our namespace."""
    return parent.find(f"sting:{tag}", NSMAP)


def _text(parent: ET.Element, tag: str, default: str = "") -> str:
    el = _t(parent, tag)
    return el.text.strip() if el is not None and el.text else default


def canonical_values(enum_root: ET.Element) -> list[dict]:
    """Return values list in canonical form for hashing."""
    values_el = _t(enum_root, "Values")
    if values_el is None:
        return []
    out = []
    for v in values_el.findall("sting:Value", NSMAP):
        entry = {
            "code": v.attrib["code"],
            "sentinel": v.attrib.get("sentinel", "false").lower() == "true",
            "since": _text(v, "SinceVersion", "0.0.0"),
            "deprecated": v.attrib.get("deprecated", "false").lower() == "true",
        }
        if "replaced_by" in v.attrib:
            entry["replaced_by"] = v.attrib["replaced_by"]
        out.append(entry)
    out.sort(key=lambda e: e["code"])
    return out


def canonical_json(enum_root: ET.Element) -> str:
    identity = _t(enum_root, "Identity")
    name = _text(identity, "Name") if identity is not None else ""
    version = enum_root.attrib.get("version", "0.0.0")
    payload = {
        "name": name,
        "version": version,
        "values": canonical_values(enum_root),
    }
    return json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256_hex(s: str) -> str:
    return hashlib.sha256(s.encode("utf-8")).hexdigest()


def parse_enum(xml_path: Path) -> dict:
    tree = ET.parse(xml_path)
    root = tree.getroot()
    if root.tag != f"{{{NS}}}StingPropertyEnumeration":
        raise ValueError(f"{xml_path.name}: root element is not StingPropertyEnumeration")
    identity = _t(root, "Identity")
    governance = _t(root, "Governance")
    ifc_mapping = _t(root, "IfcMapping")
    return {
        "path": xml_path,
        "tree": tree,
        "root": root,
        "name": _text(identity, "Name"),
        "definition": _text(identity, "Definition"),
        "ifd_guid": _text(identity, "IfdGuid"),
        "bsdd_iri": _text(identity, "BsddIri"),
        "scope": _text(governance, "Scope"),
        "origin": _text(governance, "Origin"),
        "since_version": _text(governance, "SinceVersion"),
        "standards_basis": _text(governance, "StandardsBasis"),
        "version": root.attrib.get("version", "0.0.0"),
        "schema_version": root.attrib.get("schema_version", "1"),
        "primary_ifc_type": _text(ifc_mapping, "PrimaryType"),
        "applicable_ifc_versions": _text(ifc_mapping, "ApplicableIfcVersions"),
        "value_count": len(_t(root, "Values").findall("sting:Value", NSMAP)) if _t(root, "Values") is not None else 0,
        "has_corporate_lock": _t(root, "CorporateLock") is not None,
    }


def stored_sha256(root: ET.Element) -> str | None:
    lock = _t(root, "CorporateLock")
    if lock is None:
        return None
    sha = _t(lock, "Sha256")
    return sha.text.strip() if sha is not None and sha.text else None


def write_back_sha256(tree: ET.ElementTree, root: ET.Element, new_sha: str, path: Path) -> None:
    """Rewrite the <Sha256> element in place. Preserves XML formatting as much as feasible."""
    lock = _t(root, "CorporateLock")
    if lock is None:
        return  # project_template scope — no lock to update
    sha_el = _t(lock, "Sha256")
    if sha_el is None:
        return
    sha_el.text = new_sha
    # Reserialise. Use minidom for pretty-printing? Keep ET output — round-trips OK if file was
    # created from the same source. For our hand-authored files this preserves indentation.
    ET.register_namespace("", NS)
    raw = ET.tostring(root, encoding="utf-8", xml_declaration=False).decode("utf-8")
    # ET strips the XML declaration we want; rebuild with the header.
    out = '<?xml version="1.0" encoding="UTF-8"?>\n' + raw + ("\n" if not raw.endswith("\n") else "")
    path.write_text(out, encoding="utf-8")


def main(argv: list[str]) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--update", action="store_true", help="rewrite each XML's <Sha256>")
    p.add_argument("--manifest", action="store_true", help="rewrite _manifest.json")
    p.add_argument("--check", action="store_true", help="exit 1 on any drift; no rewrites")
    p.add_argument("--verbose", action="store_true", help="per-file detail")
    args = p.parse_args(argv)

    # default: update + manifest + verbose
    if not any([args.update, args.manifest, args.check]):
        args.update = args.manifest = args.verbose = True

    ET.register_namespace("", NS)

    xml_files = sorted(p for p in ENUMS_DIR.glob("*.xml") if not p.name.startswith("_"))
    if not xml_files:
        print(f"no enum files found under {ENUMS_DIR}", file=sys.stderr)
        return 1

    entries: list[dict] = []
    drift_count = 0
    for xml_path in xml_files:
        meta = parse_enum(xml_path)
        cj = canonical_json(meta["root"])
        computed = sha256_hex(cj)
        existing = stored_sha256(meta["root"])

        status = "n/a"
        if meta["scope"] == "corporate":
            if existing == computed:
                status = "ok"
            elif existing and existing.startswith("UNCOMPUTED_"):
                status = "first_compute"
            else:
                status = "drift"
                drift_count += 1

        if args.verbose:
            print(f"  {meta['name']:<32} scope={meta['scope']:<17} values={meta['value_count']:>2} status={status} sha={computed[:12]}")

        if args.update and meta["scope"] == "corporate":
            write_back_sha256(meta["tree"], meta["root"], computed, xml_path)

        entries.append({
            "name": meta["name"],
            "file": xml_path.name,
            "version": meta["version"],
            "schema_version": int(meta["schema_version"]),
            "scope": meta["scope"],
            "origin": meta["origin"],
            "since_version": meta["since_version"],
            "ifd_guid": meta["ifd_guid"],
            "bsdd_iri": meta["bsdd_iri"] if meta["bsdd_iri"] else None,
            "primary_ifc_type": meta["primary_ifc_type"],
            "applicable_ifc_versions": meta["applicable_ifc_versions"].split(),
            "standards_basis": meta["standards_basis"],
            "value_count": meta["value_count"],
            "sha256": computed,
            "canonical_json_length": len(cj),
        })

    if args.check and drift_count > 0:
        print(f"DRIFT: {drift_count} enum(s) have stored hashes that do not match computed.", file=sys.stderr)
        return 1

    if args.manifest:
        manifest = {
            "schema_version": 1,
            "format": "STING IFC PropertyEnumeration manifest",
            "generated_by": "tools/enums/compute_checksums.py",
            "generated_for_repo_relative_path": "shared/ifc/enums/",
            "total_enums": len(entries),
            "by_tier": {
                "tier_1_essentials": 11,
                "tier_2_drawing": 7,
                "tier_3_workflow": 8,
                "tier_4_engineering": 14,
                "tier_5_healthcare": 11,
            },
            "enums": entries,
        }
        MANIFEST_PATH.write_text(
            json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )
        if args.verbose:
            print(f"manifest written: {MANIFEST_PATH.relative_to(REPO_ROOT)}")

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
