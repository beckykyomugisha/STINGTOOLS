"""StingTools for Bonsai — ISO 19650 tagging + IDS validation + Planscape federation.

Sits on top of Bonsai (the OpenBIM add-on for Blender, formerly known
as BlenderBIM). Bonsai owns the IFC layer (parsing, geometry,
ifcopenshell.api mutations). StingTools adds:

  - the 8-segment STING tag grammar (Pset_StingTags)
  - corporate enum + project-overlay loaders (52 enums, 2 psets)
  - IDS validation against Pset_Sting* (sting-tag-grammar.ids,
    sting-spatial-codes.ids)
  - Planscape Server federation (cross-host IFC ingest, issue raise,
    SignalR live-coordination)
  - SHA-256-chained audit log

Day-1 scaffold. The N-panel + diagnostic operators prove the add-on
registers and renders correctly on Blender 4.2 LTS through 5.x with
or without Bonsai installed. Real tagging / validation / sync ops
land per the MVP scope doc on branch
`claude/stingtools-bim-research-8Kkwv`.
"""

from __future__ import annotations

bl_info = {
    "name": "StingTools for Bonsai",
    "author": "Planscape Limited",
    "version": (0, 1, 0),
    "blender": (4, 2, 0),
    "location": "View3D > Sidebar > STING",
    "description": "ISO 19650 tagging + IDS validation + Planscape federation, on top of Bonsai",
    "warning": "Day-1 scaffold — most ops are placeholders. See README.md + MVP scope.",
    "doc_url": "https://stingtools.io",
    "category": "3D View",
}


# Make `stingtools_core` resolvable when it lives in the repo's
# stingtools-core/python/ folder during dev. In a packaged extension
# it'll be vendored under _vendor/.
import os
import sys
from pathlib import Path

_THIS = Path(__file__).resolve()
_VENDOR = _THIS.parent / "_vendor"
_REPO_ROOT_CANDIDATES = [_THIS.parent.parent, _VENDOR]
for _cand in _REPO_ROOT_CANDIDATES:
    _core = _cand / "stingtools-core" / "python"
    if _core.exists() and str(_core) not in sys.path:
        sys.path.insert(0, str(_core))
    _vendored = _cand / "stingtools_core"
    if _vendored.exists() and str(_cand) not in sys.path:
        sys.path.insert(0, str(_cand))

# Make the shared/ifc substrate (enums + psets + IDS) reachable.
#
# In dev the add-on is symlinked from a repo checkout, so the core
# package's own walk-up discovery (paths.find_shared_ifc) finds the
# repo's shared/ifc/ — nothing to do here.
#
# In a packaged .zip the substrate is vendored at _vendor/shared/ifc/.
# Walk-up can't reach it (it's a self-contained install dir), so point
# STINGTOOLS_SHARED_IFC at the vendored copy. setdefault so a user- or
# dev-set env var always wins.
_VENDORED_SUBSTRATE = _VENDOR / "shared" / "ifc"
if (_VENDORED_SUBSTRATE / "enums" / "_README.md").exists():
    os.environ.setdefault("STINGTOOLS_SHARED_IFC", str(_VENDORED_SUBSTRATE))


def register():
    """Blender entry point. Registers preferences + operator + panel classes."""
    from . import prefs, ui, ops
    prefs.register()
    ui.register()
    ops.register()
    print("[STING] add-on registered")


def unregister():
    """Blender exit point. Unregisters in reverse order."""
    from . import prefs, ui, ops
    ops.unregister()
    ui.unregister()
    prefs.unregister()
    print("[STING] add-on unregistered")


if __name__ == "__main__":
    register()
