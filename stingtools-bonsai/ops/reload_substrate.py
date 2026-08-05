"""Force-reload the STING enum + pset registry. Useful during dev."""

from __future__ import annotations

import bpy


class StingReloadSubstrateOperator(bpy.types.Operator):
    """Reload the STING enum + pset XML from disk."""

    bl_idname = "sting.reload_substrate"
    bl_label = "Reload Substrate"
    bl_description = "Re-read shared/ifc/ from disk (after editing XML in another editor)"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        try:
            import stingtools_core  # noqa: F401 — confirms package is importable
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
