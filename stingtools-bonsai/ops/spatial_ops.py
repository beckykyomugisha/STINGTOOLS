"""Spatial auto-detect operators — Location, Zone, Function, pre-tag audit."""

from __future__ import annotations

import json

import bpy


def _get_ifc():
    """Return the active IFC model via BonsaiBridge, or None."""
    try:
        from ..core.bonsai import bonsai as _bridge
        return _bridge.active_ifc()
    except Exception:
        return None


def _write_stag_prop(ifc, element, prop: str, value: str) -> None:
    """Write a single Pset_StingTags property via ifcopenshell.api."""
    try:
        from ..core.bonsai import bonsai as _bridge
        _bridge.add_pset(ifc, element, "Pset_StingTags", {prop: value})
    except Exception as exc:
        print(f"[STING] write_stag_prop failed ({prop}={value}): {exc}")


# ---------------------------------------------------------------------------
# Auto-detect Location
# ---------------------------------------------------------------------------

class StingAutoDetectLocationOperator(bpy.types.Operator):
    """Derive Location code from the IFC spatial hierarchy (IfcBuilding)."""

    bl_idname = "sting.auto_detect_location"
    bl_label = "Auto-Detect Location"
    bl_description = (
        "Walk IfcBuilding containment hierarchy and stamp the Location "
        "token on every element using the building short-name or 'BLD1'"
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell
            import ifcopenshell.util.element as ifc_util
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        # Build building -> short-code map
        building_code: dict[int, str] = {}
        buildings = ifc.by_type("IfcBuilding")
        for i, bld in enumerate(buildings, start=1):
            name = (bld.Name or "").strip()
            # Use first 4 alphanumeric chars of building name, else BLD<n>
            code = "".join(c for c in name.upper() if c.isalnum())[:4] or f"BLD{i}"
            building_code[bld.id()] = code

        # Build element -> building mapping via IfcRelContainedInSpatialStructure
        el_to_bld: dict[int, str] = {}
        for rel in ifc.by_type("IfcRelContainedInSpatialStructure"):
            container = rel.RelatingStructure
            # Walk up until IfcBuilding
            node = container
            code = None
            for _ in range(10):
                if node.is_a("IfcBuilding"):
                    code = building_code.get(node.id(), "BLD1")
                    break
                try:
                    decomp = ifc.get_inverse(node, "Decomposes")
                    if decomp:
                        node = list(decomp)[0].RelatingObject
                    else:
                        break
                except Exception:
                    break
            if code is None:
                code = "BLD1"
            for el in rel.RelatedElements or []:
                el_to_bld[el.id()] = code

        stamped = 0
        for el in ifc.by_type("IfcElement"):
            loc_code = el_to_bld.get(el.id(), "BLD1")
            _write_stag_prop(ifc, el, "Location", loc_code)
            stamped += 1

        self.report({"INFO"}, f"Location stamped on {stamped} elements")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Auto-detect Zone
# ---------------------------------------------------------------------------

class StingAutoDetectZoneOperator(bpy.types.Operator):
    """Derive Zone code from IfcZone or IfcSpace membership."""

    bl_idname = "sting.auto_detect_zone"
    bl_label = "Auto-Detect Zone"
    bl_description = (
        "Map element containment through IfcSpace → IfcZone and stamp "
        "Zone token (Z01 … Z99) on each element"
    )
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        # Build zone index: zone name -> short code Z01, Z02 ...
        zone_code: dict[int, str] = {}
        for i, zone in enumerate(ifc.by_type("IfcZone"), start=1):
            zone_code[zone.id()] = f"Z{i:02d}"

        # Build space -> zone mapping
        space_to_zone: dict[int, str] = {}
        for zone in ifc.by_type("IfcZone"):
            code = zone_code[zone.id()]
            for rel in ifc.get_inverse(zone):
                if rel.is_a("IfcRelAssignsToGroup"):
                    for obj in rel.RelatedObjects or []:
                        if obj.is_a("IfcSpace"):
                            space_to_zone[obj.id()] = code

        # Build element -> zone via IfcRelContainedInSpatialStructure
        el_to_zone: dict[int, str] = {}
        for rel in ifc.by_type("IfcRelContainedInSpatialStructure"):
            container = rel.RelatingStructure
            if container.is_a("IfcSpace"):
                code = space_to_zone.get(container.id(), "ZZ")
                for el in rel.RelatedElements or []:
                    el_to_zone[el.id()] = code

        stamped = 0
        for el in ifc.by_type("IfcElement"):
            code = el_to_zone.get(el.id(), "ZZ")
            _write_stag_prop(ifc, el, "Zone", code)
            stamped += 1

        self.report({"INFO"}, f"Zone stamped on {stamped} elements")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Auto-detect Function
# ---------------------------------------------------------------------------

class StingAutoDetectFunctionOperator(bpy.types.Operator):
    """Derive Function token from IFC class heuristics."""

    bl_idname = "sting.auto_detect_function"
    bl_label = "Auto-Detect Function"
    bl_description = (
        "Map IfcClass to a STING Function code (SUP, RET, SAN, PWR …) "
        "using heuristic lookup and stamp Pset_StingTags.Function"
    )
    bl_options = {"REGISTER", "UNDO"}

    # IFC class prefix -> Function code heuristics
    _CLASS_TO_FUNC = {
        "IfcAirTerminal": "SUP",
        "IfcAirTerminalBox": "SUP",
        "IfcFan": "EXH",
        "IfcUnitaryEquipment": "SUP",
        "IfcCoil": "CLG",
        "IfcBoiler": "HTG",
        "IfcChiller": "CLG",
        "IfcPump": "PMP",
        "IfcValve": "ISO",
        "IfcPipeSegment": "DCW",
        "IfcPipeFitting": "DCW",
        "IfcSanitaryTerminal": "SAN",
        "IfcFlowTerminal": "SUP",
        "IfcElectricAppliance": "PWR",
        "IfcElectricDistributionBoard": "PWR",
        "IfcLightFixture": "LGT",
        "IfcOutlet": "PWR",
        "IfcCableSegment": "PWR",
        "IfcCableFitting": "PWR",
        "IfcDoor": "ACC",
        "IfcWindow": "ENV",
        "IfcSlab": "STR",
        "IfcBeam": "STR",
        "IfcColumn": "STR",
        "IfcWall": "ENV",
        "IfcRoof": "ENV",
        "IfcStair": "CIR",
        "IfcFireSuppression": "FP",
        "IfcAlarm": "DET",
        "IfcSensor": "DET",
    }

    def _resolve_func(self, ifc_class: str) -> str:
        for prefix, code in self._CLASS_TO_FUNC.items():
            if ifc_class.startswith(prefix):
                return code
        return "GEN"

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        stamped = 0
        for el in ifc.by_type("IfcElement"):
            func = self._resolve_func(el.is_a())
            _write_stag_prop(ifc, el, "Function", func)
            stamped += 1

        self.report({"INFO"}, f"Function stamped on {stamped} elements")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Pre-tag audit (read-only dry run)
# ---------------------------------------------------------------------------

class StingPreTagAuditOperator(bpy.types.Operator):
    """READ-ONLY dry run — audit tag completeness without writing anything."""

    bl_idname = "sting.pre_tag_audit"
    bl_label = "Pre-Tag Audit"
    bl_description = (
        "Dry-run audit: count tagged / untagged / incomplete elements "
        "and store a summary in scene['sting_pretag_audit']. No writes."
    )
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        ifc = _get_ifc()
        if ifc is None:
            self.report({"ERROR"}, "No IFC file loaded")
            return {"CANCELLED"}

        try:
            import ifcopenshell
            import ifcopenshell.util.element as ifc_util
        except ImportError as exc:
            self.report({"ERROR"}, f"ifcopenshell unavailable: {exc}")
            return {"CANCELLED"}

        pset_name = "Pset_StingTags"
        required = ["Discipline", "Location", "Zone", "Level", "System", "Function", "Product", "Sequence"]
        sentinel = {"", "XX"}

        total = complete = incomplete = untagged = 0
        issues: list[dict] = []

        for el in ifc.by_type("IfcElement"):
            psets = ifc_util.get_psets(el)
            stag = psets.get(pset_name, {})
            total += 1
            if not stag:
                untagged += 1
                continue
            missing = [f for f in required if stag.get(f, "XX") in sentinel]
            if not missing:
                complete += 1
            else:
                incomplete += 1
                if len(issues) < 50:
                    issues.append({
                        "id": el.GlobalId,
                        "class": el.is_a(),
                        "name": el.Name or "",
                        "missing": missing,
                    })

        pct = round(complete / total * 100, 1) if total else 0.0
        result = {
            "total": total,
            "complete": complete,
            "incomplete": incomplete,
            "untagged": untagged,
            "compliance_pct": pct,
            "sample_issues": issues,
        }
        context.scene["sting_pretag_audit"] = json.dumps(result)

        self.report(
            {"INFO"},
            f"Pre-tag audit: {pct}% complete — {untagged} untagged, {incomplete} incomplete / {total} total",
        )
        return {"FINISHED"}


CLASSES = (
    StingAutoDetectLocationOperator,
    StingAutoDetectZoneOperator,
    StingAutoDetectFunctionOperator,
    StingPreTagAuditOperator,
)
