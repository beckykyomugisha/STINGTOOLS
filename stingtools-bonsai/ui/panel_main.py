"""STING N-panel — root + 4 sub-panels (SELECT / TAGS / VALIDATE / COORD)."""

from __future__ import annotations

import json

import bpy

_RESULTS_KEY = "sting_validation_results"
_TOKEN_KEY = "sting_planscape_token"
_EMAIL_KEY = "sting_planscape_email"


# ---------------------------------------------------------------------------
# Root panel
# ---------------------------------------------------------------------------

class StingMainPanel(bpy.types.Panel):
    bl_idname = "STING_PT_main"
    bl_label = "STING"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "STING"

    def draw(self, context: bpy.types.Context) -> None:
        layout = self.layout

        box = layout.box()
        box.label(text="StingTools for Bonsai", icon="OUTLINER")
        try:
            from stingtools_core import __version__ as core_v  # type: ignore
            box.label(text=f"core v{core_v}")
        except ImportError:
            box.label(text="core: NOT LOADED — check PYTHONPATH", icon="ERROR")

        bonsai_ok = False
        try:
            from ..core import bonsai as _bridge
            caps = _bridge.bonsai.capabilities
            bonsai_ok = caps.installed
            if bonsai_ok:
                box.label(text=f"Bonsai v{caps.version}", icon="CHECKMARK")
        except Exception:
            pass

        # Phase A1 — StingTools requires Bonsai (it provides ifcopenshell + the
        # undo-aware IFC layer). There is no in-Blender standalone mode: surface a
        # clear call-to-action and stop, rather than rendering ops that would no-op.
        if not bonsai_ok:
            warn = layout.box()
            warn.alert = True
            warn.label(text="Bonsai is required", icon="ERROR")
            warn.label(text="Install + enable the Bonsai add-on,")
            warn.label(text="then reopen this panel.")
            warn.operator("sting.bonsai_probe", text="Re-check for Bonsai", icon="FILE_REFRESH")
            return

        layout.separator()
        col = layout.column(align=True)
        col.label(text="Diagnostics", icon="INFO")
        col.operator("sting.about",             icon="QUESTION")
        col.operator("sting.reload_substrate",  icon="FILE_REFRESH")
        col.operator("sting.bonsai_probe",      icon="VIEWZOOM")


# ---------------------------------------------------------------------------
# SELECT sub-panel
# ---------------------------------------------------------------------------

class StingSelectPanel(bpy.types.Panel):
    bl_idname = "STING_PT_select"
    bl_label = "SELECT"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "STING"
    bl_parent_id = "STING_PT_main"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context: bpy.types.Context) -> None:
        layout = self.layout
        col = layout.column(align=True)
        col.operator("sting.select_untagged",       icon="QUESTION")
        col.operator("sting.select_stale",          icon="ERROR")
        col.operator("sting.select_by_discipline",  icon="FILTER")
        col.operator("sting.select_compliant",      icon="CHECKMARK")


# ---------------------------------------------------------------------------
# TAGS sub-panel
# ---------------------------------------------------------------------------

class StingTagsPanel(bpy.types.Panel):
    bl_idname = "STING_PT_tags"
    bl_label = "TAGS"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "STING"
    bl_parent_id = "STING_PT_main"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context: bpy.types.Context) -> None:
        layout = self.layout

        col = layout.column(align=True)
        col.label(text="Auto-populate", icon="AUTO")
        col.operator("sting.auto_tag",      icon="OUTLINER_OB_MESH")
        col.operator("sting.tag_selected",  icon="RESTRICT_SELECT_OFF")

        layout.separator()
        col = layout.column(align=True)
        col.label(text="Token writers", icon="GREASEPENCIL")
        col.operator("sting.set_discipline", icon="OBJECT_DATA")
        col.operator("sting.set_system",     icon="NETWORK_DRIVE")

        layout.separator()
        col = layout.column(align=True)
        col.label(text="Sequence & full tag", icon="SORTALPHA")
        col.operator("sting.assign_sequence",   icon="LINENUMBERS_ON")
        col.operator("sting.build_full_tags",   icon="LINKED")


# ---------------------------------------------------------------------------
# VALIDATE sub-panel
# ---------------------------------------------------------------------------

class StingValidatePanel(bpy.types.Panel):
    bl_idname = "STING_PT_validate"
    bl_label = "VALIDATE"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "STING"
    bl_parent_id = "STING_PT_main"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context: bpy.types.Context) -> None:
        layout = self.layout

        # Live summary strip if results are cached
        raw = context.scene.get(_RESULTS_KEY)
        if raw:
            try:
                data = json.loads(raw)
                total = data.get("total", 0)
                valid = data.get("valid", 0)
                invalid = data.get("invalid", 0)
                pct = round(valid / total * 100, 1) if total else 0
                stage = data.get("stage", "")
                box = layout.box()
                row = box.row()
                icon = "CHECKMARK" if invalid == 0 else "ERROR"
                row.label(text=f"{stage}: {pct}% valid ({valid}/{total})", icon=icon)
            except Exception:
                pass

        col = layout.column(align=True)
        col.operator("sting.validate_ids",          icon="PLAY")
        col.operator("sting.validation_dashboard",  icon="TEXT")
        col.operator("sting.clear_validation",      icon="TRASH")


# ---------------------------------------------------------------------------
# COORD sub-panel
# ---------------------------------------------------------------------------

class StingCoordPanel(bpy.types.Panel):
    bl_idname = "STING_PT_coord"
    bl_label = "COORD"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "STING"
    bl_parent_id = "STING_PT_main"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context: bpy.types.Context) -> None:
        layout = self.layout

        # Login + target status (source of truth = add-on preferences).
        token = ""
        email = ""
        project_id = ""
        try:
            from .. import prefs as _p
            pr = _p.get_prefs(context)
            token = pr.api_token or ""
            email = pr.email or ""
            project_id = pr.project_id or ""
        except Exception:
            token = context.scene.get(_TOKEN_KEY, "") or ""
            email = context.scene.get(_EMAIL_KEY, "") or ""

        box = layout.box()
        if token:
            box.label(text=f"Logged in: {email}" if email else "Logged in", icon="CHECKMARK")
        else:
            box.label(text="Not logged in", icon="LOCKED")
        box.label(
            text=f"Project: {project_id[:8]}…" if project_id else "Project: (set in prefs)",
            icon="FILE_BLEND" if project_id else "INFO",
        )

        col = layout.column(align=True)
        col.operator("sting.planscape_login",   icon="URL")
        col.separator()
        col.operator("sting.sync_to_planscape", text="Push to Planscape (bonsai)", icon="EXPORT")
        col.separator()
        col.operator("sting.raise_issue",       icon="ERROR")


# ---------------------------------------------------------------------------
# registration
# ---------------------------------------------------------------------------

CLASSES = (
    StingMainPanel,
    StingSelectPanel,
    StingTagsPanel,
    StingValidatePanel,
    StingCoordPanel,
)
