"""Host Adapter Contract — the single seam every host plugs into.

Planscape is the hub; Revit / Bonsai / ArchiCAD-IFC / Tekla-IFC are spokes.
Each host implements this contract once; all coordination + model review then
works for *any combination* of hosts through the hub (see
``docs/MULTI_HOST_INTEGRATION_PLAN.md`` §1.0). Adapters contain ONLY these
methods — no tag grammar, enum, IDS, or alignment math (that lives in core and
is enforced by the Phase A6 boundary lint).

Pure module — imports no ``bpy`` and no ``ifcopenshell`` at top level.
"""

from __future__ import annotations

import abc
from dataclasses import dataclass, field
from typing import Any, Iterable, Optional

from ..tag_grammar import Tag


# ── Cross-host value objects ──────────────────────────────────────────────────

@dataclass(frozen=True)
class GeorefDescriptor:
    """The coordinate evidence a host can offer for its model.

    Consumed by the Federation Placement Resolver (Part 2 of the plan) to pick
    the best LoGeoRef tier. All optional — the resolver downgrades the tier as
    fields are missing, falling back to geometric / manual placement.
    """

    logeoref_tier: int = 0                 # 0/20/30/40/50
    crs_epsg: Optional[str] = None         # e.g. "EPSG:27700"
    easting: Optional[float] = None        # metres
    northing: Optional[float] = None       # metres
    elevation: Optional[float] = None      # metres
    true_north_deg: Optional[float] = None # clockwise from CRS Y
    scale: float = 1.0                     # IfcMapConversion.Scale
    length_unit: str = "mm"                # canonical project unit on this model


@dataclass(frozen=True)
class ChangeDelta:
    """One remote change to apply locally during a pull (Phase B).

    ``kind`` is the review/coordination currency that flows through the hub —
    keyed on ``global_id`` so it resolves across every host.
    """

    kind: str                              # "tag" | "issue" | "bcf" | "clash" | "transform"
    global_id: str                         # 22-char IFC GlobalId — the cross-host key
    payload: dict = field(default_factory=dict)
    last_modified_utc: Optional[str] = None  # ISO-8601; drives last-writer-wins
    cursor: Optional[str] = None             # server change cursor for resumption


# ── The contract ──────────────────────────────────────────────────────────────

class HostAdapter(abc.ABC):
    """Implement these eight members for a host. Nothing else."""

    #: "revit" | "bonsai" | "archicad" | "tekla" — matches MappingHosts on the server.
    host_name: str = "ifc"

    @abc.abstractmethod
    def read_elements(self) -> Iterable[Any]:
        """Iterate the host's taggable elements (ifcopenshell entities or host objects)."""

    @abc.abstractmethod
    def global_id(self, element: Any) -> str:
        """The 22-char IFC GlobalId — the canonical cross-host key."""

    @abc.abstractmethod
    def host_element_id(self, element: Any) -> str:
        """Host-native id: Revit ElementId / Blender object name / ArchiCAD/Tekla GUID."""

    @abc.abstractmethod
    def read_tag(self, element: Any) -> Tag:
        """Read the element's STING 8-segment tag (from Pset_StingTags)."""

    @abc.abstractmethod
    def write_tag(self, element: Any, tag: Tag) -> bool:
        """Write the tag back, host-correctly + undo-aware where the host supports it."""

    @abc.abstractmethod
    def apply_remote_change(self, delta: ChangeDelta) -> bool:
        """Pull side — apply one ChangeDelta to the local model. Return True on success."""

    @abc.abstractmethod
    def georef_descriptor(self) -> GeorefDescriptor:
        """The model's coordinate evidence for the Federation Placement Resolver."""
