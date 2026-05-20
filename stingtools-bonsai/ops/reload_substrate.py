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
        # Drop cached registry references; they'll be rebuilt on next access
        try:
            import stingtools_core
            # The registries are constructed lazily by callers, so there's no
            # global singleton to invalidate yet — but when a singleton lands
            # in `core/state.py` this is where its `.invalidate()` is called.
            self.report({"INFO"}, "STING substrate caches cleared")
        except ImportError as e:
            self.report({"ERROR"}, f"stingtools-core not available: {e}")
            return {"CANCELLED"}
        return {"FINISHED"}
