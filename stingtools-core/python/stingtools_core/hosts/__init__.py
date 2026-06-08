"""Host adapter layer — the single seam every host (Revit / Bonsai / ArchiCAD-IFC
/ Tekla-IFC) implements. See ``adapter.HostAdapter`` and
``docs/MULTI_HOST_INTEGRATION_PLAN.md`` §1.0–§1.1.
"""

from __future__ import annotations

from .adapter import ChangeDelta, GeorefDescriptor, HostAdapter
from .ifc_file import IfcFileHostAdapter
from . import inference

__all__ = [
    "HostAdapter",
    "GeorefDescriptor",
    "ChangeDelta",
    "IfcFileHostAdapter",
    "inference",
]
