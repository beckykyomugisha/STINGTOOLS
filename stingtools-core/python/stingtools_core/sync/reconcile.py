"""Reconciliation engine — last-writer-wins with stated tie rules (plan §1.4.2/§1.4.3).

Replaces StingBridge's "if the server row was modified in the last 60 seconds,
the server wins" heuristic, which was not last-writer-wins at all: it compared
the remote timestamp to *now* rather than to the local edit time, so a local
change made five seconds ago lost to a remote change made fifty-nine seconds
ago. It also could not see an element the local model did not already hold.

The rules, stated once so every host behaves identically:

1. **Remote-only** (no local element) → apply. Nothing local to lose.
2. **Local-only** (no remote delta) → keep local; the push half sends it.
3. **Equal payloads** → no-op, whatever the timestamps say. Applying a delta
   that changes nothing still costs a host write and, in Blender/Revit, an undo
   entry.

Then reconciliation is **per token**, not per element (see `_resolve`):

4. **Blank vs set** — a token that is empty/missing on one side and set on the
   other is **adopted from the set side, regardless of timestamps**, and is NOT
   a conflict. This is the asymmetry that matters most for `seq`: a freshly
   re-exported IFC carries no STING pset, so *every* token is blank locally.
   Under a per-element LWW the newer export would "win" and the server's known
   SEQs would be re-minted from scratch — identity churn and counter burn on
   every re-export. Filling a blank from the other host is never a loss, so it
   never waits on a clock. Applied uniformly to all `TOKEN_KEYS`.
5. **Set vs set, differing** — the only genuine conflict — resolves by
   last-writer-wins on the element's timestamps:
   - timestamps differ → newer wins;
   - timestamps equal → higher **content digest** wins (a pure function of the
     payload, so both hosts pick the *same* winner and converge on the first
     pass — preferring "local" here never converges);
   - remote timestamp missing/unparseable → local wins (an unordered remote edit
     cannot be shown newer, and clobbering the user on a missing field is the
     worst failure);
   - local timestamp missing → remote wins, and this is NOT a conflict (an
     element the host has never written).

The result is a **merge**: blanks are filled from whichever side has the value,
and only truly contested tokens go to LWW. Every genuine conflict is *reported*
whether or not it was applied, so §1.4.3's "surface the loser rather than
clobbering silently" has something to surface; a blank-fill is not one.
"""
from __future__ import annotations

import hashlib
import logging
from dataclasses import dataclass, field, replace
from datetime import datetime, timezone
from typing import Any, Callable, Iterable, Optional

from ..hosts.adapter import ChangeDelta

log = logging.getLogger(__name__)

#: Token keys compared when deciding whether a delta actually changes anything.
#:
#: `status` is included: the change feed already carries it
#: (`ChangesController.cs:108`), and without it a status-only remote edit — the
#: element moved WIP → Shared with no tag change — compares equal and is silently
#: dropped. `category` and `family` remain excluded on purpose: a delta whose
#: family name differs but whose tag is identical must not trigger a write.
TOKEN_KEYS = ("disc", "loc", "zone", "lvl", "sys", "func", "prod", "seq", "status")


def parse_utc(value: Any) -> Optional[datetime]:
    """Parse an ISO-8601 timestamp to an aware UTC datetime, or None.

    Naive inputs are assumed UTC — the server emits UTC and a naive local
    reading would otherwise compare hours out.
    """
    if value is None:
        return None
    if isinstance(value, datetime):
        dt = value
    else:
        text = str(value).strip()
        if not text:
            return None
        if text.endswith("Z"):
            text = text[:-1] + "+00:00"
        try:
            dt = datetime.fromisoformat(text)
        except ValueError:
            return None
    return dt.replace(tzinfo=timezone.utc) if dt.tzinfo is None else dt.astimezone(timezone.utc)


def tokens_equal(a: dict, b: dict) -> bool:
    """True when two token payloads agree on every tag segment.

    Compares only TOKEN_KEYS: a delta whose category or family name differs but
    whose tag is identical should not trigger a write.
    """
    return all(str(a.get(k) or "") == str(b.get(k) or "") for k in TOKEN_KEYS)


