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
    "IfcRailing": "A", "IfcStair": "A", "IfcStairFlight": "A", "IfcRamp": "A",
    "IfcRampFlight": "A", "IfcFurnishingElement": "A", "IfcFurniture": "A",
    "IfcShadingDevice": "A", "IfcGeographicElement": "A",
    # Structural
    "IfcColumn": "S", "IfcBeam": "S", "IfcMember": "S", "IfcPile": "S",
    "IfcFooting": "S", "IfcPlate": "S", "IfcReinforcingBar": "S",
    # Fire protection
    "IfcFireSuppressionTerminal": "FP", "IfcAlarm": "FP",
}

# IFC class → STING Function code. Single source of truth (was inline as
# ``_CLASS_TO_FUNC`` in the Bonsai spatial operator). Unknown class → ``GEN``,
# never ``XX``: "generic" is a real function bucket, not a missing-value sentinel.
FUNCTION_SENTINEL = "GEN"
FUNCTION_BY_IFC_CLASS: dict[str, str] = {
    "IfcAirTerminal": "SUP", "IfcAirTerminalBox": "SUP", "IfcFan": "SUP",
    "IfcDuctSegment": "SUP", "IfcDuctFitting": "SUP", "IfcDuctSilencer": "SUP",
    "IfcFilter": "RET",
    "IfcSanitaryTerminal": "SAN", "IfcSanitaryTerminalType": "SAN",
    "IfcFlowTerminal": "SAN",
    "IfcPipeSegment": "SUP", "IfcPipeFitting": "SUP", "IfcValve": "SUP",
    "IfcPump": "SUP",
    "IfcElectricDistributionBoard": "PWR", "IfcElectricMotor": "PWR",
    "IfcOutlet": "PWR", "IfcCableCarrierSegment": "PWR", "IfcCableSegment": "PWR",
    "IfcProtectiveDevice": "PWR", "IfcSwitchingDevice": "PWR",
    "IfcLamp": "LTG", "IfcLightFixture": "LTG",
    "IfcFireSuppressionTerminal": "FP", "IfcAlarm": "FP", "IfcSensor": "FP",
    "IfcBoiler": "HTG", "IfcHeatExchanger": "HTG",
    "IfcChiller": "CLG", "IfcCoolingTower": "CLG",
    "IfcUnitaryEquipment": "SUP",
}

# Element Name / ObjectType keyword → discipline. The fallback for elements
# whose IFC class is unclassified — chiefly IfcBuildingElementProxy, which CAD
# exporters (Revit especially) emit for any family without a standard IFC class.
# The real semantic is in the family name ("M_Trim-Window", "Wardrobe",
# "Shower Door", "gate", "Toposolid"). First matching rule wins, so the specific
# MEP/structural buckets are tried before the broad architectural one.
_DISCIPLINE_BY_NAME_KEYWORD: tuple[tuple[tuple[str, ...], str], ...] = (
    (("SHOWER", "SINK", "BASIN", "TOILET", "URINAL", "BATH", "TAP", "FAUCET",
      "SANITARY", "LAVATORY", "CISTERN", "BIDET", "PIPE", "VALVE", "DRAIN",
      "GULLY", "MANHOLE", "GUTTER", "DWV", "WASTE", "SOIL", "FOUL"), "P"),
    (("DUCT", "DIFFUSER", "GRILLE", "VAV", "AHU", "FCU", "HVAC", "RADIATOR",
      "CHILLER", "BOILER", "EXTRACT", "VENTIL"), "M"),
    (("CABLE", "CONDUIT", "SOCKET", "SWITCH", "LUMINAIRE", "LIGHT FIXTURE",
      "DISTRIBUTION BOARD", "TRANSFORMER", "BUSBAR"), "E"),
    (("SPRINKLER", "HYDRANT", "EXTINGUISHER", "FIRE ALARM"), "FP"),
    (("COLUMN", "FOOTING", "FOUNDATION", "PILE", "TRUSS", "RAFTER", "PURLIN",
      "REBAR", "REINFORC"), "S"),
    # Architectural / joinery / FF&E — the large residual, matched last.
    (("WINDOW", "TRIM", "MUNTIN", "SILL", "GLAZING", "MULLION", "DOOR", "GATE",
      "FENCE", "RAILING", "BALUSTRADE", "HANDRAIL", "WALL", "WARDROBE",
      "CABINET", "CUPBOARD", "SHELF", "DESK", "TABLE", "CHAIR", "BED", "SOFA",
      "FURNITURE", "COUNTER", "WORKTOP", "STAIR", "RAMP", "ROOF", "SLAB",
      "FLOOR", "CEILING", "COVERING", "TOPO", "SITE", "LANDSCAPE", "CURTAIN",
      "PARTITION", "SKIRTING", "CORNICE",
      # Domestic FF&E / appliances — decorative and kitchen/laundry equipment
      # that CAD exports drop as proxies. Specific terms only (e.g. "HANGING
      # PLANT" not bare "PLANT", which would collide with mechanical plant).
      "RANGE", "COOK TOP", "COOKTOP", "COOKER", "HOB", "OVEN", "DISHWASHER",
      "WASHING MACHINE", "DRYER", "FRIDGE", "REFRIGERAT", "MICROWAVE",
      "HANGING PLANT", "VACUUM", "IRONING"), "A"),
)


