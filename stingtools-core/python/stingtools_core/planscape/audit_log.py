"""Append-only JSONL audit log with SHA-256 tamper-evidence chain.

Mirrors the STING-Revit AuditLog convention so server-side verification
treats Blender-emitted log entries identically to Revit-emitted ones.

File format: one JSON object per line, with `prev_sha` field linking to
the previous entry's `entry_sha`. First entry's prev_sha is "GENESIS".

    entry_sha = sha256(prev_sha + canonical_json(entry_minus_entry_sha))
"""

from __future__ import annotations

import hashlib
import json
import os
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Optional


GENESIS = "GENESIS"

# Bump when the entry shape changes in a backward-incompatible way.
# Consumers should read entries with schema_version <= their_supported_max
# and either migrate or reject older versions explicitly.
SCHEMA_VERSION = 1


class AuditLog:
    def __init__(self, path: str | Path):
        self._path = Path(path)
        self._path.parent.mkdir(parents=True, exist_ok=True)

    def _last_sha(self) -> str:
        if not self._path.exists():
            return GENESIS
        last_line = ""
        with self._path.open("r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if line:
                    last_line = line
        if not last_line:
            return GENESIS
        try:
            return json.loads(last_line).get("entry_sha", GENESIS)
        except json.JSONDecodeError:
            return GENESIS

    @staticmethod
    def _canonical(d: dict) -> str:
        return json.dumps(d, sort_keys=True, separators=(",", ":"), ensure_ascii=False)

    def append(
        self,
        event: str,
        actor: str,
        payload: Optional[dict] = None,
        ifc_global_id: Optional[str] = None,
    ) -> dict:
        """Append a single audit entry. Returns the written entry."""
        prev_sha = self._last_sha()
        entry: dict[str, Any] = {
            "schema_version": SCHEMA_VERSION,
            "ts_utc": datetime.now(timezone.utc).isoformat(),
            "event": event,
            "actor": actor,
            "pid": os.getpid(),
            "prev_sha": prev_sha,
        }
        if ifc_global_id:
            entry["ifc_global_id"] = ifc_global_id
        if payload:
            entry["payload"] = payload

        entry_sha = hashlib.sha256(
            (prev_sha + self._canonical(entry)).encode("utf-8")
        ).hexdigest()
        entry["entry_sha"] = entry_sha

        with self._path.open("a", encoding="utf-8") as f:
            f.write(self._canonical(entry) + "\n")

        return entry

    def verify_chain(self) -> tuple[bool, list[str]]:
        """Re-walk the log and verify every entry's hash chain.

        Returns (chain_valid, errors). On chain_valid=True the log has
        not been tampered with since each entry was written.
        """
        if not self._path.exists():
            return True, []
        errors: list[str] = []
        prev_sha = GENESIS
        with self._path.open("r", encoding="utf-8") as f:
            for i, line in enumerate(f, start=1):
                line = line.strip()
                if not line:
                    continue
                try:
                    entry = json.loads(line)
                except json.JSONDecodeError as e:
                    errors.append(f"line {i}: invalid JSON: {e}")
                    return False, errors

                stored_sha = entry.pop("entry_sha", None)
                if stored_sha is None:
                    errors.append(f"line {i}: missing entry_sha")
                    return False, errors
                if entry.get("prev_sha") != prev_sha:
                    errors.append(
                        f"line {i}: prev_sha mismatch (expected {prev_sha!r}, got {entry.get('prev_sha')!r})"
                    )
                    return False, errors

                computed_sha = hashlib.sha256(
                    (prev_sha + self._canonical(entry)).encode("utf-8")
                ).hexdigest()
                if computed_sha != stored_sha:
                    errors.append(f"line {i}: entry_sha mismatch (computed {computed_sha[:16]})")
                    return False, errors

                prev_sha = stored_sha
        return True, []
