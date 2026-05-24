"""Repo-root + shared/ifc/ discovery.

In dev: walks parent directories looking for shared/ifc/enums/_README.md.
In a bundled Blender add-on: set STINGTOOLS_SHARED_IFC=/path/to/embedded/shared/ifc.
"""

from __future__ import annotations

import os
from pathlib import Path
from typing import Optional

ENV_VAR = "STINGTOOLS_SHARED_IFC"


def find_shared_ifc(start: Optional[Path] = None) -> Path:
    """Locate the shared/ifc/ directory.

    Discovery order:
      1. Environment variable STINGTOOLS_SHARED_IFC (absolute path).
      2. Walk parents of `start` (or __file__'s parent) looking for
         shared/ifc/enums/_README.md.

    Returns absolute Path. Raises FileNotFoundError when nothing found.
    """
    if (env := os.environ.get(ENV_VAR)):
        p = Path(env).resolve()
        if not p.exists():
            raise FileNotFoundError(f"{ENV_VAR} points at {p}, which does not exist")
        return p

    cursor = (start or Path(__file__)).resolve()
    if cursor.is_file():
        cursor = cursor.parent

    sentinel = Path("shared") / "ifc" / "enums" / "_README.md"
    for _ in range(20):  # walk up to 20 levels
        candidate = cursor / sentinel
        if candidate.exists():
            return (cursor / "shared" / "ifc").resolve()
        if cursor.parent == cursor:  # filesystem root
            break
        cursor = cursor.parent

    raise FileNotFoundError(
        f"Could not locate shared/ifc/ — set {ENV_VAR} or run from inside the STING repo."
    )


def enums_dir(start: Optional[Path] = None) -> Path:
    return find_shared_ifc(start) / "enums"


def psets_dir(start: Optional[Path] = None) -> Path:
    return find_shared_ifc(start) / "psets"


def ids_dir(start: Optional[Path] = None) -> Path:
    return find_shared_ifc(start) / "ids"
