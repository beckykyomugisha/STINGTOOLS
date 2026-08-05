"""IFC class → STING token inference.

Pure, host-agnostic logic extracted out of the Bonsai operators
(`stingtools-bonsai/ops/tagging_ops.py`) so it lives in core and every
host adapter calls the same rules. Imports no ``bpy``. ``ifcopenshell`` is
imported lazily and defensively — the module is importable without it, and
element-dependent helpers degrade to the ``XX`` sentinel rather than raising.

Boundary rule (Phase A6): adapters must NOT re-implement these — they call here.
"""

from __future__ import annotations

from typing import Any

SENTINEL = "XX"

# IFC class → STING discipline code. Single source of truth (was inline in the
# Bonsai auto-tag operator).
DISCIPLINE_BY_IFC_CLASS: dict[str, str] = {
    # Mechanical
    "IfcFlowTerminal": "M", "IfcAirTerminal": "M", "IfcUnitaryEquipment": "M",
    "IfcCoil": "M", "IfcDamper": "M", "IfcDuctFitting": "M", "IfcDuctSegment": "M",
    "IfcDuctSilencer": "M", "IfcFilter": "M", "IfcSpaceHeater": "M",
    # Plumbing / public health
    "IfcPipeFitting": "P", "IfcPipeSegment": "P", "IfcSanitaryTerminal": "P",
    "IfcValve": "P",
    # Electrical
    "IfcElectricAppliance": "E", "IfcElectricDistributionBoard": "E",
    "IfcElectricFlowStorageDevice": "E", "IfcElectricGenerator": "E",
    "IfcElectricMotor": "E", "IfcLamp": "E", "IfcLightFixture": "E",
    "IfcOutlet": "E", "IfcProtectiveDevice": "E", "IfcSwitchingDevice": "E",
    "IfcTransformer": "E", "IfcCableCarrierFitting": "E",
    "IfcCableCarrierSegment": "E", "IfcCableFitting": "E", "IfcCableSegment": "E",
    # Architectural
    "IfcWall": "A", "IfcWallStandardCase": "A", "IfcWindow": "A", "IfcDoor": "A",
    "IfcSlab": "A", "IfcRoof": "A", "IfcCovering": "A", "IfcCurtainWall": "A",
    # Structural
    "IfcColumn": "S", "IfcBeam": "S", "IfcMember": "S", "IfcPile": "S",
    "IfcFooting": "S",
    # Fire protection
    "IfcFireSuppressionTerminal": "FP", "IfcAlarm": "FP",
}

# IfcSystem name keyword → STING system code, evaluated in order.
_SYSTEM_KEYWORDS: tuple[tuple[tuple[str, ...], str], ...] = (
    (("HVAC", "AIR", "VENT", "DUCT"), "HVAC"),
    (("DRAIN", "SANIT", "WASTE", "SEWAGE"), "SAN"),
    (("COLD", "DCW", "CWS"), "DCW"),
    (("HOT", "DHW", "HWS"), "DHW"),
    (("ELECTRIC", "POWER", "LV"), "ELC"),
    (("FIRE", "SPRINKLER", "FP"), "FP"),
    (("GAS",), "GAS"),
)


def discipline_for_class(ifc_class: str) -> str:
    """Pure lookup: IFC class string → discipline code. ``XX`` when unknown."""
    return DISCIPLINE_BY_IFC_CLASS.get(ifc_class, SENTINEL)


def infer_discipline(element: Any) -> str:
    """Discipline code for an ifcopenshell element via its ``is_a()`` class."""
    try:
        return discipline_for_class(element.is_a())
    except Exception:  # pragma: no cover - defensive
        return SENTINEL


import re as _re

# Storey-name patterns, ordered. This is the UNION of what the IFC path and the
# ArchiCAD bridge each recognised before Phase 205 single-sourced them; they had
# drifted into two functions answering the same question differently (13 of 31
# probe cases disagreed). Ordering matters — roof before the digit sweep, so
# "Roof 2" is RF not L02.
_ROOF_RE     = _re.compile(r"roof|rooftop|penthouse|attic|\btop\b", _re.I)
_MEZZ_RE     = _re.compile(r"mezzanine|\bmez\b", _re.I)
_PLANT_RE    = _re.compile(r"plant", _re.I)
_BASEMENT_RE = _re.compile(r"basement|below|sous[- ]sol|\bb\.?\s*(\d+)\b", _re.I)
_BASEMENT_N  = _re.compile(r"(?:basement|sous[- ]sol|\bb)\D*?(\d+)", _re.I)
_GROUND_RE   = _re.compile(r"ground|gr\.?\s*fl|\bg\s*/?\s*f\b|rez[- ]de[- ]chauss", _re.I)
_LEVEL_N_RE  = _re.compile(r"\b[lL]\s*(\d+)\b|\b(\d+)\s*(?:st|nd|rd|th)\b|\b(\d+)\b")


