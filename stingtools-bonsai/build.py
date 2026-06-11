#!/usr/bin/env python3
"""Build the installable StingTools-for-Bonsai extension .zip.

The add-on depends on two things that live OUTSIDE its own folder in the
repo:

  1. the ``stingtools_core`` Python package  (../stingtools-core/python/)
  2. the ``shared/ifc/`` substrate            (../shared/ifc/)

In a dev checkout the add-on finds both by walking up to the repo root.
A packaged ``.zip`` is a self-contained install dir — it can't walk up
to anything — so both must be VENDORED into ``_vendor/`` before zipping.
That vendoring is the step the original scaffold was missing, which is
why an installed ``.zip`` reported "core: NOT LOADED".

This script:

  1. clears + repopulates ``_vendor/stingtools_core`` and
     ``_vendor/shared/ifc`` from the repo,
  2. builds the ``.zip`` — via Blender's official extension toolchain
     when a ``blender`` executable is found (``--blender`` or PATH),
     otherwise a manual zip that mirrors what Blender produces
     (honouring ``[build] paths_exclude_pattern`` from the manifest),
  3. unless ``--keep-vendor`` is passed, empties ``_vendor/`` again so
     the dev tree stays clean (the dev-stays-empty contract).

Usage
-----
    python build.py                         # auto-detect blender, else manual
    python build.py --blender /path/blender # force the official toolchain
    python build.py --manual                # force the manual zip
    python build.py --keep-vendor           # leave _vendor/ populated
    python build.py --no-clean              # don't wipe _vendor/ first

The artefact lands in ``dist/stingtools_bonsai-<version>.zip``.
"""

from __future__ import annotations

import argparse
import fnmatch
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ADDON_DIR = Path(__file__).resolve().parent
REPO_ROOT = ADDON_DIR.parent
VENDOR_DIR = ADDON_DIR / "_vendor"
DIST_DIR = ADDON_DIR / "dist"

CORE_SRC = REPO_ROOT / "stingtools-core" / "python" / "stingtools_core"
SUBSTRATE_SRC = REPO_ROOT / "shared" / "ifc"

CORE_DST = VENDOR_DIR / "stingtools_core"
SUBSTRATE_DST = VENDOR_DIR / "shared" / "ifc"

# Vendored content we never want shipped: caches, the package's own tests,
# and build/dist metadata.
_VENDOR_PRUNE = ("__pycache__", "*.pyc", "tests", "*.egg-info", ".pytest_cache")


def _log(msg: str) -> None:
    print(f"[build] {msg}")


def _fail(msg: str) -> "NoReturn":  # type: ignore[name-defined]
    print(f"[build] ERROR: {msg}", file=sys.stderr)
    raise SystemExit(1)


def read_version() -> str:
    """Pull the ``version = "x.y.z"`` line out of the manifest."""
    manifest = (ADDON_DIR / "blender_manifest.toml").read_text(encoding="utf-8")
    m = re.search(r'^\s*version\s*=\s*"([^"]+)"', manifest, re.MULTILINE)
    if not m:
        _fail("could not read version from blender_manifest.toml")
    return m.group(1)


def read_exclude_patterns() -> list[str]:
    """Parse ``[build] paths_exclude_pattern`` from the manifest.

    A tiny hand-rolled reader (no tomllib dependency for 3.8-3.10 hosts).
    Falls back to a sane default if the section is malformed.
    """
    manifest = (ADDON_DIR / "blender_manifest.toml").read_text(encoding="utf-8")
    m = re.search(r"paths_exclude_pattern\s*=\s*\[(.*?)\]", manifest, re.DOTALL)
    patterns: list[str] = []
    if m:
        patterns = re.findall(r'"([^"]+)"', m.group(1))
    # Dev-only files that must never ship inside the package, even if the
    # manifest forgets them.
    for extra in ("dist/", "build.py", ".gitignore", "*.egg-info"):
        if extra not in patterns:
            patterns.append(extra)
    return patterns


def _prune(root: Path) -> None:
    for pattern in _VENDOR_PRUNE:
        for path in root.rglob(pattern):
            if path.is_dir():
                shutil.rmtree(path, ignore_errors=True)
            else:
                path.unlink(missing_ok=True)


def clean_vendor() -> None:
    """Empty _vendor/ back to just its _README.md."""
    for child in VENDOR_DIR.iterdir():
        if child.name == "_README.md":
            continue
        if child.is_dir():
            shutil.rmtree(child, ignore_errors=True)
        else:
            child.unlink(missing_ok=True)


def vendor() -> None:
    if not CORE_SRC.is_dir():
        _fail(f"stingtools_core not found at {CORE_SRC}")
    if not (SUBSTRATE_SRC / "enums" / "_README.md").exists():
        _fail(f"shared/ifc substrate not found at {SUBSTRATE_SRC}")

    _log(f"vendoring stingtools_core  <-  {CORE_SRC}")
    if CORE_DST.exists():
        shutil.rmtree(CORE_DST)
    shutil.copytree(CORE_SRC, CORE_DST)
    _prune(CORE_DST)

    _log(f"vendoring shared/ifc       <-  {SUBSTRATE_SRC}")
    if SUBSTRATE_DST.exists():
        shutil.rmtree(SUBSTRATE_DST)
    shutil.copytree(SUBSTRATE_SRC, SUBSTRATE_DST)
    _prune(SUBSTRATE_DST)

    # Sanity: the two facts an installed .zip needs to be true.
    assert (CORE_DST / "__init__.py").exists(), "vendored core missing __init__.py"
    assert (SUBSTRATE_DST / "enums" / "_README.md").exists(), "vendored substrate incomplete"
    _log("vendor OK - core package + substrate present under _vendor/")


