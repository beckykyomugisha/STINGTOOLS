"""Drop-folder lifecycle — the `processing/ done/ failed/` contract (SB-4).

The C# side (`StingBridge/src/IFC/IfcDropWatcher.cs`) has always moved files
through subfolders: pick up → `processing/` (so nothing is picked twice) →
`done/YYYYMMDD_<name>` on success, or `failed/` plus a `.log` sidecar on error.
The Python watcher instead left every file where it landed and dropped a
`.sync_result.json` beside it.

Two contracts over one folder is a real problem, not a cosmetic one: with files
left in place, "what is still outstanding?" has no answer, a re-run reprocesses
everything, and a publisher writing into the folder cannot tell finished work
from pending. This module makes the Python side follow the C# contract, keeping
the sidecars — they are strictly more information than the C# side records.

Windows detail: a file can still be open when watchdog reports it, and
`os.replace` then fails with `PermissionError`/`OSError`. Every move retries
briefly rather than failing the file outright.
"""
from __future__ import annotations

import logging
import os
import time
from datetime import datetime
from pathlib import Path

log = logging.getLogger(__name__)

PROCESSING = "processing"
DONE = "done"
FAILED = "failed"
SUBFOLDERS = (PROCESSING, DONE, FAILED)

# Windows holds a share lock while the producing app finishes writing. The C#
# watcher waits up to 30 s for an exclusive open; matching that order of
# magnitude here keeps a large IFC export from being failed for being slow.
_MOVE_RETRY_S = 30.0
_MOVE_BACKOFF_S = 0.25


def ensure_layout(root: Path) -> None:
    """Create the drop root and its three subfolders. Idempotent."""
    root.mkdir(parents=True, exist_ok=True)
    for sub in SUBFOLDERS:
        (root / sub).mkdir(exist_ok=True)


def is_in_managed_subfolder(path: Path, root: Path) -> bool:
    """True when *path* already lives in processing/, done/ or failed/.

    Guards against re-picking our own output: `done/` holds the `_sting.ifc`
    we produced, and without this a recursive watch would loop forever.
    """
    try:
        rel = path.resolve().relative_to(root.resolve())
    except (ValueError, OSError):
        return False
    return len(rel.parts) > 1 and rel.parts[0] in SUBFOLDERS


def _move(src: Path, dst: Path, timeout_s: float = _MOVE_RETRY_S) -> Path:
    """Move with retry, returning the final destination.

    Retries on the Windows file-in-use race. If the destination already exists
    the name is disambiguated rather than overwritten — two exports of the same
    name on the same day are a normal occurrence and silently destroying the
    earlier result would lose work.
    """
    dst.parent.mkdir(parents=True, exist_ok=True)

    final = dst
    if final.exists():
        stem, suffix = dst.stem, dst.suffix
        n = 2
        while final.exists():
            final = dst.with_name(f"{stem}({n}){suffix}")
            n += 1

    deadline = time.monotonic() + timeout_s
    last: Exception | None = None
    while time.monotonic() < deadline:
        try:
            os.replace(src, final)
            return final
        except (PermissionError, OSError) as e:
            last = e
            time.sleep(_MOVE_BACKOFF_S)

    raise OSError(f"could not move {src.name} to {final.parent.name}/ "
                  f"after {timeout_s:.0f}s: {last}")


def claim(src: Path, root: Path) -> Path | None:
    """Move a freshly dropped file into `processing/`.

    Returns its new path, or None when the file could not be claimed (already
    gone, or still locked past the timeout) — in which case the caller should
    skip it rather than process a file it does not own.
    """
    if not src.exists():
        return None
    try:
        claimed = _move(src, root / PROCESSING / src.name)
        log.info("Claimed %s -> %s/", src.name, PROCESSING)
        return claimed
    except OSError as e:
        log.warning("Could not claim %s: %s", src.name, e)
        return None


def _companions(processing_path: Path) -> list[Path]:
    """Artefacts produced beside the source: `<stem>_sting.ifc` and sidecars."""
    stem = processing_path.stem
    parent = processing_path.parent
    candidates = [
        parent / f"{stem}_sting.ifc",
        parent / f"{stem}.sync_result.json",
        processing_path.with_suffix(".sync_result.json"),
    ]
    seen: set[Path] = set()
    out = []
    for c in candidates:
        if c != processing_path and c.exists() and c not in seen:
            seen.add(c)
            out.append(c)
    return out


def complete(processing_path: Path, root: Path, when: datetime | None = None) -> Path:
    """Archive a processed file to `done/YYYYMMDD_<name>`.

    The `_sting.ifc` output and any sidecar travel with it, so `done/` holds the
    whole record of one drop instead of scattering it. Returns the archived
    source path.
    """
    stamp = (when or datetime.now()).strftime("%Y%m%d")
    companions = _companions(processing_path)

    archived = _move(processing_path, root / DONE / f"{stamp}_{processing_path.name}")
    for c in companions:
        try:
            _move(c, root / DONE / f"{stamp}_{c.name}")
        except OSError as e:
            # The source is already archived; losing a sidecar is not worth
            # failing an otherwise successful drop.
            log.warning("Could not archive companion %s: %s", c.name, e)

    log.info("Completed %s -> %s/", processing_path.name, DONE)
    return archived


def fail(processing_path: Path, root: Path, error: str) -> Path:
    """Move a file to `failed/` and write a `.log` sidecar describing why."""
    companions = _companions(processing_path)
    moved = _move(processing_path, root / FAILED / processing_path.name)

    log_path = moved.with_suffix(moved.suffix + ".log")
    try:
        log_path.write_text(
            f"{datetime.now().isoformat()}\n"
            f"file: {processing_path.name}\n"
            f"error: {error}\n",
            encoding="utf-8",
        )
    except OSError as e:
        log.warning("Could not write failure log for %s: %s", processing_path.name, e)

    for c in companions:
        try:
            _move(c, root / FAILED / c.name)
        except OSError:
            pass

    log.warning("Failed %s -> %s/ (%s)", processing_path.name, FAILED, error)
    return moved


def recover_orphans(root: Path) -> int:
    """Return files stranded in `processing/` by a crash to the drop root.

    Without this, a process killed mid-file leaves that file invisible to every
    future run: it is no longer in the root to be picked up, and nothing moves
    it out of `processing/`. Called once at watcher start.
    """
    proc = root / PROCESSING
    if not proc.is_dir():
        return 0

    recovered = 0
    for f in sorted(proc.iterdir()):
        if not f.is_file() or f.suffix.lower() != ".ifc":
            continue
        if f.stem.endswith("_sting"):
            continue  # our own output, not an unprocessed input
        try:
            _move(f, root / f.name, timeout_s=5.0)
            recovered += 1
        except OSError as e:
            log.warning("Could not recover orphan %s: %s", f.name, e)

    if recovered:
        log.info("Recovered %d file(s) stranded in %s/ by a previous run",
                 recovered, PROCESSING)
    return recovered
