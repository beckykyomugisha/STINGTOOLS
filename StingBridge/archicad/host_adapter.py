"""ArchiCAD-live implementation of the core ``HostAdapter`` contract (SB-3).

StingBridge's sync engine predates the adapter contract, so ArchiCAD was the one
host reaching the hub through a bespoke path. This is the seam that puts it on
the same footing as Revit / Bonsai / the IFC-file path, which matters for the
Phase B pull + reconcile work: the reconcile engine drives hosts through
``apply_remote_change``, and a host without an adapter cannot participate.

Lives in StingBridge rather than core because it depends on ``ArchiCadClient``;
core stays dependency-free (Phase A6 boundary rule). It contains no tag grammar
or inference of its own — it calls core for both.
"""
from __future__ import annotations

import logging
from typing import Any, Iterable, Optional

from .._core import archicad as _core_ac

log = logging.getLogger(__name__)


def _adapter_base():
    """Import the contract, tolerating core not being pip-installed."""
    from .._core import archicad  # noqa: F401 — ensures sys.path is primed
    from stingtools_core.hosts.adapter import (
        HostAdapter, ChangeDelta, GeorefDescriptor,
    )
    from stingtools_core.tag_grammar import Tag
    return HostAdapter, ChangeDelta, GeorefDescriptor, Tag


HostAdapter, ChangeDelta, GeorefDescriptor, Tag = _adapter_base()

# STING property names, as written by PropertyWriter, in the "STING." group.
_PROP_PREFIX = "STING."
_TOKEN_PROPS = {
    "discipline": "ASS_DISCIPLINE_COD_TXT",
    "location":   "ASS_LOC_TXT",
    "zone":       "ASS_ZONE_TXT",
    "level":      "ASS_LVL_COD_TXT",
    "system":     "ASS_SYSTEM_TYPE_TXT",
    "function":   "ASS_FUNC_TXT",
    "product":    "ASS_PRODCT_COD_TXT",
    "sequence":   "ASS_SEQ_NUM_TXT",
}


def ifc_global_id_from_acguid(ac_guid: str) -> str:
    """ArchiCAD element GUID → the compressed IfcGuid ArchiCAD assigns on export.

    Shared with the sync engine so both produce the same cross-host key. Falls
    back to the raw GUID when ifcopenshell is unavailable or the GUID is
    malformed — ``host_element_id`` is exact either way.

    ASSUMPTION (unverified against live ArchiCAD, tracked as SB-1): that
    ArchiCAD derives the export GlobalId from this same JSON-API GUID.
    """
    cleaned = (ac_guid or "").strip().strip("{}").replace("-", "")
    if len(cleaned) != 32:
        return ac_guid
    try:
        import ifcopenshell.guid as _g
        return _g.compress(cleaned.lower())
    except Exception:  # noqa: BLE001 — ifcopenshell optional / bad input
        return ac_guid


