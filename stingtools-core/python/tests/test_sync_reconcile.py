"""Tests for the pull + reconcile engine (plan §1.4.2 / §1.4.3).

These pin the conflict policy. It replaces StingBridge's heuristic, which
compared the remote timestamp to *now* rather than to the local edit time — so a
local change made five seconds ago lost to a remote change made fifty-nine
seconds ago. Getting this wrong silently overwrites users' work, so each rule
has its own test and its own name.
"""
from __future__ import annotations

import json
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path

import pytest

from stingtools_core.hosts.adapter import ChangeDelta
from stingtools_core.sync import (
    CursorStore, PullClient, ReconcileEngine, parse_utc, tokens_equal,
)

NOW = datetime(2026, 7, 20, 12, 0, 0, tzinfo=timezone.utc)


def iso(dt: datetime) -> str:
    return dt.isoformat().replace("+00:00", "Z")


def tokens(**over) -> dict:
    base = {"disc": "M", "loc": "BLD1", "zone": "Z01", "lvl": "L02",
            "sys": "HVAC", "func": "SUP", "prod": "AHU", "seq": "0001"}
    base.update(over)
    return base


def delta(gid="g1", ts=NOW, **over) -> ChangeDelta:
    return ChangeDelta(kind="tag", global_id=gid, payload=tokens(**over),
                       last_modified_utc=iso(ts) if ts else None)


def local(ts=NOW, **over) -> dict:
    return {"tokens": tokens(**over),
            "modified_utc": iso(ts) if ts else None,
            "element": "host-handle"}


class _Adapter:
    """Records what it was asked to apply."""

    def __init__(self, fail=False, raise_on=None):
        self.applied: list[ChangeDelta] = []
        self._fail = fail
        self._raise_on = raise_on

    def apply_remote_change(self, d: ChangeDelta) -> bool:
        if self._raise_on and d.global_id == self._raise_on:
            raise RuntimeError("host exploded")
        self.applied.append(d)
        return not self._fail


# ── timestamp parsing ────────────────────────────────────────────────────────

def test_parse_utc_handles_the_shapes_the_server_emits():
    assert parse_utc("2026-07-20T12:00:00Z") == NOW
    assert parse_utc("2026-07-20T12:00:00+00:00") == NOW
    # Naive is assumed UTC — reading it as local time would compare hours out.
    assert parse_utc("2026-07-20T12:00:00") == NOW
    assert parse_utc(NOW) == NOW


def test_parse_utc_rejects_junk_rather_than_guessing():
    assert parse_utc(None) is None
    assert parse_utc("") is None
    assert parse_utc("not a date") is None


def test_tokens_equal_ignores_non_tag_fields():
    a = tokens()
    b = dict(tokens(), categoryName="Walls", familyName="Basic")
    assert tokens_equal(a, b), "a metadata-only difference must not force a write"


def test_tokens_equal_detects_a_real_difference():
    assert not tokens_equal(tokens(), tokens(seq="0002"))


# ── rule 1: remote-only ──────────────────────────────────────────────────────

def test_remote_only_element_is_applied():
    a = _Adapter()
    r = ReconcileEngine(a).reconcile([delta()], {})
    assert r.applied == 1
    assert r.remote_only == 1
    assert len(a.applied) == 1


# ── rule 3: equal payloads ───────────────────────────────────────────────────

def test_identical_payloads_are_a_noop_even_when_remote_is_newer():
    # Applying a delta that changes nothing still costs a host write and an
    # undo entry.
    a = _Adapter()
    r = ReconcileEngine(a).reconcile(
        [delta(ts=NOW + timedelta(hours=1))], {"g1": local(ts=NOW)})
    assert r.applied == 0
    assert r.skipped_equal == 1
    assert a.applied == []
    assert r.conflicts == []


# ── rule 4: newer wins, both directions ──────────────────────────────────────

def test_newer_remote_wins():
    a = _Adapter()
    r = ReconcileEngine(a).reconcile(
        [delta(ts=NOW + timedelta(minutes=5), seq="0009")],
        {"g1": local(ts=NOW)})
    assert r.applied == 1
    assert a.applied[0].payload["seq"] == "0009"
    assert len(r.conflicts) == 1


