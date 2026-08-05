"""Spatial auto-detection operators — Location, Zone, Function tokens + pre-tag audit."""

from __future__ import annotations

import json

import bpy

# Phase A3/A6 — IFC→token inference and the zone group walk live in
# stingtools_core.hosts.inference (the shared core), NOT here. This module is a
# thin Blender adapter that calls core; the boundary lint (Phase A6) enforces
# that no inference logic is (re)defined in adapter files like this one.
from stingtools_core.hosts import inference as _inf


def _get_ifc():
    try:
        from ..core.bonsai import bonsai as _bridge
        return _bridge.active_ifc()
    except Exception:
        return None


def _write_stag(ifc, element, props: dict) -> None:
    try:
        from ..core.bonsai import bonsai as _bridge
        _bridge.add_pset(ifc, element, "Pset_StingTags", props)
    except Exception as exc:
        print(f"[STING] write_stag failed: {exc}")


# ---------------------------------------------------------------------------
# Auto-detect Location
# ---------------------------------------------------------------------------

class StingAutoDetectLocationOperator(bpy.types.Operator):
    """Derive Location token from IfcBuilding containment hierarchy."""

    bl_idname = "sting.auto_detect_location"
    bl_label = "Auto-detect Location"
    bl_description = (
        "Walk IfcBuilding → IfcBuildingStorey → IfcSpace containment and "
        "stamp Location (BLD1 / BLD2 / …) on every IfcElement"
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell.util.element as ifc_util
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        # Build building → code map (BLD1, BLD2, …)
        buildings = ifc.by_type("IfcBuilding")
        bld_code: dict[int, str] = {}
        for idx, bld in enumerate(buildings, start=1):
            bld_code[bld.id()] = f"BLD{idx}"

        # Map element → building via IfcRelContainedInSpatialStructure
        element_to_bld: dict[int, str] = {}
        for rel in ifc.by_type("IfcRelContainedInSpatialStructure"):
            spatial = rel.RelatingStructure
            # Walk up to IfcBuilding
            code = None
            node = spatial
            for _ in range(10):  # max 10 levels up
                if node is None:
                    break
                if node.is_a("IfcBuilding") and node.id() in bld_code:
                    code = bld_code[node.id()]
                    break
                # Traverse Decomposes
                decomp = getattr(node, "Decomposes", None) or []
                if not decomp:
                    break
                node = decomp[0].RelatingObject if decomp else None
            if code:
                for el in (rel.RelatedElements or []):
                    element_to_bld[el.id()] = code

        stamped = 0
        for el in ifc.by_type("IfcElement"):
            code = element_to_bld.get(el.id(), "BLD1")  # default BLD1
            _write_stag(ifc, el, {"Location": code})
            stamped += 1

        self.report({"INFO"}, f"Location stamped on {stamped} element(s)")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Auto-detect Zone
# ---------------------------------------------------------------------------

class StingAutoDetectZoneOperator(bpy.types.Operator):
    """Derive Zone token from IfcZone group assignments."""

    bl_idname = "sting.auto_detect_zone"
    bl_label = "Auto-detect Zone"
    bl_description = (
        "Map IfcZone memberships to Z01/Z02/… codes and stamp Zone on every element. "
        "Elements with no zone get ZZ."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell  # noqa: F401 — availability guard
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        # Zone membership (the IfcZone group walk) is core inference — delegate
        # so that logic lives in stingtools_core, not this adapter (Phase A6).
        element_to_zone, zone_count = _inf.zone_codes_for_model(ifc)

        stamped = 0
        for el in ifc.by_type("IfcElement"):
            code = element_to_zone.get(el.id(), "ZZ")
            _write_stag(ifc, el, {"Zone": code})
            stamped += 1

        self.report({"INFO"}, f"Zone stamped on {stamped} element(s) ({zone_count} zone(s) found)")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Auto-detect Function
# ---------------------------------------------------------------------------

class StingAutoDetectFunctionOperator(bpy.types.Operator):
    """Derive Function token from IFC element class."""

    bl_idname = "sting.auto_detect_function"
    bl_label = "Auto-detect Function"
    bl_description = (
        "Map each element's IFC class to a STING Function code "
        "(SUP / RET / SAN / PWR / LTG / FP / HTG / CLG) and stamp the token. "
        "Unknown classes get GEN."
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell  # noqa: F401
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        stamped = unrecognised = 0
        for el in ifc.by_type("IfcElement"):
            ifc_class = el.is_a()
            func = _inf.function_for_class(ifc_class)
            if func == "GEN":
                unrecognised += 1
            _write_stag(ifc, el, {"Function": func})
            stamped += 1

        self.report(
            {"INFO"},
            f"Function stamped on {stamped} element(s) "
            f"({unrecognised} mapped to GEN — add to stingtools_core "
            f"FUNCTION_BY_IFC_CLASS to refine)",
        )
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Pre-tag audit (read-only dry run)
# ---------------------------------------------------------------------------

class StingPreTagAuditOperator(bpy.types.Operator):
    """Dry-run audit showing tagging readiness without writing any data."""

    bl_idname = "sting.pre_tag_audit"
    bl_label = "Pre-tag Audit"
    bl_description = (
        "Read-only scan: count complete / incomplete / untagged elements "
        "and cache the summary in scene properties for the SPATIAL panel strip."
    )
    bl_options = {"REGISTER"}

    _REQUIRED = ["Discipline", "Location", "Zone", "Level",
                 "System", "Function", "Product", "Sequence"]
    _SENTINEL = {"", "XX"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell.util.element as ifc_util
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        total = complete = incomplete = untagged = 0
        sample_issues: list[dict] = []

        for el in ifc.by_type("IfcElement"):
            psets = ifc_util.get_psets(el)
            stag = psets.get("Pset_StingTags", {})
            total += 1
            disc = stag.get("Discipline", "XX")
            if not stag or disc in self._SENTINEL:
                untagged += 1
                if len(sample_issues) < 5:
                    sample_issues.append({
                        "id": getattr(el, "GlobalId", ""),
                        "class": el.is_a(),
                        "issue": "untagged",
                    })
                continue
            missing = [f for f in self._REQUIRED if stag.get(f, "XX") in self._SENTINEL]
            if missing:
                incomplete += 1
                if len(sample_issues) < 5:
                    sample_issues.append({
                        "id": getattr(el, "GlobalId", ""),
                        "class": el.is_a(),
                        "issue": f"missing: {', '.join(missing)}",
                    })
            else:
                complete += 1

        pct = round(complete / total * 100, 1) if total else 0.0
        result = {
            "total": total,
            "complete": complete,
            "incomplete": incomplete,
            "untagged": untagged,
            "compliance_pct": pct,
            "sample_issues": sample_issues,
        }
        context.scene["sting_pretag_audit"] = json.dumps(result)

        self.report(
            {"INFO"},
            f"Pre-tag audit: {pct}% ready ({complete}/{total} complete, "
            f"{untagged} untagged, {incomplete} incomplete)",
        )
        return {"FINISHED"}


CLASSES = (
    StingAutoDetectLocationOperator,
    StingAutoDetectZoneOperator,
    StingAutoDetectFunctionOperator,
    StingPreTagAuditOperator,
)
