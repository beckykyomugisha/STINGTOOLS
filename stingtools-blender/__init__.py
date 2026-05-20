"""STINGTools for Blender — ISO 19650 BIM tagging + IDS validation + Planscape federation.

Day-1 scaffold. The N-panel + "About STING" operator prove the add-on
registers and renders correctly on Blender 4.2 LTS through 5.x.
Real ops + tagging logic land per the MVP scope doc on
`claude/stingtools-bim-research-8Kkwv`.
"""

from __future__ import annotations

bl_info = {
    "name": "STINGTools",
    "author": "Planscape Limited",
    "version": (0, 1, 0),
    "blender": (4, 2, 0),
    "location": "View3D > Sidebar > STING",
    "description": "ISO 19650 BIM tagging + IDS validation + Planscape federation",
    "warning": "Day-1 scaffold — most ops are placeholders. See docs/MVP.md.",
    "doc_url": "https://stingtools.io",
    "category": "3D View",
}


# Make `stingtools_core` resolvable when it lives in the repo's
# stingtools-core/python/ folder during dev. In a packaged extension
# it'll be vendored under _vendor/.
import sys
from pathlib import Path

_THIS = Path(__file__).resolve()
_REPO_ROOT_CANDIDATES = [_THIS.parent.parent, _THIS.parent / "_vendor"]
for _cand in _REPO_ROOT_CANDIDATES:
    _core = _cand / "stingtools-core" / "python"
    if _core.exists() and str(_core) not in sys.path:
        sys.path.insert(0, str(_core))
    _vendored = _cand / "stingtools_core"
    if _vendored.exists() and str(_cand) not in sys.path:
        sys.path.insert(0, str(_cand))


def register():
    """Blender entry point. Registers all operator + panel classes."""
    from . import ui, ops
    ui.register()
    ops.register()
    print("[STING] add-on registered")


def unregister():
    """Blender exit point. Unregisters in reverse order."""
    from . import ui, ops
    ops.unregister()
    ui.unregister()
    print("[STING] add-on unregistered")


if __name__ == "__main__":
    register()
