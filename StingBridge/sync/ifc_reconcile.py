"""Wire the IFC drop path to pull → reconcile → push (ROADMAP SB-5a).

The multi-host engine (``PullClient`` / ``CursorStore`` / ``ReconcileEngine``)
landed in ``stingtools_core.sync`` and was verified two-way against real
Postgres, but nothing called it: the IFC watcher still did extract → push, so an
edit made in Revit was overwritten the next time someone re-exported the IFC.
This module is the adapter layer that closes that loop, and it is the first cut
that is testable today — the live-ArchiCAD path (SB-5b) needs a licence.

What it contributes over the raw engine:

* a **host adapter** for an in-memory IFC token map, so remote wins land in the
  same dicts the write-back and the push already read;
* a **local index** built from the file's own modification time (see
  ``build_local_index`` for why that is the honest choice here);
* a **per-document cursor**, not per-project (see ``cursor_host_key``);
* a **conflict sidecar**, so §1.4.3's loser is recorded somewhere an operator
  will actually find it.

Deliberately NOT here: raising a Planscape issue for the loser. That is server
contract work owned by another lane; §1.4.3 stays open. The sidecar is the
local, honest half — it means a conflict is never *silent*, which is the part
that was actually dangerous.
"""
from __future__ import annotations

import json
import logging
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Iterable, Optional

log = logging.getLogger(__name__)

_TIMEOUT = 60

#: Filename of the per-drop-folder cursor store.
CURSOR_FILENAME = ".sting_sync_cursor.json"

#: Suffix of the per-file conflict sidecar. Must also be listed in
#: ``hot_folder._companions`` or it is stranded in ``processing/`` forever.
CONFLICT_SUFFIX = ".conflicts.jsonl"


# ── HTTP shim ─────────────────────────────────────────────────────────────────

class ClientHttp:
    """Adapts ``PlanscapeClient`` to the ``get(url, params=)`` PullClient wants.

    Routes through the client's ``_send`` when available so an expired token is
    refreshed mid-drain. A watcher runs for days; the token does not.
    """

    def __init__(self, client: Any) -> None:
        self._client = client

    def get(self, url: str, params: Optional[dict] = None):
        send = getattr(self._client, "_send", None)
        if callable(send):
            return send("get", url, params=params, timeout=_TIMEOUT)
        session = getattr(self._client, "_session", None)
        if session is None:
            raise AttributeError("Planscape client exposes neither _send nor _session")
        return session.get(url, params=params, timeout=_TIMEOUT)


# ── Host adapter ──────────────────────────────────────────────────────────────

class IfcTokenApplyAdapter:
    """Applies a winning remote delta into the in-memory token map.

    The token dicts are mutated **in place** on purpose: the watcher holds the
    same objects in its ``rows`` list, so a remote win automatically reaches
    both the IFC write-back and the outgoing push without any second plumbing
    step. Two paths reading one object cannot drift apart.

    Only ``TOKEN_KEYS`` are copied. The feed also carries category/family, which
    describe the *authoring host's* element, not ours — writing them into our
    tokens would let a remote host rename our IFC types.
    """

    host_name = "ifc"

    def __init__(self, token_map: dict[str, dict]) -> None:
        self._tokens = token_map
        self.applied_gids: list[str] = []

    def apply_remote_change(self, delta) -> bool:
        from stingtools_core.sync import TOKEN_KEYS

        local = self._tokens.get(delta.global_id)
        if local is None:
            # Guarded, but should not happen: absent gids are partitioned out
            # before reconcile so they are reported rather than counted failed.
            return False

        payload = delta.payload or {}
        for key in TOKEN_KEYS:
            if key in payload:
                local[key] = "" if payload[key] is None else str(payload[key])
        self.applied_gids.append(delta.global_id)
        return True


# ── Local index ───────────────────────────────────────────────────────────────

def file_modified_utc(path: Path) -> str:
    """The IFC file's mtime as an ISO-8601 UTC string."""
    return (datetime.fromtimestamp(path.stat().st_mtime, tz=timezone.utc)
            .isoformat().replace("+00:00", "Z"))


