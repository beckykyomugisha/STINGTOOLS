#!/usr/bin/env python3
"""End-to-end: two-way sync — server change feed + reconcile engine.

Proves against a REAL server and REAL database that:

  1. a change made server-side appears in the cursor-paged feed;
  2. the cursor is exactly-once — resuming skips nothing and replays nothing,
     including across rows written in the same instant;
  3. a newer remote edit is applied to the host;
  4. a newer local edit is kept and the remote one is surfaced, not applied;
  5. a tie resolves deterministically AND converges from both perspectives;
  6. an identical payload is a no-op regardless of timestamps.

The unit tests prove the decision table against fakes. This proves the server
actually emits what the engine expects, and that cursor semantics survive real
timestamp precision — which no fake can establish.

Usage:

    export STING_E2E_URL=http://localhost:5099
    export STING_E2E_EMAIL=admin@planscape.demo
    export STING_E2E_PASSWORD=admin123
    export STING_E2E_PROJECT_ID=<a project uuid>
    python StingBridge/tests/e2e_pull_reconcile.py
"""
from __future__ import annotations

import os
import sys
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

import requests

_REPO_ROOT = Path(__file__).resolve().parents[2]
for p in (str(_REPO_ROOT), str(_REPO_ROOT / "stingtools-core" / "python")):
    if p not in sys.path:
        sys.path.insert(0, p)

from stingtools_core.sync import PullClient, ReconcileEngine  # noqa: E402
from stingtools_core.hosts.adapter import ChangeDelta  # noqa: E402

BASE = os.environ.get("STING_E2E_URL", "http://localhost:5099").rstrip("/")
EMAIL = os.environ.get("STING_E2E_EMAIL", "admin@planscape.demo")
PASSWORD = os.environ.get("STING_E2E_PASSWORD", "admin123")
PROJECT = os.environ.get("STING_E2E_PROJECT_ID", "")

_step = 0


def step(msg):
    global _step
    _step += 1
    print(f"\n[{_step}] {msg}")


def ok(msg): print(f"    OK  {msg}")


def fail(msg):
    print(f"    FAIL {msg}")
    sys.exit(1)


class _Session:
    """Minimal HTTP wrapper satisfying PullClient's `get(url, params=)`."""

    def __init__(self, token):
        self.s = requests.Session()
        self.s.headers.update({"Authorization": f"Bearer {token}",
                               "Content-Type": "application/json"})

    def get(self, url, params=None):
        return self.s.get(url, params=params, timeout=30)

    def post(self, url, json=None):
        return self.s.post(url, json=json, timeout=30)


class _RecordingAdapter:
    """Stands in for a host: records what reconcile asked it to write."""

    def __init__(self):
        self.applied = []

    def apply_remote_change(self, delta: ChangeDelta) -> bool:
        self.applied.append(delta)
        return True


def push(sess, uid, seq, when):
    """Write one element server-side via the tagsync path the plugin uses."""
    body = {
        "projectId": PROJECT,
        "elements": [{
            "revitElementId": abs(hash(uid)) % 10_000_000,
            "uniqueId": uid,
            "disc": "M", "loc": "BLD1", "zone": "Z01", "lvl": "L02",
            "sys": "HVAC", "func": "SUP", "prod": "AHU", "seq": seq,
            "tag1": f"M-BLD1-Z01-L02-HVAC-SUP-AHU-{seq}",
            "categoryName": "Mechanical Equipment",
            "familyName": "E2E AHU",
            "typeName": "E2E",
            "lastModifiedUtc": when.isoformat().replace("+00:00", "Z"),
        }],
    }
    r = sess.post(f"{BASE}/api/tagsync/sync", json=body)
    if r.status_code >= 400:
        fail(f"push failed {r.status_code}: {r.text[:300]}")
    return r