def test_newer_local_wins_and_nothing_is_written():
    a = _Adapter()
    r = ReconcileEngine(a).reconcile(
        [delta(ts=NOW, seq="0009")],
        {"g1": local(ts=NOW + timedelta(minutes=5))})
    assert r.applied == 0
    assert r.kept_local == 1
    assert a.applied == []
    assert len(r.conflicts) == 1, "a losing remote edit must still be surfaced"


def test_a_local_edit_seconds_old_beats_a_remote_edit_a_minute_old():
    # The exact case the old 60-second heuristic got backwards.
    a = _Adapter()
    r = ReconcileEngine(a).reconcile(
        [delta(ts=NOW - timedelta(seconds=59), seq="0009")],
        {"g1": local(ts=NOW - timedelta(seconds=5))})
    assert r.kept_local == 1
    assert a.applied == []


# ── rule 5: ties ─────────────────────────────────────────────────────────────

def test_equal_timestamps_prefer_local():
    a = _Adapter()
    r = ReconcileEngine(a).reconcile(
        [delta(ts=NOW, seq="0009")], {"g1": local(ts=NOW)})
    assert r.kept_local == 1
    assert a.applied == []
    assert r.conflicts[0].reason.startswith("timestamps equal")


def test_tie_resolution_is_deterministic():
    # Two hosts reconciling the same pair must reach the same answer.
    for _ in range(5):
        a = _Adapter()
        r = ReconcileEngine(a).reconcile(
            [delta(ts=NOW, seq="0009")], {"g1": local(ts=NOW)})
        assert r.kept_local == 1


# ── rule 6: missing timestamps ───────────────────────────────────────────────

def test_remote_without_a_timestamp_cannot_win():
    a = _Adapter()
    r = ReconcileEngine(a).reconcile(
        [delta(ts=None, seq="0009")], {"g1": local(ts=NOW)})
    assert r.kept_local == 1
    assert a.applied == []
    assert r.conflicts[0].reason == "remote has no usable timestamp"


def test_unparseable_remote_timestamp_cannot_win():
    a = _Adapter()
    d = ChangeDelta(kind="tag", global_id="g1", payload=tokens(seq="0009"),
                    last_modified_utc="whenever")
    r = ReconcileEngine(a).reconcile([d], {"g1": local(ts=NOW)})
    assert r.kept_local == 1


def test_local_without_a_timestamp_yields_to_an_ordered_remote_edit():
    # Common for an element the host has never written — not a real conflict.
    a = _Adapter()
    r = ReconcileEngine(a).reconcile(
        [delta(ts=NOW, seq="0009")], {"g1": local(ts=None)})
    assert r.applied == 1
    assert r.conflicts == []


# ── failures and non-tag kinds ───────────────────────────────────────────────

def test_adapter_failure_is_counted_not_swallowed():
    r = ReconcileEngine(_Adapter(fail=True)).reconcile([delta()], {})
    assert r.applied == 0
    assert r.failed == 1


def test_one_raising_element_does_not_end_the_pass():
    a = _Adapter(raise_on="bad")
    r = ReconcileEngine(a).reconcile(
        [delta(gid="bad"), delta(gid="good")], {})
    assert r.failed == 1
    assert r.applied == 1
    assert [d.global_id for d in a.applied] == ["good"]


def test_non_tag_kinds_are_counted_separately_not_treated_as_failures():
    a = _Adapter()
    r = ReconcileEngine(a).reconcile(
        [ChangeDelta(kind="issue", global_id="g1"),
         ChangeDelta(kind="bcf", global_id="g2")], {})
    assert r.unhandled_kind == 2
    assert r.failed == 0
    assert a.applied == []


# ── conflict reporting (§1.4.3) ──────────────────────────────────────────────

def test_conflicts_are_reported_to_the_callback():
    seen = []
    e = ReconcileEngine(_Adapter(), on_conflict=seen.append)
    e.reconcile([delta(ts=NOW + timedelta(minutes=1), seq="0009")],
                {"g1": local(ts=NOW)})
    assert len(seen) == 1
    assert seen[0].global_id == "g1"
    assert seen[0].winner == "remote"


def test_a_raising_conflict_reporter_does_not_break_sync():
    def boom(_):
        raise RuntimeError("reporter down")
    r = ReconcileEngine(_Adapter(), on_conflict=boom).reconcile(
        [delta(ts=NOW + timedelta(minutes=1), seq="0009")], {"g1": local(ts=NOW)})
    assert r.applied == 1