def discipline_for_name(name: str | None) -> str:
    """Fallback: element Name / ObjectType keyword → discipline. Case-insensitive
    substring match, first rule wins; ``XX`` when nothing matches."""
    up = (name or "").upper()
    for keywords, code in _DISCIPLINE_BY_NAME_KEYWORD:
        if any(k in up for k in keywords):
            return code
    return SENTINEL


# When an element belongs to no IfcSystem — the norm for architectural and
# structural elements — fall back to a discipline-appropriate system bucket so
# the tag can still complete instead of stalling on System = XX.
_SYSTEM_BY_DISCIPLINE: dict[str, str] = {
    "A": "ARC", "S": "STR", "M": "HVAC", "E": "ELC",
    "P": "PHE", "FP": "FP", "RP": "RAD", "MG": "MGS",
}


def system_default_for_discipline(discipline: str) -> str:
    """Sensible System code for a discipline when there is no IfcSystem group."""
    return _SYSTEM_BY_DISCIPLINE.get(discipline, SENTINEL)


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


def function_for_class(ifc_class: str) -> str:
    """Pure lookup: IFC class string → STING Function code. ``GEN`` when unknown."""
    return FUNCTION_BY_IFC_CLASS.get(ifc_class, FUNCTION_SENTINEL)


def infer_discipline(element: Any) -> str:
    """Discipline code for an element. IFC class first; when the class is
    unclassified (e.g. IfcBuildingElementProxy), fall back to keyword-matching
    the element's Name then ObjectType — CAD exporters put the real semantic
    there. ``XX`` only when nothing resolves."""
    try:
        code = discipline_for_class(element.is_a())
        if code != SENTINEL:
            return code
        for attr in ("Name", "ObjectType"):
            code = discipline_for_name(getattr(element, attr, None))
            if code != SENTINEL:
                return code
        return SENTINEL
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


def _unit_scale_m(element: Any) -> float:
    """Metres per file length-unit (1.0 if unknown). ``level_for_storey_name``
    expects elevation in METRES (its 3 m/floor rule), but IFC elevations are in
    the file unit — millimetres for a Revit IFC2X3 export. Without this scale a
    2850 mm storey became ``L950`` instead of ``L01``."""
    try:
        import ifcopenshell.util.unit as ifc_unit  # type: ignore
        f = getattr(element, "file", None)
        return float(ifc_unit.calculate_unit_scale(f)) if f is not None else 1.0
    except Exception:
        return 1.0


def _element_world_z_m(element: Any, scale: float) -> float | None:
    """Absolute Z of the element's placement, in metres — or None if it has no
    resolvable placement."""
    try:
        import ifcopenshell.util.placement as ifc_place  # type: ignore
        placement = getattr(element, "ObjectPlacement", None)
        if placement is None:
            return None
        return float(ifc_place.get_local_placement(placement)[2][3]) * scale
    except Exception:
        return None


