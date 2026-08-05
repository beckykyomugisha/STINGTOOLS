"""Spatial auto-detection operators — Location, Zone, Function tokens + pre-tag audit."""

from __future__ import annotations

import json

import bpy

# All token-inference logic lives in stingtools_core.hosts.inference — this
# module is a thin Blender adapter. The Phase A6 boundary lint
# (tools/ci/check_adapter_boundary.py) fails the build if inference rules are
# re-implemented here.
from stingtools_core.hosts import inference as _inf


def _get_ifc():
    try:
        from ..core.bonsai import bonsai as _bridge
        return _bridge.active_ifc()
    except Exception:
        return None


def _write_stag(element, props: dict) -> bool:
    """Write props into Pset_StingTags on element. Returns True on success.

    BonsaiBridge.add_pset takes (element, pset_name, properties) — it resolves
    the active model itself. Passing the model as a fourth argument raises
    TypeError, which is exactly the bug that made every spatial operator report
    success while writing nothing.
    """
    from ..core.bonsai import bonsai as _bridge
    return _bridge.add_pset(element, "Pset_StingTags", props)


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

        element_to_bld = _inf.building_codes_by_element(ifc)

        stamped = failed = 0
        for el in ifc.by_type("IfcElement"):
            code = element_to_bld.get(el.id(), _inf.LOCATION_FALLBACK)
            if _write_stag(el, {"Location": code}):
                stamped += 1
            else:
                failed += 1

        if stamped == 0 and failed:
            self.report({"ERROR"}, f"Location write failed on all {failed} element(s)")
            return {"CANCELLED"}
        msg = f"Location stamped on {stamped} element(s)"
        self.report({"WARNING"} if failed else {"INFO"},
                    msg + (f" — {failed} write(s) failed" if failed else ""))
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

        element_to_zone = _inf.zone_codes_by_element(ifc)
        zone_count = len(set(element_to_zone.values()))

        stamped = failed = 0
        for el in ifc.by_type("IfcElement"):
            code = element_to_zone.get(el.id(), _inf.ZONE_FALLBACK)
            if _write_stag(el, {"Zone": code}):
                stamped += 1
            else:
                failed += 1

        if stamped == 0 and failed:
            self.report({"ERROR"}, f"Zone write failed on all {failed} element(s)")
            return {"CANCELLED"}
        msg = f"Zone stamped on {stamped} element(s) ({zone_count} zone(s) found)"
        self.report({"WARNING"} if failed else {"INFO"},
                    msg + (f" — {failed} write(s) failed" if failed else ""))
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

        stamped = failed = unrecognised = 0
        for el in ifc.by_type("IfcElement"):
            func = _inf.infer_function(el)
            if func == _inf.FUNCTION_FALLBACK:
                unrecognised += 1
            if _write_stag(el, {"Function": func}):
                stamped += 1
            else:
                failed += 1

        if stamped == 0 and failed:
            self.report({"ERROR"}, f"Function write failed on all {failed} element(s)")
            return {"CANCELLED"}
        msg = (
            f"Function stamped on {stamped} element(s) "
            f"({unrecognised} mapped to {_inf.FUNCTION_FALLBACK} — extend "
            f"stingtools_core.hosts.inference.FUNCTION_BY_IFC_CLASS to refine)"
        )
        self.report({"WARNING"} if failed else {"INFO"},
                    msg + (f" — {failed} write(s) failed" if failed else ""))
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
