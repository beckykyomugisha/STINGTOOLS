"""STING operator registrations.

Day-1 scaffold: three diagnostic operators that confirm the substrate
loads and Bonsai integration is working. Real tagging / validation /
sync operators land per the MVP scope.
"""

from __future__ import annotations

import bpy

from .about import StingAboutOperator
from .reload_substrate import StingReloadSubstrateOperator
from .bonsai_probe import StingBonsaiProbeOperator

CLASSES = (
    StingAboutOperator,
    StingReloadSubstrateOperator,
    StingBonsaiProbeOperator,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