class ArchiCadHostAdapter(HostAdapter):
    """Drives a live ArchiCAD session through the JSON API."""

    host_name = "archicad"

    def __init__(self, client: Any, property_writer: Any | None = None,
                 element_types: Optional[list[str]] = None) -> None:
        self._client = client
        self._writer = property_writer
        self._types = element_types or _core_ac.SYNCABLE_TYPES

    # ── read ─────────────────────────────────────────────────────────────────

    def read_elements(self) -> Iterable[str]:
        """Yield ArchiCAD element GUIDs for every syncable type.

        Yields GUIDs rather than rich objects because that is ArchiCAD's own
        currency — details and properties are fetched in batches by GUID, and
        materialising every element up front would defeat that batching.
        """
        for element_type in self._types:
            try:
                yield from self._client.get_elements_by_type(element_type)
            except Exception as e:  # noqa: BLE001 — one bad type must not end the sweep
                log.warning("get_elements_by_type(%s) failed: %s", element_type, e)

    def global_id(self, element: Any) -> str:
        return ifc_global_id_from_acguid(str(element))

    def host_element_id(self, element: Any) -> str:
        # The ArchiCAD GUID itself — exact, and what write-back addresses.
        return str(element)

    def read_tag(self, element: Any) -> Tag:
        """Read the element's STING tokens back out of its ArchiCAD properties."""
        guid = str(element)
        try:
            rows = self._client.get_property_values([guid])
        except Exception as e:  # noqa: BLE001
            log.warning("get_property_values failed for %s: %s", guid, e)
            return Tag()

        bag: dict[str, str] = {}
        for epv in rows or []:
            for pv in epv.get("propertyValues", []):
                pid = pv.get("propertyId", {})
                name = pid.get("name") if isinstance(pid, dict) else None
                value = pv.get("propertyValue", {})
                if name and value.get("type") == "normal":
                    bag[str(name)] = str(value.get("value", ""))

        def get(prop: str) -> str | None:
            return bag.get(_PROP_PREFIX + prop) or bag.get(prop) or None

        fields = {f: get(p) for f, p in _TOKEN_PROPS.items()}
        # Drop empties so Tag's own sentinel defaults apply rather than "".
        return Tag(**{k: v for k, v in fields.items() if v})

    # ── write ────────────────────────────────────────────────────────────────

    def write_tag(self, element: Any, tag: Tag, status: str | None = None) -> bool:
        """Write the tag back as STING User-Defined properties.

        ``status`` is passed separately because it is NOT a tag segment — `Tag`
        is a strict eight-segment grammar. `PropertyWriter._TOKEN_PROPS` already
        maps it to `ASS_STATUS_TXT`; this method simply never supplied it, so a
        status-only remote change could not be persisted and would re-apply on
        every pull once SB-5a wires the loop. Empty/None values are skipped by
        the writer, so existing callers that omit it are unaffected.
        """
        if self._writer is None:
            log.warning("No PropertyWriter - cannot write back to ArchiCAD")
            return False
        tokens = {
            "disc": tag.discipline, "loc": tag.location, "zone": tag.zone,
            "lvl": tag.level, "sys": tag.system, "func": tag.function,
            "prod": tag.product, "seq": tag.sequence,
            "tag1": tag.to_full_tag(),
        }
        if status:
            tokens["status"] = str(status)
        try:
            self._writer.write_tokens([(str(element), tokens)])
            return True
        except Exception as e:  # noqa: BLE001
            log.warning("write_tag failed for %s: %s", element, e)
            return False

    def apply_remote_change(self, delta: ChangeDelta) -> bool:
        """Pull side — apply one remote change to the live model.

        Only ``tag`` deltas are actionable in ArchiCAD today: issues and BCF
        live server-side with no ArchiCAD surface to write them to, and
        transforms would move the user's model, which this adapter will not do
        unprompted. Unhandled kinds return False rather than silently claiming
        success, so the reconcile engine can report them.
        """
        if delta.kind != "tag":
            log.info("ArchiCAD adapter ignores '%s' delta for %s",
                     delta.kind, delta.global_id)
            return False

        guid = self._host_guid_for_global_id(delta.global_id)
        if guid is None:
            log.warning("No local element for GlobalId %s", delta.global_id)
            return False

        payload = delta.payload or {}
        tag = Tag(
            discipline=payload.get("disc") or payload.get("discipline") or "XX",
            location=payload.get("loc") or payload.get("location") or "XX",
            zone=payload.get("zone") or "XX",
            level=payload.get("lvl") or payload.get("level") or "XX",
            system=payload.get("sys") or payload.get("system") or "XX",
            function=payload.get("func") or payload.get("function") or "XX",
            product=payload.get("prod") or payload.get("product") or "XX",
            sequence=payload.get("seq") or payload.get("sequence") or "0000",
        )
        return self.write_tag(guid, tag, status=payload.get("status"))

    def georef_descriptor(self) -> GeorefDescriptor:
        """Coordinate evidence for the Federation Placement Resolver.

        ArchiCAD's JSON API exposes no survey point or CRS, so this reports
        tier 0 (unknown) rather than guessing. Raising the tier requires the
        Tapir add-on or reading it from an IFC export — deliberate future work,
        not something to fake with a default that downstream code would trust.
        """
        return GeorefDescriptor(logeoref_tier=0)

    # ── helpers ──────────────────────────────────────────────────────────────

    def _host_guid_for_global_id(self, global_id: str) -> Optional[str]:
        """Reverse the GlobalId → ArchiCAD GUID mapping by scanning elements.

        Linear, so callers applying many deltas should build their own index;
        this is the correct-but-simple path for one-off application.
        """
        for guid in self.read_elements():
            if self.global_id(guid) == global_id:
                return guid
        return None