def token_digest(tokens: dict) -> str:
    """A stable content hash over TOKEN_KEYS — the equal-timestamp tiebreak.

    Must be a pure function of the DATA and nothing else. Anything that depends
    on which side is asking (host name, "local", arrival order) gives the two
    hosts opposite answers and they never converge.
    """
    canonical = "\x1f".join(str(tokens.get(k) or "") for k in TOKEN_KEYS)
    return hashlib.sha256(canonical.encode("utf-8")).hexdigest()


@dataclass
class Resolution:
    """What reconciliation decided for one GlobalId, and why."""

    global_id: str
    winner: str                      # "remote" | "local" | "none"
    reason: str
    applied: bool = False
    conflict: bool = False           # both sides changed
    delta: Optional[ChangeDelta] = None


@dataclass
class ReconcileResult:
    applied: int = 0
    skipped_equal: int = 0
    kept_local: int = 0
    remote_only: int = 0
    failed: int = 0
    unhandled_kind: int = 0
    conflicts: list[Resolution] = field(default_factory=list)
    resolutions: list[Resolution] = field(default_factory=list)

    def summary(self) -> str:
        return (f"{self.applied} applied, {self.kept_local} kept local, "
                f"{self.skipped_equal} unchanged, {self.remote_only} new from remote, "
                f"{self.failed} failed, {len(self.conflicts)} conflict(s)")


