# STING Tools — Tester Installation & Activation Guide
### Revit 2025 / 2026 · compiled build (no Visual Studio or .NET SDK needed)

Welcome, and thank you for testing. This guide takes you from a zip file to a
working, licensed plugin in about **5 minutes**. Follow it top to bottom.

> **Licensing in one line:** a fresh install runs a **90-day trial** straight away —
> nothing to send or paste. To keep using STING after that, add a licence (**Step 2**).
> If we sent you a **portable licence** (`StingTools.lic`), put it next to
> `install.bat` before installing and the installer activates this PC for you.

---

## What you need before you start

- A Windows 10/11 (64-bit) PC.
- **Autodesk Revit 2025 or 2026 already installed.** (Revit ships with the .NET 8
  runtime STING needs — there is nothing else to install.)
- The zip file we sent you: `StingTools_Deploy_<date>_gated.zip`.
- About 5 minutes. A licence is optional for the first 90 days.

---

## STEP 1 — Install (about 60 seconds)

1. **Copy the zip to the test PC** and **extract it to a permanent folder you won't
   delete**, for example:

   ```
   C:\STINGTOOLS
   ```

   > ⚠️ Keep the folder structure intact — `install.bat` must sit **next to** the
   > `CompiledPlugin` folder. Don't extract "loose" into Downloads and then move
   > files around.

2. **Double-click `install.bat`.**
   - If Windows SmartScreen warns ("Windows protected your PC"): click **More info → Run anyway**. (It's an unsigned in-house tool — that warning is normal.)
   - It detects your Revit versions and prints **"Installed for Revit 2025 / 2026 …"** in green.
   - It needs **no admin rights** — it writes a small per-user manifest only.

3. **Fully close Revit** if it's open (all windows), then **reopen it**.

4. On the Revit ribbon you'll now see a **"STING Tools"** tab and the STING panels.
   On the trial there is also an **"Activate STING"** button — it shows how many trial
   days are left, and is where you paste a licence (Step 2).

> **Do not move or rename the extract folder after installing.** The manifest points
> at that exact location. If you must move it, run `install.bat` again from the new
> spot.

---

## STEP 2 — Activate (before the 90-day trial ends)

There are two kinds of licence. Either one is pasted the same way.

- **Portable licence** — works on any PC until its expiry date (usually 90 days). We
  send you `StingTools.lic`; no machine code needed. Easiest: put it next to
  `install.bat` and run the installer again. Or paste it as in step 4 below.
- **Machine licence** — tied to one PC. Swap your machine code for it:

1. In Revit: **STING Tools** ribbon → **Activate STING**.

2. A dialog **"Activate STING Tools"** opens. It shows your **Machine code** in a box,
   with a **Copy** button.

3. **Click Copy**, then **send that machine code to us** (paste it into an email/chat
   to **support@planscape.app** or to Davis directly). Tell us your name so we label
   the licence.

4. We generate a licence file keyed to *your* machine and send it back (usually a
   short block of text). **Paste it into the "Paste your license below" box** and
   click **Apply license**.

5. You'll see **"Activated."** with the expiry date. If the trial had already ended,
   **fully close and reopen Revit** to load the panels.

6. Now the full plugin loads: the **STING dockable panels appear on the right** and
   the ribbon fills with commands. You're ready to test.

> **Why a machine code?** It's a fingerprint of this PC (no personal data). A machine
> licence only works on the machine that produced the code, and it has an expiry date.
> A portable licence skips the code but still expires — treat the file like a key.
>
> **A second PC** gets its own 90-day trial, and a portable licence works there too.
> A machine licence does not — repeat Step 2 there for a new one.

---

## STEP 3 — Open the panels & start testing

STING is mostly **dockable panels** docked to the right of the Revit window. Open
them from the **STING Tools ribbon** (each panel has a toggle button):

| Panel | What it's for |
|---|---|
| **Main STING panel** | 9 tabs: Select / Organise / Docs / Temp / Create / View / Model / **BIM** / Tags. General tagging, sheets, modelling, BIM management. |
| **BOQ & Cost Manager** | Bill of Quantities, costing, tenders, payment certs, EVM (see the separate **BOQ/QS + PM guide**). |
| **STING Electrical** | Cable/feeder sizing, fault current, arc flash, SLD, panel schedules. |
| **STING Plumbing** | Water supply / drainage sizing, routing, audits. |
| **STING HVAC** | Loads, duct/pipe sizing, refrigerant, fabrication. |
| **Placement Center** | Rule-based fixture placement. |

If a panel is hidden: **STING Tools ribbon → the panel's toggle button**, or
**Revit View tab → User Interface**.

**Open a small test project first** (not a huge production model) so things respond
quickly while you click around.

---

## IF SOMETHING GOES WRONG — this is the important part

When a button errors, crashes, or "does nothing", we need two things to fix it fast:

**A) A screenshot** of the error dialog or the screen.

