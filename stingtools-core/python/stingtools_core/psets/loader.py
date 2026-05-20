"""Pset XML loader. Mirrors EnumRegistry but for shared/ifc/psets/."""

from __future__ import annotations

from pathlib import Path
from typing import Iterator, Optional
from xml.etree import ElementTree as ET

from ..paths import psets_dir
from ..exceptions import PsetLoadError
from .models import Pset, PsetProperty, ValidationRule

NS = "https://stingtools.io/schema/ifc/psets/v1"
NSTAG = f"{{{NS}}}"


def _text(parent: ET.Element, tag: str, default: str = "") -> str:
    el = parent.find(f"{NSTAG}{tag}")
    return el.text.strip() if el is not None and el.text else default


def _parse_pset(path: Path) -> Pset:
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as e:
        raise PsetLoadError(f"{path.name}: XML parse error: {e}") from e
    if root.tag != f"{NSTAG}StingPropertySetTemplate":
        raise PsetLoadError(f"{path.name}: not a StingPropertySetTemplate")

    identity = root.find(f"{NSTAG}Identity")
    governance = root.find(f"{NSTAG}Governance")
    applicability_el = root.find(f"{NSTAG}Applicability")
    props_el = root.find(f"{NSTAG}Properties")
    rules_el = root.find(f"{NSTAG}CrossEntityValidationRules")
    lock_el = root.find(f"{NSTAG}CorporateLock")

    applicability = tuple(
        (e.text or "").strip()
        for e in (applicability_el.findall(f"{NSTAG}ApplicableEntity") if applicability_el is not None else [])
        if e.text
    )

    properties: list[PsetProperty] = []
    if props_el is not None:
        for p in props_el.findall(f"{NSTAG}Property"):
            properties.append(PsetProperty(
                name=p.attrib["name"],
                cardinality=p.attrib.get("cardinality", "optional"),
                data_type=_text(p, "DataType", "IfcLabel"),
                definition=_text(p, "Definition"),
                enumeration=(_text(p, "Enumeration") or None),
                applies_to=tuple(_text(p, "AppliesTo").split()),
                since=_text(p, "SinceVersion", "0.0.0"),
            ))

    rules: list[ValidationRule] = []
    if rules_el is not None:
        for r in rules_el.findall(f"{NSTAG}Rule"):
            rules.append(ValidationRule(
                id=r.attrib["id"],
                description=_text(r, "Description"),
                active_from=_text(r, "ActiveFrom"),
                fail_message=_text(r, "FailMessage"),
                remediation=_text(r, "RemediationInstruction"),
            ))

    return Pset(
        name=_text(identity, "Name"),
        definition=_text(identity, "Definition"),
        ifd_guid=_text(identity, "IfdGuid"),
        scope=_text(governance, "Scope", "corporate"),
        origin=_text(governance, "Origin", "baseline"),
        since_version=_text(governance, "SinceVersion"),
        maintainer=_text(governance, "Maintainer"),
        standards_basis=_text(governance, "StandardsBasis"),
        version=root.attrib.get("version", "0.0.0"),
        template_type=_text(root, "TemplateType", "PSET_TYPEDRIVENOVERRIDE"),
        applicability=applicability,
        properties=tuple(properties),
        rules=tuple(rules),
        stored_sha256=_text(lock_el, "Sha256") if lock_el is not None else None,
        source_path=str(path),
    )


class PsetRegistry:
    def __init__(self, psets_directory: Optional[Path] = None):
        self._psets_dir = Path(psets_directory) if psets_directory else psets_dir()
        self._psets: dict[str, Pset] = {}
        self._loaded = False

    def load(self) -> "PsetRegistry":
        if self._loaded:
            return self
        for xml in sorted(self._psets_dir.glob("*.xml")):
            if xml.name.startswith("_"):
                continue
            pset = _parse_pset(xml)
            self._psets[pset.name] = pset
        self._loaded = True
        return self

    def __iter__(self) -> Iterator[Pset]:
        if not self._loaded:
            self.load()
        return iter(self._psets.values())

    def __len__(self) -> int:
        if not self._loaded:
            self.load()
        return len(self._psets)

    def __contains__(self, name: str) -> bool:
        if not self._loaded:
            self.load()
        return name in self._psets

    def get(self, name: str) -> Optional[Pset]:
        if not self._loaded:
            self.load()
        return self._psets.get(name)

    def require(self, name: str) -> Pset:
        p = self.get(name)
        if p is None:
            raise KeyError(f"Pset {name!r} not in registry (have {sorted(self._psets)})")
        return p

    def names(self) -> list[str]:
        if not self._loaded:
            self.load()
        return sorted(self._psets.keys())

    def referenced_enums(self) -> set[str]:
        """Collect every <Enumeration> reference across all psets."""
        if not self._loaded:
            self.load()
        out: set[str] = set()
        for pset in self._psets.values():
            for p in pset.properties:
                if p.enumeration:
                    out.add(p.enumeration)
        return out
