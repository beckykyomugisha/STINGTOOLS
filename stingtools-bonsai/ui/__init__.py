"""N-panel registration — root panel + 7 sub-panels."""

from __future__ import annotations

import bpy

from .panel_main import (
    StingMainPanel,
    StingSelectPanel,
    StingTagsPanel,
    StingValidatePanel,
    StingCoordPanel,
    StingExportPanel,
    StingSpatialPanel,
    StingMEPPanel,
)

CLASSES = (
    StingMainPanel,
    StingSelectPanel,
    StingTagsPanel,
    StingValidatePanel,
    StingCoordPanel,
    StingExportPanel,
    StingSpatialPanel,
    StingMEPPanel,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
