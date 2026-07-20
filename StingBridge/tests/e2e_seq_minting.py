#!/usr/bin/env python3
"""End-to-end: SEQ minting against a real server and real counters (SB-2).

The guarantee under test is the one that matters for cross-host parity:
**two runs must never mint the same number for the same counter key**, and
re-running over already-numbered elements must consume nothing.

The unit tests prove the client logic against a fake counter server. This
proves the server's INSERT … ON CONFLICT … RETURNING actually behaves that way
under real concurrency, which no fake can establish.

Usage:

    export STING_E2E_URL=http://localhost:5099
    export STING_E2E_EMAIL=admin@planscape.demo
    export STING_E2E_PASSWORD=admin123
    export STING_E2E_PROJECT_ID=<a project uuid>
    python StingBridge/tests/e2e_seq_minting.py
"""
from __future__ import annotations

import os
import sys
import uuid
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.planscape.client import PlanscapeClient  # noqa: E402
from StingBridge.sync.seq_minter import assign_sequences  # noqa: E402

BASE = os.environ.get("STING_E2E_URL", "http://localhost:5099").rstrip("/")
EMAIL = os.environ.get("STING_E2E_EMAIL", "admin@planscape.demo")
PASSWORD = os.environ.get("STING_E2E_PASSWORD", "admin123")
PROJECT = os.environ.get("STING_E2E_PROJECT_ID", "")

_step = 0


def step(msg: str) -> None:
    global _step
    _step += 1
    print(f"\n[{_step}] {msg}")


def ok(msg: str) -> None:
    print(f"    OK  {msg}")


def fail(msg: str) -> None:
    print(f"    FAIL {msg}")
    sys.exit(1)


def client() -> PlanscapeClient:
    c = PlanscapeClient(base_url=BASE, project_id=PROJECT)
    c.login(EMAIL, PASSWORD)
    return c


def tok(disc: str, sys_: str, lvl: str) -> dict:
    return {"disc": disc, "sys": sys_, "lvl": lvl, "seq": ""}


def main() -> int:
    if not PROJECT:
        fail("STING_E2E_PROJECT_ID is not set")

    # A unique discipline code per run keeps these counters off any real key.
    marker = uuid.uuid4().hex[:6].upper()
    disc = f"T{marker}"

    print(f"Server : {BASE}")
    print(f"Project: {PROJECT}")
    print(f"Counter: {disc}_HVAC_L01 (unique to this run)")

    ps = client()

    # ── 1. sequential runs must not collide ─────────────────────────────────
    step("Two sequential runs over different elements")
    run1 = [tok(disc, "HVAC", "L01") for _ in range(5)]
    run2 = [tok(disc, "HVAC", "L01") for _ in range(5)]
    assign_sequences(ps, run1)
    assign_sequences(ps, run2)

    seqs = [t["seq"] for t in run1] + [t["seq"] for t in run2]
    if "" in seqs:
        fail(f"some elements went unnumbered: {seqs}")
    if len(set(seqs)) != len(seqs):
        fail(f"DUPLICATE SEQ minted across runs: {seqs}")
    ok(f"10 distinct numbers: {seqs[0]}..{seqs[-1]}")

    if seqs != sorted(seqs):
        fail(f"numbers are not monotonic: {seqs}")
    ok("numbering is monotonic across runs")

    # ── 2. idempotence ──────────────────────────────────────────────────────
    step("Re-running over already-numbered elements consumes nothing")
    before = [t["seq"] for t in run1]
    minted = assign_sequences(ps, run1)
    if minted != 0:
        fail(f"expected 0 new assignments, got {minted}")
    if [t["seq"] for t in run1] != before:
        fail("re-run renumbered stable elements")
    ok("no numbers assigned, no values changed")

    # A fresh element must continue from where run2 stopped — proving the
    # no-op pass did not advance the counter either.
    probe = [tok(disc, "HVAC", "L01")]
    assign_sequences(ps, probe)
    expected = f"{int(seqs[-1]) + 1:04d}"
    if probe[0]["seq"] != expected:
        fail(f"counter drifted: expected {expected}, got {probe[0]['seq']}")
    ok(f"counter did not advance during the no-op run (next = {expected})")

    # ── 3. concurrency ──────────────────────────────────────────────────────
    step("Eight concurrent clients reserving from the same key")
    conc_disc = f"C{uuid.uuid4().hex[:6].upper()}"

    def worker(_n: int) -> list[str]:
        c = client()
        batch = [tok(conc_disc, "HVAC", "L01") for _ in range(10)]
        assign_sequences(c, batch)
        return [t["seq"] for t in batch]

    with ThreadPoolExecutor(max_workers=8) as pool:
        results = list(pool.map(worker, range(8)))

    flat = [s for r in results for s in r]
    if "" in flat:
        fail(f"{flat.count('')} elements went unnumbered under concurrency")
    if len(set(flat)) != len(flat):
        dupes = sorted({s for s in flat if flat.count(s) > 1})
        fail(f"DUPLICATE SEQ under concurrency: {dupes}")
    ok(f"80 concurrent reservations, {len(set(flat))} distinct numbers, zero duplicates")

    # Blocks must be contiguous per client — a client that got 10 numbers
    # should own a run of 10, not 10 scattered values.
    for i, r in enumerate(results):
        nums = sorted(int(x) for x in r)
        if nums != list(range(nums[0], nums[0] + len(nums))):
            fail(f"client {i} received a non-contiguous block: {nums}")
    ok("every client received one contiguous block")

    covered = sorted(int(x) for x in flat)
    if covered != list(range(1, 81)):
        fail(f"expected 1..80 with no gaps, got {covered[0]}..{covered[-1]} "
             f"({len(covered)} values)")
    ok("blocks tile 1..80 exactly - no gaps, no overlaps")

    # ── 4. multi-key batching ───────────────────────────────────────────────
    step("One call reserves for several keys at once")
    multi = ([tok(disc, "HVAC", "L02") for _ in range(3)] +
             [tok(disc, "ELEC", "L02") for _ in range(2)])
    assign_sequences(ps, multi)
    hvac = [t["seq"] for t in multi if t["sys"] == "HVAC"]
    elec = [t["seq"] for t in multi if t["sys"] == "ELEC"]
    if hvac != ["0001", "0002", "0003"] or elec != ["0001", "0002"]:
        fail(f"independent keys did not number independently: {hvac} / {elec}")
    ok("separate keys numbered independently from 0001")

    print("\nE2E PASSED - SEQ minting is collision-free across sequential "
          "and concurrent runs.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
