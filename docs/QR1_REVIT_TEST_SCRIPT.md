# QR-1 — the Revit check

Everything in the QR work is verified except the part that touches Revit. The
placement *decision* is Revit-free and unit-tested; `ImageType.Create` /
`ImageInstance.Create` is the first image-placement code in the plugin and is
confirmed **only by the compiler**. This is the script that closes that gap.

Roughly 20 minutes. Do it on a real project, not a blank one.

---

## 0. Make sure you are running THIS build

The single most wasted hour on this codebase is debugging code that never ran.
The `.addin` `<Assembly>` path moves between checkouts.

```bash
grep -h "<Assembly>" "$APPDATA/Autodesk/Revit/Addins"/*/StingTools.addin | sort -u
```

Whatever that prints is what Revit loads. To make this branch live:

```bash
cd C:/Dev/STINGTOOLS/.claude/worktrees/relaxed-goodall-bd632a && ./deploy.bat
```

Close Revit first — it holds `StingTools.dll` and ~17 dependencies, and the
Planscape Companion tray app holds them too. A half-failed copy is silent.

Then confirm the log is today's:

```
%APPDATA%\..\<plugin dir>\StingTools_20260914.log
```

The filename is **date-stamped**. Searching for `StingTools.log` finds nothing and
reads as "the plugin never logged", which is a different and much more alarming
conclusion.

---

## 1. The anchor — the thing your own sheets proved was needed

Your three exported sheets each reserve a QR cell already, in a different place,
and none of those families is in `STING_TITLE_BLOCKS.json`. This is the step that
makes the stamp land in *your* cell rather than a guessed corner.

1. Open a sheet whose title block draws the **SCAN** box.
2. Dock panel → **DOCS** → **QR Cell** (`Sheet_SetQRAnchor`).
3. Read the dialog. "Current" should say *nothing declared — falling back to a
   corner*. That is the state this whole step exists to change.
4. Pick the **two opposite corners of the empty square** next to the SCAN label.
5. It reports the recorded cell, e.g. `{"x":701,"y":85,"size":24}`.

**Expected:** the numbers match the square you clicked, ±1 mm.

**Also try the refusal:** run it again and pick a tiny box (under 12 mm). It must
**refuse and write nothing** — believing the cell was set and finding a corner
stamp on the plot is worse than being told no.

---

## 2. The stamp itself — the unverified Revit API

Take the "Stamp this sheet now" link from step 1, or **DOCS → Sheet QR**
(`Sheet_StampQR`).

**Expected:**
- a QR appears **inside** the SCAN square, not in a corner;
- the summary says `Stamped: 1`, `Images placed: 1`, `Failed: 0`;
- the title block's `TB_QR_PAYLOAD_TXT` now holds
  `https://app.planscape.build/s/<PROJECT>/<SHEET>?r=<REV>`.

**If it lands in a corner instead**, the log says why — search the log for
`fell back to`. That sentence names the reason and is the whole point of the
resolution chain; do not guess.

### 2a. Run it twice

Run `Sheet_StampQR` again on the same sheet.

**Expected: exactly ONE image.** Stacked QR images overlap exactly — invisible on
screen, muddy on the plot, and bloated in the file. The summary should report
`Images placed: 1 (replaced 1)`.

Check in Revit: *Manage → Manage Images* should list one `STING QR - <sheet>`
entry, not two.

---

## 3. A0 and A3 portrait — the two that were wrong

These are the sizes the first placement attempt got wrong (off-paper at
y = −29 mm and −5 mm). Do both.

Stamp one A0 sheet and one **A3 portrait** sheet.

**Expected:** the code is on the paper and inside the title block on both. On A3
portrait it should sit in the empty band between the APPROVED BY cell and the
PAPER SIZE cell.

---

## 4. Inspect and clear

**DOCS → Sheet QR ?** (`Sheet_InspectQR`) — read-only.

**Expected:** `QR image present` counts the sheets you stamped; it names any
title-block family lacking `PRJ_TB_SHOW_QR_CODE_BOOL` or `TB_QR_PAYLOAD_TXT`
(older families will, until regenerated).

**DOCS → Sheet QR X** (`Sheet_ClearQR`).

**Expected:** your QR goes; **any image you placed yourself stays.** Put an
ordinary image on a sheet first and confirm it survives — the cleanup matches the
`"STING QR - "` prefix only, and that contract is worth proving once.

---

## 5. Scan it with a phone

Point a normal camera app — not the Planscape app — at a stamped code.

**Expected:** the camera offers to open `app.planscape.build/s/…`. That is the
whole reason the payload is an https link rather than a custom scheme.

Opening it will land on the sheet page. Note that **app links are not verified
yet** (QR-11), so it opens in a browser rather than the app; that is expected
until the signing secrets are set and `planscape-web` is redeployed.

---

## 6. Element labels

Select a handful of **tagged** elements → **SELECT → QR Labels**
(`QR_LabelSheet`).

**Expected:**
- a new `QR-01` sheet with a grid of codes, each captioned with its tag;
- **no label runs off the sheet edge** — that was the QR-12 bug;
- untagged elements are **reported by name and skipped**, never given a blank
  label. Include one deliberately: a blank label gets stuck on an asset, scanned,
  resolves to nothing, and by then the asset is in a ceiling void.
- select two elements sharing a tag: it labels them **once** and says so.

---

## 7. Regenerate the families (optional, but it closes the loop)

**DOCS → Title Blocks → Create All** (`TitleBlock_CreateAll`).

**Expected:** every family builds, and the new params reach them —
`PRJ_TB_SHOW_QR_CODE_BOOL`, `TB_QR_PAYLOAD_TXT`, `TB_QR_ANCHOR_JSON_TXT`,
`TB_QR_SIZE_MM_TXT`, `TB_QR_PAYLOAD_TEMPLATE_TXT`.

This is also where **TB-LINES-1** shows up: an A3 family should now carry an
A3 border and **not** an A1 one drawn across it. Open the built A3-portrait `.rfa`
and check there is exactly one border rectangle.

---

## What to send back if something fails

The log line, not the symptom. `StingTools_<date>.log` in the plugin folder, and
the summary dialog text. An absent side effect never says why on its own — a
silent no-op looks identical to a wrong fix, a bad path and a command that never
ran.
