"""Tests for the drop-folder lifecycle (SB-4).

Mirrors the contract in StingBridge/src/IFC/IfcDropWatcher.cs:
pick up -> processing/ -> done/YYYYMMDD_<name> on success, or failed/ plus a
.log sidecar on error.

Uses real temp directories — the behaviour under test is filesystem moves, and
a mocked filesystem would prove nothing about the Windows file-in-use race this
module exists to survive.

Run from the repo root:  python StingBridge/tests/test_hot_folder.py
(or via pytest).
"""
from __future__ import annotations

import sys
import tempfile
from datetime import datetime
from pathlib import Path

_REPO_ROOT = Path(__file__).resolve().parents[2]
if str(_REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(_REPO_ROOT))

from StingBridge.watch import hot_folder as hf  # noqa: E402
from StingBridge.watch.ifc_watcher import _is_failure  # noqa: E402


class _Root:
    """A temp drop root with the three subfolders created."""

    def __enter__(self) -> Path:
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        hf.ensure_layout(self.root)
        return self.root

    def __exit__(self, *exc):
        self._tmp.cleanup()


def _drop(root: Path, name: str = "model.ifc", body: str = "ISO-10303-21;") -> Path:
    p = root / name
    p.write_text(body, encoding="utf-8")
    return p


# ── layout ───────────────────────────────────────────────────────────────────

def test_ensure_layout_creates_the_three_subfolders():
    with _Root() as root:
        for sub in ("processing", "done", "failed"):
            assert (root / sub).is_dir(), f"{sub}/ missing"


def test_ensure_layout_is_idempotent():
    with _Root() as root:
        keep = root / "done" / "marker.txt"
        keep.write_text("x", encoding="utf-8")
        hf.ensure_layout(root)
        assert keep.exists(), "re-running the layout wiped existing content"


# ── claim ────────────────────────────────────────────────────────────────────

def test_claim_moves_the_file_into_processing():
    with _Root() as root:
        src = _drop(root)
        claimed = hf.claim(src, root)
        assert claimed is not None
        assert claimed.parent.name == "processing"
        assert claimed.name == "model.ifc"
        assert not src.exists(), "original left behind — it could be picked twice"


def test_claim_of_a_missing_file_returns_none():
    with _Root() as root:
        assert hf.claim(root / "gone.ifc", root) is None


def test_claiming_twice_yields_only_one_owner():
    # The whole point of processing/: a second picker must come away empty.
    with _Root() as root:
        src = _drop(root)
        first = hf.claim(src, root)
        second = hf.claim(src, root)
        assert first is not None
        assert second is None


# ── complete ─────────────────────────────────────────────────────────────────

def test_complete_archives_with_a_date_prefix():
    with _Root() as root:
        claimed = hf.claim(_drop(root), root)
        archived = hf.complete(claimed, root, when=datetime(2026, 7, 20))

        assert archived.parent.name == "done"
        assert archived.name == "20260720_model.ifc"
        assert not claimed.exists()
        assert not list((root / "processing").iterdir()), "processing/ not drained"


def test_complete_takes_the_sting_output_and_sidecar_along():
    with _Root() as root:
        claimed = hf.claim(_drop(root), root)
        # what the processor writes beside the source
        (claimed.parent / "model_sting.ifc").write_text("out", encoding="utf-8")
        (claimed.parent / "model.sync_result.json").write_text("{}", encoding="utf-8")

        hf.complete(claimed, root, when=datetime(2026, 7, 20))

        done = sorted(p.name for p in (root / "done").iterdir())
        assert done == ["20260720_model.ifc",
                        "20260720_model.sync_result.json",
                        "20260720_model_sting.ifc"]
        assert not list((root / "processing").iterdir())


def test_completing_the_same_name_twice_does_not_overwrite():
    # Two exports of the same name on the same day is normal; silently
    # destroying the earlier result would lose work.
    with _Root() as root:
        first = hf.complete(hf.claim(_drop(root), root), root, when=datetime(2026, 7, 20))
        second = hf.complete(hf.claim(_drop(root), root), root, when=datetime(2026, 7, 20))

        assert first.exists() and second.exists()
        assert first.name != second.name
        assert second.name == "20260720_model(2).ifc"


# ── fail ─────────────────────────────────────────────────────────────────────

def test_fail_moves_to_failed_and_writes_a_log():
    with _Root() as root:
        claimed = hf.claim(_drop(root), root)
        moved = hf.fail(claimed, root, "IfcOpenShell exploded")

        assert moved.parent.name == "failed"
        assert moved.name == "model.ifc"
        assert not list((root / "processing").iterdir())

        log = moved.with_suffix(moved.suffix + ".log")
        assert log.exists(), "no .log sidecar"
        assert "IfcOpenShell exploded" in log.read_text(encoding="utf-8")


