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

#: ISO 19650 suitability/status, stored ALONGSIDE the tag segments — never
#: inside :class:`Tag`.
#:
#: `Tag` is a strict 8-segment grammar: `to_full_tag`/`from_full_tag` assume
#: exactly eight parts, so adding a ninth field would corrupt every tag string
#: the system renders. Status is metadata *about* the element, not a segment
#: *of* its tag, so it gets its own property in the same pset.
#:
#: Why it is written at all: `status` is one of the reconcile engine's
#: TOKEN_KEYS (sync/reconcile.py), and the change feed carries it
#: (ChangesController.cs:108). An adapter that applies a status-only remote
#: change without persisting it re-applies that same change on EVERY subsequent
#: pull, forever — the delta never converges because the local read-back never
#: reflects the write.
STATUS_PROP = "Status"

#: Change-feed token key -> Tag field.
#:
#: The feed sends short lower-case keys (`ChangesController.cs:105-108`:
#: `disc, loc, zone, lvl, sys, func, prod, seq, status, …`), which are also the
#: reconcile engine's TOKEN_KEYS. The PSET uses long capitalised names
#: (`Discipline`, `Location`, …). These are two different vocabularies for the
#: same eight segments and they were being conflated: `apply_remote_change` fed
#: `delta.payload` straight into `Tag.from_pset`, which looks up `"Discipline"`
#: in a dict that only has `"disc"`. Every lookup missed, so applying a REAL feed
#: delta wrote eight UNKNOWN sentinels — it did not just lose status, it blanked
#: the tag. Latent only because nothing wires the pull loop yet (SB-5a).
_FEED_TO_TAG = {
    "disc": "discipline", "loc": "location", "zone": "zone", "lvl": "level",
    "sys": "system", "func": "function", "prod": "product", "seq": "sequence",
}


