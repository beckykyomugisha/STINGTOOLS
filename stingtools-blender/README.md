# STINGTools for Blender — Day-1 scaffold

ISO 19650 BIM tagging + IDS validation + Planscape federation, as a
Blender 4.2 LTS / 5.x extension.

This is the **Day-1 scaffold** referenced in the BlenderBIM MVP scope
doc on branch `claude/stingtools-bim-research-8Kkwv`. It proves the
add-on registers, the N-panel renders, and `stingtools-core` loads the
real `shared/ifc/` substrate. Real ops (tagging, validation, sync) land
per the 8-week MVP plan.

## Status

| Capability | Status |
|---|---|
| Add-on registers in Blender 4.2 / 5.x | ✅ |
| N-panel renders under sidebar `STING` tab | ✅ |
| `About STING` operator confirms 52 enums + 2 psets load | ✅ |
| `Reload Substrate` clears registry caches | ✅ |
| Auto Tag, Tag Selected, etc. | ❌ scaffold only |
| IDS validation pipeline | ❌ MVP week 4 |
| Planscape sync | ❌ MVP week 5 |

## Install (dev)

From a Blender Python console or via the `Edit → Preferences → Extensions`
panel:

1. Clone the STINGTOOLS repo.
2. Symlink (or copy) `stingtools-blender/` into your Blender extensions
   folder, e.g.:
   - Windows: `%APPDATA%\Blender Foundation\Blender\5.1\extensions\user_default\stingtools\`
   - macOS:   `~/Library/Application Support/Blender/5.1/extensions/user_default/stingtools/`
   - Linux:   `~/.config/blender/5.1/extensions/user_default/stingtools/`
3. Enable in `Edit → Preferences → Extensions → STINGTools`.
4. Open the 3D viewport sidebar (press `N`) → look for the **STING** tab.

## How `stingtools_core` is resolved

The add-on's `__init__.py` injects two candidate paths onto `sys.path`:

1. `../stingtools-core/python/` (when the add-on is symlinked from a
   repo checkout).
2. `./_vendor/stingtools_core/` (when the add-on is built into a
   packaged extension `.zip`).

The MVP build step copies `stingtools-core/python/stingtools_core/` into
`_vendor/` so a deployed extension is self-contained.

If neither works, set the environment variable:

```bash
export STINGTOOLS_SHARED_IFC=/path/to/STINGTOOLS/shared/ifc
```

before launching Blender.

## Layout

```
stingtools-blender/
├── blender_manifest.toml              extension manifest (Blender 4.2+)
├── __init__.py                        bl_info + register / unregister
├── README.md                          this file
├── core/                              shared logic (delegates to stingtools-core)
├── ops/                               bpy.types.Operator subclasses
│   ├── about.py                       diagnostic + substrate load check
│   └── reload_substrate.py            cache invalidation
├── ui/                                bpy.types.Panel subclasses
│   └── panel_main.py                  STING N-panel root
├── handlers/                          bpy.app.handlers (MVP weeks 4+)
├── planscape/                         Planscape REST client (MVP week 5)
├── workflows/                         JSON preset chains (MVP week 6)
└── _vendor/                           vendored stingtools_core for built extensions
```

## Next steps (per MVP scope)

Items 1–3 of the Day-1 plan are now complete on this branch:

- ✅ `stingtools-core/python/` — Python package with enum + pset
  loaders, tag grammar, spatial checker, Planscape client, audit log
- ✅ `POST /api/projects/{id}/ifc/data` + `ExternalElementMapping` on
  Planscape Server
- ✅ This scaffold — Blender add-on registers + N-panel renders

Week-2 work picks up at `core/state.py` (registry singleton),
`handlers/on_load.py` (IFC-open trigger), `ops/tagging.py` (the first
real operator — Auto Tag for active selection).
