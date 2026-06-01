"""STING operator registrations — Day-1 diagnostics + 16 MVP operators."""

from __future__ import annotations

import bpy

from .about import StingAboutOperator
from .reload_substrate import StingReloadSubstrateOperator
from .bonsai_probe import StingBonsaiProbeOperator

from .select_ops import CLASSES as SELECT_CLASSES
from .tagging_ops import CLASSES as TAGGING_CLASSES
from .validation_ops import CLASSES as VALIDATION_CLASSES
from .coord_ops import CLASSES as COORD_CLASSES

CLASSES = (
    StingAboutOperator,
    StingReloadSubstrateOperator,
    StingBonsaiProbeOperator,
    *SELECT_CLASSES,
    *TAGGING_CLASSES,
    *VALIDATION_CLASSES,
    *COORD_CLASSES,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
