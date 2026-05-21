"""StingPropertyEnumeration XML loader + project-overlay resolver.

The loader:
  - parses every *.xml under shared/ifc/enums/
  - validates the SHA-256 corporate lock (when present)
  - layers project-overlay enums from <project>/_BIM_COORD/enums/ on top
  - exposes EnumRegistry with lookup + project-effective views
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Iterable, Iterator, Optional
from xml.etree import ElementTree as ET

from ..paths import enums_dir
from ..exceptions import EnumLoadError, CorporateLockDriftError, ProjectOverlayError
from .models import Enum, EnumValue, EnumScope, EnumOrigin

NS = "https://stingtools.io/schema/ifc/enums/v1"
NSTAG = f"{{{NS}}}"

# Codes that projects may NOT redefine via overlay
RESERVED_CODES = frozenset({"XX", "*", "SITE", "EXT", "GF", "MZ", "RF", "PR", "ZZ"})


# ----------------------------------------------------------------------
# canonical-JSON form (must match tools/enums/compute_checksums.py)
# ----------------------------------------------------------------------

def canonical_values_json(values: Iterable[EnumValue], enum_name: str, version: str) -> str:
    payload = {
        "name": enum_name,
        "version": version,
        "values": sorted(
            (
                {
                    "code": v.code,
                    "sentinel": v.sentinel,
                    "since": v.since,
                    "deprecated": v.deprecated,
                    **({"display_form": v.display_form} if v.display_form else {}),
                    **({"replaced_by": v.replaced_by} if v.replaced_by else {}),
                }
                for v in values
            ),
            key=lambda e: e["code"],
        ),
    }
    return json.dumps(payload, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def compute_sha256(enum: Enum) -> str:
    return hashlib.sha256(
        canonical_values_json(enum.values, enum.name, enum.version).encode("utf-8")
    ).hexdigest()


# ----------------------------------------------------------------------
# parser
# ----------------------------------------------------------------------

def _text(parent: ET.Element, tag: str, default: str = "") -> str:
    el = parent.find(f"{NSTAG}{tag}")
    return el.text.strip() if el is not None and el.text else default


def _parse_enum(path: Path) -> Enum:
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as e:
        raise EnumLoadError(f"{path.name}: XML parse error: {e}") from e
    if root.tag != f"{NSTAG}StingPropertyEnumeration":
        raise EnumLoadError(f"{path.name}: not a StingPropertyEnumeration")

    identity = root.find(f"{NSTAG}Identity")
    governance = root.find(f"{NSTAG}Governance")
    ifc_mapping = root.find(f"{NSTAG}IfcMapping")
    values_el = root.find(f"{NSTAG}Values")
    lock_el = root.find(f"{NSTAG}CorporateLock")

    values: list[EnumValue] = []
    if values_el is not None:
        for v in values_el.findall(f"{NSTAG}Value"):
            values.append(EnumValue(
                code=v.attrib["code"],
                sentinel=v.attrib.get("sentinel", "false").lower() == "true",
                definition=_text(v, "Definition"),
                since=_text(v, "SinceVersion", "0.0.0"),
                deprecated=v.attrib.get("deprecated", "false").lower() == "true",
                deprecated_in_version=v.attrib.get("deprecated_in_version"),
                replaced_by=v.attrib.get("replaced_by"),
                display_form=v.attrib.get("display_form"),
            ))

    return Enum(
        name=_text(identity, "Name"),
        definition=_text(identity, "Definition"),
        ifd_guid=_text(identity, "IfdGuid"),
        bsdd_iri=_text(identity, "BsddIri") or None,
        scope=EnumScope(_text(governance, "Scope") or "corporate"),
        origin=EnumOrigin(_text(governance, "Origin") or "baseline"),
        since_version=_text(governance, "SinceVersion"),
        maintainer=_text(governance, "Maintainer"),
        standards_basis=_text(governance, "StandardsBasis"),
        version=root.attrib.get("version", "0.0.0"),
        primary_ifc_type=_text(ifc_mapping, "PrimaryType") if ifc_mapping is not None else "IfcLabel",
        applicable_ifc_versions=tuple(
            (_text(ifc_mapping, "ApplicableIfcVersions") if ifc_mapping is not None else "").split()
        ),
        values=tuple(values),
        stored_sha256=_text(lock_el, "Sha256") if lock_el is not None else None,
        locked_at_version=_text(lock_el, "LockedAtVersion") if lock_el is not None else None,
        source_path=str(path),
    )


# ----------------------------------------------------------------------
# registry
# ----------------------------------------------------------------------

class EnumRegistry:
    """Loads + serves enums; supports project overlays."""

    def __init__(
        self,
        enums_directory: Optional[Path] = None,
        project_overlay_dir: Optional[Path] = None,
        verify_locks: bool = True,
    ):
        self._enums_dir = Path(enums_directory) if enums_directory else enums_dir()
        self._overlay_dir = Path(project_overlay_dir) if project_overlay_dir else None
        self._verify = verify_locks
        self._baseline: dict[str, Enum] = {}
        self._effective: dict[str, Enum] = {}
        self._drift: list[tuple[str, str, str]] = []  # (name, stored, computed)
        self._overlay_applied: set[str] = set()
        self._loaded = False

    # ------------------------------------------------------------------
    # loading
    # ------------------------------------------------------------------

    def load(self) -> "EnumRegistry":
        if self._loaded:
            return self

        # 1. baseline
        for xml in sorted(self._enums_dir.glob("*.xml")):
            if xml.name.startswith("_"):
                continue
            enum = _parse_enum(xml)
            self._baseline[enum.name] = enum
            self._effective[enum.name] = enum

        # 2. verify corporate locks
        if self._verify:
            for name, enum in self._baseline.items():
                if enum.scope is EnumScope.CORPORATE and enum.stored_sha256:
                    computed = compute_sha256(enum)
                    if computed != enum.stored_sha256:
                        self._drift.append((name, enum.stored_sha256, computed))

        # 3. project overlay
        if self._overlay_dir and self._overlay_dir.exists():
            for xml in sorted(self._overlay_dir.glob("*.xml")):
                if xml.name.startswith("_"):
                    continue
                try:
                    overlay = _parse_enum(xml)
                except (ValueError, ET.ParseError):
                    continue
                base = self._baseline.get(overlay.name)
                if base is None:
                    # project-only enum — accept verbatim
                    self._effective[overlay.name] = overlay
                    self._overlay_applied.add(overlay.name)
                    continue
                if base.scope is not EnumScope.PROJECT_TEMPLATE:
                    # corporate-locked enums are not overlayable; emit drift
                    self._drift.append((overlay.name, "(corporate, not overlayable)", "(overlay rejected)"))
                    continue
                # project-template overlay — merge while preserving reserved sentinels
                merged_values = self._merge_overlay_values(base, overlay)
                merged = Enum(
                    name=base.name,
                    definition=overlay.definition or base.definition,
                    ifd_guid=base.ifd_guid,
                    bsdd_iri=base.bsdd_iri,
                    scope=EnumScope.PROJECT_TEMPLATE,
                    origin=EnumOrigin.PROJECT,
                    since_version=base.since_version,
                    maintainer=overlay.maintainer or base.maintainer,
                    standards_basis=overlay.standards_basis or base.standards_basis,
                    version=overlay.version,
                    primary_ifc_type=base.primary_ifc_type,
                    applicable_ifc_versions=base.applicable_ifc_versions,
                    values=merged_values,
                    stored_sha256=None,
                    locked_at_version=None,
                    source_path=str(xml),
                )
                self._effective[base.name] = merged
                self._overlay_applied.add(base.name)

        self._loaded = True
        return self

    def _merge_overlay_values(self, base: Enum, overlay: Enum) -> tuple[EnumValue, ...]:
        """Project overlay replaces non-reserved values; reserved codes preserved from baseline."""
        reserved_from_base = [v for v in base.values if v.code in RESERVED_CODES]
        overlay_non_reserved = [v for v in overlay.values if v.code not in RESERVED_CODES]
        # de-duplicate by code; overlay wins on collision
        seen: set[str] = set()
        out: list[EnumValue] = []
        for v in overlay_non_reserved + reserved_from_base:
            if v.code in seen:
                continue
            seen.add(v.code)
            out.append(v)
        return tuple(out)

    # ------------------------------------------------------------------
    # accessors
    # ------------------------------------------------------------------

    def __iter__(self) -> Iterator[Enum]:
        if not self._loaded:
            self.load()
        return iter(self._effective.values())

    def __len__(self) -> int:
        if not self._loaded:
            self.load()
        return len(self._effective)

    def __contains__(self, name: str) -> bool:
        if not self._loaded:
            self.load()
        return name in self._effective

    def get(self, name: str) -> Optional[Enum]:
        if not self._loaded:
            self.load()
        return self._effective.get(name)

    def require(self, name: str) -> Enum:
        e = self.get(name)
        if e is None:
            raise KeyError(f"Enum {name!r} not in registry (have {sorted(self._effective)})")
        return e

    def names(self) -> list[str]:
        if not self._loaded:
            self.load()
        return sorted(self._effective.keys())

    @property
    def drift(self) -> list[tuple[str, str, str]]:
        """Returns list of (enum_name, stored_sha, computed_sha) for any locked enum that fails verification."""
        if not self._loaded:
            self.load()
        return list(self._drift)

    @property
    def overlay_applied(self) -> set[str]:
        if not self._loaded:
            self.load()
        return set(self._overlay_applied)
