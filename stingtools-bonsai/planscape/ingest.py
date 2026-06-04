"""Collect IFC elements into IfcIngestRequest element dicts.

Pure module — imports no ``bpy``. Takes an ``ifcopenshell`` model (the
file Bonsai has open, or one opened directly with ``ifcopenshell.open``)
and returns a list of IfcElementDto-shaped dicts ready to POST to
``/api/projects/{id}/ifc/data``.

The cross-host key is the IFC ``GlobalId`` — the 22-character base64
GUID that is stable for the same physical element across Revit /
ArchiCAD / Bonsai exports. ``revitElementId`` is intentionally absent:
for a non-Revit host the server keys on the GlobalId, not a Revit id.
"""

from __future__ import annotations

from typing import Any, Callable, Optional

# IFC pset that carries the 8-segment STING tag, when present.
STING_PSET = "Pset_StingTags"

# Map STING pset property names → IfcElementDto field names.
_TAG_FIELDS = {
    "Discipline": "discipline",
    "Location": "location",
    "Zone": "zone",
    "Level": "level",
    "System": "system",
    "Function": "function",
    "Product": "product",
    "Sequence": "sequence",
    "FullTag": "fullTag",
}


def _get_pset(element: Any, pset_name: str) -> dict:
    """Best-effort pset read via ifcopenshell.util.element; {} on any failure."""
    try:
        import ifcopenshell.util.element as ue  # type: ignore
        return ue.get_pset(element, pset_name) or {}
    except Exception:  # noqa: BLE001 — missing util / no pset must not break collection
        return {}


def _global_id(element: Any) -> str:
    gid = getattr(element, "GlobalId", None)
    return str(gid) if gid else ""


def _default_host_id(element: Any) -> str:
    """Host element id when no host resolver is supplied: IFC Name, else #step-id."""
    name = getattr(element, "Name", None)
    if name:
        return str(name)
    try:
        return f"#{element.id()}"
    except Exception:  # noqa: BLE001
        return ""


def document_guid(model: Any) -> Optional[str]:
    """A stable per-model document id: the IfcProject GlobalId when available."""
    try:
        projects = model.by_type("IfcProject")
        if projects:
            gid = getattr(projects[0], "GlobalId", None)
            if gid:
                return str(gid)
    except Exception:  # noqa: BLE001
        pass
    return None


def collect_elements(
    model: Any,
    host_id_fn: Optional[Callable[[Any], str]] = None,
    ifc_type: str = "IfcElement",
    display_label_fn: Optional[Callable[[Any], str]] = None,
) -> list[dict]:
    """Build IfcElementDto-shaped dicts for every ``ifc_type`` with a GlobalId.

    Parameters
    ----------
    model        : an open ifcopenshell file.
    host_id_fn   : callable(element) -> host element id (e.g. the Bonsai/Blender
                   object name). Falls back to IFC Name / #step-id.
    ifc_type     : IFC base type to collect; default ``IfcElement`` (all
                   physical building elements).
    display_label_fn : optional callable(element) -> human label.
    """
    out: list[dict] = []
    try:
        elements = model.by_type(ifc_type)
    except Exception as e:  # noqa: BLE001
        raise ValueError(f"cannot enumerate {ifc_type} from model: {e}") from None

    for el in elements:
        gid = _global_id(el)
        if not gid:
            continue  # cross-host key is mandatory

        host_eid = ""
        if host_id_fn is not None:
            try:
                host_eid = host_id_fn(el) or ""
            except Exception:  # noqa: BLE001 — host bridge drift must not break the push
                host_eid = ""
        if not host_eid:
            host_eid = _default_host_id(el)

        try:
            ifc_class = el.is_a()
        except Exception:  # noqa: BLE001
            ifc_class = ifc_type

        label = ""
        if display_label_fn is not None:
            try:
                label = display_label_fn(el) or ""
            except Exception:  # noqa: BLE001
                label = ""
        if not label:
            label = (getattr(el, "Name", None) or ifc_class)

        record = {
            "ifcGlobalId": gid,
            "hostElementId": host_eid,
            "hostDisplayLabel": label,
            "ifcClass": ifc_class,
        }

        # Overlay the STING tag segments when the element carries them.
        pset = _get_pset(el, STING_PSET)
        if pset:
            for prop, field in _TAG_FIELDS.items():
                val = pset.get(prop)
                if val not in (None, ""):
                    record[field] = str(val)

        out.append(record)

    return out
