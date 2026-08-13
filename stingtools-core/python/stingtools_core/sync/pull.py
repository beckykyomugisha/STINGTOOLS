"""Pull client — the read half of bidirectional sync (plan §1.4.1).

Push already existed: every host could send tags to the hub. Nothing could ask
"what changed since I last looked?", so a host had no way to learn about an edit
made anywhere else.

Host-agnostic by construction: this speaks HTTP and yields ``ChangeDelta``
objects. It knows nothing about ArchiCAD, Revit or Blender — the adapter applies
what this produces.
"""
from __future__ import annotations

import json
import logging
from pathlib import Path
from typing import Any, Iterator, Optional

from ..hosts.adapter import ChangeDelta

log = logging.getLogger(__name__)

DEFAULT_PAGE_LIMIT = 200
#: Stop after this many pages in one drain, so a runaway feed cannot spin forever.
MAX_PAGES_PER_DRAIN = 500


class CursorStore:
    """Persists the change cursor between runs.

    Without persistence every start is a full backfill, which on a large project
    means re-applying thousands of deltas the host already has. The file is
    per-(project, host) so two hosts sharing a machine keep separate positions.
    """

    def __init__(self, path: Path | str) -> None:
        self._path = Path(path)

    def read(self, project_id: str, host: str) -> Optional[str]:
        try:
            data = json.loads(self._path.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return None
        return data.get(self._key(project_id, host))

    def write(self, project_id: str, host: str, cursor: Optional[str]) -> None:
        if not cursor:
            return
        try:
            data = json.loads(self._path.read_text(encoding="utf-8"))
            if not isinstance(data, dict):
                data = {}
        except (OSError, ValueError):
            data = {}
        data[self._key(project_id, host)] = cursor
        try:
            self._path.parent.mkdir(parents=True, exist_ok=True)
            self._path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        except OSError as e:
            # A lost cursor costs a backfill next run, never correctness.
            log.warning("Could not persist sync cursor: %s", e)

    @staticmethod
    def _key(project_id: str, host: str) -> str:
        return f"{project_id}:{host}"


class PullClient:
    """Drains the server's cursor-paged change feed into ``ChangeDelta``s.

    ``http`` is anything exposing ``get(url, params=...) -> response`` with
    ``.status_code`` and ``.json()`` — the StingBridge client and core client
    both satisfy this, and so does a fake in tests.
    """

    def __init__(self, http: Any, base_url: str, project_id: str,
                 page_limit: int = DEFAULT_PAGE_LIMIT) -> None:
        self._http = http
        self._base = base_url.rstrip("/")
        self._project_id = project_id
        self._page_limit = max(1, min(page_limit, 1000))

    def fetch_page(self, cursor: Optional[str] = None) -> tuple[list[ChangeDelta], Optional[str], bool]:
        """One page. Returns (deltas, next_cursor, has_more)."""
        params: dict[str, Any] = {"limit": self._page_limit}
        if cursor:
            params["since"] = cursor

        url = f"{self._base}/api/projects/{self._project_id}/changes"
        resp = self._http.get(url, params=params)

        status = getattr(resp, "status_code", 0)
        if status == 404:
            # An older server without the feed. Degrade to push-only rather than
            # failing the run — the alternative is refusing to sync at all
            # against a server that works fine for everything else.
            log.warning("Change feed unavailable (404) - this server may predate "
                        "/api/projects/{id}/changes. Pull is disabled this run.")
            return [], cursor, False
        if status >= 400:
            log.warning("Change feed returned HTTP %s - skipping pull this run", status)
            return [], cursor, False

        try:
            body = resp.json() or {}
        except Exception as e:  # noqa: BLE001
            log.warning("Change feed returned unreadable body: %s", e)
            return [], cursor, False

        deltas = []
        for item in body.get("items") or []:
            gid = item.get("globalId")
            if not gid:
                continue  # nothing to key on; cannot be resolved cross-host
            deltas.append(ChangeDelta(
                kind=item.get("kind") or "tag",
                global_id=gid,
                payload=item.get("payload") or {},
                last_modified_utc=item.get("lastModifiedUtc"),
                cursor=body.get("nextCursor"),
            ))

        return deltas, body.get("nextCursor") or cursor, bool(body.get("hasMore"))

    def drain(self, cursor: Optional[str] = None) -> Iterator[ChangeDelta]:
        """Yield every delta from ``cursor`` to the end of the feed.

        The final cursor is available afterwards as :attr:`last_cursor`, so a
        caller that persists it only advances once the page has been consumed —
        crashing mid-drain replays that page rather than losing it.
        """
        self.last_cursor = cursor
        pages = 0
        while True:
            deltas, next_cursor, has_more = self.fetch_page(self.last_cursor)
            for d in deltas:
                yield d
            self.last_cursor = next_cursor
            pages += 1

            if not has_more:
                return
            if pages >= MAX_PAGES_PER_DRAIN:
                log.warning("Stopped draining after %d pages - resuming next run",
                            MAX_PAGES_PER_DRAIN)
                return
            if not deltas:
                # hasMore with an empty page would otherwise spin.
                log.warning("Change feed reported more data but returned none - stopping")
                return

    last_cursor: Optional[str] = None
