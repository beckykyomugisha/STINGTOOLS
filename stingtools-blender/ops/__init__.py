"""STING operator registrations.

Day-1 scaffold: one diagnostic operator (`sting.about`) that confirms
the substrate loads and prints a count of enums + psets. Real tagging /
validation / sync operators land per the MVP scope.
"""

from __future__ import annotations

import bpy

from .about import StingAboutOperator
from .reload_substrate import StingReloadSubstrateOperator

CLASSES = (
    StingAboutOperator,
    StingReloadSubstrateOperator,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
