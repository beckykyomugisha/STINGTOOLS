"""Tests for the ArchiCAD HostAdapter (SB-3, Phase 205).

StingBridge's sync engine predates the adapter contract, so ArchiCAD was the one
host reaching the hub through a bespoke path. These pin the contract behaviour
that the Phase B reconcile engine will depend on.

Run from the repo root:  python StingBridge/tests/test_host_adapter.py
"""
from __future__ import annotations

import sys
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.archicad.host_adapter import (  # noqa: E402
    ArchiCadHostAdapter, ifc_global_id_from_acguid, ChangeDelta, Tag,
)


class _FakeClient:
    def __init__(self, by_type=None, props=None, fail_types=()):
        self._by_type = by_type or {}
        self._props = props or {}
        self._fail = set(fail_types)
        self.calls = []

    def get_elements_by_type(self, t):
        self.calls.append(t)
        if t in self._fail:
            raise RuntimeError("ArchiCAD said no")
        return self._by_type.get(t, [])

    def get_property_values(self, guids):
        out = []
        for g in guids:
            bag = self._props.get(g, {})
            out.append({"propertyValues": [
                {"propertyId": {"name": k},
                 "propertyValue": {"type": "normal", "value": v}}
                for k, v in bag.items()
            ]})
        return out


class _FakeWriter:
    def __init__(self, fail=False):
        self.written = []
        self._fail = fail

    def write_tokens(self, pairs):
        if self._fail:
            raise RuntimeError("write failed")
        self.written.extend(pairs)


# ── identity ─────────────────────────────────────────────────────────────────

def test_global_id_compresses_the_archicad_guid():
    gid = ifc_global_id_from_acguid("12345678-1234-1234-1234-123456789ABC")
    assert len(gid) == 22, f"expected a 22-char IfcGuid, got {gid!r}"


def test_malformed_guid_falls_back_to_the_raw_value():
    assert ifc_global_id_from_acguid("not-a-guid") == "not-a-guid"
    assert ifc_global_id_from_acguid("") == ""


def test_host_element_id_is_the_exact_archicad_guid():
    a = ArchiCadHostAdapter(_FakeClient())
    guid = "12345678-1234-1234-1234-123456789ABC"
    assert a.host_element_id(guid) == guid
    # …and is distinct from the cross-host key.
    assert a.global_id(guid) != guid


def test_host_name_matches_the_server_mapping_host():
    assert ArchiCadHostAdapter(_FakeClient()).host_name == "archicad"


# ── reading elements ─────────────────────────────────────────────────────────

def test_read_elements_sweeps_every_syncable_type():
    client = _FakeClient(by_type={"Wall": ["g1", "g2"], "Column": ["g3"]})
    a = ArchiCadHostAdapter(client, element_types=["Wall", "Column"])
    assert list(a.read_elements()) == ["g1", "g2", "g3"]
    assert client.calls == ["Wall", "Column"]


def test_one_failing_type_does_not_end_the_sweep():
    # A single unsupported/erroring type must not cost the whole model.
    client = _FakeClient(by_type={"Wall": ["g1"], "Column": ["g3"]},
                         fail_types=["Wall"])
    a = ArchiCadHostAdapter(client, element_types=["Wall", "Column"])
    assert list(a.read_elements()) == ["g3"]


def test_read_elements_defaults_to_the_core_syncable_list():
    a = ArchiCadHostAdapter(_FakeClient())
    sys.path.insert(0, str(_REPO_ROOT / "stingtools-core" / "python"))
    from stingtools_core.hosts import archicad as core
    assert a._types == core.SYNCABLE_TYPES


# ── reading tags ─────────────────────────────────────────────────────────────

def test_read_tag_pulls_tokens_from_sting_properties():
    client = _FakeClient(props={"g1": {
        "STING.ASS_DISCIPLINE_COD_TXT": "M",
        "STING.ASS_LOC_TXT": "BLD1",
        "STING.ASS_ZONE_TXT": "Z01",
        "STING.ASS_LVL_COD_TXT": "L02",
        "STING.ASS_SYSTEM_TYPE_TXT": "HVAC",
        "STING.ASS_FUNC_TXT": "SUP",
        "STING.ASS_PRODCT_COD_TXT": "AHU",
        "STING.ASS_SEQ_NUM_TXT": "0003",
    }})
    tag = ArchiCadHostAdapter(client).read_tag("g1")
    assert tag.to_full_tag() == "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003"


def test_read_tag_of_an_untagged_element_yields_sentinels_not_blanks():
    tag = ArchiCadHostAdapter(_FakeClient(props={"g1": {}})).read_tag("g1")
    assert tag.discipline == "XX"
    assert "--" not in tag.to_full_tag(), "empty segments leaked into the tag"


def test_read_tag_survives_a_client_error():
    class _Boom:
        def get_property_values(self, guids):
            raise RuntimeError("offline")
    tag = ArchiCadHostAdapter(_Boom()).read_tag("g1")
    assert tag.discipline == "XX"


