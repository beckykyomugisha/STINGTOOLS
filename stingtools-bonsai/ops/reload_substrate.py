"""Reload substrate operator — clears StingState caches."""

from __future__ import annotations

import bpy


class StingReloadSubstrateOperator(bpy.types.Operator):
    """Clear and reload the STING substrate caches (enums, psets, tag grammar)."""

    bl_idname = "sting.reload_substrate"
    bl_label = "Reload Substrate"
    bl_description = (
        "Invalidate all cached enum/pset/tag-grammar data so the next "
        "operation reloads from disk. Use after editing shared/ifc/ files."
    )
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        try:
            import stingtools_core  # noqa: F401
        except ImportError as e:
            self.report({"ERROR"}, f"stingtools-core not available: {e}")
            return {"CANCELLED"}
        try:
            from ..core.state import StingState
            StingState.get().invalidate()
            self.report({"INFO"}, "STING substrate caches cleared")
        except Exception as e:
            self.report({"WARNING"}, f"Cache invalidation error: {e}")
        return {"FINISHED"}