def _excluded(rel: Path, patterns: list[str]) -> bool:
    """Mirror Blender's exclude semantics: a pattern ending in '/' matches a
    directory anywhere in the path; otherwise fnmatch against the name and
    the full posix relpath."""
    parts = rel.parts
    name = rel.name
    posix = rel.as_posix()
    for pat in patterns:
        if pat.endswith("/"):
            d = pat.rstrip("/")
            if d in parts:
                return True
        elif fnmatch.fnmatch(name, pat) or fnmatch.fnmatch(posix, pat):
            return True
    return False


def build_manual(version: str) -> Path:
    """Zip the add-on dir honouring the manifest's exclude patterns.

    Produces the canonical Blender extension layout: a FLAT archive with
    ``blender_manifest.toml`` at the root. That is what
    ``extensions.package_install_files`` expects — the manifest must NOT be
    nested under an extra directory. Mirrors what ``blender --command
    extension build`` produces.
    """
    patterns = read_exclude_patterns()
    DIST_DIR.mkdir(exist_ok=True)
    out = DIST_DIR / f"stingtools_bonsai-{version}.zip"
    if out.exists():
        out.unlink()

    count = 0
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zf:
        for path in sorted(ADDON_DIR.rglob("*")):
            if path.is_dir():
                continue
            rel = path.relative_to(ADDON_DIR)
            if _excluded(rel, patterns):
                continue
            zf.write(path, rel.as_posix())
            count += 1
    _log(f"manual zip: {count} files -> {out}")
    return out


def build_blender(blender: str, version: str) -> Path:
    DIST_DIR.mkdir(exist_ok=True)
    cmd = [
        blender, "--command", "extension", "build",
        "--source-dir", str(ADDON_DIR),
        "--output-dir", str(DIST_DIR),
    ]
    _log("running: " + " ".join(cmd))
    res = subprocess.run(cmd, capture_output=True, text=True)
    sys.stdout.write(res.stdout)
    sys.stderr.write(res.stderr)
    if res.returncode != 0:
        _fail(f"blender extension build failed (exit {res.returncode})")
    out = DIST_DIR / f"stingtools_bonsai-{version}.zip"
    if not out.exists():
        # Blender names by id+version; find whatever zip it produced.
        zips = sorted(DIST_DIR.glob("*.zip"))
        if not zips:
            _fail("blender reported success but produced no .zip")
        out = zips[-1]
    return out


def find_blender(explicit: str | None) -> str | None:
    if explicit:
        return explicit
    return shutil.which("blender")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--blender", help="path to the blender executable")
    ap.add_argument("--manual", action="store_true", help="force manual zip (skip Blender)")
    ap.add_argument("--keep-vendor", action="store_true", help="leave _vendor/ populated after build")
    ap.add_argument("--no-clean", action="store_true", help="don't wipe _vendor/ before vendoring")
    args = ap.parse_args()

    version = read_version()
    _log(f"building stingtools_bonsai v{version}")

    if not args.no_clean:
        clean_vendor()
    vendor()

    blender = None if args.manual else find_blender(args.blender)
    if blender:
        _log(f"using Blender extension toolchain: {blender}")
        out = build_blender(blender, version)
    else:
        if not args.manual:
            _log("no 'blender' on PATH — falling back to manual zip "
                 "(pass --blender <path> to use the official toolchain)")
        out = build_manual(version)

    # Verify the artefact actually carries the two missing pieces. Match on
    # suffix so this holds for both the flat layout Blender produces
    # (manifest at root) and any nested layout.
    with zipfile.ZipFile(out) as zf:
        names = zf.namelist()
    has_core = any(n.endswith("_vendor/stingtools_core/__init__.py") for n in names)
    has_substrate = any(n.endswith("_vendor/shared/ifc/enums/_README.md") for n in names)
    has_manifest = any(n == "blender_manifest.toml" or n.endswith("/blender_manifest.toml")
                       for n in names)
    if not has_manifest:
        _fail("built zip has no blender_manifest.toml — not a valid extension")
    if not has_core:
        _fail("built zip is missing _vendor/stingtools_core — core would not load")
    if not has_substrate:
        _fail("built zip is missing _vendor/shared/ifc — substrate would not load")
    # dist/ must not be bundled into the package (zip-in-zip bloat).
    if any("/dist/" in n or n.startswith("dist/") for n in names):
        _fail("built zip bundles dist/ — add 'dist/' to manifest paths_exclude_pattern")
    _log("verified: zip has manifest + vendored core + substrate, no dist/ bloat")

    if not args.keep_vendor:
        clean_vendor()
        _log("cleaned _vendor/ (pass --keep-vendor to retain)")

    size_kb = out.stat().st_size / 1024
    _log(f"DONE -> {out}  ({size_kb:.0f} KB)")
    _log("install: Blender -> Preferences -> Get Extensions -> (v) -> Install from Disk")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
