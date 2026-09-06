"""§1.4.5 — the standing GlobalId-stability fixture.

> "one element, Revit→IFC→Bonsai, assert identical GUID. This is the linchpin of
> *every* cross-host join and *every* coordinate transform keyed on a model —
> guard it permanently."

The plan's Revit→Bonsai leg needs both applications. This is the leg that can
run in CI today and that guards the same invariant on the path we actually ship:
the IFC watcher's **round trip**. The watcher does not merely read an IFC — it
rewrites one, and the server keys `ExternalElementMapping` on
`(project, host, hostDocumentGuid, ifcGlobalId)`. If a GlobalId shifted when we
wrote tokens back, or if a re-drop presented the same element under a new key,
every element would silently fork into a second mapping row on each drop:
duplicate rows, doubled counts, and cross-host joins that resolve to nothing.

That failure is invisible from inside one host — which is exactly why it needs a
standing test rather than a manual check.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

_REPO_ROOT = Path(__file__).resolve().parents[2]
for _p in (str(_REPO_ROOT), str(_REPO_ROOT / "stingtools-core" / "python")):
    if _p not in sys.path:
        sys.path.insert(0, _p)

ifcopenshell = pytest.importorskip(
    "ifcopenshell", reason="GlobalId stability can only be proven with a real parser")

from StingBridge.tests.test_ifc_pull_reconcile import (  # noqa: E402
    _MINIMAL_IFC, _Cfg, _FakeServer,
)
from StingBridge.watch.ifc_watcher import IFCDropHandler  # noqa: E402


class _MappingServer(_FakeServer):
    """Counts distinct mapping rows the way the server would key them."""

    def __init__(self):
        super().__init__([])
        self.mapping_keys: set[tuple[str, str]] = set()
        self.ingest_calls = 0

    def ingest_ifc_data(self, elements, host=None, host_document_guid=None, **kw):
        self.ingest_calls += 1
        for el in elements:
            # The server's composite key, minus the constants (project + host).
            self.mapping_keys.add((str(host_document_guid), str(el["ifc_global_id"])))
        return super().ingest_ifc_data(elements, host=host,
                                       host_document_guid=host_document_guid, **kw)


def _global_ids(ifc_path: Path) -> set[str]:
    """Every product GlobalId in a file, read independently of the watcher."""
    model = ifcopenshell.open(str(ifc_path))
    return {el.GlobalId for el in model.by_type("IfcProduct") if el.GlobalId}


def test_redropping_the_same_ifc_creates_no_new_mapping_rows(tmp_path):
    """The headline invariant: drop twice, and the hub sees the same elements."""
    src = tmp_path / "stability.ifc"
    src.write_text(_MINIMAL_IFC, encoding="utf-8")

    server = _MappingServer()
    handler = IFCDropHandler(server, _Cfg(), cursor_dir=tmp_path)

    handler.process(str(src))
    after_first = set(server.mapping_keys)
    assert after_first, "nothing was ingested — the test would be vacuous"

    handler.process(str(src))

    new_rows = server.mapping_keys - after_first
    assert not new_rows, (
        f"re-dropping the same IFC minted {len(new_rows)} new mapping row(s): "
        f"{sorted(new_rows)[:5]}. Every drop would fork its elements again, "
        "doubling counts and breaking cross-host joins.")
    assert server.ingest_calls == 2, "the second drop did not actually push"


def test_write_back_preserves_every_globalid(tmp_path):
    """Stamping STING_TOKENS must not renumber the elements it stamps."""
    src = tmp_path / "roundtrip.ifc"
    src.write_text(_MINIMAL_IFC, encoding="utf-8")
    before = _global_ids(src)

    IFCDropHandler(_MappingServer(), _Cfg(), cursor_dir=tmp_path).process(str(src))

    written = src.with_name(src.stem + "_sting.ifc")
    assert written.exists(), "no write-back produced — nothing to compare"

    after = _global_ids(written)
    assert before <= after, (
        f"the write-back lost or rewrote GlobalId(s): {sorted(before - after)[:5]}")


def test_the_written_back_file_maps_to_the_same_elements(tmp_path):
    """Re-ingesting our own output must resolve to the elements it came from.

    A user who re-drops `<name>_sting.ifc` — a completely ordinary thing to do —
    must not create a parallel universe of elements. The *document* differs (a
    different path, so a different `hostDocumentGuid`, which is deliberate), but
    the GlobalIds keying each element must be identical.
    """
    src = tmp_path / "reingest.ifc"
    src.write_text(_MINIMAL_IFC, encoding="utf-8")

    server = _MappingServer()
    handler = IFCDropHandler(server, _Cfg(), cursor_dir=tmp_path)

    handler.process(str(src))
    first_gids = {gid for _doc, gid in server.mapping_keys}

    written = src.with_name(src.stem + "_sting.ifc")
    server.mapping_keys.clear()
    handler.process(str(written))
    second_gids = {gid for _doc, gid in server.mapping_keys}

    assert second_gids == first_gids, (
        "re-ingesting the written-back file presented different GlobalIds: "
        f"+{sorted(second_gids - first_gids)[:3]} "
        f"-{sorted(first_gids - second_gids)[:3]}")
