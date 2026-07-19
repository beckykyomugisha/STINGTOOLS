# StingBridge — Quickstart

StingBridge connects **ArchiCAD** (and any IFC export) to **Planscape**. It reads
your model, derives STING ISO 19650 tokens
(`DISC-LOC-ZONE-LVL-SYS-FUNC-PROD-SEQ`), pushes them to your Planscape project,
and writes them back into the model so the tags live in the file too.

There is **no licence key**. StingBridge is useless without a Planscape login,
so your Planscape account *is* the entitlement.

---

## 1. Install

### Option A — Windows, no Python needed

1. Download `StingBridge_<version>_win64.zip` from
   [planscape.build/downloads](https://planscape.build/downloads).
2. Unzip anywhere, e.g. `C:\Tools\StingBridge`.
3. Open a terminal there and check it runs:

   ```
   stingbridge.exe --version
   ```

Add that folder to your `PATH` if you want to call `stingbridge` from anywhere.

### Option B — any platform, from source

Requires **Python 3.11 or newer**.

1. Download and unzip `StingBridge_<version>_any.zip`.
2. From inside the unzipped folder:

   ```bash
   python -m venv .venv
   # Windows:        .venv\Scripts\activate
   # macOS / Linux:  source .venv/bin/activate

   pip install wheels/stingtools_core-*.whl wheels/stingbridge-*.whl
   ```

   Or just run `run.bat` (Windows) / `./run.sh` (macOS/Linux), which does the
   above and then passes your arguments straight through to the CLI.

3. Check it:

   ```bash
   stingbridge --version
   ```

> **Why two wheels?** `stingtools-core` holds the shared Planscape wire
> contract used by every STING host (Revit, ArchiCAD, Blender). It is not on
> PyPI, so the release zip ships the built wheel next to StingBridge's. Install
> both and the package is entirely self-contained — no monorepo checkout, no
> network fetch. Developers working in the STING repo can instead run
> `pip install -e ../stingtools-core/python`.

---

## 2. Connect to Planscape

StingBridge needs four settings. Put them in a **config file** or in
**environment variables** — environment always wins, so you can override a
shared config for a one-off run.

Create `stingbridge.toml` next to where you run the command:

```toml
planscape_url        = "https://api.planscape.build"
planscape_email      = "you@example.com"
planscape_password   = "your-password"
planscape_project_id = "00000000-0000-0000-0000-000000000000"

# Optional
building_name = "Block B"   # drives the LOC token; default BLD1
write_back    = true        # write tokens back into ArchiCAD / the IFC
watch_interval = 300        # seconds between passes in `watch` mode
ifc_drop_dir  = "./IFC_DROP"
```

A `.env` file with `KEY=VALUE` lines works too. The `STING_` prefix is
optional in both formats, so `planscape_url` and `STING_PLANSCAPE_URL` are the
same setting. Point at a specific file with `--config path/to/file`.

The equivalent environment variables:

| Variable | Meaning |
|---|---|
| `STING_PLANSCAPE_URL` | Planscape server base URL |
| `STING_PLANSCAPE_EMAIL` | Login email |
| `STING_PLANSCAPE_PASSWORD` | Login password |
| `STING_PLANSCAPE_PROJECT_ID` | Target project UUID |
| `STING_BUILDING_NAME` | Building name → LOC token |
| `STING_ARCHICAD_PORT` | ArchiCAD JSON API port (`0` = auto-discover) |
| `STING_WRITE_BACK` | `0` to disable write-back |
| `STING_WATCH_INTERVAL` | Seconds between `watch` passes |
| `STING_IFC_DROP_DIR` | Folder watched by `watch-ifc` |
| `STING_CONFIG_FILE` | Explicit config-file path |

**Finding your project ID:** open the project in Planscape and copy the UUID
from the URL, or run `python get_project_id.py` from the source checkout.

> Storing a password in a file is a convenience for a workstation you control.
> On a shared machine, prefer environment variables.

---

## 3. The five commands

```bash
stingbridge sync                        # one pass; ArchiCAD must be open
stingbridge watch                       # repeat sync on a timer
stingbridge watch-ifc --drop-dir ./IFC_DROP   # watch a folder for IFC files
stingbridge process-ifc model.ifc       # process one IFC file now
stingbridge auto-publish                # trigger ArchiCAD's Publisher Set, then ingest
```

`sync` and `watch` talk to a **running ArchiCAD** over its JSON API.
`watch-ifc` and `process-ifc` need **no ArchiCAD at all** — they read IFC files,
so they work on a server, overnight, or from any BIM tool that exports IFC.

---

## 4. The IFC drop-folder workflow (recommended)

The most reliable path today. You export IFC; StingBridge does the rest.

1. Pick a folder, e.g. `C:\IFC_DROP`.
2. Start the watcher and leave it running:

   ```bash
   stingbridge watch-ifc --drop-dir C:\IFC_DROP
   ```

3. Export IFC from ArchiCAD (or Revit, or anything) into that folder.

For each file that lands, StingBridge will:

- wait for the file to finish writing (3-second debounce),
- extract elements and derive STING tokens,
- push them to your Planscape project,
- write the tokens back as a `STING_TOKENS` property set, saved alongside as
  `<name>_sting.ifc` (your original is never modified),
- convert to GLB for the Planscape 3D viewer *if* `IfcConvert` is available,
- drop a `<name>.sync_result.json` next to the file with the outcome.

The watcher re-authenticates on its own, so it can run for days.

### ArchiCAD Publisher Set automation

If ArchiCAD is open, `stingbridge auto-publish` will find your IFC Publisher
Set, trigger it, and immediately ingest the file it produces:

```bash
stingbridge auto-publish                          # auto-detect the set
stingbridge auto-publish --publisher-set "IFC Export"   # or name it
```

Point the Publisher Set's output at your drop folder and one command takes you
from model to Planscape.

---

## 5. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `STING_PLANSCAPE_EMAIL and STING_PLANSCAPE_PASSWORD must be set` | No config found. Check you are running in the folder holding `stingbridge.toml`, or pass `--config`. |
| `Invalid credentials` | Wrong email/password, or pointed at the wrong server. Confirm you can sign in at the same URL in a browser. |
| `STING_PLANSCAPE_PROJECT_ID must be set` | Missing project UUID — see §2. |
| `Cannot find ArchiCAD` | ArchiCAD is not running, or its JSON API is off. Enable *Options → Work Environment → JSON API*, or set `STING_ARCHICAD_PORT`. `watch-ifc` / `process-ifc` do not need ArchiCAD. |
| `ifcopenshell is not installed` | Source install missed a dependency: re-run the `pip install` in §1B. |
| `IfcConvert not found — skipping GLB` | Informational. 3D viewer files are skipped; tokens still sync. Install IfcOpenShell's `IfcConvert` and set `IFC_CONVERT_PATH` to enable. |
| `No recognised IFC elements found` | The IFC has none of the supported classes, or exported as a schema without them. Check the export preset includes walls/doors/MEP. |
| Every element tagged `ZZ` / `BLD1` | Zones carry no number/name, or no zones exist. `ZZ` and `BLD1` are the documented fallbacks. Set `building_name` for LOC; name/number your ArchiCAD zones for ZONE. |
| Tokens sync but do not appear in ArchiCAD | `write_back` is off, or the elements are locked / on a locked layer. |
| Watcher stops ingesting after a long run | Should not happen — expired logins refresh automatically. Check `<name>.sync_result.json` for the recorded error and report it. |

Every run logs to the console. For more detail, set `STING_LOG_LEVEL=DEBUG`
(where supported) or run with output redirected to a file when reporting an
issue.

---

## 6. Beta caveats

- The **IFC path is the verified one**. Live ArchiCAD JSON-API sync works but is
  still maturing — prefer `watch-ifc` / `process-ifc` for production.
- Zone labels are read from ArchiCAD's `Zone_ZoneNumber` / `Zone_ZoneName`
  built-in properties. If your template renames them, ZONE falls back to `ZZ`.
- ArchiCAD exposes no building name, so LOC comes from your `building_name`
  setting rather than the model.
- GLB conversion needs `IfcConvert` on the machine; it is not bundled.

Issues: <https://github.com/beckykyomugisha/STINGTOOLS/issues>
