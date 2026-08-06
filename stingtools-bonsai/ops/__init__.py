"""STING operator registrations — Day-1 diagnostics + 29 MVP operators."""

from __future__ import annotations

import bpy

from .about import StingAboutOperator
from .reload_substrate import StingReloadSubstrateOperator
from .bonsai_probe import StingBonsaiProbeOperator

from .select_ops import CLASSES as SELECT_CLASSES
from .tagging_ops import CLASSES as TAGGING_CLASSES
from .validation_ops import CLASSES as VALIDATION_CLASSES
from .coord_ops import CLASSES as COORD_CLASSES
from .export_ops import CLASSES as EXPORT_CLASSES
from .spatial_ops import CLASSES as SPATIAL_CLASSES
from .repair_ops import CLASSES as REPAIR_CLASSES
from .mep_ops import CLASSES as MEP_CLASSES

CLASSES = (
    StingAboutOperator,
    StingReloadSubstrateOperator,
    StingBonsaiProbeOperator,
    *SELECT_CLASSES,
    *TAGGING_CLASSES,
    *VALIDATION_CLASSES,
    *COORD_CLASSES,
    *EXPORT_CLASSES,
    *SPATIAL_CLASSES,
    *REPAIR_CLASSES,
    *MEP_CLASSES,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
