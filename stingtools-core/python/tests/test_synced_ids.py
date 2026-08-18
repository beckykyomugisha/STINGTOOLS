"""TRACK C3 — deletion detection for hosts that push full exports.

THE DEFECT
----------
An ingest is an UPSERT over the elements it carries, so an element that
disappears from the source is simply not mentioned — and "not mentioned" is
indistinguishable from "unchanged, and this was a partial push". Without a
record of what was pushed last time, a wall deleted in ArchiCAD stayed on the
server forever: visible in the viewer, answering clash and compliance queries,
and counted in every metric.

**Absence cannot mean deletion.** The only safe way to turn a full export into a
deletion signal is to diff it against what this same document pushed before.

THE PART THAT NEEDED A GUARD
----------------------------
The diff is only as trustworthy as the export it is diffing. A crashed or
filtered export yielding 3 elements instead of 30,000 looks, at this layer,
exactly like a coordinator deleting almost the whole model. One of those
readings is catastrophic and not recoverable by retrying; the other costs one
sync cycle. So a removal set covering most of the known elements is refused.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from stingtools_core.sync.synced_ids import (  # noqa: E402
    SyncedIdStore, diff_removals, should_send_removals,
)

PROJECT = "proj-1"
DOC_A = "doc-archicad"
DOC_B = "doc-revit"


# ── the store ─────────────────────────────────────────────────────────────────

def test_an_unknown_document_reads_as_empty(tmp_path):
    store = SyncedIdStore(tmp_path / "synced.json")
    assert store.read(PROJECT, DOC_A) == set()


def test_ids_round_trip(tmp_path):
    store = SyncedIdStore(tmp_path / "synced.json")
    store.write(PROJECT, DOC_A, ["g1", "g2", "g3"])
    assert store.read(PROJECT, DOC_A) == {"g1", "g2", "g3"}


def test_documents_are_isolated(tmp_path):
    # The load-bearing property. If the set were per-project, a full-export diff
    # from the ArchiCAD file would list every Revit-contributed GlobalId as
    # "removed" and wipe it.
    store = SyncedIdStore(tmp_path / "synced.json")
    store.write(PROJECT, DOC_A, ["a1", "a2"])
    store.write(PROJECT, DOC_B, ["r1"])

    assert store.read(PROJECT, DOC_A) == {"a1", "a2"}
    assert store.read(PROJECT, DOC_B) == {"r1"}


def test_projects_are_isolated(tmp_path):
    store = SyncedIdStore(tmp_path / "synced.json")
    store.write("p1", DOC_A, ["x"])
    store.write("p2", DOC_A, ["y"])
    assert store.read("p1", DOC_A) == {"x"}


def test_a_corrupt_store_reads_as_empty_rather_than_raising(tmp_path):
    # An unreadable store costs one missed deletion cycle; raising would fail
    # the whole sync, which is a strictly worse trade.
    path = tmp_path / "synced.json"
    path.write_text("{ not json", encoding="utf-8")
    assert SyncedIdStore(path).read(PROJECT, DOC_A) == set()


def test_blank_ids_are_not_stored(tmp_path):
    store = SyncedIdStore(tmp_path / "synced.json")
    store.write(PROJECT, DOC_A, ["g1", "", None])
    assert store.read(PROJECT, DOC_A) == {"g1"}


# ── the diff ──────────────────────────────────────────────────────────────────

def test_removals_are_what_vanished():
    assert diff_removals({"a", "b", "c"}, ["a", "c"]) == ["b"]


def test_new_elements_are_not_removals():
    assert diff_removals({"a"}, ["a", "b", "c"]) == []


def test_an_unchanged_export_removes_nothing():
    assert diff_removals({"a", "b"}, ["b", "a"]) == []


def test_removals_are_sorted_so_a_push_is_deterministic():
    # Two runs over an unchanged file must produce byte-identical payloads —
    # that is what makes a replay detectable rather than a new write.
    assert diff_removals({"c", "a", "b"}, []) == ["a", "b", "c"]


def test_a_first_ever_push_removes_nothing():
    # No baseline means no knowledge, not "everything is gone".
    assert diff_removals(set(), ["a", "b"]) == []


# ── the truncated-export guard ────────────────────────────────────────────────

def test_a_normal_deletion_is_allowed():
    previous = {f"g{i}" for i in range(100)}
    current = [f"g{i}" for i in range(100) if i != 7]

    removals, refusal = should_send_removals(previous, current)
    assert refusal is None
    assert removals == ["g7"]


def test_a_truncated_export_is_refused_not_applied():
    # 100 known elements, an export that yields 3. Applying this would tombstone
    # 97 elements on the strength of a broken export.
    previous = {f"g{i}" for i in range(100)}
    current = ["g0", "g1", "g2"]

    removals, refusal = should_send_removals(previous, current)
    assert removals == []
    assert refusal is not None
    assert "truncated export" in refusal
    # The message must carry the numbers, or an operator cannot judge it.
    assert "97" in refusal and "100" in refusal


def test_the_guard_can_be_disabled_deliberately():
    previous = {f"g{i}" for i in range(100)}
    removals, refusal = should_send_removals(previous, [], max_fraction=None)
    assert refusal is None
    assert len(removals) == 100


def test_the_guard_does_not_fire_on_a_first_push():
    # No baseline: nothing to be a fraction of, and nothing to remove.
    removals, refusal = should_send_removals(set(), ["a"])
    assert removals == [] and refusal is None


def test_deleting_exactly_half_is_allowed():
    # The threshold is "more than", so the boundary case is not refused —
    # pinned because an off-by-one here silently changes a product decision.
    previous = {"a", "b", "c", "d"}
    removals, refusal = should_send_removals(previous, ["a", "b"])
    assert refusal is None
    assert removals == ["c", "d"]
