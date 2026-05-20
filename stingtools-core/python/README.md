# stingtools-core — shared Python package

Dual-language core for the STING substrate. This is the Python half; the
.NET half lives in `stingtools-core/dotnet/` (future).

The package re-packages the contracts in `shared/ifc/` into Python so the
BlenderBIM plugin, the ArchiCAD plugin (future), the Tekla connector
(future), the Planscape Server IFC-ingest pipeline, and headless tooling
all read from the same canonical loaders.

## Layout

```
stingtools-core/python/
├── pyproject.toml
├── README.md
├── stingtools_core/
│   ├── __init__.py             package root, version, public API
│   ├── paths.py                resolve shared/ifc/ relative to repo root or env var
│   ├── enums/                  enum XML loaders + overlay resolver + models
│   ├── psets/                  Pset XML loaders + models
│   ├── tag_grammar/            8-segment tag builder + per-segment validator
│   ├── ids/                    ifctester wrapper (skip-if-missing)
│   ├── planscape/              REST client + audit log
│   └── spatial/                cross-entity equality checks
└── tests/
    └── test_enum_loader.py     smoke tests
```

## Install

```bash
cd stingtools-core/python
pip install -e .
```

Or vendor it inside a Blender add-on by copying the `stingtools_core/`
folder under `_vendor/`.

## Public API (stable)

```python
from stingtools_core import (
    EnumRegistry,         # load + project-overlay all enums
    PsetRegistry,         # load all psets
    TagGrammar,           # build + validate 8-segment tags
    PlanscapeClient,      # REST client to Planscape Server
    SpatialChecker,       # cross-entity LOC/LVL/ZONE equality
    AuditLog,             # SHA-256 chained JSONL
)
```

## Dependency policy

- Pure-Python where possible. No native extensions.
- Optional integrations (`ifctester`, `ifcopenshell`, `signalrcore`) are
  imported lazily via `_try_import()` — package loads cleanly even when
  they're missing.
- Python 3.11 baseline (matches Blender 5.x default).

## Repo-root discovery

`stingtools_core.paths.find_shared_ifc()` walks parent directories
looking for `shared/ifc/enums/_README.md`. Override via the
`STINGTOOLS_SHARED_IFC` environment variable when the repo isn't on
disk (e.g. embedded in a Blender add-on bundle).
