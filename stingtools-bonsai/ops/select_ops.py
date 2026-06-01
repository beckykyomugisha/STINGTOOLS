"""SELECT operators — identify elements by tag completeness or discipline."""

from __future__ import annotations

import bpy


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def _active_ifc():
    from ..core.bonsai import bonsai
    return bonsai.active_ifc()


def _get_pset(element, pset_name: str) -> dict:
    try:
        import ifcopenshell.util.element as ifc_util  # type: ignore
        return ifc_util.get_pset(element, pset_name) or {}
    except Exception:
        return {}


def _select_objects_by_ids(context, definition_ids: set):
    """Select Blender objects whose Bonsai ifc_definition_id is in definition_ids."""
    bpy.ops.object.select_all(action="DESELECT")
    for obj in context.scene.objects:
        props = getattr(obj, "BIMObjectProperties", None)
        if props and getattr(props, "ifc_definition_id", 0) in definition_ids:
            obj.select_set(True)


# ---------------------------------------------------------------------------
# 1 — Select Untagged
# ---------------------------------------------------------------------------

class StingSelectUntaggedOperator(bpy.types.Operator):
    """Select all IFC elements that have no Discipline tag (missing or XX)."""

    bl_idname = "sting.select_untagged"
    bl_label = "Select Untagged"
    bl_description = "Select IFC elements with missing or XX Discipline in Pset_StingTags"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai first")
            return {"CANCELLED"}

        try:
            elements = model.by_type("IfcElement")
        except Exception as e:
            self.report({"ERROR"}, f"Cannot query IFC: {e}")
            return {"CANCELLED"}

        untagged_ids: set = set()
        for el in elements:
            pset = _get_pset(el, "Pset_StingTags")
            disc = pset.get("Discipline", "")
            if not disc or disc == "XX":
                untagged_ids.add(el.id())

        _select_objects_by_ids(context, untagged_ids)
        self.report({"INFO"}, f"{len(untagged_ids)} untagged element(s) selected")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 2 — Select Stale
# ---------------------------------------------------------------------------

class StingSelectStaleOperator(bpy.types.Operator):
    """Select IFC elements whose FullTag is set but still contains XX segments."""

    bl_idname = "sting.select_stale"
    bl_label = "Select Stale"
    bl_description = "Select elements with partial tags (FullTag present but segments contain XX)"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai first")
            return {"CANCELLED"}

        stale_ids: set = set()
        try:
            for el in model.by_type("IfcElement"):
                pset = _get_pset(el, "Pset_StingTags")
                full_tag = pset.get("FullTag", "")
                if full_tag and "XX" in full_tag.upper().split("-"):
                    stale_ids.add(el.id())
        except Exception as e:
            self.report({"ERROR"}, f"Cannot query IFC: {e}")
            return {"CANCELLED"}

        _select_objects_by_ids(context, stale_ids)
        self.report({"INFO"}, f"{len(stale_ids)} stale element(s) selected")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 3 — Select by Discipline
# ---------------------------------------------------------------------------

_DISCIPLINE_ITEMS = [
    ("M",  "Mechanical",            ""),
    ("E",  "Electrical",            ""),
    ("P",  "Plumbing / Public Health", ""),
    ("A",  "Architectural",         ""),
    ("S",  "Structural",            ""),
    ("H",  "Healthcare",            ""),
    ("MG", "Medical Gas",           ""),
    ("RP", "Radiation Protection",  ""),
    ("FP", "Fire Protection",       ""),
    ("LV", "LV / Comms",            ""),
    ("G",  "Civil",                 ""),
]


class StingSelectByDisciplineOperator(bpy.types.Operator):
    """Select all IFC elements whose Discipline matches the chosen code."""

    bl_idname = "sting.select_by_discipline"
    bl_label = "Select by Discipline"
    bl_description = "Select elements whose Pset_StingTags.Discipline matches the chosen code"
    bl_options = {"REGISTER"}

    discipline: bpy.props.EnumProperty(  # type: ignore[valid-type]
        name="Discipline",
        description="ISO 19650 discipline code",
        items=_DISCIPLINE_ITEMS,
        default="M",
    )

    def invoke(self, context: bpy.types.Context, event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai first")
            return {"CANCELLED"}

        matched_ids: set = set()
        try:
            for el in model.by_type("IfcElement"):
                pset = _get_pset(el, "Pset_StingTags")
                if pset.get("Discipline", "") == self.discipline:
                    matched_ids.add(el.id())
        except Exception as e:
            self.report({"ERROR"}, f"Cannot query IFC: {e}")
            return {"CANCELLED"}

        _select_objects_by_ids(context, matched_ids)
        self.report({"INFO"}, f"{len(matched_ids)} {self.discipline} element(s) selected")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# 4 — Select Compliant
# ---------------------------------------------------------------------------

class StingSelectCompliantOperator(bpy.types.Operator):
    """Select IFC elements with a fully-complete 8-segment tag (no XX)."""

    bl_idname = "sting.select_compliant"
    bl_label = "Select Compliant"
    bl_description = "Select elements where all 8 tag segments are set (no XX)"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        model = _active_ifc()
        if model is None:
            self.report({"ERROR"}, "No active IFC file — open one via Bonsai first")
            return {"CANCELLED"}

        try:
            from stingtools_core.tag_grammar import Tag  # type: ignore
        except ImportError:
            self.report({"ERROR"}, "stingtools_core not available")
            return {"CANCELLED"}

        compliant_ids: set = set()
        try:
            for el in model.by_type("IfcElement"):
                pset = _get_pset(el, "Pset_StingTags")
                if not pset:
                    continue
                tag = Tag.from_pset(pset)
                if tag.is_complete():
                    compliant_ids.add(el.id())
        except Exception as e:
            self.report({"ERROR"}, f"Cannot query IFC: {e}")
            return {"CANCELLED"}

        _select_objects_by_ids(context, compliant_ids)
        self.report({"INFO"}, f"{len(compliant_ids)} fully-compliant element(s) selected")
        return {"FINISHED"}


# ---------------------------------------------------------------------------
# registration
# ---------------------------------------------------------------------------

CLASSES = (
    StingSelectUntaggedOperator,
    StingSelectStaleOperator,
    StingSelectByDisciplineOperator,
    StingSelectCompliantOperator,
)
