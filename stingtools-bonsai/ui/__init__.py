"""N-panel registration. Day-1 scaffold — one root panel with About + Reload."""

from __future__ import annotations

import bpy

from .panel_main import StingMainPanel

CLASSES = (StingMainPanel,)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