def level_for_storey_name(name: str | None, elevation: float | None = None) -> str:
    """Derive a short STING level code from a storey name (+ optional elevation).

    Single source of truth for every host. Returns ``XX`` when nothing can be
    determined — callers treat that as "unknown", never as a real level.
    """
    raw = (name or "").strip()
    if raw:
        if _ROOF_RE.search(raw):
            return "RF"
        if _MEZZ_RE.search(raw):
            return "MZ"
        if _PLANT_RE.search(raw):
            return "PR"
        if _BASEMENT_RE.search(raw):
            # Capture the level number wherever it appears. The previous ArchiCAD
            # regex put the bare word "basement" before the digit group, so it
            # matched first and "Basement 2" fell through to the default B1 —
            # silently colliding with Basement 1. Fixed in Phase 205.
            m = _BASEMENT_N.search(raw)
            return f"B{int(m.group(1))}" if m else "B1"
        if _GROUND_RE.search(raw) or raw.upper() in ("GF", "G", "0"):
            return "GF"
        m = _LEVEL_N_RE.search(raw)
        if m:
            digits = next((g for g in m.groups() if g), None)
            if digits is not None:
                return f"L{int(digits):02d}"

    if elevation is not None:
        try:
            elev = float(elevation)
            if elev < -0.5:
                return "B1"
            if abs(elev) <= 0.5:
                return "GF"
            return f"L{max(1, round(elev / 3)):02d}"
        except (ValueError, TypeError):
            pass
    return SENTINEL


def system_for_name(name: str | None) -> str:
    """Pure: IfcSystem name → STING system code."""
    name = (name or "").upper()
    for keywords, code in _SYSTEM_KEYWORDS:
        if any(k in name for k in keywords):
            return code
    return SENTINEL


def infer_level(element: Any) -> str:
    """Level code from the element's containing ``IfcBuildingStorey``."""
    try:
        import ifcopenshell.util.element as ifc_util  # type: ignore
        container = ifc_util.get_container(element)
        if container is None or not container.is_a("IfcBuildingStorey"):
            return SENTINEL
        return level_for_storey_name(container.Name, getattr(container, "Elevation", None))
    except Exception:
        return SENTINEL


def infer_system(element: Any) -> str:
    """System code from ``IfcRelAssignsToGroup`` → ``IfcSystem`` membership."""
    try:
        model = element.wrapped_data.file
        for rel in model.get_inverse(element):
            if rel.is_a("IfcRelAssignsToGroup"):
                grp = rel.RelatingGroup
                if grp.is_a("IfcSystem"):
                    code = system_for_name(grp.Name)
                    if code != SENTINEL:
                        return code
        return SENTINEL
    except Exception:
        return SENTINEL


def product_for_type_name(type_name: str | None) -> str:
    """Pure: element-type name → 3-letter product code (alpha-only, upper)."""
    clean = "".join(c for c in (type_name or "").upper() if c.isalpha())
    return clean[:3] if clean else SENTINEL


def infer_product(element: Any) -> str:
    """Product code from the element's ``IfcRelDefinesByType`` type name."""
    try:
        for rel in element.wrapped_data.file.get_inverse(element):
            if rel.is_a("IfcRelDefinesByType"):
                type_obj = rel.RelatingType
                if type_obj and type_obj.Name:
                    code = product_for_type_name(type_obj.Name)
                    if code != SENTINEL:
                        return code
        return SENTINEL
    except Exception:
        return SENTINEL


class SequenceAllocator:
    """Per-(path, disc, sys, level) monotonic 4-digit sequence allocator.

    Replaces the module-global ``_SEQ_COUNTERS`` dict that used to live in the
    Bonsai operator. Construct one per tagging pass; ``seed_from_model`` primes
    it from existing ``Pset_StingTags.Sequence`` values so new tags continue
    from max+1 rather than colliding.
    """

    PAD = 4

    def __init__(self) -> None:
        self._counters: dict[tuple, int] = {}

    @staticmethod
    def _key(path: str, disc: str, sys: str, level: str) -> tuple:
        return (path, disc, sys, level)

    def next(self, path: str, disc: str, sys: str, level: str) -> str:
        key = self._key(path, disc, sys, level)
        self._counters[key] = self._counters.get(key, 0) + 1
        return str(self._counters[key]).zfill(self.PAD)

    def peek(self, path: str, disc: str, sys: str, level: str) -> int:
        return self._counters.get(self._key(path, disc, sys, level), 0)

    def observe(self, path: str, disc: str, sys: str, level: str, seq_value: int) -> None:
        """Raise the high-water mark for a group from an existing tag."""
        key = self._key(path, disc, sys, level)
        if seq_value > self._counters.get(key, 0):
            self._counters[key] = seq_value

    def seed_from_model(self, model: Any, get_pset: Any | None = None) -> None:
        """Prime counters from a model's existing ``Pset_StingTags.Sequence`` values.

        ``get_pset`` is an optional callable ``(element, pset_name) -> dict`` so
        the caller can supply its own pset reader; defaults to
        ``ifcopenshell.util.element.get_pset``.
        """
        path = getattr(model, "path", "") or ""
        if get_pset is None:
            try:
                import ifcopenshell.util.element as ue  # type: ignore
                get_pset = lambda el, name: ue.get_pset(el, name) or {}  # noqa: E731
            except Exception:
                return
        try:
            for el in model.by_type("IfcElement"):
                pset = get_pset(el, "Pset_StingTags") or {}
                if not pset:
                    continue
                try:
                    seq_val = int(pset.get("Sequence", "0000"))
                except (ValueError, TypeError):
                    continue
                self.observe(
                    path,
                    pset.get("Discipline", SENTINEL),
                    pset.get("System", SENTINEL),
                    pset.get("Level", SENTINEL),
                    seq_val,
                )
        except Exception:
            pass
