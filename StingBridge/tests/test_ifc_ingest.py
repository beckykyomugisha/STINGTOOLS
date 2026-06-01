"""Smoke tests for the ArchiCAD bridge → Planscape /ifc/data path (Drift 5).

Verifies the bridge now posts cross-host IFC ingest payloads (no fabricated
Revit id) and that the element→wire shaping is the SHARED core mapping, not a
local re-implementation.

Run from the repo root:  python StingBridge/tests/test_ifc_ingest.py
(or via pytest). No live server needed — the HTTP session is faked.
"""
from __future__ import annotations

import sys
from pathlib import Path

# Allow `python StingBridge/tests/test_ifc_ingest.py` from the repo root.
_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.planscape.client import PlanscapeClient, PlanscapeAuthError  # noqa: E402


class _FakeResponse:
    def __init__(self, payload):
        self.status_code = 200
        self._payload = payload

    def raise_for_status(self):
        pass

    def json(self):
        return self._payload


class _FakeSession:
    """Captures the last POST so the test can assert URL + body."""

    def __init__(self):
        self.headers = {}
        self.last_url = None
        self.last_json = None

    def post(self, url, json=None, timeout=None, **kw):
        self.last_url = url
        self.last_json = json
        return _FakeResponse({"newMappings": 1, "updatedMappings": 0,
                              "newElements": 1, "updatedElements": 0, "skipped": 0})


def test_build_ifc_element_has_no_fabricated_revit_id():
    el = PlanscapeClient.build_ifc_element(
        ifc_global_id="1Abc234Def567Ghi890Jkl",
        host_element_id="AC-GUID-001",
        disc="M", loc="BLD1", zone="Z01", lvl="L02",
        sys="HVAC", func="SUP", prod="AHU", seq="0042",
        category_name="Ducts", family_name="Round Duct",
        status="NEW", is_complete=True,
    )
    assert "revitElementId" not in el, "must NOT fabricate a Revit id"
    assert el["ifc_global_id"] == "1Abc234Def567Ghi890Jkl"
    assert el["host_element_id"] == "AC-GUID-001"
    assert el["full_tag"] == "M-BLD1-Z01-L02-HVAC-SUP-AHU-0042"
    # snake_case source keys that _element_to_wire maps onto the DTO
    for k in ("discipline", "location", "level", "system", "function",
              "product", "sequence", "category_name", "family_name", "is_complete"):
        assert k in el, f"missing source key {k!r}"


def test_build_ifc_element_defaults_host_id_to_global_id():
    """Watcher case: only the IFC GlobalId is known → it serves as host id too."""
    el = PlanscapeClient.build_ifc_element(ifc_global_id="2Xyz", disc="E")
    assert el["host_element_id"] == "2Xyz"


def test_ingest_ifc_data_posts_cross_host_contract():
    client = PlanscapeClient(base_url="http://srv", project_id="proj-1")
    client._token = "tok"  # bypass login for the unit test
    fake = _FakeSession()
    client._session = fake

    el = PlanscapeClient.build_ifc_element(
        ifc_global_id="1Abc", host_element_id="AC-1",
        disc="M", sys="HVAC", prod="AHU", seq="0001",
    )
    resp = client.ingest_ifc_data([el], host="archicad")

    # right endpoint
    assert fake.last_url == "http://srv/api/projects/proj-1/ifc/data"
    body = fake.last_json
    # request envelope is camelCase per IfcIngestRequest
    assert body["host"] == "archicad"
    assert set(["host", "hostDocumentGuid", "pluginVersion", "userName", "elements"]) <= set(body)
    wire = body["elements"][0]
    # element shaped to IfcElementDto (camelCase) by the SHARED _element_to_wire
    assert wire["ifcGlobalId"] == "1Abc"
    assert wire["hostElementId"] == "AC-1"
    assert wire["fullTag"] == "M-HVAC-AHU-0001"
    assert "revitElementId" not in wire, "no fabricated Revit id on the wire"
    assert "uniqueId" not in wire, "ifc/data keys on ifcGlobalId, not uniqueId"
    assert resp["newMappings"] == 1


def test_ingest_requires_token_and_project():
    c = PlanscapeClient(base_url="http://srv", project_id="proj-1")
    try:
        c.ingest_ifc_data([{}])
        assert False, "should require login"
    except PlanscapeAuthError:
        pass


def test_element_to_wire_is_the_shared_core_mapping():
    """The bridge must NOT carry its own copy of the wire field map."""
    from StingBridge.planscape.client import _load_element_to_wire
    to_wire = _load_element_to_wire()
    from stingtools_core.planscape.client import PlanscapeClient as Core
    # _element_to_wire is a @classmethod, so each attribute access yields a
    # fresh bound object — compare the underlying function, not identity.
    assert getattr(to_wire, "__func__", to_wire) is Core._element_to_wire.__func__, \
        "bridge must reuse core _element_to_wire, not a local copy"


def test_engine_acguid_compresses_to_ifc_globalid():
    """Engine path: ArchiCAD GUID → 22-char IfcGuid (ArchiCAD's export key)."""
    try:
        from StingBridge.sync.engine import _ifc_global_id_from_acguid
    except Exception as e:  # noqa: BLE001 — archicad lib may be absent
        print(f"  SKIP engine import unavailable: {e}")
        return
    gid = _ifc_global_id_from_acguid("{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}")
    # ifcopenshell present → 22-char compressed IfcGuid; absent → raw fallback
    try:
        import ifcopenshell.guid  # noqa: F401
        assert len(gid) == 22, f"expected 22-char IfcGuid, got {gid!r}"
    except ImportError:
        assert gid  # fallback returns the raw guid, still non-empty
    # malformed GUID → raw fallback, never raises
    assert _ifc_global_id_from_acguid("not-a-guid") == "not-a-guid"


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
