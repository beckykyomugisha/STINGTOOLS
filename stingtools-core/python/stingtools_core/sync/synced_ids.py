"""C3 — remembers which elements a host document last pushed, so deletions can
be detected.

THE PROBLEM
-----------
An ingest is an UPSERT over the elements it carries, so an element that
disappears from the source is simply not mentioned — and "not mentioned" is
indistinguishable from "unchanged, and this was a partial push". Without a
record of what was pushed last time, a wall deleted in ArchiCAD stayed on the
server forever: visible in the viewer, answering clash and compliance queries,
and counted in every metric.

**Absence cannot mean deletion.** The only safe way to turn a full export into a
deletion signal is to diff it against what this same document pushed before, and
send the difference explicitly.

WHY PER (PROJECT, HOST DOCUMENT)
--------------------------------
Two hosts contributing to one project must not be able to tombstone each other's
geometry. If the set were per-project, a full-export diff from the ArchiCAD file
would list every Revit-contributed GlobalId as "removed" and wipe it. Keying on
the document is what makes the diff mean "gone from THIS file" rather than "not
in this file".
"""

from __future__ import annotations

import json
import logging
from pathlib import Path
from typing import Iterable, Optional, Set

log = logging.getLogger(__name__)


class SyncedIdStore:
    """Persists the set of IFC GlobalIds a document last pushed."""

    def __init__(self, path: Path | str) -> None:
        self._path = Path(path)

    def read(self, project_id: str, host_document_guid: str) -> Set[str]:
        """The ids last pushed, or an empty set when nothing is recorded."""
        try:
            data = json.loads(self._path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return set()
        if not isinstance(data, dict):
            return set()
        ids = data.get(self._key(project_id, host_document_guid))
        return set(ids) if isinstance(ids, list) else set()

    def write(self, project_id: str, host_document_guid: str, ids: Iterable[str]) -> None:
        """Record the ids this document now contains."""
        try:
            data = json.loads(self._path.read_text(encoding="utf-8"))
            if not isinstance(data, dict):
                data = {}
        except (OSError, ValueError):
            data = {}

        # Sorted so the file diffs cleanly and a human can read it.
        data[self._key(project_id, host_document_guid)] = sorted({i for i in ids if i})
        try:
            self._path.parent.mkdir(parents=True, exist_ok=True)
            self._path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        except OSError as e:
            # A lost record costs one missed deletion cycle, never correctness:
            # the next successful push re-establishes the baseline. Failing the
            # whole sync over it would be a worse trade.
            log.warning("Could not persist synced-id set: %s", e)

    @staticmethod
    def _key(project_id: str, host_document_guid: str) -> str:
        return f"{project_id}:{host_document_guid}"


def diff_removals(previous: Set[str], current: Iterable[str]) -> list[str]:
    """Ids present in the last push and absent from this one.

    Sorted so a push is deterministic and two runs over an unchanged file
    produce byte-identical payloads — which is what makes a replay detectable.
    """
    current_set = {i for i in current if i}
    return sorted(previous - current_set)


def should_send_removals(
    previous: Set[str], current: Iterable[str], max_fraction: Optional[float] = 0.5
) -> tuple[list[str], Optional[str]]:
    """Compute removals, with a guard against a truncated export wiping a model.

    Returns ``(removals, refusal_reason)``. When ``refusal_reason`` is not None
    the caller should push the upserts and SKIP the removals.

    **Why a guard at all.** The diff is only as trustworthy as the export it is
    diffing. A crashed or filtered export that yields 3 elements instead of
    30,000 is indistinguishable, at this layer, from a coordinator having
    deleted almost the whole model — and one of those two readings is
    catastrophic and unrecoverable-by-retry, while the other is a delay of one
    sync cycle. So a removal set covering more than ``max_fraction`` of the
    previously-known elements is refused and reported rather than applied.

    Pass ``max_fraction=None`` to disable (a genuine bulk deletion then needs an
    operator who has read this docstring).
    """
    removals = diff_removals(previous, current)
    if not removals or max_fraction is None or not previous:
        return removals, None

    fraction = len(removals) / len(previous)
    if fraction > max_fraction:
        return [], (
            f"refusing to remove {len(removals)} of {len(previous)} known element(s) "
            f"({fraction:.0%}) in one push — this looks like a truncated export "
            f"rather than a deletion. Re-run once the export is complete, or "
            f"raise the threshold deliberately."
        )
    return removals, None
