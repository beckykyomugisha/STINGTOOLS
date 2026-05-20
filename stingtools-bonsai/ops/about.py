"""Diagnostic operator — confirms STING substrate loads from this Blender session."""

from __future__ import annotations

import bpy


class StingAboutOperator(bpy.types.Operator):
    """Print the loaded STING substrate state to the System Console."""

    bl_idname = "sting.about"
    bl_label = "About STING"
    bl_description = "Verify the STING IFC substrate is loaded; report enum + pset counts"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        try:
            from stingtools_core import EnumRegistry, PsetRegistry, __version__ as core_version
        except ImportError as e:
            self.report({"ERROR"}, f"stingtools-core not available: {e}")
            return {"CANCELLED"}

        try:
            enums = EnumRegistry().load()
            psets = PsetRegistry().load()
        except FileNotFoundError as e:
            self.report({"ERROR"}, f"shared/ifc not found — set STINGTOOLS_SHARED_IFC env var: {e}")
            return {"CANCELLED"}

        drift_count = len(enums.drift)
        msg = (
            f"STING core v{core_version}  ·  "
            f"{len(enums)} enums  ·  {len(psets)} psets  ·  "
            f"{drift_count} drift"
            + (" (FAIL)" if drift_count else "")
        )
        self.report({"INFO"}, msg)
        print(f"[STING] {msg}")
        for pset in psets:
            print(f"[STING]   pset: {pset.name} ({len(pset.properties)} props, {len(pset.rules)} rules)")

        # Bonsai integration status
        try:
            from ..core import bonsai
            print(f"[STING] {bonsai.capabilities.summary()}")
        except Exception as e:
            print(f"[STING] Bonsai probe failed: {e}")

        return {"FINISHED"}
