# StingTools for Bonsai

ISO 19650 tagging + IDS validation + Planscape federation, layered on
top of [Bonsai](https://bonsaibim.org) (formerly known as BlenderBIM) —
the OpenBIM add-on for Blender.

## Why on top of Bonsai

Bonsai already does the hard work: IFC parsing, geometry creation,
ifcopenshell.api transaction management, the Blender UI patterns the
OpenBIM community knows. **StingTools doesn't reinvent any of that.**
It adds the layer Bonsai doesn't ship:

| Bonsai handles | StingTools adds |
|---|---|
| IFC load / save | ISO 19650 8-segment tag grammar (Pset_StingTags) |
| Geometry + materials | Enum + project-overlay loaders (52 enums, 2 psets) |
| ifcopenshell.api mutations | IDS validation against Pset_Sting* contracts |
| Property sets, classifications | Cross-host SpatialChecker (LOC/LVL/ZONE equality) |
| BCF import / export | Planscape Server federation (sync + issue raise) |
| Drawings + sections | SHA-256-chained audit log |
| Cost / quantities | Healthcare validators, RDS docx, MGPS analysis (future) |

The two add-ons coexist as siblings — Bonsai's tabs in the sidebar,
STING's "STING" tab next to them. **You install both.** STING delegates
every IFC write through `ifcopenshell.api.run()` so Bonsai's undo /
redo / UI refresh hooks fire normally. When Bonsai isn't present,
STING falls back to direct API calls — degraded but functional.

## Status

| Capability | Status |
|---|---|
| Add-on registers in Blender 4.2 LTS / 5.x | ✅ |
| Detects Bonsai installation + version | ✅ |
| N-panel renders under sidebar `STING` tab | ✅ |
| `About STING` reports `52 enums · 2 psets · 0 drift` from live substrate | ✅ |
| `Probe Bonsai` reports Bonsai version + active IFC file | ✅ |
| Auto Tag, Tag Selected, Token writers | ❌ MVP week 3 |
| IDS validation pipeline | ❌ MVP week 4 |
| **Planscape login + cross-host IFC push (`host=bonsai`)** | ✅ WORKING — stdlib-only (urllib), verified in Blender 4.2.21 against a live server |
| Raise issue | ✅ best-effort (offline JSON fallback) |
| 16 production ops | ❌ MVP week 6-7 |

This is the **Day-1 scaffold**. Full feature roadmap on branch
`claude/stingtools-bim-research-8Kkwv` (the MVP scope doc lists every
operator + module + week).

## Install (packaged `.zip` — recommended)

Build the extension zip (one-off), then install it through Blender's GUI.

**Build the zip:**

```bash
blender --command extension build \
  --source-dir stingtools-bonsai \
  --output-dir stingtools-bonsai/dist
# → stingtools-bonsai/dist/stingtools_bonsai-0.1.0.zip
blender --command extension validate stingtools-bonsai/dist/stingtools_bonsai-0.1.0.zip
```

**Install in Blender 4.2+ (exact GUI steps):**

1. Install **Bonsai** first (Blender's extension picker → "Bonsai") — it
   provides the IFC layer (ifcopenshell) this add-on builds on.
2. `Edit → Preferences → Get Extensions`.
3. Click the **⌄** (chevron, top-right of the panel) → **Install from Disk…**.
4. Pick `stingtools-bonsai/dist/stingtools_bonsai-0.1.0.zip`.
5. **Enable** "StingTools for Bonsai" (tick the checkbox).
6. Press **`N`** in the 3D viewport → open the **STING** tab.

**Connect to Planscape + push (`host=bonsai`):**

7. In `Edit → Preferences → Add-ons → StingTools for Bonsai`, set:
   - **Server URL** (default `http://localhost:5000` — use `http`, not
     `https`, for localhost),
   - **Email** + **Password**, then click **Planscape Login** (this
     calls `POST /api/auth/login`, stores the JWT in the add-on prefs,
     and clears the password),
   - **Project ID** (the project GUID — from the project URL or
     `GET /api/projects`).
8. Open an IFC: Bonsai `File → IFC → Open`.
9. In the **STING → COORD** sub-panel, click **Push to Planscape (bonsai)**.
   Every IFC element is sent to `POST /api/projects/{id}/ifc/data` keyed on
   its 22-char IFC `GlobalId` with `host="bonsai"` (no `revitElementId`) —
   so a Bonsai element resolves cross-host against the same Revit / ArchiCAD
   `GlobalId` (`GET /ifc/mappings?ifcGuid=…` returns every host).

The HTTP client is **Python-standard-library only** (`urllib`) — no
`requests`, no `_vendor`, no `pip install` — so the push works on a stock
Blender install.

## Install (dev)

1. Clone the STINGTOOLS repo.
2. Install Bonsai first if you haven't already
   (https://docs.bonsaibim.org/quickstart/installation.html — it's the
   "Bonsai" extension in Blender 4.2+'s extension picker).
3. Symlink (or copy) `stingtools-bonsai/` into your Blender extensions
   user folder:

   | OS | Path |
   |---|---|
   | Windows | `%APPDATA%\Blender Foundation\Blender\5.1\extensions\user_default\stingtools_bonsai\` |
   | macOS   | `~/Library/Application Support/Blender/5.1/extensions/user_default/stingtools_bonsai/` |
   | Linux   | `~/.config/blender/5.1/extensions/user_default/stingtools_bonsai/` |

4. Enable in `Edit → Preferences → Extensions → StingTools for Bonsai`.
5. Open the 3D viewport sidebar (press `N`) → tab **STING**.
6. Click **Probe Bonsai** → confirms Bonsai is detected.
7. Click **About STING** → confirms the 52 enums + 2 psets load from
   the live `shared/ifc/` substrate.

If the substrate isn't found, set the environment variable before
launching Blender:

```bash
export STINGTOOLS_SHARED_IFC=/path/to/STINGTOOLS/shared/ifc
```

## How `stingtools_core` is resolved

The add-on's `__init__.py` injects two candidate paths onto `sys.path`:

1. `../stingtools-core/python/` (when the add-on is symlinked from a
   repo checkout — dev mode).
2. `./_vendor/stingtools_core/` (when the add-on is built into a
   packaged extension `.zip` — release mode).

The MVP build step copies `stingtools-core/python/stingtools_core/`
into `_vendor/` so a deployed extension is self-contained.

## Layout

```
stingtools-bonsai/
├── blender_manifest.toml       extension manifest (Blender 4.2+)
├── __init__.py                 bl_info + register / unregister + sys.path bridge
├── README.md                   this file
├── core/                       Bonsai coexistence layer
│   └── bonsai.py               BonsaiBridge: detect + delegate IFC mutations
├── ops/                        bpy.types.Operator subclasses
│   ├── about.py                substrate load + Bonsai status diagnostic
│   ├── reload_substrate.py     cache invalidation
│   └── bonsai_probe.py         re-detect Bonsai + dump capabilities
├── ui/                         bpy.types.Panel subclasses
│   └── panel_main.py           STING N-panel root
├── handlers/                   bpy.app.handlers (MVP weeks 4+)
├── planscape/                  Planscape REST client (MVP week 5)
├── workflows/                  JSON preset chains (MVP week 6)
└── _vendor/                    vendored stingtools_core for built extensions
```

## Coexistence rules

1. **Never own the IFC file.** Bonsai owns it. STING reads via
   `BonsaiBridge.active_ifc()`; STING writes via `BonsaiBridge.add_pset()`
   which calls `ifcopenshell.api.run("pset.add_pset", ...)`.
2. **Never duplicate Bonsai's UI.** Bonsai handles file load,
   geometry, classifications, BCF. STING does not. STING's panel
   carries only ISO 19650 + Planscape + IDS validation operations.
3. **Never write IFC without a Bonsai-aware transaction.** Bonsai
   tracks edits for its undo stack and Blender's depsgraph. Direct
   `ifcopenshell.file.add()` calls bypass that — don't use them.
4. **Degrade gracefully when Bonsai is absent.** STING still loads,
   the N-panel still renders, the substrate still validates. The user
   gets a banner "Bonsai not detected (standalone mode)" and writes
   route through ifcopenshell.api directly with reduced undo fidelity.

## Next steps

- Items 1–3 of the Day-1 plan are complete (see commit `ef7bb2a1` on
  `claude/stingtools-bim-research-8Kkwv`).
- Week-2 of the MVP scope picks up at:
  - `core/state.py` — cached registry singleton
  - `handlers/on_load.py` — IFC-open trigger
  - `ops/tagging.py` — the first real op: Auto Tag for active selection