class ReconcileEngine:
    """Decides remote-vs-local per GlobalId and drives the host adapter.

    ``local_index`` maps GlobalId → ``{"tokens": {...}, "modified_utc": ...,
    "element": <host handle>}``. Building it is the host's job — only the host
    knows how to read its own model.
    """

    def __init__(self, adapter: Any, on_conflict: Optional[Callable[[Resolution], None]] = None) -> None:
        self._adapter = adapter
        self._on_conflict = on_conflict

    def reconcile(self, deltas: Iterable[ChangeDelta], local_index: dict[str, dict]) -> ReconcileResult:
        result = ReconcileResult()

        for delta in deltas:
            if delta.kind != "tag":
                # Issues/BCF/clash flow to hosts that can show them; a host that
                # cannot is not a failure, just not a participant for that kind.
                result.unhandled_kind += 1
                continue

            gid = delta.global_id
            local = local_index.get(gid)
            resolution = self._resolve(delta, local)

            if resolution.winner == "remote":
                # Apply what reconciliation DECIDED — the merged payload (blanks
                # filled per token, contested tokens resolved) — not the raw
                # remote delta, which may carry blanks this host had already
                # filled locally.
                ok = self._apply(resolution.delta or delta)
                resolution.applied = ok
                if ok:
                    result.applied += 1
                    if local is None:
                        result.remote_only += 1
                else:
                    result.failed += 1
            elif resolution.winner == "local":
                result.kept_local += 1
            else:
                result.skipped_equal += 1

            result.resolutions.append(resolution)
            if resolution.conflict:
                result.conflicts.append(resolution)
                if self._on_conflict:
                    try:
                        self._on_conflict(resolution)
                    except Exception as e:  # noqa: BLE001 — reporting must not break sync
                        log.warning("Conflict reporter raised for %s: %s", gid, e)

        return result

    # ── decision ─────────────────────────────────────────────────────────────

    def _resolve(self, delta: ChangeDelta, local: Optional[dict]) -> Resolution:
        gid = delta.global_id

        # Rule 1 — nothing local to lose.
        if local is None:
            return Resolution(gid, "remote", "no local element", delta=delta)

        local_tokens = local.get("tokens") or {}
        remote_tokens = delta.payload or {}

        # Rule 3 — identical payloads are a no-op regardless of timestamps.
        if tokens_equal(local_tokens, remote_tokens):
            return Resolution(gid, "none", "payloads identical", delta=delta)

        # ── Per-token classification (rules 4 & 5) ───────────────────────────
        # A token blank on one side and set on the other is ADOPTED from the set
        # side regardless of timestamps — never a conflict (rule 4). Only tokens
        # set-and-differing on both sides are genuinely contested (rule 5).
        adopt_remote, adopt_local, contested = [], [], []
        for k in TOKEN_KEYS:
            lv, rv = str(local_tokens.get(k) or ""), str(remote_tokens.get(k) or "")
            if lv == rv:
                continue
            if not lv:
                adopt_remote.append(k)      # remote fills a local blank
            elif not rv:
                adopt_local.append(k)       # local fills a remote blank
            else:
                contested.append(k)         # a real value-vs-value disagreement

        # Contested tokens (if any) are decided by last-writer-wins on the
        # element timestamps. Blank-fills below are applied regardless.
        contested_winner, conflict, lww_reason = (
            self._lww(delta, local, local_tokens, remote_tokens)
            if contested else (None, False, None)
        )

        # Build the merged winning token map: equal → that value; blank-vs-set →
        # the set value; contested → the LWW winner's value.
        merged = dict(remote_tokens)
        for k in TOKEN_KEYS:
            lv, rv = local_tokens.get(k), remote_tokens.get(k)
            lvs, rvs = str(lv or ""), str(rv or "")
            if lvs == rvs or not lvs:
                merged[k] = rv                       # equal, or adopt remote
            elif not rvs:
                merged[k] = lv                       # adopt local into the blank
            else:
                merged[k] = rv if contested_winner == "remote" else lv

        reason = self._merge_reason(lww_reason, adopt_remote, adopt_local)

        # If the merge leaves every token at its local value, no write is needed
        # — the push half carries any local-only fills up to the server.
        writes = any(str(merged.get(k) or "") != str(local_tokens.get(k) or "")
                     for k in TOKEN_KEYS)
        if not writes:
            return Resolution(gid, "local", reason, conflict=conflict, delta=delta)

        return Resolution(gid, "remote", reason, conflict=conflict,
                          delta=replace(delta, payload=merged))

    @staticmethod
    def _merge_reason(lww_reason, adopt_remote, adopt_local) -> str:
        # Kept exact for the reasons the tests pin (e.g. "remote has no usable
        # timestamp"): when nothing was blank-filled, the reason IS the LWW
        # reason verbatim.
        bits = []
        if lww_reason:
            bits.append(lww_reason)
        if adopt_remote:
            bits.append(f"adopt remote for blank {','.join(adopt_remote)}")
        if adopt_local:
            bits.append(f"keep local for blank {','.join(adopt_local)}")
        return "; ".join(bits) or "no change"

    def _lww(self, delta: ChangeDelta, local: dict,
             local_tokens: dict, remote_tokens: dict) -> tuple[Optional[str], bool, str]:
        """Resolve genuinely-contested tokens. Returns (winner, conflict, reason).

        `winner` is "remote" or "local"; `conflict` is whether the loser must be
        surfaced. Preserved verbatim from the original element-level rules so the
        set-vs-set cases behave exactly as before.
        """
        remote_ts = parse_utc(delta.last_modified_utc)
        local_ts = parse_utc(local.get("modified_utc"))

        # An unordered remote edit cannot be shown to be newer.
        if remote_ts is None:
            return "local", True, "remote has no usable timestamp"

        # No local timestamp: the local copy cannot defend itself, so the ordered
        # remote edit wins. Common for an element the host never wrote — not a
        # real conflict.
        if local_ts is None:
            return "remote", False, "no local timestamp"

        if remote_ts > local_ts:
            return "remote", True, f"remote newer ({remote_ts.isoformat()})"
        if local_ts > remote_ts:
            return "local", True, f"local newer ({local_ts.isoformat()})"

        # Equal timestamps — decide by content hash, NOT by "local". Preferring
        # local is the one choice that cannot converge; the digest is a pure
        # function of the payload so both hosts pick the SAME winner.
        local_digest = token_digest(local_tokens)
        remote_digest = token_digest(remote_tokens)
        if remote_digest > local_digest:
            return "remote", True, (f"timestamps equal - remote wins content tiebreak "
                                    f"({remote_digest[:8]} > {local_digest[:8]})")
        return "local", True, (f"timestamps equal - local wins content tiebreak "
                               f"({local_digest[:8]} > {remote_digest[:8]})")

    def _apply(self, delta: ChangeDelta) -> bool:
        try:
            return bool(self._adapter.apply_remote_change(delta))
        except Exception as e:  # noqa: BLE001 — one bad element must not end the pass
            log.warning("apply_remote_change failed for %s: %s", delta.global_id, e)
            return False
