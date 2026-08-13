"""Tests for ZONE/LOC resolution in the live ArchiCAD sync path.

Before this, ``SyncEngine`` never passed ``zone_name``/``building_name`` into
``map_element_to_tokens``, so every element in every model got the ``ZZ`` /
``BLD1`` fallbacks and the ZONE segment of the tag carried no information.

Fixture-driven — no ArchiCAD session required.

Run from the repo root:  python StingBridge/tests/test_zone_index.py
(or via pytest).
"""
from __future__ import annotations

import sys
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.archicad.client import ArchiCadError  # noqa: E402
from StingBridge.sync.engine import _build_zone_index, _extract_guid  # noqa: E402
from StingBridge.sync.token_mapper import map_element_to_tokens  # noqa: E402


class _FakeAC:
    """Duck-typed stand-in for ArchiCadClient's two zone-relevant calls."""

    def __init__(self, relations, zone_props, *, raise_on_relations=False,
                 raise_on_props=False):
        self._relations = relations
        self._zone_props = zone_props
        self._raise_relations = raise_on_relations
        self._raise_props = raise_on_props
        self.property_calls = 0

    def get_elements_related_to_zones(self):
        if self._raise_relations:
            raise ArchiCadError("zones unavailable")
        return self._relations

    def get_property_values(self, guids, props):
        self.property_calls += 1
        if self._raise_props:
            raise ArchiCadError("properties unavailable")
        return [p for p in self._zone_props if _extract_guid(p) in guids]


def _zone_prop(zone_guid, number="", name=""):
    values = []
    if number:
        values.append({"propertyId": {"type": "BuiltIn",
                                      "nonLocalizedName": "Zone_ZoneNumber"},
                       "propertyValue": {"type": "normal", "value": number}})
    if name:
        values.append({"propertyId": {"type": "BuiltIn",
                                      "nonLocalizedName": "Zone_ZoneName"},
                       "propertyValue": {"type": "normal", "value": name}})
    return {"elementId": {"guid": zone_guid}, "propertyValues": values}


def test_zone_index_maps_members_to_zone_number():
    ac = _FakeAC(
        relations=[
            {"zoneId": {"guid": "zone-1"},
             "elementIds": [{"guid": "el-a"}, {"guid": "el-b"}]},
            {"zoneId": {"guid": "zone-2"}, "elementIds": [{"guid": "el-c"}]},
        ],
        zone_props=[_zone_prop("zone-1", number="101", name="Office"),
                    _zone_prop("zone-2", number="205", name="Plant Room")],
    )
    idx = _build_zone_index(ac)
    assert idx == {"el-a": "101", "el-b": "101", "el-c": "205"}
    assert ac.property_calls == 1, "zone labels must resolve in ONE round-trip"


def test_zone_index_accepts_the_nested_elementid_shape():
    """The AC JSON API returns element refs both bare and nested depending on
    the command; both must resolve or the index comes back silently empty."""
    ac = _FakeAC(
        relations=[{"zoneId": {"guid": "zone-1"},
                    "elementIds": [{"elementId": {"guid": "el-a"}}]}],
        zone_props=[_zone_prop("zone-1", number="7")],
    )
    assert _build_zone_index(ac) == {"el-a": "7"}


def test_zone_index_falls_back_to_name_when_number_absent():
    ac = _FakeAC(
        relations=[{"zoneId": {"guid": "z"}, "elementIds": [{"guid": "e"}]}],
        zone_props=[_zone_prop("z", name="Zone 12")],
    )
    assert _build_zone_index(ac) == {"e": "Zone 12"}


def test_zone_index_degrades_to_empty_on_api_errors():
    """Every failure path must fall back to today's behaviour, not abort sync."""
    rel = [{"zoneId": {"guid": "z"}, "elementIds": [{"guid": "e"}]}]
    props = [_zone_prop("z", number="1")]
    assert _build_zone_index(_FakeAC(rel, props, raise_on_relations=True)) == {}
    assert _build_zone_index(_FakeAC(rel, props, raise_on_props=True)) == {}
    assert _build_zone_index(_FakeAC([], [])) == {}
    # Zone present but unlabelled → member simply carries no zone.
    assert _build_zone_index(_FakeAC(rel, [_zone_prop("z")])) == {}


def test_resolved_zone_reaches_the_token():
    """End of the wire: a resolved label becomes a real ZONE token, and an
    unresolved one still yields the documented ZZ fallback."""
    tokens = map_element_to_tokens(
        element_type="Wall", props={}, storey_name="Level 02",
        building_name="Block B", zone_name="101",
    )
    assert tokens["zone"] == "Z101"
    assert tokens["loc"] == "BLDB"

    fallback = map_element_to_tokens(element_type="Wall", props={},
                                     storey_name="Level 02")
    assert fallback["zone"] == "ZZ"
    assert fallback["loc"] == "BLD1"


def test_zone_property_reads_are_batched():
    """Thousands of zones must not produce one oversized API request — reads
    go out in chunks of 100, and every zone still resolves."""
    n = 250
    relations = [
        {"zoneId": {"guid": f"zone-{i}"}, "elementIds": [{"guid": f"el-{i}"}]}
        for i in range(n)
    ]
    props = [_zone_prop(f"zone-{i}", number=str(i)) for i in range(n)]
    ac = _FakeAC(relations=relations, zone_props=props)
    idx = _build_zone_index(ac)
    assert len(idx) == n
    assert idx["el-249"] == "249"
    assert ac.property_calls == 3, "250 zones → ceil(250/100) = 3 batched reads"


def test_extract_guid_shapes():
    assert _extract_guid({"guid": "a"}) == "a"
    assert _extract_guid({"elementId": {"guid": "b"}}) == "b"
    assert _extract_guid({"zoneId": {"guid": "c"}}) == "c"
    assert _extract_guid("d") == "d"
    assert _extract_guid({}) == ""
    assert _extract_guid(None) == ""


if __name__ == "__main__":
    import traceback
    tests = [v for k, v in sorted(globals().items()) if k.startswith("test_")]
    fails = 0
    for t in tests:
        try:
            t()
            print(f"  OK   {t.__name__}")
        except Exception:
            fails += 1
            print(f"  FAIL {t.__name__}")
            traceback.print_exc()
    print(f"\n{len(tests)} tests, {fails} failures")
    sys.exit(1 if fails else 0)