def test_fail_also_relocates_partial_output():
    # A half-written _sting.ifc must not be left in processing/ where a later
    # run would treat it as someone's in-flight work.
    with _Root() as root:
        claimed = hf.claim(_drop(root), root)
        (claimed.parent / "model_sting.ifc").write_text("partial", encoding="utf-8")

        hf.fail(claimed, root, "boom")

        assert (root / "failed" / "model_sting.ifc").exists()
        assert not list((root / "processing").iterdir())


# ── subfolder guard ──────────────────────────────────────────────────────────

def test_files_inside_managed_subfolders_are_recognised():
    with _Root() as root:
        assert hf.is_in_managed_subfolder(root / "processing" / "a.ifc", root)
        assert hf.is_in_managed_subfolder(root / "done" / "b.ifc", root)
        assert hf.is_in_managed_subfolder(root / "failed" / "c.ifc", root)


def test_a_file_in_the_root_is_not_in_a_managed_subfolder():
    with _Root() as root:
        assert not hf.is_in_managed_subfolder(root / "fresh.ifc", root)


def test_an_unrelated_path_is_not_in_a_managed_subfolder():
    with _Root() as root:
        with tempfile.TemporaryDirectory() as other:
            assert not hf.is_in_managed_subfolder(Path(other) / "x.ifc", root)


# ── crash recovery ───────────────────────────────────────────────────────────

def test_orphans_left_by_a_crash_are_returned_to_the_root():
    # Without this a file killed mid-process is invisible forever: no longer in
    # the root to be picked up, and nothing moves it out of processing/.
    with _Root() as root:
        (root / "processing" / "stranded.ifc").write_text("ISO-10303-21;", encoding="utf-8")

        assert hf.recover_orphans(root) == 1
        assert (root / "stranded.ifc").exists()
        assert not list((root / "processing").iterdir())


def test_recovery_does_not_re_drop_our_own_partial_output():
    with _Root() as root:
        (root / "processing" / "model_sting.ifc").write_text("out", encoding="utf-8")
        assert hf.recover_orphans(root) == 0
        # never fed back through the watcher…
        assert not (root / "model_sting.ifc").exists()
        # …and not abandoned in processing/ either
        assert not (root / "processing" / "model_sting.ifc").exists()


def test_recovery_does_not_re_drop_non_ifc_files():
    """Non-input files are not recovered to the root — they'd be re-ingested."""
    with _Root() as root:
        (root / "processing" / "notes.txt").write_text("x", encoding="utf-8")
        assert hf.recover_orphans(root) == 0
        assert not (root / "notes.txt").exists()


def test_recovery_clears_stranded_artefacts_out_of_processing():
    """…but it must not leave them in processing/ forever either.

    This previously asserted only `recover_orphans(...) == 0` and stopped there,
    which is exactly how a stranded .glb went unnoticed: `processing/` stayed
    non-empty after a completed run and nothing ever swept it. Recovery is the
    one place that sees the folder at rest, so it is the place to clear it.
    """
    with _Root() as root:
        proc = root / "processing"
        (proc / "notes.txt").write_text("x", encoding="utf-8")
        (proc / "model.glb").write_bytes(b"glTF-ish")
        (proc / "model_sting.ifc").write_text("out", encoding="utf-8")

        assert hf.recover_orphans(root) == 0        # none were INPUTS
        assert not list(proc.iterdir()), "processing/ must be left empty"

        moved = {p.name for p in (root / "failed").iterdir()}
        assert moved == {"notes.txt", "model.glb", "model_sting.ifc"}
        # and none of them were re-dropped for another ingest pass
        assert [p.name for p in root.iterdir() if p.is_file()] == []


def test_recovery_on_an_empty_folder_is_a_no_op():
    with _Root() as root:
        assert hf.recover_orphans(root) == 0


# ── full lifecycle ───────────────────────────────────────────────────────────

def test_success_path_leaves_root_and_processing_empty():
    with _Root() as root:
        claimed = hf.claim(_drop(root), root)
        (claimed.parent / "model_sting.ifc").write_text("out", encoding="utf-8")
        hf.complete(claimed, root, when=datetime(2026, 7, 20))

        assert [p.name for p in root.iterdir() if p.is_file()] == []
        assert not list((root / "processing").iterdir())
        assert len(list((root / "done").iterdir())) == 2
        assert not list((root / "failed").iterdir())