# ── writing tags ─────────────────────────────────────────────────────────────

def test_write_tag_sends_every_token_plus_the_assembled_tag():
    writer = _FakeWriter()
    a = ArchiCadHostAdapter(_FakeClient(), property_writer=writer)
    tag = Tag(discipline="M", location="BLD1", zone="Z01", level="L02",
              system="HVAC", function="SUP", product="AHU", sequence="0003")

    assert a.write_tag("g1", tag) is True
    (guid, tokens), = writer.written
    assert guid == "g1"
    assert tokens["disc"] == "M"
    assert tokens["seq"] == "0003"
    assert tokens["tag1"] == "M-BLD1-Z01-L02-HVAC-SUP-AHU-0003"


def test_write_tag_without_a_writer_reports_failure_rather_than_pretending():
    a = ArchiCadHostAdapter(_FakeClient(), property_writer=None)
    assert a.write_tag("g1", Tag()) is False


def test_write_tag_reports_failure_when_the_write_throws():
    a = ArchiCadHostAdapter(_FakeClient(), property_writer=_FakeWriter(fail=True))
    assert a.write_tag("g1", Tag()) is False


# ── pull side ────────────────────────────────────────────────────────────────

def test_apply_remote_change_writes_a_tag_delta_to_the_matching_element():
    guid = "12345678-1234-1234-1234-123456789ABC"
    writer = _FakeWriter()
    a = ArchiCadHostAdapter(_FakeClient(by_type={"Wall": [guid]}),
                            property_writer=writer, element_types=["Wall"])

    delta = ChangeDelta(kind="tag", global_id=ifc_global_id_from_acguid(guid),
                        payload={"disc": "E", "sys": "LV", "lvl": "L01",
                                 "seq": "0007"})
    assert a.apply_remote_change(delta) is True
    (written_guid, tokens), = writer.written
    assert written_guid == guid
    assert tokens["disc"] == "E"
    assert tokens["seq"] == "0007"


def test_apply_remote_change_accepts_long_form_payload_keys():
    guid = "12345678-1234-1234-1234-123456789ABC"
    writer = _FakeWriter()
    a = ArchiCadHostAdapter(_FakeClient(by_type={"Wall": [guid]}),
                            property_writer=writer, element_types=["Wall"])
    a.apply_remote_change(ChangeDelta(
        kind="tag", global_id=ifc_global_id_from_acguid(guid),
        payload={"discipline": "P", "system": "SAN"}))
    (_, tokens), = writer.written
    assert tokens["disc"] == "P"
    assert tokens["sys"] == "SAN"


def test_unknown_global_id_is_reported_not_silently_dropped():
    a = ArchiCadHostAdapter(_FakeClient(by_type={"Wall": []}),
                            property_writer=_FakeWriter(), element_types=["Wall"])
    assert a.apply_remote_change(
        ChangeDelta(kind="tag", global_id="0nothingmatchesthis00")) is False


def test_non_tag_deltas_return_false_rather_than_claiming_success():
    # Issues/BCF/transforms have no ArchiCAD write surface here. Returning True
    # would tell the reconcile engine the change landed when it did not.
    a = ArchiCadHostAdapter(_FakeClient(), property_writer=_FakeWriter())
    for kind in ("issue", "bcf", "clash", "transform"):
        assert a.apply_remote_change(
            ChangeDelta(kind=kind, global_id="x")) is False


# ── georeferencing ───────────────────────────────────────────────────────────

def test_georef_reports_tier_zero_rather_than_guessing():
    # The JSON API exposes no survey point or CRS. Reporting a tier we cannot
    # support would have downstream placement trust bad coordinates.
    g = ArchiCadHostAdapter(_FakeClient()).georef_descriptor()
    assert g.logeoref_tier == 0
    assert g.crs_epsg is None


# ── contract conformance ─────────────────────────────────────────────────────

def test_adapter_implements_every_abstract_member():
    sys.path.insert(0, str(_REPO_ROOT / "stingtools-core" / "python"))
    from stingtools_core.hosts.adapter import HostAdapter

    assert issubclass(ArchiCadHostAdapter, HostAdapter)
    # Instantiable ⇒ nothing abstract is left unimplemented.
    ArchiCadHostAdapter(_FakeClient())
    for member in ("read_elements", "global_id", "host_element_id", "read_tag",
                   "write_tag", "apply_remote_change", "georef_descriptor"):
        assert callable(getattr(ArchiCadHostAdapter, member)), member


if __name__ == "__main__":
    passed = failed = 0
    for name, fn in sorted(globals().items()):
        if name.startswith("test_") and callable(fn):
            try:
                fn()
                passed += 1
                print(f"  PASS  {name}")
            except Exception as e:  # noqa: BLE001
                failed += 1
                print(f"  FAIL  {name}: {e}")
    print(f"\n{passed} passed, {failed} failed")
    sys.exit(1 if failed else 0)
