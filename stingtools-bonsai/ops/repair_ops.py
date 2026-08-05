"""Repair operators — duplicate SEQ fix, missing Pset creation, stale tag clear."""

from __future__ import annotations

import bpy


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
# Repair duplicate Sequence numbers
# ---------------------------------------------------------------------------

class StingRepairDuplicateSeqOperator(bpy.types.Operator):
    """Find elements sharing the same (Disc, Sys, Level, Seq) and reassign Sequence."""

    bl_idname = "sting.repair_duplicate_seq"
    bl_label = "Repair Duplicate Sequences"
    bl_description = (
        "Scan all elements for duplicate (Discipline, System, Level, Sequence) "
        "tuples and assign new unique Sequence numbers to the duplicates"
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

        pset_name = "Pset_StingTags"
        # group_key → list of (element, current_seq_int)
        groups: dict[tuple, list] = {}

        for el in ifc.by_type("IfcElement"):
            psets = ifc_util.get_psets(el)
            stag = psets.get(pset_name, {})
            disc = stag.get("Discipline", "XX")
            sys_ = stag.get("System", "XX")
            lvl = stag.get("Level", "XX")
            seq_raw = stag.get("Sequence", "0")
            try:
                seq_int = int(seq_raw)
            except (ValueError, TypeError):
                seq_int = 0
            key = (disc, sys_, lvl)
            groups.setdefault(key, []).append((el, seq_int))

        repaired = 0
        for key, entries in groups.items():
            seen: dict[int, bool] = {}
            max_seq = max(s for _, s in entries) if entries else 0
            for el, seq in entries:
                if seq in seen:
                    # Duplicate — assign next available
                    max_seq += 1
                    _write_stag(ifc, el, {"Sequence": f"{max_seq:04d}"})
                    repaired += 1
                else:
                    seen[seq] = True

        if repaired:
            self.report({"INFO"}, f"Repaired {repaired} duplicate Sequence value(s)")
        else:
            self.report({"INFO"}, "No duplicate Sequence values found")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Repair missing Pset_StingTags
# ---------------------------------------------------------------------------

class StingRepairMissingPsetOperator(bpy.types.Operator):
    """Create an empty Pset_StingTags (all XX) on elements that have none."""

    bl_idname = "sting.repair_missing_pset"
    bl_label = "Repair Missing Psets"
    bl_description = (
        "Create Pset_StingTags with placeholder XX values on elements "
        "that have no STING property set at all"
    )
    bl_options = {"REGISTER", "UNDO"}

    _DEFAULT = {
        "Discipline": "XX",
        "Location": "XX",
        "Zone": "XX",
        "Level": "XX",
        "System": "XX",
        "Function": "XX",
        "Product": "XX",
        "Sequence": "0000",
        "FullTag": "",
    }

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

        created = 0
        for el in ifc.by_type("IfcElement"):
            psets = ifc_util.get_psets(el)
            if "Pset_StingTags" not in psets:
                _write_stag(ifc, el, self._DEFAULT.copy())
                created += 1

        self.report({"INFO"}, f"Created Pset_StingTags on {created} element(s)")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# Clear stale (partially-completed) tags
# ---------------------------------------------------------------------------

class StingClearStaleTagsOperator(bpy.types.Operator):
    """Reset FullTag to empty string on elements with partial/stale tags."""

    bl_idname = "sting.clear_stale_tags"
    bl_label = "Clear Stale Tags"
    bl_description = (
        "Find elements where FullTag is set but one or more required tokens "
        "are still XX/empty, and reset FullTag to '' so they re-queue for tagging"
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

        pset_name = "Pset_StingTags"
        required = ["Discipline", "Location", "Zone", "Level", "System", "Function", "Product", "Sequence"]
        sentinel = {"", "XX"}
        cleared = 0

        for el in ifc.by_type("IfcElement"):
            psets = ifc_util.get_psets(el)
            stag = psets.get(pset_name, {})
            full_tag = stag.get("FullTag", "")
            if not full_tag:
                continue
            missing = [f for f in required if stag.get(f, "XX") in sentinel]
            if missing:
                _write_stag(ifc, el, {"FullTag": ""})
                cleared += 1

        self.report({"INFO"}, f"Cleared stale FullTag on {cleared} element(s)")
        return {"FINISHED"}


CLASSES = (
    StingRepairDuplicateSeqOperator,
    StingRepairMissingPsetOperator,
    StingClearStaleTagsOperator,
)