def build_local_index(token_map: dict[str, dict], modified_utc: str) -> dict[str, dict]:
    """Shape the extracted tokens into what ``ReconcileEngine`` expects.

    **Every element shares the file's modification time.** An IFC export carries
    no per-element edit timestamp, so file granularity is the best evidence that
    actually exists. It gives the right answer for the cases that matter: an
    export made after a remote edit wins (the author has seen and superseded it),
    and a remote edit made after the export wins (it is genuinely newer).

    The alternative — leaving ``modified_utc`` unset — is worse and not merely
    less precise: the engine's rule "no local timestamp ⇒ remote wins" would
    then hand *every* contested element to the server, so a freshly exported
    model would be silently reverted to whatever the hub last held.

    The cost is honest and bounded: two edits inside the same export cannot be
    told apart, so a remote edit made between an element's authoring and the
    export can beat it. That is a conflict either way, and it is reported.
    """
    return {gid: {"tokens": tokens, "modified_utc": modified_utc}
            for gid, tokens in token_map.items()}


def cursor_host_key(doc_guid: str) -> str:
    """Cursor key for one IFC document within a project.

    Per-**document**, not per-project. A drop folder normally holds several
    federated exports pointing at one project; with a single shared cursor the
    first file to drain the feed would consume every other file's deltas, and
    those would never be seen again. The server already keys mappings on
    ``(project, host, hostDocumentGuid)`` — this matches that grain.
    """
    return f"ifc:{doc_guid}" if doc_guid else "ifc"


# ── Conflict sidecar ──────────────────────────────────────────────────────────

def conflict_rows(resolution, local_tokens: dict, source: str) -> list[dict]:
    """Expand one Resolution into per-key conflict records.

    One row per *differing token*, not per element: "SEQ was 0007 here and 0042
    there" is what an operator can act on. An element-level row would say only
    that something disagreed.
    """
    from stingtools_core.sync import TOKEN_KEYS

    remote_tokens = (resolution.delta.payload if resolution.delta else {}) or {}
    stamped = datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")

    def _row(key, local_value, remote_value):
        return {
            "ts": stamped,
            "source": source,
            "guid": resolution.global_id,
            "key": key,
            "local": local_value,
            "remote": remote_value,
            "winner": resolution.winner,
            "applied": bool(resolution.applied),
            "reason": resolution.reason,
        }

    rows = []
    for key in TOKEN_KEYS:
        local_value = str((local_tokens or {}).get(key) or "")
        remote_value = str(remote_tokens.get(key) or "")
        if local_value != remote_value:
            rows.append(_row(key, local_value, remote_value))

    if not rows:
        # A conflict with no differing token should be unreachable (identical
        # payloads short-circuit before any conflict is raised). Emit a marker
        # rather than nothing, so the sidecar count always matches the summary.
        rows.append(_row(None, None, None))
    return rows


def conflict_sidecar_path(ifc_path: Path | str) -> Path:
    """``foo.ifc`` → ``foo.conflicts.jsonl``, beside the source file."""
    p = Path(ifc_path)
    return p.with_name(p.stem + CONFLICT_SUFFIX)


class ConflictSidecar:
    """Appends conflict records to ``<ifc>.conflicts.jsonl``.

    JSONL because a drop can conflict on thousands of elements and a growing
    array would have to be re-read and re-written for each one. Append-only also
    means a crash mid-write costs one line, not the file.
    """

    def __init__(self, path: Path, source: str) -> None:
        self.path = Path(path)
        self._source = source
        self.count = 0

    def record(self, resolution, local_tokens: dict) -> None:
        rows = conflict_rows(resolution, local_tokens, self._source)
        # Structured line in the watcher log too — the sidecar is next to the
        # file, but the log is where someone looks when a sync "did something
        # odd" and they have no idea which file to open.
        log.warning("SYNC CONFLICT %s", json.dumps({
            "guid": resolution.global_id,
            "winner": resolution.winner,
            "reason": resolution.reason,
            "applied": bool(resolution.applied),
            "keys": [r["key"] for r in rows],
        }))
        try:
            with open(self.path, "a", encoding="utf-8") as f:
                for row in rows:
                    f.write(json.dumps(row) + "\n")
            self.count += len(rows)
        except OSError as e:
            # Losing the audit trail must not lose the sync.
            log.warning("Could not write conflict sidecar %s: %s", self.path.name, e)


# ── Orchestration ─────────────────────────────────────────────────────────────

