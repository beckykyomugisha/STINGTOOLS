# stingtools-bonsai/_vendor/

Empty in a dev checkout (the add-on is symlinked from the repo and walks
up to find `stingtools-core/python/` + `shared/ifc/`). Populated only by
the **packaged-extension build step** (`build.py`) for distribution.

`build.py` vendors **two** things a self-contained `.zip` needs, because
neither lives inside the add-on folder in the repo and an installed
extension can't walk up to reach them:

```
_vendor/
├── stingtools_core/     ← copied from ../stingtools-core/python/stingtools_core
└── shared/ifc/          ← copied from ../shared/ifc   (enums + psets + ids)
```

Resolution in a packaged install (see `../__init__.py`):

- **core package** — `__init__.py` puts `_vendor/` on `sys.path`, so
  `import stingtools_core` resolves to `_vendor/stingtools_core/`.
- **substrate** — `__init__.py` sets `STINGTOOLS_SHARED_IFC` to
  `_vendor/shared/ifc` (via `os.environ.setdefault`), so the core's
  `paths.find_shared_ifc()` finds the enums / psets / ids without a
  repo-root walk-up.

Without **both**, an installed `.zip` fails: missing `stingtools_core`
→ "core: NOT LOADED"; missing `shared/ifc` → `About STING` errors with
"shared/ifc not found — set STINGTOOLS_SHARED_IFC".

To build:

```bash
python build.py                          # auto-detect blender, else manual zip
python build.py --blender /path/blender  # force the official toolchain
```

By default `build.py` empties `_vendor/` again after zipping so the dev
tree stays clean. Everything here except this `_README.md` is
`.gitignore`d.