def _nearest_storey_at_or_below(element: Any, z_m: float, scale: float) -> Any:
    """The IfcBuildingStorey whose elevation is closest at or below ``z_m``
    (metres); falls back to the lowest storey when the element sits below them
    all. Used to give an orphaned element (no spatial container) a level."""
    f = getattr(element, "file", None)
    if f is None:
        return None
    best = best_e = None
    lowest = lowest_e = None
    for s in f.by_type("IfcBuildingStorey"):
        e = getattr(s, "Elevation", None)
        if e is None:
            continue
        em = float(e) * scale
        if lowest_e is None or em < lowest_e:
            lowest, lowest_e = s, em
        if em <= z_m + 1e-6 and (best_e is None or em > best_e):
            best, best_e = s, em
    return best if best is not None else lowest


def infer_level(element: Any) -> str:
    """Level code for an element.

    Primary: its containing ``IfcBuildingStorey`` (elevation normalised to
    metres). Fallback for elements Revit never placed in a storey — furniture,
    fixtures, CAD proxies — match the element's world Z to the nearest storey
    at or below it, so they no longer stall the tag at ``Level = XX``."""
    try:
        import ifcopenshell.util.element as ifc_util  # type: ignore
        scale = _unit_scale_m(element)
        container = ifc_util.get_container(element)
        if container is not None and container.is_a("IfcBuildingStorey"):
            elev = getattr(container, "Elevation", None)
            elev_m = float(elev) * scale if elev is not None else None
            return level_for_storey_name(container.Name, elev_m)
        z_m = _element_world_z_m(element, scale)
        if z_m is None:
            return SENTINEL
        storey = _nearest_storey_at_or_below(element, z_m, scale)
        if storey is not None:
            selev = getattr(storey, "Elevation", None)
            selev_m = float(selev) * scale if selev is not None else z_m
            return level_for_storey_name(storey.Name, selev_m)
        return level_for_storey_name(None, z_m)
    except Exception:
        return SENTINEL


def infer_system(element: Any) -> str:
    """System code from ``IfcSystem`` membership; falls back to a
    discipline-derived default (A→ARC, S→STR, …) when the element belongs to no
    system, so non-MEP elements can still reach a complete tag."""
    try:
        model = element.wrapped_data.file
        for rel in model.get_inverse(element):
            if rel.is_a("IfcRelAssignsToGroup"):
                grp = rel.RelatingGroup
                if grp.is_a("IfcSystem"):
                    code = system_for_name(grp.Name)
                    if code != SENTINEL:
                        return code
        # No IfcSystem membership (normal for architectural / structural
        # elements) → a discipline-appropriate default so the tag can complete.
        return system_default_for_discipline(infer_discipline(element))
    except Exception:
        return SENTINEL


def infer_function(element: Any) -> str:
    """Function code for an ifcopenshell element via its ``is_a()`` class."""
    try:
        return function_for_class(element.is_a())
    except Exception:  # pragma: no cover - defensive
        return FUNCTION_SENTINEL


def zone_codes_for_model(model: Any) -> tuple[dict[int, str], int]:
    """Map element id → zone code (``Z01``, ``Z02`` …) via ``IfcZone`` +
    ``IfcRelAssignsToGroup``. Returns ``(element_id_to_zone, zone_count)``;
    elements with no zone are absent, so the caller supplies the ``ZZ`` default.

    Lives here, not in the adapters: the ``IfcRelAssignsToGroup`` group walk is
    exactly the core-inference logic the Phase A6 boundary lint keeps out of
    host adapter files.
    """
    zones = model.by_type("IfcZone")
    zone_code = {z.id(): f"Z{i:02d}" for i, z in enumerate(zones, start=1)}
    element_to_zone: dict[int, str] = {}
    for rel in model.by_type("IfcRelAssignsToGroup"):
        group = rel.RelatingGroup
        if not group.is_a("IfcZone"):
            continue
        code = zone_code.get(group.id())
        if code is None:
            continue
        for obj in (rel.RelatedObjects or []):
            if obj.is_a("IfcElement"):
                element_to_zone[obj.id()] = code
    return element_to_zone, len(zones)


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
        # Fallback: the element's own ObjectType / Name — proxies carry the
        # Revit family name there when there is no IfcTypeObject relationship.
        for attr in ("ObjectType", "Name"):
            code = product_for_type_name(getattr(element, attr, None))
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
