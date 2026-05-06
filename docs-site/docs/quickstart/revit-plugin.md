# Connect Revit

The Planscape Revit plugin is a single compiled `.addin` that supports Revit 2025, 2026, and 2027 — one download covers all three. Installation takes about five minutes.

## System requirements

- Autodesk **Revit 2025, 2026, or 2027**
- Windows 10 (build 19041+) or Windows 11
- **.NET 8 Desktop Runtime** — Revit 2025+ ships with this; if you're on a fresh build, install from Microsoft
- Network access to `api.planscape.app` on port 443
- Approximately 80 MB of free disk space for the plugin and shared parameter files

!!! note
    Revit 2023 and 2024 are **not** supported. The plugin uses APIs introduced in Revit 2025 and cannot be downgraded without a re-write.

## Step 1 — Download the .addin

1. Sign in to your Planscape dashboard.
2. Go to **Settings → Plugin**.
3. Click **Download Revit plugin (latest)** — you'll receive a ZIP containing:
    - `StingTools.addin` (XML manifest)
    - `StingTools.dll` (the compiled plugin)
    - `Newtonsoft.Json.dll`, `ClosedXML.dll` (dependencies)
    - `data/` (shared parameter files, JSON config, ISO 19650 templates)

## Step 2 — Install the manifest

Copy `StingTools.addin` to one of:

- **Per-machine** (recommended for shared workstations):
  `C:\ProgramData\Autodesk\Revit\Addins\2025\` (or `2026\`, `2027\`)
- **Per-user** (no admin rights required):
  `%APPDATA%\Autodesk\Revit\Addins\2025\`

## Step 3 — Install the binaries

Open `StingTools.addin` in a text editor — you'll see an `<Assembly>` line pointing at where it expects to find the DLL, e.g.:

```xml
<Assembly>C:\ProgramData\Planscape\StingTools.dll</Assembly>
```

Copy `StingTools.dll`, the dependency DLLs, and the entire `data/` folder to that directory. The folder layout should look like:

```
C:\ProgramData\Planscape\
├── StingTools.dll
├── Newtonsoft.Json.dll
├── ClosedXML.dll
└── data\
    ├── MR_PARAMETERS.txt
    ├── PARAMETER_REGISTRY.json
    └── …
```

## Step 4 — Restart Revit and activate

Restart Revit. Open any project — the **STING Tools** dockable panel appears on the right and a new **STING Tools** ribbon tab is visible under **Add-Ins**.

Activate your licence:

1. Open the dockable panel, switch to the **BIM** tab.
2. Click **Activate Licence**.
3. Paste the licence key from your Planscape dashboard (Settings → Licence).
4. The status bar at the bottom of the panel turns green: ✓ Licensed.

## Step 5 — First sync

1. Open the Revit project you want to coordinate.
2. In the dockable panel, **BIM → Planscape Sync → Login**.
3. Enter your Planscape email and password (same as the web dashboard).
4. The plugin connects, retrieves your project list, and asks which project to link.
5. Select the project — the first sync indexes all tagged elements. For a 30k-element model this typically takes under a second.

The sync indicator in the status bar shows green when up to date, amber while syncing, and red on connection error.

## Common issues

??? warning "DLL blocked by Windows"
    If you downloaded the plugin via a browser, Windows may flag the DLLs as "from the internet" and block them. Right-click each DLL → **Properties** → tick **Unblock** → OK. Restart Revit.

??? warning "Plugin doesn't appear after restart"
    Check the path in `StingTools.addin` matches where you actually copied `StingTools.dll`. The path is absolute — relative paths won't work. Open the Revit log file (`%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit 2025\Journal*.log`) and search for `StingTools` — it will report exactly why it failed to load.

??? warning "Wrong Revit version"
    The `.addin` manifest contains `<VendorId>` and target Revit version metadata. The same `.addin` file works for 2025/2026/2027, but if you copy it into the addins folder for an older Revit version (2023/2024) it will silently not load.

??? warning "Firewall blocking sync"
    The plugin needs outbound HTTPS access to `api.planscape.app`. On corporate networks you may need to whitelist this domain. The plugin retries with exponential backoff and reports errors in the status bar.

## Next steps

- [Install the mobile app](mobile.md) for your site coordinators
- [Create your first project](first-project.md)
- Read the [ISO 19650 tag format](../concepts/iso19650-tag.md) before you start tagging
