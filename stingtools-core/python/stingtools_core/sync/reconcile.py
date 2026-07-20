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
4. **Both changed, timestamps differ** → newer wins.
5. **Both changed, timestamps equal** → *local* wins. A tie is genuinely
   ambiguous, and preferring the copy the user is looking at is the choice that
   never surprises them. Deterministic, so two hosts reconciling the same pair
   reach the same answer.
6. **Both changed, remote timestamp missing/unparseable** → local wins. An
   unordered remote edit cannot be shown to be newer, and silently overwriting
   the user's work on the strength of a missing field is the worst failure here.

Every conflict is *reported* whether or not it was applied, so §1.4.3's
"surface the loser rather than clobbering silently" has something to surface.
"""
from __future__ import annotations

import logging
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Callable, Iterable, Optional

from ..hosts.adapter import ChangeDelta

log = logging.getLogger(__name__)

#: Token keys compared when deciding whether a delta actually changes anything.
TOKEN_KEYS = ("disc", "loc", "zone", "lvl", "sys", "func", "prod", "seq")


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
                ok = self._apply(delta)
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

        remote_ts = parse_utc(delta.last_modified_utc)
        local_ts = parse_utc(local.get("modified_utc"))

        # Rule 6 — an unordered remote edit cannot be shown to be newer.
        if remote_ts is None:
            return Resolution(gid, "local", "remote has no usable timestamp",
                              conflict=True, delta=delta)

        # No local timestamp: the local copy cannot defend itself, so the
        # ordered remote edit wins. This is the common case for an element the
        # host has never written — not a real conflict.
        if local_ts is None:
            return Resolution(gid, "remote", "no local timestamp", delta=delta)

        # Rules 4 and 5.
        if remote_ts > local_ts:
            return Resolution(gid, "remote", f"remote newer ({remote_ts.isoformat()})",
                              conflict=True, delta=delta)
        if local_ts > remote_ts:
            return Resolution(gid, "local", f"local newer ({local_ts.isoformat()})",
                              conflict=True, delta=delta)
        return Resolution(gid, "local", "timestamps equal - local preferred",
                          conflict=True, delta=delta)

    def _apply(self, delta: ChangeDelta) -> bool:
        try:
            return bool(self._adapter.apply_remote_change(delta))
        except Exception as e:  # noqa: BLE001 — one bad element must not end the pass
            log.warning("apply_remote_change failed for %s: %s", delta.global_id, e)
            return False
