"""STING N-panel root — shown in the 3D viewport sidebar under the "STING" tab.

Day-1: shell only. Sub-panels (SELECT / TAGS / VALIDATE / COORD) will
fan out from here per the MVP scope doc.
"""

from __future__ import annotations

import bpy


class StingMainPanel(bpy.types.Panel):
    bl_idname = "STING_PT_main"
    bl_label = "STING"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "STING"

    def draw(self, context: bpy.types.Context) -> None:
        layout = self.layout

        # ─── Status header ───────────────────────────────────────
        box = layout.box()
        box.label(text="StingTools for Bonsai", icon="OUTLINER")
        try:
            from stingtools_core import __version__ as core_v
            box.label(text=f"core v{core_v}  ·  scaffold")
        except ImportError:
            box.label(text="core: NOT LOADED — check PYTHONPATH", icon="ERROR")

        # ─── Bonsai integration status ──────────────────────────
        try:
            from ..core import bonsai
            caps = bonsai.capabilities
            sub = box.row()
            if caps.installed:
                sub.label(text=f"Bonsai v{caps.version}", icon="CHECKMARK")
            else:
                sub.label(text="Bonsai not detected (standalone mode)", icon="INFO")
        except Exception:
            pass

        # ─── Day-1 diagnostics ──────────────────────────────────
        col = layout.column(align=True)
        col.label(text="Diagnostics", icon="INFO")
        col.operator("sting.about", icon="QUESTION")
        col.operator("sting.reload_substrate", icon="FILE_REFRESH")
        col.operator("sting.bonsai_probe", icon="VIEWZOOM")

        # ─── Placeholder for the MVP sections ───────────────────
        layout.separator()
        col = layout.column(align=True)
        col.label(text="Coming in MVP (per scope doc):", icon="TIME")
        for label in (
            "SELECT   — Untagged / Stale / By Discipline",
            "TAGS     — Auto Tag / Tag Selected / Token writers",
            "VALIDATE — IDS pipeline + dashboard",
            "COORD    — Raise issue + Planscape sync",
        ):
            row = col.row()
            row.enabled = False
            row.label(text="  " + label)
