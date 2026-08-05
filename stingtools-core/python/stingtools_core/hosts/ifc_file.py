"""IfcFileHostAdapter — headless host adapter over a plain ifcopenshell file.

This is the adapter used by the StingBridge worker and, in Phase E, by the
ArchiCAD-IFC and Tekla-IFC routes (no native plugins). It reads/writes an
``.ifc`` directly via ``ifcopenshell.api`` — there is no Blender undo stack to
hook, so writes go straight to the model.

``ifcopenshell`` is imported lazily so this module is importable (and the
contract is testable) without it installed.
"""

from __future__ import annotations

from typing import Any, Iterable, Optional

from ..tag_grammar import Tag
from .adapter import ChangeDelta, GeorefDescriptor, HostAdapter
from . import inference

STING_PSET = "Pset_StingTags"

_TAG_TO_PSET = {
    "discipline": "Discipline", "location": "Location", "zone": "Zone",
    "level": "Level", "system": "System", "function": "Function",
    "product": "Product", "sequence": "Sequence",
}


def _ue():
    import ifcopenshell.util.element as ue  # type: ignore
    return ue


class IfcFileHostAdapter(HostAdapter):
    """Adapter over an opened ``ifcopenshell.file``.

    Parameters
    ----------
    model:
        An ``ifcopenshell.file`` (e.g. ``ifcopenshell.open(path)``).
    host_name:
        "ifc" (default), or "archicad" / "tekla" for the IFC-route adapters so
        pushes carry the correct ``Host`` attribution to the server.
    """

    def __init__(self, model: Any, host_name: str = "ifc") -> None:
        self.model = model
        self.host_name = host_name

    # -- read ------------------------------------------------------------------
    def read_elements(self) -> Iterable[Any]:
        return self.model.by_type("IfcElement")

    def global_id(self, element: Any) -> str:
        gid = getattr(element, "GlobalId", None)
        return str(gid) if gid else ""

    def host_element_id(self, element: Any) -> str:
        name = getattr(element, "Name", None)
        if name:
            return str(name)
        try:
            return f"#{element.id()}"
        except Exception:
            return ""

    def read_tag(self, element: Any) -> Tag:
        try:
            pset = _ue().get_pset(element, STING_PSET) or {}
        except Exception:
            pset = {}
        return Tag.from_pset(pset)

    def georef_descriptor(self) -> GeorefDescriptor:
        """Best-effort coordinate evidence. Full tiered extraction is Part 2/Phase C;
        this returns what is cheaply available so headless callers have a value."""
        try:
            import ifcopenshell.util.geolocation as geo  # type: ignore
            mc = self.model.by_type("IfcMapConversion")
            if mc:
                m = mc[0]
                return GeorefDescriptor(
                    logeoref_tier=50,
                    easting=getattr(m, "Eastings", None),
                    northing=getattr(m, "Northings", None),
                    elevation=getattr(m, "OrthogonalHeight", None),
                    scale=getattr(m, "Scale", None) or 1.0,
                )
            _ = geo  # available for the Phase C tiered resolver
        except Exception:
            pass
        return GeorefDescriptor(logeoref_tier=0)

    # -- write -----------------------------------------------------------------
    def write_tag(self, element: Any, tag: Tag) -> bool:
        try:
            import ifcopenshell.api  # type: ignore
        except ImportError:
            return False
        try:
            props = {_TAG_TO_PSET[name]: getattr(tag, name) for name in _TAG_TO_PSET}
            pset = ifcopenshell.api.run("pset.add_pset", self.model,
                                        product=element, name=STING_PSET)
            ifcopenshell.api.run("pset.edit_pset", self.model,
                                 pset=pset, properties=props)
            return True
        except Exception:
            return False

    def apply_remote_change(self, delta: ChangeDelta) -> bool:
        """Apply a pulled change. Tags are applied here; issue/bcf/clash/transform
        deltas are recorded by the caller's sync layer (no local IFC mutation)."""
        if delta.kind != "tag":
            return True  # non-geometry review payloads handled by the sync layer
        el = self._element_by_gid(delta.global_id)
        if el is None:
            return False
        return self.write_tag(el, Tag.from_pset(delta.payload))

    # -- helpers ---------------------------------------------------------------
    def _element_by_gid(self, gid: str) -> Optional[Any]:
        try:
            return self.model.by_guid(gid)
        except Exception:
            return None
