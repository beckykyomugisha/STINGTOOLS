# Building StingBridge release artifacts

Two artifacts ship per release, and the gated downloads area expects both:

| Label   | What it is                                    |
|---------|-----------------------------------------------|
| `win64` | one-file PyInstaller EXE                      |
| `any`   | the two wheels + `run.bat` / `run.sh` launchers |

## 1. The EXE (`win64`)

From the **repo root**, in a clean venv:

```bash
python -m venv .buildvenv
.buildvenv/Scripts/pip install -r StingBridge/requirements.txt pyinstaller
.buildvenv/Scripts/pip install ./stingtools-core/python      # NOT -e, see below
.buildvenv/Scripts/pyinstaller --clean --noconfirm StingBridge/StingBridge.spec
# -> dist/stingbridge.exe
```

The build is defined by [`StingBridge.spec`](StingBridge.spec). Change the build
by changing that file — never by typing flags at the shell. The 0.1.0-beta.3 EXE
was built from a hand-typed command, and the flag that mattered
(`collect_data_files("ifcopenshell")`) survived only in a shell history.

### Two traps, both of which have already bitten

**`collect_data_files("ifcopenshell")` is load-bearing.** ifcopenshell opens JSON
schema tables by path at runtime; PyInstaller cannot see them. Drop that line and
the EXE still builds, still passes `--version`, and still processes a fresh IFC —
then fails write-back on a **re-drop**, because the map is only consulted when a
`STING_TOKENS` pset already exists. Verified both ways:

```
without collect_data_files:  run 1 errors: 0   run 2 errors: 1
                             ("No such file ... entity_to_type_map_2x3.json")
with collect_data_files:     run 1 errors: 0   run 2 errors: 0
```

**Install `stingtools-core` non-editable.** `pip install -e` resolves through a
path hook PyInstaller's analysis does not follow: the build succeeds and the EXE
dies at startup with `ModuleNotFoundError: No module named 'stingtools_core'`.

## 2. The wheels zip (`any`)

```bash
python -m build --wheel --outdir dist/wheels StingBridge
python -m build --wheel --outdir dist/wheels stingtools-core/python
```

Zip them under a single top-level `StingBridge_<version>/` folder containing
`wheels/`, `LICENSE.txt`, `QUICKSTART.md`, `run.bat`, `run.sh`. The launchers are
version-agnostic (`pip install --find-links wheels stingbridge`), so they need no
edit between releases.

## 3. Before publishing — the smoke test that actually matters

Run **both** passes against a live API. A single fresh-IFC run is not sufficient
and has already let a broken EXE through:

```bash
stingbridge.exe process-ifc model.ifc            # expect: N SEQ minted, errors: 0
stingbridge.exe process-ifc model_sting.ifc      # expect: NO minting, errors: 0,
                                                 #         SEQ values unchanged
```

The second pass is the regression test for both the SEQ re-mint (Phase 211) and
the packaging bug above.

## 4. Publishing

See `marketing-site/tools/release-download.mjs`. It computes sha256 and size from
the files themselves and prints the catalogue block to paste into
`functions/api/_lib/downloads/catalog.ts`, so the catalogue cannot drift from the
uploaded objects. Keep prior versions listed.