def main() -> int:
    if not PROJECT:
        fail("STING_E2E_PROJECT_ID is not set")

    login = requests.post(f"{BASE}/api/auth/login",
                          json={"email": EMAIL, "password": PASSWORD}, timeout=30)
    if login.status_code != 200:
        fail(f"login failed {login.status_code}: {login.text[:200]}")
    sess = _Session(login.json()["accessToken"])

    marker = uuid.uuid4().hex[:10].upper()
    print(f"Server : {BASE}\nProject: {PROJECT}\nMarker : {marker}")

    now = datetime.now(timezone.utc).replace(microsecond=0)

    # ── 1. baseline cursor ──────────────────────────────────────────────────
    step("Take a baseline cursor at the end of the feed")
    pull = PullClient(sess, BASE, PROJECT, page_limit=200)
    list(pull.drain())                     # drain to the end
    baseline = pull.last_cursor
    if not baseline:
        fail("no cursor returned - is the project empty and the feed working?")
    ok(f"baseline cursor: {baseline[:28]}…")

    # ── 2. server-side change shows up ──────────────────────────────────────
    step("Make a server-side change and pull it")
    gid_a = f"E2E{marker}A"
    push(sess, gid_a, "0001", now)

    pull2 = PullClient(sess, BASE, PROJECT)
    fresh = list(pull2.drain(baseline))
    seen = {d.global_id for d in fresh}
    if gid_a not in seen:
        fail(f"{gid_a} did not appear in the feed (got {len(fresh)} deltas)")
    ok(f"change appeared in the feed ({len(fresh)} delta(s) since baseline)")

    after_a = pull2.last_cursor

    # ── 3. cursor is exactly-once ───────────────────────────────────────────
    step("Re-pull from the advanced cursor - nothing should repeat")
    pull3 = PullClient(sess, BASE, PROJECT)
    repeat = list(pull3.drain(after_a))
    if any(d.global_id == gid_a for d in repeat):
        fail("cursor replayed an already-seen change")
    ok(f"no replay ({len(repeat)} delta(s), none of them the one just seen)")

    step("Write a burst in the same instant - the cursor must not skip any")
    # Strictly AFTER the cursor position. A timestamp-ordered feed cannot return
    # a row written with a timestamp at or below the cursor - that is inherent
    # to the design, not a defect, and a client that backdates its writes can
    # miss its own row. What must hold is that rows written forward of the
    # cursor are delivered exactly once even when they share an instant.
    burst_ts = (now + timedelta(minutes=1)).replace(microsecond=0)
    burst_ids = [f"E2E{marker}B{i}" for i in range(5)]
    for gid in burst_ids:
        push(sess, gid, "0002", burst_ts)   # identical timestamp for all five

    # Page size 2 forces the cursor to split the same-instant batch — the exact
    # case a bare-timestamp cursor gets wrong.
    pull4 = PullClient(sess, BASE, PROJECT, page_limit=2)
    got = [d.global_id for d in pull4.drain(after_a)]
    missing = [g for g in burst_ids if g not in got]
    if missing:
        fail(f"cursor skipped same-instant rows: {missing}")
    if len(got) != len(set(got)):
        fail(f"cursor replayed rows within the burst: {got}")
    ok(f"all 5 same-instant rows delivered exactly once across {len(got)//2 + 1} pages")

    after_burst = pull4.last_cursor

    # ── 4. reconcile: remote newer ⇒ applied ────────────────────────────────
    step("Remote newer than local -> applied to the host")
    push(sess, gid_a, "0009", now + timedelta(minutes=10))  # ahead of the burst
    pull5 = PullClient(sess, BASE, PROJECT)
    deltas = [d for d in pull5.drain(after_burst) if d.global_id == gid_a]
    if not deltas:
        fail("the updated element did not come through the feed")

    local_index = {gid_a: {
        "tokens": {"disc": "M", "loc": "BLD1", "zone": "Z01", "lvl": "L02",
                   "sys": "HVAC", "func": "SUP", "prod": "AHU", "seq": "0001"},
        "modified_utc": now.isoformat().replace("+00:00", "Z"),
    }}
    adapter = _RecordingAdapter()
    res = ReconcileEngine(adapter).reconcile(deltas, local_index)
    if res.applied != 1:
        fail(f"expected 1 applied, got {res.summary()}")
    if adapter.applied[0].payload.get("seq") != "0009":
        fail(f"wrong payload applied: {adapter.applied[0].payload}")
    ok(f"applied remote edit ({res.summary()})")

    # ── 5. reconcile: local newer ⇒ kept, remote surfaced ───────────────────
    step("Local newer than remote -> kept, and the loser is surfaced")
    local_newer = {gid_a: {
        "tokens": {"disc": "M", "loc": "BLD1", "zone": "Z01", "lvl": "L02",
                   "sys": "HVAC", "func": "SUP", "prod": "AHU", "seq": "0777"},
        "modified_utc": (now + timedelta(hours=2)).isoformat().replace("+00:00", "Z"),
    }}
    conflicts = []
    adapter2 = _RecordingAdapter()
    res2 = ReconcileEngine(adapter2, on_conflict=conflicts.append).reconcile(
        deltas, local_newer)
    if adapter2.applied:
        fail("a newer local edit was overwritten")
    if res2.kept_local != 1:
        fail(f"expected kept_local=1, got {res2.summary()}")
    if len(conflicts) != 1:
        fail("the losing remote edit was not surfaced")
    ok(f"local kept, conflict reported ({res2.summary()})")

    # ── 6. tie ⇒ content digest, and it CONVERGES ───────────────────────────
    step("Equal timestamps -> content tiebreak, converging from both sides")
    remote_ts = deltas[0].last_modified_utc
    local_tokens = {"disc": "M", "loc": "BLD1", "zone": "Z01", "lvl": "L02",
                    "sys": "HVAC", "func": "SUP", "prod": "AHU", "seq": "0555"}
    remote_tokens = dict(deltas[0].payload or {})

    def _settle(local_side, remote_side):
        """Run one host's reconcile and report the value it ends up holding."""
        adapter = _RecordingAdapter()
        d = ChangeDelta(kind="tag", global_id=gid_a, payload=remote_side,
                        last_modified_utc=remote_ts)
        r = ReconcileEngine(adapter).reconcile(
            [d], {gid_a: {"tokens": local_side, "modified_utc": remote_ts}})
        return (remote_side if adapter.applied else local_side), r

    # Stability: same inputs, same answer, every time.
    first, _ = _settle(local_tokens, remote_tokens)
    for _ in range(3):
        again, r = _settle(local_tokens, remote_tokens)
        if again != first:
            fail(f"tie-break is not stable: {r.summary()}")

    # Convergence: swap the perspectives. This is the assertion the previous
    # version of this E2E lacked — it re-ran ONE side three times and asserted
    # it kept saying "local", which the non-converging rule satisfied perfectly.
    host_a, _ = _settle(local_tokens, remote_tokens)
    host_b, _ = _settle(remote_tokens, local_tokens)
    if host_a != host_b:
        fail(f"hosts diverged and will never reconcile: "
             f"A={host_a.get('seq')} B={host_b.get('seq')}")
    ok(f"tie stable and convergent (both hosts settled on seq={host_a.get('seq')})")

    # ── 7. identical payload ⇒ no-op ────────────────────────────────────────
    step("Identical payload -> no host write at all")
    same = {gid_a: {
        "tokens": dict(deltas[0].payload),
        "modified_utc": (now - timedelta(days=1)).isoformat().replace("+00:00", "Z"),
    }}
    a = _RecordingAdapter()
    r = ReconcileEngine(a).reconcile(deltas, same)
    if a.applied:
        fail("wrote to the host despite nothing changing")
    if r.skipped_equal != 1:
        fail(f"expected skipped_equal=1, got {r.summary()}")
    ok("no write issued (an identical delta still costs an undo entry)")

    print("\nE2E PASSED - two-way sync resolves deterministically in both "
          "directions, and the cursor is exactly-once.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
