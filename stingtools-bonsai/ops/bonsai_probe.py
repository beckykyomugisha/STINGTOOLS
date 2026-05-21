"""Diagnostic — re-probe Bonsai integration and print the result."""

from __future__ import annotations

import bpy


class StingBonsaiProbeOperator(bpy.types.Operator):
    """Re-detect Bonsai (BlenderBIM) and report what's wired up."""

    bl_idname = "sting.bonsai_probe"
    bl_label = "Probe Bonsai"
    bl_description = "Re-detect Bonsai (BlenderBIM) and report capabilities"
    bl_options = {"REGISTER"}

    def execute(self, context: bpy.types.Context) -> set[str]:
        from ..core import bonsai
        caps = bonsai.refresh()
        msg = caps.summary()
        self.report({"INFO"}, msg)
        print(f"[STING] {msg}")
        if caps.installed:
            print(f"[STING]   API path:        {caps.api_module_path}")
            print(f"[STING]   pset API:        {caps.has_api_pset}")
            print(f"[STING]   attribute API:   {caps.has_api_attribute}")
            print(f"[STING]   active IfcStore: {caps.has_blender_context}")
            model = bonsai.active_ifc()
            if model is None:
                print(f"[STING]   active file:     (none — open an IFC via Bonsai or load one)")
            else:
                try:
                    schema = model.schema
                    n_elements = len(model.by_type("IfcElement"))
                    print(f"[STING]   active file:     {schema}, {n_elements} IfcElement(s)")
                except Exception as e:
                    print(f"[STING]   active file:     unreadable ({e})")
        return {"FINISHED"}
