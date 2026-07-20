"""Tests for SEQ minting (SB-2).

The property that matters: two runs must never mint the same number for the
same counter key, and re-running over already-numbered elements must be a
no-op. Everything else — batching, key format, graceful degrade — exists to
serve that.

Run from the repo root:  python StingBridge/tests/test_seq_minting.py
(or via pytest).
"""
from __future__ import annotations

import sys
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.sync.seq_minter import (  # noqa: E402
    assign_sequences, build_seq_key, format_seq, SEQ_PAD,
)


class _FakeCounterServer:
    """Stands in for POST /api/projects/{id}/seq/reserve.

    Holds a high-water mark per key and hands out disjoint blocks, which is the
    contract the real endpoint implements with INSERT … ON CONFLICT … RETURNING.
    """

    def __init__(self, *, fail=False, empty=False, drop_keys=()):
        self.counters: dict[str, int] = {}
        self.calls: list[dict] = []
        self._fail = fail
        self._empty = empty
        self._drop = set(drop_keys)
        self.project_id = "proj-1"

    def reserve_seq(self, reservations):
        self.calls.append(dict(reservations))
        if self._fail:
            raise RuntimeError("server exploded")
        if self._empty:
            return {}
        out = {}
        for key, count in reservations.items():
            if key in self._drop:
                continue
            start = self.counters.get(key, 0) + 1
            end = start + count - 1
            self.counters[key] = end
            out[key] = {"start": start, "end": end, "count": count}
        return out


def _tok(disc="M", sys="HVAC", lvl="L01", seq="", **kw):
    d = {"disc": disc, "sys": sys, "lvl": lvl, "seq": seq}
    d.update(kw)
    return d


# ── key format (must match SeqAssigner.BuildSeqKey) ──────────────────────────

def test_default_key_shape():
    assert build_seq_key("M", "HVAC", "L01") == "M_HVAC_L01"


def test_key_with_zone():
    assert build_seq_key("M", "HVAC", "L01", zone="Z02", include_zone=True) \
        == "M_Z02_HVAC_L01"


def test_key_with_loc():
    assert build_seq_key("M", "HVAC", "L01", loc="BLD2", include_loc=True) \
        == "M_BLD2_HVAC_L01"


def test_key_with_loc_and_zone():
    assert build_seq_key("M", "HVAC", "L01", zone="Z02", loc="BLD2",
                         include_zone=True, include_loc=True) \
        == "M_BLD2_Z02_HVAC_L01"


def test_placeholders_normalise_like_the_plugin():
    # Untagged elements must land in the same bucket as their Revit
    # counterparts rather than opening a private counter.
    assert build_seq_key("", "", "") == "A_GEN_L00"
    assert build_seq_key("M", "HVAC", "XX") == "M_HVAC_L00"
    assert build_seq_key("M", "HVAC", "L01", zone="ZZ", include_zone=True) \
        == "M_Z01_HVAC_L01"
    assert build_seq_key("M", "HVAC", "L01", zone="XX", include_zone=True) \
        == "M_Z01_HVAC_L01"
    assert build_seq_key("M", "HVAC", "L01", loc="XX", include_loc=True) \
        == "M_BLD1_HVAC_L01"


def test_format_is_zero_padded_to_four():
    assert format_seq(1) == "0001"
    assert format_seq(42) == "0042"
    assert format_seq(9999) == "9999"
    assert SEQ_PAD == 4


# ── assignment ───────────────────────────────────────────────────────────────

def test_assigns_consecutive_numbers_within_a_key():
    srv = _FakeCounterServer()
    toks = [_tok(), _tok(), _tok()]
    assert assign_sequences(srv, toks) == 3
    assert [t["seq"] for t in toks] == ["0001", "0002", "0003"]


def test_separate_keys_number_independently():
    srv = _FakeCounterServer()
    toks = [_tok(disc="M"), _tok(disc="E"), _tok(disc="M")]
    assign_sequences(srv, toks)
    assert toks[0]["seq"] == "0001"   # M_HVAC_L01 #1
    assert toks[1]["seq"] == "0001"   # E_HVAC_L01 #1 — different counter
    assert toks[2]["seq"] == "0002"   # M_HVAC_L01 #2


