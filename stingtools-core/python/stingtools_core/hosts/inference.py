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


# IFC class → STING Function code. Sibling of DISCIPLINE_BY_IFC_CLASS: discipline
# says *whose* element it is, function says *what it does*. Unmapped classes fall
# back to GEN rather than XX — an element always has some function, we just may
# not know a more specific one.
FUNCTION_BY_IFC_CLASS: dict[str, str] = {
    # Air-side supply / return
    "IfcAirTerminal": "SUP", "IfcAirTerminalBox": "SUP", "IfcFan": "SUP",
    "IfcDuctSegment": "SUP", "IfcDuctFitting": "SUP", "IfcDuctSilencer": "SUP",
    "IfcUnitaryEquipment": "SUP", "IfcFilter": "RET",
    # Public health
    "IfcSanitaryTerminal": "SAN", "IfcSanitaryTerminalType": "SAN",
    "IfcFlowTerminal": "SAN",
    # Wet services
    "IfcPipeSegment": "SUP", "IfcPipeFitting": "SUP", "IfcValve": "SUP",
    "IfcPump": "SUP",
    # Power
    "IfcElectricDistributionBoard": "PWR", "IfcElectricMotor": "PWR",
    "IfcOutlet": "PWR", "IfcCableCarrierSegment": "PWR", "IfcCableSegment": "PWR",
    "IfcProtectiveDevice": "PWR", "IfcSwitchingDevice": "PWR",
    # Lighting
    "IfcLamp": "LTG", "IfcLightFixture": "LTG",
    # Fire
    "IfcFireSuppressionTerminal": "FP", "IfcAlarm": "FP", "IfcSensor": "FP",
    # Thermal plant
    "IfcBoiler": "HTG", "IfcHeatExchanger": "HTG",
    "IfcChiller": "CLG", "IfcCoolingTower": "CLG",
}

#: Fallback when an element's class carries no known function.
FUNCTION_FALLBACK = "GEN"


def discipline_for_class(ifc_class: str) -> str:
    """Pure lookup: IFC class string → discipline code. ``XX`` when unknown."""
    return DISCIPLINE_BY_IFC_CLASS.get(ifc_class, SENTINEL)


def function_for_class(ifc_class: str) -> str:
    """Pure lookup: IFC class string → function code. ``GEN`` when unknown."""
    return FUNCTION_BY_IFC_CLASS.get(ifc_class, FUNCTION_FALLBACK)


def infer_function(element: Any) -> str:
    """Function code for an ifcopenshell element via its ``is_a()`` class."""
    try:
        return function_for_class(element.is_a())
    except Exception:  # pragma: no cover - defensive
        return FUNCTION_FALLBACK


def infer_discipline(element: Any) -> str:
    """Discipline code for an ifcopenshell element via its ``is_a()`` class."""
    try:
        return discipline_for_class(element.is_a())
    except Exception:  # pragma: no cover - defensive
        return SENTINEL


def level_for_storey_name(name: str | None, elevation: float | None = None) -> str:
    """Pure: derive a short level code from a storey name (+ optional elevation)."""
    name = (name or "").strip().upper()
    if "ROOF" in name or "ROOFTOP" in name:
        return "RF"
    if "MEZZANINE" in name or "MEZ" in name:
        return "MZ"
    if "PLANT" in name:
        return "PR"
    if "BASEMENT" in name or (name.startswith("B") and any(c.isdigit() for c in name)):
        digit = next((c for c in name if c.isdigit()), "1")
        return f"B{digit}"
    if "GROUND" in name or name in ("GF", "G", "GROUND FLOOR", "0"):
        return "GF"
    for token in name.split():
        if token.isdigit():
            return f"L{int(token):02d}"
    if elevation is not None:
        try:
            elev = float(elevation)
            if elev < 0:
                return "B1"
            if elev < 0.5:
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


#: Zone code assigned to an element belonging to no ``IfcZone``.
ZONE_FALLBACK = "ZZ"

#: Location code assigned when an element resolves to no ``IfcBuilding``.
LOCATION_FALLBACK = "BLD1"

#: Guard against a malformed decomposition cycle when walking up to IfcBuilding.
_MAX_SPATIAL_DEPTH = 10


def zone_codes_by_element(model: Any) -> dict[int, str]:
    """Map element step-id → ``Z01``/``Z02``/… from ``IfcZone`` membership.

    Zones are numbered in model order. Elements belonging to no zone are absent
    from the result — callers apply :data:`ZONE_FALLBACK` themselves so they can
    distinguish "no zone" from "zone 0".

    Model-level rather than element-level because the code depends on the zone's
    ordinal across the whole file, which a per-element call cannot know.
    """
    out: dict[int, str] = {}
    try:
        zone_code = {
            zone.id(): f"Z{idx:02d}"
            for idx, zone in enumerate(model.by_type("IfcZone"), start=1)
        }
        if not zone_code:
            return out
        for rel in model.by_type("IfcRelAssignsToGroup"):
            group = getattr(rel, "RelatingGroup", None)
            if group is None or not group.is_a("IfcZone"):
                continue
            code = zone_code.get(group.id())
            if code is None:
                continue
            for obj in (rel.RelatedObjects or []):
                if obj.is_a("IfcElement"):
                    out[obj.id()] = code
    except Exception:
        return out
    return out


def building_codes_by_element(model: Any) -> dict[int, str]:
    """Map element step-id → ``BLD1``/``BLD2``/… via spatial containment.

    Walks ``IfcRelContainedInSpatialStructure`` then up the ``Decomposes`` chain
    (space → storey → building). Elements that resolve to no building are absent;
    callers apply :data:`LOCATION_FALLBACK`.
    """
    out: dict[int, str] = {}
    try:
        bld_code = {
            bld.id(): f"BLD{idx}"
            for idx, bld in enumerate(model.by_type("IfcBuilding"), start=1)
        }
        if not bld_code:
            return out
        for rel in model.by_type("IfcRelContainedInSpatialStructure"):
            node = getattr(rel, "RelatingStructure", None)
            code = None
            for _ in range(_MAX_SPATIAL_DEPTH):
                if node is None:
                    break
                if node.is_a("IfcBuilding"):
                    code = bld_code.get(node.id())
                    break
                decomp = getattr(node, "Decomposes", None) or []
                node = decomp[0].RelatingObject if decomp else None
            if code:
                for el in (rel.RelatedElements or []):
                    out[el.id()] = code
    except Exception:
        return out
    return out


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
