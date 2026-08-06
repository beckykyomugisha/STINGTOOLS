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


# Resolve `stingtools_core`. When packaged, it is provided by the wheel declared
# in blender_manifest.toml (`wheels = [...]`), which Blender installs into the
# extension's isolated environment — so a plain import works and NO sys.path
# manipulation happens. That is what keeps Blender's extension-policy check clean
# (the old _vendor/sys.path approach raised "top level module" + "sys.path"
# policy violations). The fallback below runs ONLY in a monorepo dev checkout,
# where the wheel isn't installed and core lives at ../stingtools-core/python.
try:
    import stingtools_core  # noqa: F401 — provided by the bundled wheel when packaged
except ImportError:
    import sys
    from pathlib import Path

    _THIS = Path(__file__).resolve()
    for _cand in (_THIS.parent.parent, _THIS.parent / "_vendor"):
        _core = _cand / "stingtools-core" / "python"
        if _core.exists() and str(_core) not in sys.path:
            sys.path.insert(0, str(_core))
        _vendored = _cand / "stingtools_core"
        if _vendored.exists() and str(_cand) not in sys.path:
            sys.path.insert(0, str(_cand))


def register():
    """Blender entry point. Registers preferences + operator + panel classes."""
    from . import prefs, ui, ops, handlers
    prefs.register()
    ui.register()
    ops.register()
    handlers.register()
    print("[STING] add-on registered")


def unregister():
    """Blender exit point. Unregisters in reverse order."""
    from . import prefs, ui, ops, handlers
    handlers.unregister()
    ops.unregister()
    ui.unregister()
    prefs.unregister()
    print("[STING] add-on unregistered")


if __name__ == "__main__":
    register()