def pull_and_reconcile(
    client: Any,
    *,
    token_map: dict[str, dict],
    ifc_path: Path,
    doc_guid: str,
    cursor_path: Path,
    page_limit: int = 200,
    on_progress: Optional[Callable[[str], None]] = None,
) -> dict:
    """Pull changes since this document's cursor and reconcile them locally.

    Returns a summary dict; never raises. A pull failure degrades to push-only,
    which is exactly the pre-SB-5a behaviour — a hub that is unreachable must
    not cost the operator their ingest.

    The cursor is persisted **after** reconcile, so a crash mid-pass replays the
    page rather than losing it. Re-applying a delta is a no-op (the engine skips
    identical payloads); skipping one loses an edit.
    """
    from stingtools_core.sync import CursorStore, PullClient, ReconcileEngine

    emit = on_progress or (lambda _msg: None)
    summary = {
        "pulled": 0, "applied": 0, "kept_local": 0, "unchanged": 0,
        "absent": 0, "conflicts": 0, "failed": 0, "cursor": None, "error": None,
    }

    base_url = getattr(client, "base_url", None)
    project_id = getattr(client, "project_id", None)
    if not isinstance(base_url, str) or not base_url or \
            not isinstance(project_id, str) or not project_id:
        # No usable endpoint — nothing to pull from. Not an error: the
        # single-file path and several test doubles legitimately have neither.
        summary["error"] = "no base_url/project_id — pull skipped"
        log.info("Pull skipped: %s", summary["error"])
        return summary

    host_key = cursor_host_key(doc_guid)
    store = CursorStore(cursor_path)
    cursor = store.read(project_id, host_key)

    try:
        puller = PullClient(ClientHttp(client), base_url, project_id, page_limit=page_limit)
        deltas = list(puller.drain(cursor))
    except Exception as e:  # noqa: BLE001 — pull is best-effort by design
        summary["error"] = f"pull failed: {e}"
        log.warning("Pull failed (%s) — continuing push-only", e)
        emit(f"Pull failed ({e}) — pushing without reconcile")
        return summary

    summary["pulled"] = len(deltas)

    # Partition before reconcile. A delta for a GlobalId this file does not
    # contain belongs to another document in the same project (or to an element
    # deleted from this one — the feed has no tombstones, see ROADMAP SB-5).
    # Feeding it to the engine would take rule 1 ("no local element ⇒ apply"),
    # the adapter would refuse it, and it would land in `failed` alongside real
    # failures. Counting it honestly as `absent` keeps `failed` meaningful.
    known = [d for d in deltas if d.global_id in token_map]
    summary["absent"] = len(deltas) - len(known)

    if not known:
        if deltas:
            emit(f"Pulled {len(deltas)} change(s), none for elements in this file")
        store.write(project_id, host_key, puller.last_cursor)
        summary["cursor"] = puller.last_cursor
        return summary

    local_index = build_local_index(token_map, file_modified_utc(ifc_path))
    sidecar = ConflictSidecar(
        conflict_sidecar_path(ifc_path),
        source=Path(ifc_path).name,
    )

    # Snapshot the local values BEFORE anything is applied.
    #
    # `IfcTokenApplyAdapter` mutates these dicts in place (that is how a remote
    # win reaches the write-back and the push), and `ReconcileEngine` calls
    # `_apply` *before* `on_conflict`. Reading `local_index` inside the callback
    # would therefore report the value we just overwrote it WITH, so every
    # remote-wins row would read local == remote and the one fact the audit
    # trail exists to preserve — what the local value used to be — would be the
    # one fact it lost.
    pre_apply = {gid: dict(tokens) for gid, tokens in token_map.items()}

    def _on_conflict(resolution) -> None:
        sidecar.record(resolution, pre_apply.get(resolution.global_id) or {})

    adapter = IfcTokenApplyAdapter(token_map)
    result = ReconcileEngine(adapter, on_conflict=_on_conflict).reconcile(known, local_index)

    summary.update({
        "applied": result.applied,
        "kept_local": result.kept_local,
        "unchanged": result.skipped_equal,
        "failed": result.failed,
        "conflicts": len(result.conflicts),
    })

    emit(f"Reconciled {len(known)} remote change(s): {result.summary()}")
    if summary["absent"]:
        emit(f"{summary['absent']} change(s) were for elements not in this file")
    if result.conflicts:
        emit(f"{len(result.conflicts)} conflict(s) recorded in {sidecar.path.name}")

    # Only now — a crash before this point replays the page instead of losing it.
    store.write(project_id, host_key, puller.last_cursor)
    summary["cursor"] = puller.last_cursor
    return summary