def test_reservation_is_batched_into_one_call():
    srv = _FakeCounterServer()
    toks = [_tok() for _ in range(50)] + [_tok(disc="E") for _ in range(20)]
    assign_sequences(srv, toks)
    # One request for the whole run, carrying per-key counts.
    assert len(srv.calls) == 1
    assert srv.calls[0] == {"M_HVAC_L01": 50, "E_HVAC_L01": 20}


# ── the core guarantee ───────────────────────────────────────────────────────

def test_two_runs_never_mint_the_same_number():
    srv = _FakeCounterServer()
    run1 = [_tok() for _ in range(5)]
    run2 = [_tok() for _ in range(5)]

    assign_sequences(srv, run1)
    assign_sequences(srv, run2)

    seqs = [t["seq"] for t in run1] + [t["seq"] for t in run2]
    assert len(seqs) == len(set(seqs)), f"duplicate SEQ minted: {seqs}"
    assert seqs == ["0001", "0002", "0003", "0004", "0005",
                    "0006", "0007", "0008", "0009", "0010"]


def test_rerunning_over_numbered_elements_is_idempotent():
    srv = _FakeCounterServer()
    toks = [_tok(), _tok()]
    assign_sequences(srv, toks)
    before = [t["seq"] for t in toks]

    # Same elements, second pass — nothing to do.
    assert assign_sequences(srv, toks) == 0
    assert [t["seq"] for t in toks] == before
    # And the counter was not advanced by the no-op pass.
    assert srv.counters["M_HVAC_L01"] == 2
    assert len(srv.calls) == 1


def test_mixed_numbered_and_unnumbered_only_consumes_for_the_gaps():
    srv = _FakeCounterServer()
    toks = [_tok(seq="0007"), _tok(), _tok(seq="0008"), _tok()]
    assert assign_sequences(srv, toks) == 2
    assert srv.calls == [{"M_HVAC_L01": 2}]
    assert toks[0]["seq"] == "0007"   # untouched
    assert toks[2]["seq"] == "0008"   # untouched
    assert toks[1]["seq"] == "0001"
    assert toks[3]["seq"] == "0002"


def test_whitespace_only_seq_counts_as_unnumbered():
    srv = _FakeCounterServer()
    toks = [_tok(seq="   ")]
    assert assign_sequences(srv, toks) == 1
    assert toks[0]["seq"] == "0001"


# ── graceful degrade ─────────────────────────────────────────────────────────

def test_server_failure_leaves_tags_unnumbered_without_raising():
    srv = _FakeCounterServer(fail=True)
    toks = [_tok(), _tok()]
    try:
        assign_sequences(srv, toks)
    except RuntimeError:
        # assign_sequences lets the client's own error surface; the CALLERS
        # wrap it. What must not happen is a partially-numbered result.
        pass
    assert all(not t["seq"] for t in toks)


def test_empty_reservation_response_leaves_tags_unnumbered():
    srv = _FakeCounterServer(empty=True)
    toks = [_tok(), _tok()]
    assert assign_sequences(srv, toks) == 0
    assert all(not t["seq"] for t in toks)


def test_a_missing_key_in_the_response_is_not_invented():
    # If the server did not reserve a key, minting a number locally would be
    # exactly how two runs collide. Those elements must stay unnumbered.
    srv = _FakeCounterServer(drop_keys={"E_HVAC_L01"})
    toks = [_tok(disc="M"), _tok(disc="E")]
    assert assign_sequences(srv, toks) == 1
    assert toks[0]["seq"] == "0001"
    assert toks[1]["seq"] == ""


def test_nothing_to_do_makes_no_request():
    srv = _FakeCounterServer()
    assert assign_sequences(srv, []) == 0
    assert assign_sequences(srv, [_tok(seq="0001")]) == 0
    assert srv.calls == []


def test_overflow_past_the_pad_stops_rather_than_widening_the_tag():
    # A wider tag would break every downstream schedule, so the plugin treats
    # this as overflow. Mirror that.
    srv = _FakeCounterServer()
    srv.counters["M_HVAC_L01"] = 9998
    toks = [_tok(), _tok(), _tok()]
    assign_sequences(srv, toks)
    assert toks[0]["seq"] == "9999"
    assert toks[1]["seq"] == ""      # 10000 exceeds pad-4
    assert toks[2]["seq"] == ""


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
