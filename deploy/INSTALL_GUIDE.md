# STING Tools — Tester Installation & Activation Guide
### Revit 2025 / 2026 · compiled build (no Visual Studio or .NET SDK needed)

Welcome, and thank you for testing. This guide takes you from a zip file to a
working, licensed plugin in about **5 minutes**. Follow it top to bottom.

> **The one thing that surprises everyone:** STING is **licensed per machine**.
> After you install it, **every button is locked** until you activate with a
> license file. Activation is **Step 2** below and takes 2 minutes. Don't skip it —
> if buttons "do nothing", you're almost certainly not activated yet.

---

## What you need before you start

- A Windows 10/11 (64-bit) PC.
- **Autodesk Revit 2025 or 2026 already installed.** (Revit ships with the .NET 8
  runtime STING needs — there is nothing else to install.)
- The zip file we sent you: `StingTools_Deploy_<date>_gated.zip`.
- About 5 minutes, and an email channel back to us (to swap your machine code for a
  license).

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

4. On the Revit ribbon you'll now see a **"STING Tools"** tab. **At first it shows
   only one button: "Activate STING".** That's expected — go to Step 2.

> **Do not move or rename the extract folder after installing.** The manifest points
> at that exact location. If you must move it, run `install.bat` again from the new
> spot.

---

## STEP 2 — Activate (about 2 minutes — REQUIRED)

The plugin is locked to your specific machine for security. Here's the swap:

1. In Revit: **STING Tools** ribbon → **Activate STING**.

2. A dialog **"Activate STING Tools"** opens. It shows your **Machine code** in a box,
   with a **Copy** button.

3. **Click Copy**, then **send that machine code to us** (paste it into an email/chat
   to **support@planscape.app** or to Davis directly). Tell us your name so we label
   the licence.

4. We generate a licence file keyed to *your* machine and send it back (usually a
   short block of text). **Paste it into the "Paste your license below" box** and
   click **Apply license**.

5. You'll see **"Activated. Please restart Revit to load STING."** → **fully close and
   reopen Revit.**

6. Now the full plugin loads: the **STING dockable panels appear on the right** and
   the ribbon fills with commands. You're ready to test.

> **Why a machine code?** It's a fingerprint of this PC (no personal data). The
> licence only works on the machine that produced the code, and it has an expiry
> date — so it's safe to email. You can't accidentally share your install with
> someone else's PC.
>
> **One machine = one code = one licence.** If you test on a second PC, repeat Step 2
> there (it will have a different code).

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
| **Only an "Activate STING" button shows, nothing else** | That's the un-activated state. Do **Step 2** (activate), then restart Revit. |
| **"Your licence has expired" / "not valid for this machine"** | Send us your machine code again (Step 2) for a fresh licence. Expiry and machine-binding are normal. |
| **Buttons are greyed out or error "not licensed"** | Not activated, or the licence didn't save. Re-open **Activate STING**, re-paste, **Apply**, restart Revit. |
| **Nothing appears in Revit after install** | Did you **fully close all Revit windows** before reopening? Confirm this file exists — paste into Explorer's address bar: `%AppData%\Autodesk\Revit\Addins\2025\StingTools.addin` (or `\2026\`). |
| **Revit shows an add-in security / load warning at startup** | Choose **"Always Load"** so STING runs every time. |
| **"It loaded but every command errors"** | Run `collect-logs.bat` and send the zip — that's a code issue we fix on our side, not your setup. |
| **You moved the folder and now it's broken** | Run `install.bat` again from the new location, restart Revit. |

**Where the logs live (if you ever need them by hand):**
- Plugin log: `<your extract folder>\CompiledPlugin\StingTools.log`
- Revit journals: `%LocalAppData%\Autodesk\Revit\Autodesk Revit 2025\Journals\`

---

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