**B) The logs.** Double-click **`collect-logs.bat`** in your extract folder. It makes
   **`STING_logs_<date>.zip` on your Desktop** containing:
   - `StingTools.log` (the plugin's own log)
   - the newest Revit journal file

**C) Send us BOTH** (screenshot + the `STING_logs_*.zip`), and **tell us**:
   - **which button/command** you clicked,
   - **what you expected** to happen, and
   - **what actually happened**.

That trio — button + expectation + log — is exactly what pins down a bug.

---

## Quick troubleshooting

| Symptom | Fix |
|---|---|
| **Only an "Activate STING" button shows, nothing else** | The 90-day trial has ended and there is no valid licence. Do **Step 2**, then restart Revit. |
| **"Your licence has expired" / "not valid for this machine"** | Ask us for a fresh licence — a portable one, or a machine one for your code (Step 2). Expiry and machine-binding are normal. |
| **Buttons are greyed out or error "not licensed"** | Not activated, or the licence didn't save. Re-open **Activate STING**, re-paste, **Apply**, restart Revit. |
| **Nothing appears in Revit after install** | Did you **fully close all Revit windows** before reopening? Confirm this file exists — paste into Explorer's address bar: `%AppData%\Autodesk\Revit\Addins\2025\StingTools.addin` (or `\2026\`). |
| **Revit shows an add-in security / load warning at startup** | Choose **"Always Load"** so STING runs every time. |
| **"It loaded but every command errors"** | Run `collect-logs.bat` and send the zip — that's a code issue we fix on our side, not your setup. |
| **You moved the folder and now it's broken** | Run `install.bat` again from the new location, restart Revit. |

**Where the logs live (if you ever need them by hand):**
- Plugin log: `<your extract folder>\CompiledPlugin\StingTools.log`
- Revit journals: `%LocalAppData%\Autodesk\Revit\Autodesk Revit 2025\Journals\`

---

## Shared tag library (optional, set by whoever sends you the zip)

If your team keeps its STING tag families on a network drive, the installer points STING
at it. Whoever builds the zip puts the folder path in `content_library.txt` next to
`install.bat` (see `content_library.example.txt`); `install.bat` then saves it for you and
says whether the folder's `Tags` subfolder can be reached.

- You can also set or change it yourself: `install.bat -ContentLibrary \\server\share\STING\ContentLibrary`.
- STING reads `<that folder>\Tags` first and its own shipped families second. Off the
  network, it uses its own copy, so tagging still works.
- The setting lives in `%APPDATA%\STING\sting_content.json` (`"content_root"`). An
  environment variable `STING_CONTENT_LIB` overrides it.
- Families on the share win over the shipped ones, so only **Promote Library** (TAG
  STUDIO) should write to it. It records every family it publishes, and **Load** (Tag
  families) warns when a family on the share differs from what this plugin ships or was
  changed without a promotion. Promote again after installing a newer build.
- Promote Library offers to move families on the share that the plugin no longer ships
  into `_retired\<date>`; nothing is deleted.
- The first load in a project reads every family over the network and takes longer on a
  slow link. Later loads skip families the project already has.

## Updating to a newer build

When we send a newer zip:
1. Run **`uninstall.bat`** (optional but clean), or just overwrite.
2. **Replace the `CompiledPlugin` folder** with the new one (keep the same extract folder).
3. Run **`install.bat`** again, restart Revit.
4. Your licence stays valid (it's stored separately, per machine) — no need to re-activate unless it expired.

## Uninstall

Double-click **`uninstall.bat`**, then restart Revit. (This removes the manifest; it
doesn't delete the folder or your licence.)

---

## What's in this package (for the curious)

```
StingTools_Deploy\
├─ install.bat / install.ps1        ← run this to install
├─ uninstall.bat / uninstall.ps1    ← run this to remove
├─ collect-logs.bat / collect-logs.ps1  ← run this when reporting a problem
├─ INSTALL_GUIDE.md                 ← this file
├─ README_DEPLOY.txt                ← the short version
└─ CompiledPlugin\
   ├─ StingTools.dll                ← the plugin
   ├─ StingTools.addin              ← manifest template (the installer rewrites the path)
   ├─ *.dll                         ← bundled dependencies
   └─ data\                         ← the plugin's reference data (rates, rules, configs)
```

There are **no passwords or secrets** in this package. Your licence is generated by
us and stored only on your machine at
`C:\ProgramData\Planscape\StingTools\StingTools.lic`.

---

*Questions or anything confusing? Email **support@planscape.app** with a screenshot —
we'd rather you ask than get stuck.*
