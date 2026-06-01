"""STING operator registrations.

Diagnostics (about / reload_substrate / bonsai_probe) confirm the
substrate loads and Bonsai integration is working. The COORD operator
(sync_planscape) is the first real federation op: it validates
Pset_StingTags-bearing elements and ingests them into Planscape Server.
"""

from __future__ import annotations

import bpy

from .about import StingAboutOperator
from .reload_substrate import StingReloadSubstrateOperator
from .bonsai_probe import StingBonsaiProbeOperator
from .sync_planscape import StingSyncPlanscapeOperator
from .raise_issue import StingRaiseIssueOperator

CLASSES = (
    StingAboutOperator,
    StingReloadSubstrateOperator,
    StingBonsaiProbeOperator,
    StingSyncPlanscapeOperator,
    StingRaiseIssueOperator,
)


def register():
    for cls in CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)