def test_every_decision_is_recorded_with_a_reason():
    r = ReconcileEngine(_Adapter()).reconcile(
        [delta(gid="a"), delta(gid="b", ts=NOW, seq="0009")],
        {"b": local(ts=NOW + timedelta(minutes=1))})
    assert len(r.resolutions) == 2
    assert all(res.reason for res in r.resolutions)


# ── pull client ──────────────────────────────────────────────────────────────

class _Resp:
    def __init__(self, status=200, body=None):
        self.status_code = status
        self._body = body or {}

    def json(self):
        return self._body


class _Http:
    def __init__(self, pages):
        self._pages = list(pages)
        self.requests = []

    def get(self, url, params=None):
        self.requests.append(dict(params or {}))
        return self._pages.pop(0) if self._pages else _Resp(200, {"items": [], "hasMore": False})


def _page(items, next_cursor, has_more):
    return _Resp(200, {"items": items, "nextCursor": next_cursor, "hasMore": has_more})


def _item(gid, ts=NOW):
    return {"kind": "tag", "globalId": gid, "lastModifiedUtc": iso(ts),
            "payload": tokens()}


def test_pull_drains_every_page_and_follows_the_cursor():
    http = _Http([
        _page([_item("a"), _item("b")], "c1", True),
        _page([_item("c")], "c2", False),
    ])
    client = PullClient(http, "http://x", "p1")
    got = list(client.drain())

    assert [d.global_id for d in got] == ["a", "b", "c"]
    assert client.last_cursor == "c2"
    # Second request resumed from the first page's cursor.
    assert http.requests[1]["since"] == "c1"


def test_first_pull_sends_no_cursor():
    http = _Http([_page([], None, False)])
    list(PullClient(http, "http://x", "p1").drain())
    assert "since" not in http.requests[0]


def test_a_404_disables_pull_without_failing_the_run():
    # An older server. Degrading to push-only beats refusing to sync at all.
    http = _Http([_Resp(404)])
    client = PullClient(http, "http://x", "p1")
    assert list(client.drain("c0")) == []
    assert client.last_cursor == "c0", "cursor must not advance past an error"


def test_a_server_error_leaves_the_cursor_untouched():
    http = _Http([_Resp(500)])
    client = PullClient(http, "http://x", "p1")
    assert list(client.drain("c0")) == []
    assert client.last_cursor == "c0"


def test_items_without_a_global_id_are_dropped():
    # Nothing to key on, so it cannot be resolved cross-host.
    http = _Http([_page([{"kind": "tag", "payload": {}}, _item("a")], "c1", False)])
    got = list(PullClient(http, "http://x", "p1").drain())
    assert [d.global_id for d in got] == ["a"]


def test_has_more_with_an_empty_page_does_not_spin():
    http = _Http([_page([], "c1", True)] * 10)
    got = list(PullClient(http, "http://x", "p1").drain())
    assert got == []
    assert len(http.requests) == 1


# ── cursor persistence ───────────────────────────────────────────────────────

def test_cursor_round_trips():
    with tempfile.TemporaryDirectory() as d:
        store = CursorStore(Path(d) / "cursors.json")
        assert store.read("p1", "archicad") is None
        store.write("p1", "archicad", "c42")
        assert store.read("p1", "archicad") == "c42"


def test_cursors_are_scoped_per_project_and_host():
    with tempfile.TemporaryDirectory() as d:
        store = CursorStore(Path(d) / "cursors.json")
        store.write("p1", "archicad", "a")
        store.write("p1", "revit", "b")
        store.write("p2", "archicad", "c")
        assert store.read("p1", "archicad") == "a"
        assert store.read("p1", "revit") == "b"
        assert store.read("p2", "archicad") == "c"


def test_writing_an_empty_cursor_is_ignored():
    with tempfile.TemporaryDirectory() as d:
        store = CursorStore(Path(d) / "cursors.json")
        store.write("p1", "archicad", "c1")
        store.write("p1", "archicad", None)
        assert store.read("p1", "archicad") == "c1"


def test_a_corrupt_cursor_file_costs_a_backfill_not_a_crash():
    with tempfile.TemporaryDirectory() as d:
        p = Path(d) / "cursors.json"
        p.write_text("{not json", encoding="utf-8")
        store = CursorStore(p)
        assert store.read("p1", "archicad") is None
        store.write("p1", "archicad", "c1")   # recovers by rewriting
        assert store.read("p1", "archicad") == "c1"
