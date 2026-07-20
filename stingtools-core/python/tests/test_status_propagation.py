"""Status-only remote changes must converge — through a REAL adapter (F4).

WHY THE EXISTING TEST WAS NOT ENOUGH
------------------------------------
`test_sync_reconcile.py::test_status_only_remote_change_is_applied` proves the
reconcile ENGINE treats a status-only delta as a change. It runs against a fake
adapter that records what it was asked to apply and returns True, so it says
nothing about whether any real adapter can actually persist status.

None could. `IfcFileHostAdapter.apply_remote_change` wrote `Tag`'s eight
segments and dropped `status` on the floor, so:

    pull  -> reconcile sees a status difference -> apply -> "success"
    read back -> status unchanged
    pull  -> reconcile sees the SAME difference -> apply -> ...

…forever, once SB-5a wires the loop. Latent, not yet firing, and invisible to a
fake-adapter test.

This test closes the loop against a real ifcopenshell model: apply, then
RE-READ local state from the file and reconcile again. The second pass must be
a no-op. It reads the pset back with plain ifcopenshell rather than any helper
added by this change, so it runs unmodified against the pre-fix code — that is
what makes the red-then-green claim checkable.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

_ROOT = Path(__file__).resolve().parents[1]
if str(_ROOT) not in sys.path:
    sys.path.insert(0, str(_ROOT))

ifcopenshell = pytest.importorskip(
    "ifcopenshell", reason="a real host adapter needs a real IFC model"
)

from stingtools_core import ChangeDelta, IfcFileHostAdapter  # noqa: E402
from stingtools_core.sync import ReconcileEngine  # noqa: E402

STING_PSET = "Pset_StingTags"
NOW = "2026-07-20T12:00:00Z"
LATER = "2026-07-20T13:00:00Z"


def _model_with_one_wall():
    """A minimal IFC4 model holding a single wall we can tag."""
    model = ifcopenshell.file(schema="IFC4")
    wall = model.create_entity("IfcWall", GlobalId=ifcopenshell.guid.new(), Name="Wall 001")
    return model, wall


def _tokens(**over) -> dict:
    base = {
        "disc": "M", "loc": "BLD1", "zone": "Z01", "lvl": "L02",
        "sys": "HVAC", "func": "SUP", "prod": "AHU", "seq": "0001",
        "status": "WIP",
    }
    base.update(over)
    return base


def _payload(tokens: dict) -> dict:
    """EXACTLY the change feed's wire shape — see ChangesController.cs:105-108.

    Short lower-case keys, not the PSET's capitalised ones. Using the real shape
    is what exposed that `apply_remote_change` was parsing feed payloads with
    `Tag.from_pset` and writing eight UNKNOWN sentinels.
    """
    return dict(tokens)


def _read_local_tokens(element) -> dict:
    """Read the token dict straight off the model with plain ifcopenshell.

    Deliberately does NOT use `IfcFileHostAdapter.read_tokens` (added by this
    change): a test that depends on the new helper cannot be run against the old
    code, and the red-then-green proof would be unfalsifiable. It also cannot
    inherit a bug from the helper it is meant to be checking.
    """
    import ifcopenshell.util.element as ue

    pset = ue.get_pset(element, STING_PSET) or {}
    return {
        "disc": str(pset.get("Discipline") or ""),
        "loc": str(pset.get("Location") or ""),
        "zone": str(pset.get("Zone") or ""),
        "lvl": str(pset.get("Level") or ""),
        "sys": str(pset.get("System") or ""),
        "func": str(pset.get("Function") or ""),
        "prod": str(pset.get("Product") or ""),
        "seq": str(pset.get("Sequence") or ""),
        "status": str(pset.get("Status") or ""),
    }


def _reconcile_once(adapter, element, remote_tokens: dict, remote_ts: str):
    """One pull→reconcile pass, with local state RE-READ from the model."""
    local = _read_local_tokens(element)
    delta = ChangeDelta(
        kind="tag",
        global_id=element.GlobalId,
        payload=_payload(remote_tokens),
        last_modified_utc=remote_ts,
    )
    result = ReconcileEngine(adapter).reconcile(
        [delta],
        {element.GlobalId: {"tokens": local, "modified_utc": NOW, "element": element}},
    )
    return result


def test_status_only_remote_change_converges_through_a_real_adapter():
    """Apply a status-only change, re-read, reconcile again → NO-OP."""
    model, wall = _model_with_one_wall()
    adapter = IfcFileHostAdapter(model)

    # Seed the element at WIP so the only difference below is `status`.
    adapter.apply_remote_change(ChangeDelta(
        kind="tag", global_id=wall.GlobalId,
        payload=_payload(_tokens(status="WIP")), last_modified_utc=NOW))

    seeded = _read_local_tokens(wall)
    assert seeded["disc"] == "M", "seeding failed — the rest of the test would be vacuous"

    remote = _tokens(status="Shared")

    # Pass 1 — the status-only change is real work and must be applied.
    first = _reconcile_once(adapter, wall, remote, LATER)
    assert first.applied == 1, f"status-only change was not applied: {first.summary()}"

    # It actually reached the model, not just the adapter's return value.
    assert _read_local_tokens(wall)["status"] == "Shared", (
        "apply_remote_change reported success but did not persist status"
    )

    # Pass 2 — the SAME delta arrives again (the feed replays until the cursor
    # moves past it). With local state re-read from the file, there is now
    # nothing to do.
    second = _reconcile_once(adapter, wall, remote, LATER)
    assert second.applied == 0, (
        "second pass re-applied a change that was already persisted — this delta "
        f"would re-apply on every pull forever: {second.summary()}"
    )
    assert second.skipped_equal == 1, f"expected a no-op, got {second.summary()}"


def test_tag_segments_still_round_trip():
    """Guard: adding status must not disturb the eight segments."""
    model, wall = _model_with_one_wall()
    adapter = IfcFileHostAdapter(model)

    tokens = _tokens(status="S2")
    adapter.apply_remote_change(ChangeDelta(
        kind="tag", global_id=wall.GlobalId,
        payload=_payload(tokens), last_modified_utc=NOW))

    assert _read_local_tokens(wall) == tokens

    # And the Tag object itself is still exactly eight segments — status must
    # never leak into the grammar, or every rendered tag string breaks.
    tag = adapter.read_tag(wall)
    assert len(tag.segments) == 8
    assert tag.to_full_tag() == "M-BLD1-Z01-L02-HVAC-SUP-AHU-0001"


def test_read_tokens_helper_matches_an_independent_read():
    """`read_tokens` is what callers will build local_index from — check it agrees."""
    model, wall = _model_with_one_wall()
    adapter = IfcFileHostAdapter(model)

    tokens = _tokens(status="S4")
    adapter.apply_remote_change(ChangeDelta(
        kind="tag", global_id=wall.GlobalId,
        payload=_payload(tokens), last_modified_utc=NOW))

    assert adapter.read_tokens(wall) == _read_local_tokens(wall)