def _tag_from_payload(payload: dict) -> "Tag":
    """Build a Tag from a change-feed payload, tolerating the PSET vocabulary.

    Feed keys win; PSET-cased keys are accepted so a caller hand-building a
    payload from a pset (as some tests and the ArchiCAD route do) still works.
    """
    payload = payload or {}
    if any(k in payload for k in _FEED_TO_TAG):
        fields = {
            field: str(payload.get(feed_key) or "")
            for feed_key, field in _FEED_TO_TAG.items()
            if payload.get(feed_key)
        }
        return Tag(**fields) if fields else Tag.from_pset(payload)
    return Tag.from_pset(payload)


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
        """The coordinate evidence this file carries.

        P4 — this used to return only ``IfcMapConversion``'s eastings /
        northings / height / scale, leaving ``crs_epsg`` and
        ``true_north_deg`` unset and ``length_unit`` at its ``"mm"`` default.
        Those three omissions matter more than they look:

        * **No CRS** means the server cannot anchor the coordinates. Its
          confidence policy grades an unanchored survey origin LOW, so the
          transform is stored as a suggestion and the model is left at the
          project origin until a coordinator confirms it by hand — the exact
          manual step this whole track exists to remove.
        * **No true north** silently drops a model's rotation. A building
          placed at the right easting and northing but rotated 20 degrees off
          is arguably worse than one left at the origin, because it looks
          placed.
        * **``length_unit="mm"`` was simply wrong.** Eastings and northings
          from ``IfcMapConversion`` are metres by IFC definition, and the
          field defaulted to millimetres, so any consumer that trusted it was
          off by 1000.

        The rotation formula ``atan2(XAxisOrdinate, XAxisAbscissa)`` matches
        ``IfcAlignmentValidator.MapConversionRotationDeg`` on the server
        exactly. It has to: the same file ingested through the server and
        described by this adapter must not disagree about which way the
        building faces.
        """
        import math

        try:
            mc = self.model.by_type("IfcMapConversion")
        except Exception:
            return GeorefDescriptor(logeoref_tier=0)

        if not mc:
            return GeorefDescriptor(logeoref_tier=0, length_unit=self._length_unit())

        m = mc[0]

        # XAxisAbscissa / XAxisOrdinate are the direction cosines of the
        # model's +X axis in the map CRS. Both default to (1, 0) — i.e. no
        # rotation — when the file omits them.
        true_north_deg = None
        xa = getattr(m, "XAxisAbscissa", None)
        xo = getattr(m, "XAxisOrdinate", None)
        if xa is not None and xo is not None:
            try:
                true_north_deg = math.degrees(math.atan2(float(xo), float(xa)))
            except (TypeError, ValueError):
                true_north_deg = None

        # IfcProjectedCRS.Name is the CRS identifier ("EPSG:27700"). It is what
        # promotes this georeference from "some coordinates" to "coordinates in
        # a named system", which is what lets the server apply it unattended.
        crs_epsg = None
        try:
            crs = self.model.by_type("IfcProjectedCRS")
            if crs:
                crs_epsg = getattr(crs[0], "Name", None) or None
        except Exception:
            crs_epsg = None

        scale = getattr(m, "Scale", None)

        return GeorefDescriptor(
            # 50 with a named CRS, 30 without: coordinates whose system is
            # unstated are a weaker claim, and the tier is what a consumer
            # reads to decide whether to trust them.
            logeoref_tier=50 if crs_epsg else 30,
            crs_epsg=crs_epsg,
            easting=getattr(m, "Eastings", None),
            northing=getattr(m, "Northings", None),
            elevation=getattr(m, "OrthogonalHeight", None),
            true_north_deg=true_north_deg,
            scale=scale if scale else 1.0,
            length_unit=self._length_unit(),
        )

    def _length_unit(self) -> str:
        """This file's own length unit, as a short code ("m" / "mm" / "ft").

        Read from the model rather than assumed. ``calculate_unit_scale``
        returns metres per file unit, which is the only thing that
        distinguishes a 10-unit wall that is 10 m long from one that is 10 mm
        long. Falls back to metres when ifcopenshell is unavailable or the file
        declares nothing — the same fail-safe the server uses, because
        "leave it alone" is the only safe response to an unknown unit.
        """
        try:
            import ifcopenshell.util.unit as unit_util  # type: ignore
            metres_per_unit = float(unit_util.calculate_unit_scale(self.model))
        except Exception:
            return "m"

        for code, factor in (("m", 1.0), ("mm", 0.001), ("cm", 0.01),
                             ("ft", 0.3048), ("in", 0.0254)):
            if abs(metres_per_unit - factor) < factor * 1e-6:
                return code
        return "m"

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

    def read_status(self, element: Any) -> str:
        """ISO 19650 suitability/status for *element*, or "" when unset."""
        try:
            pset = _ue().get_pset(element, STING_PSET) or {}
        except Exception:
            return ""
        return str(pset.get(STATUS_PROP) or "")

    def write_status(self, element: Any, status: str) -> bool:
        """Persist ISO 19650 status alongside the tag segments."""
        try:
            import ifcopenshell.api  # type: ignore
        except ImportError:
            return False
        try:
            pset = ifcopenshell.api.run("pset.add_pset", self.model,
                                        product=element, name=STING_PSET)
            ifcopenshell.api.run("pset.edit_pset", self.model,
                                 pset=pset, properties={STATUS_PROP: str(status or "")})
            return True
        except Exception:
            return False

    def read_tokens(self, element: Any) -> dict:
        """The token dict the reconcile engine compares against a remote delta.

        Keys match `stingtools_core.sync.reconcile.TOKEN_KEYS` exactly — the eight
        tag segments plus `status`. Callers building a `local_index` should use
        this rather than assembling the dict themselves; a caller that forgets
        `status` reintroduces the non-convergence this method exists to prevent.
        """
        tag = self.read_tag(element)
        return {
            "disc": tag.discipline, "loc": tag.location, "zone": tag.zone,
            "lvl": tag.level, "sys": tag.system, "func": tag.function,
            "prod": tag.product, "seq": tag.sequence,
            "status": self.read_status(element),
        }

    def apply_remote_change(self, delta: ChangeDelta) -> bool:
        """Apply a pulled change. Tags are applied here; issue/bcf/clash/transform
        deltas are recorded by the caller's sync layer (no local IFC mutation)."""
        if delta.kind != "tag":
            return True  # non-geometry review payloads handled by the sync layer
        el = self._element_by_gid(delta.global_id)
        if el is None:
            return False

        ok = self.write_tag(el, _tag_from_payload(delta.payload))

        # Status travels with the tag but is not part of it. Applying the eight
        # segments and dropping status made a status-only delta un-appliable:
        # reconcile saw a difference, applied it, the read-back still showed the
        # old status, and the next pull produced the identical delta. Forever.
        payload = delta.payload or {}
        if "status" in payload or STATUS_PROP in payload:
            status = payload.get("status", payload.get(STATUS_PROP))
            ok = self.write_status(el, status) and ok
        return ok

    # -- helpers ---------------------------------------------------------------
    def _element_by_gid(self, gid: str) -> Optional[Any]:
        try:
            return self.model.by_guid(gid)
        except Exception:
            return None