def test_failure_path_leaves_root_and_processing_empty():
    with _Root() as root:
        claimed = hf.claim(_drop(root), root)
        hf.fail(claimed, root, "nope")

        assert [p.name for p in root.iterdir() if p.is_file()] == []
        assert not list((root / "processing").iterdir())
        assert not list((root / "done").iterdir())
        # the file plus its .log
        assert len(list((root / "failed").iterdir())) == 2


# ── failure detection ────────────────────────────────────────────────────────
# process() reports parse/sync failures in result["errors"] instead of raising,
# so routing on exceptions alone archived unopenable files as successes.

def test_result_with_no_errors_is_a_success():
    assert not _is_failure({"errors": [], "synced": 3})


def test_unparseable_file_is_a_failure():
    assert _is_failure({"errors": ["IFC open failed: Unable to parse IFC SPF header"],
                        "elements": 0, "synced": 0})


def test_errors_with_nothing_synced_is_a_failure():
    # e.g. the server rejected every element.
    assert _is_failure({"errors": ["sync rejected"], "elements": 5, "synced": 0})


def test_partial_success_still_counts_as_done():
    # Elements landed; a secondary complaint (write-back, GLB) belongs in the
    # sidecar, not in failed/.
    assert not _is_failure({"errors": ["GLB conversion skipped"], "synced": 3})


def test_missing_keys_do_not_crash_the_check():
    assert not _is_failure({})
    assert _is_failure({"errors": ["boom"]})


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


# ── GLB companions (R5) ──────────────────────────────────────────────────────
#
# The GLB is written by IFCDropHandler._convert_to_glb next to the file being
# converted (ifc_watcher.py:257). It was absent from _companions(), so archiving
# the input left it behind in processing/ — invisible, because every existing
# test wrote its own fake companions and none of them wrote a .glb.

def test_complete_takes_the_glb_along():
    with _Root() as root:
        claimed = hf.claim(_drop(root), root)
        claimed.with_suffix(".glb").write_bytes(b"glTF-ish")

        hf.complete(claimed, root, when=datetime(2026, 7, 20))

        assert not list((root / "processing").iterdir()), \
            "the .glb was stranded in processing/"
        assert {p.name for p in (root / "done").iterdir()} == \
            {"20260720_model.ifc", "20260720_model.glb"}


def test_complete_takes_the_sting_output_glb_along():
    """The converter may run on the token-stamped output rather than the input."""
    with _Root() as root:
        claimed = hf.claim(_drop(root), root)
        (claimed.parent / "model_sting.ifc").write_text("out", encoding="utf-8")
        (claimed.parent / "model_sting.glb").write_bytes(b"glTF-ish")

        hf.complete(claimed, root, when=datetime(2026, 7, 20))

        assert not list((root / "processing").iterdir())
        assert {p.name for p in (root / "done").iterdir()} == {
            "20260720_model.ifc", "20260720_model_sting.ifc",
            "20260720_model_sting.glb",
        }


def test_redropping_the_same_filename_with_a_glb_stays_clean():
    """Repeat drops are the common case — fix model, re-export, same name."""
    with _Root() as root:
        for _ in range(2):
            claimed = hf.claim(_drop(root), root)
            claimed.with_suffix(".glb").write_bytes(b"glTF-ish")
            hf.complete(claimed, root, when=datetime(2026, 7, 20))

            assert not list((root / "processing").iterdir())
            assert [p.name for p in root.iterdir() if p.is_file()] == []

        # Four archived files, none overwritten: the collision suffix applies to
        # the .glb exactly as it does to the .ifc.
        names = sorted(p.name for p in (root / "done").iterdir())
        assert len(names) == 4, names
        assert len(set(names)) == 4, f"an archived file was overwritten: {names}"


def test_failed_run_takes_the_glb_to_failed_too():
    with _Root() as root:
        claimed = hf.claim(_drop(root), root)
        claimed.with_suffix(".glb").write_bytes(b"glTF-ish")

        hf.fail(claimed, root, "converter blew up")

        assert not list((root / "processing").iterdir())
        assert "model.glb" in {p.name for p in (root / "failed").iterdir()}


# ── _move fails fast on a vanished source (R5) ───────────────────────────────

def test_move_of_a_vanished_source_fails_fast():
    """A missing source is final — retrying for the full 30 s helps nobody."""
    import time as _time
    with _Root() as root:
        started = _time.monotonic()
        try:
            hf._move(root / "gone.ifc", root / "processing" / "gone.ifc")
            raise AssertionError("expected an OSError")
        except OSError:
            pass
        assert _time.monotonic() - started < 2.0, \
            "burned the retry budget on a source that will never reappear"
