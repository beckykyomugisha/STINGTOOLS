# -*- mode: python ; coding: utf-8 -*-
"""PyInstaller spec for the one-file StingBridge EXE.

    Build (from the repo root, in a clean venv):

        python -m venv .buildvenv
        .buildvenv/Scripts/pip install -r StingBridge/requirements.txt pyinstaller
        .buildvenv/Scripts/pip install ./stingtools-core/python     # NOT -e
        .buildvenv/Scripts/pyinstaller --clean --noconfirm StingBridge/StingBridge.spec

    Output: dist/stingbridge.exe

    The `pip install ./stingtools-core/python` must be a REAL install, not
    `-e`. An editable install resolves through a path hook that PyInstaller's
    analysis does not follow, so the build succeeds and the EXE then dies at
    startup with `ModuleNotFoundError: No module named 'stingtools_core'`.
    A release build installs the wheel anyway; this just makes a dev build
    match it.

WHY THIS FILE EXISTS
--------------------
The 0.1.0-beta.3 EXE was built with a hand-typed command line. The first
attempt omitted ifcopenshell's data files and shipped an EXE whose IFC
write-back worked on a fresh file and failed on a RE-DROP — the one case that
release existed to fix. The flags that fixed it lived nowhere but a shell
history and a CHANGELOG sentence, so the next release would have regressed
silently, and silently in the worst way: the smoke test everyone runs first
(process a new IFC) passes.

Encoding the build here makes the fix reviewable, diffable, and impossible to
forget. Change the build by changing this file.

THE NON-OBVIOUS PART
--------------------
`collect_data_files("ifcopenshell")` is load-bearing. ifcopenshell is not pure
Python: it ships JSON schema tables next to its modules (notably
`util/entity_to_type_map_2x3.json`) that are read at runtime, and PyInstaller's
module graph cannot see a file that is only ever opened by path. Dropping that
line reproduces the beta.3 bug exactly.

The failure is delayed, which is what makes it dangerous: the map is consulted
by `ifcopenshell.util.element.get_psets`, which the token writer only calls when
a STING_TOKENS pset ALREADY EXISTS — i.e. on the second pass over the same file.
Anything that tests a single fresh IFC will report success.
"""

from PyInstaller.utils.hooks import collect_data_files, collect_submodules

# Data files first — see the module docstring. This is the line the beta.3
# build was missing.
datas = collect_data_files("ifcopenshell")

hiddenimports = (
    # ifcopenshell dispatches to schema-specific submodules by name at runtime,
    # so static analysis under-collects it.
    collect_submodules("ifcopenshell")
    # stingtools_core is a real declared dependency, resolved from the installed
    # wheel. Collected explicitly so a monorepo build and a wheel build produce
    # the same EXE.
    + collect_submodules("stingtools_core")
    + collect_submodules("StingBridge")
)

a = Analysis(
    # NOT bridge.py. Freezing that file directly makes it __main__ with no
    # package context and its relative imports die at startup with
    # "attempted relative import with no known parent package". The shim is
    # imported as part of the package, so they resolve normally.
    ["_pyinstaller_entry.py"],
    # Repo root + the core package, so the build works from a monorepo checkout
    # as well as from an environment where both are pip-installed.
    pathex=[".", "..", "../stingtools-core/python"],
    binaries=[],
    datas=datas,
    hiddenimports=hiddenimports,
    hookspath=[],
    runtime_hooks=[],
    excludes=[],
    noarchive=False,
)

pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    a.binaries,
    a.datas,
    [],
    name="stingbridge",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,          # UPX mangles some native deps; size is not the constraint
    console=True,       # it is a CLI — a windowed build would swallow all output
    disable_windowed_traceback=False,
    onefile=True,
)
