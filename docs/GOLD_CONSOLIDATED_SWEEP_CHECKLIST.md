# GOLD Consolidated — Revit Smoke-Test / Sweep Checklist

**Build under test:** `claude/gold-consolidated` · GOLD `StingTools.dll` md5 `418D98B779E5` · license Stable `4681-584E-784F-0868-4E48` · build 0 err / 0 warn.
**Exit gate:** all **[CRIT]** items pass → PR `gold-consolidated → main`. Any [CRIT] fail → stop, note it, don't PR.

## Setup (do first)
- [ ] Open a **saved COPY** of a representative project — has M/E/P elements, some already tagged, ≥2 plan views, ≥1 titleblock/sheet. (Never sweep on the live model.)
- [ ] Back up `…\STING_PLACEMENT_GOLD\data\TagFamilies\` (Propagate overwrites .rfa).
- [ ] Open the log for tailing: `…\STING_PLACEMENT_GOLD\StingTools_<today>.log`. Keep it visible.
- [ ] Confirm no other STING session is deploying to GOLD during the sweep.

---

## 0 · Load & License  [CRIT]
- [ ] Revit starts; **STING panels load** (Main + Electrical + Plumbing + HVAC). Log: `STING Tools dockable panel loaded successfully`.
- [ ] **No activation dialog, no "Busy — try again"** on first command. (Stable license + flip-proof gate.)
- [ ] Run any trivial command (e.g. SELECT → a category selector). Log shows `RunCommand<…>: start` **and** `: done`. → license gate passes.
- [ ] Log has **no** `[ERROR]` on load except the known-benign `LiveClashUpdater.Register … AddInId` line.

## 1 · Tags & Set depth (E1–E5 — this session)  [CRIT for 1a–1e]
> Prereq: the placed tag family's Label must read **`ASS_DISPLAY_TXT`** (that's what mask/pad/order drive).
- [ ] **1a — Tab opens, no hang.** Open TAG STUDIO → Tokens & Depth. Log: `TokenDepthPresets: N preset(s) loaded`. Panel responsive. **(the preset-hang fix)**
- [ ] **1b — Presets recall.** Pick **FM Handover** → controls populate (all segments on, 4-digit, T10). Pick **Tight-space (2-digit compact)** → mask compacts, 2-digit. Press **Set depth** → active-view tags reflect it.
- [ ] **1c — Preset save.** Type a new name in the preset combo → **Save** → status "Preset saved". Confirm `<project>\_BIM_COORD\token_depth_presets.json` created + the name reappears in the dropdown.
- [ ] **1d — E1 segment order.** Change **Segment order** combo → Set depth → tag segments **reorder**; a segment you unchecked stays hidden regardless of order.
- [ ] **1e — E2 per-category.** In a view with **doors + mechanical equipment** → Set depth once → **door tags render 2-digit/compact, equipment 4-digit/full** (different treatment, one press). Tune via `data\STING_TOKEN_DEPTH_OVERRIDES.json` if needed.
- [ ] **1f — E5 live.** Tick **"Live — apply to active view…"** → toggle a mask checkbox → tags update in ~0.4 s **without** pressing Set depth. Untick → back to manual.
- [ ] **1g — E4 per-view.** Set style A in View 1 → Set depth. Switch to View 2, set style B → Set depth. Switch back to View 1 → panel **repopulates style A**. Confirm `_BIM_COORD\view_token_configs.json`.
- [ ] **1h — Separator.** Switch separator to **Slash** → Set depth → tag shows `HVAC/SUP/…` (not hyphen). **(the separator fix)**
- [ ] **1i — SEQ zero-pad.** Set **2-digit** → Set depth → SEQ shows 2 digits in the display tag.
- [ ] **1j — Reversibility.** Re-check all 8 segments + Hyphen + 4-digit + T10 → Set depth → the **full canonical tag** is restored. (Confirms display-only, non-destructive: `ASS_TAG_1` never changed.)
- [ ] Log across §1: no unhandled exceptions; only expected `Set depth live display …` info lines.

## 2 · Propagate Universal
- [ ] **2a — Duct smoke test** (already passed once): CREATE TAGS → SETUP → Advanced setup → **Propagate Universal** → pick master → **CHOOSE (Duct)** → OK. Result dialog "1 propagated, 0 failed"; Excel report OK. Verify a Duct tag: universal rows, tier toggle (Set depth 1↔10), type variants, badges survive.
- [ ] **2b — (optional, only after 2a) Scale = ALL** → verify ~3 sample categories (Pipe, Electrical Equipment, Door) got the universal label + no failures in the Excel report. Atomic save means any FAILED row left that family untouched.

## 3 · MEP print-readiness (gold-all + punch-list)  [CRIT for 3a–3e]
- [ ] **3a — M plan** (`mep-plan`/`mep-hvac-duct`): produce it → **pipes AND ducts are VISIBLE** (not greyed/halftoned) via `corp-standard-hvac`.
- [ ] **3b — E plan** (`elec-power`/`elec-lighting`): elec categories visible via `corp-standard-elec`.
- [ ] **3c — P plan** (`plumb-drainage`): pipes visible via `corp-standard-plumb`.
- [ ] **3d — Per-category auto-tag** on an MEP plan → mechanical equipment / air terminals / fixtures get **real tag families** (not blank).
- [ ] **3e — fm-asset-location**: produce it → the 3 equipment categories tag with **real families** (Mechanical/Electrical Equipment + Plumbing Fixture tags), not blank. **(punch-list fix)**
- [ ] **3f — Routing** (optional): `E/*/SPOOL` → elec-spool, `E/*/COORD` → elec-coord reachable.
- [ ] **3g — Schedules** (optional): mech-equip / elec-panel / valve schedule → **title block populates**.

## 4 · Title-blocks (gold-all-integration work)  [CRIT for 4a]
- [ ] **4a — Titled sheet**: place/produce a sheet with a STING titleblock → **value cells render on Revit 2025** (SYSTEM / DISCIPLINE / revision etc. visible, not blank). **(seed→augment fix)**
- [ ] **4b — seed↔spec** resolves without error (log clean during title-block ops).
- [ ] **4c — `${PRJ_ORG_*}` params** populate from Project Information on the sheet.

## 5 · Regression / other gold-all work
- [ ] **5a — Load Shared Params** runs; log shows spec-driven binding (`ResolvedBindings: … scoped + … universal`) and the `Binding coverage GAP` line is **benign** (only true spares).
- [ ] **5b — Stale markers**: nudge a tagged element's geometry → element marked stale (StingStaleMarker) if enabled.
- [ ] **5c — A basic Electrical command** (e.g. panel schedule or a calc) runs without error.
- [ ] **5d — Symbols**: `Symbols_Validate` (or Build Seeds) completes clean.
- [ ] **5e — Whole-session log review**: no *new* `[ERROR]`/unhandled exceptions beyond the known LiveClashUpdater line.

## 6 · Sign-off
- [ ] All **[CRIT]** items pass.
- [ ] Note any FAIL here → __________________________________________________
- [ ] If clean → open PR `claude/gold-consolidated → main` and delete feature branches **after** it merges.
- [ ] If any [CRIT] fails → do **not** PR; report the failing item + log excerpt.

---
### Log-reading cheat-sheet
- Command ran: `RunCommand<X>: start` … `: done`.
- Presets loaded: `TokenDepthPresets: N preset(s) loaded`.
- Set-depth live: `Set depth live display: mask/pad applied to N element(s)`.
- Binding: `SharedParamGuids.ResolvedBindings: … scoped + … universal`.
- Known-benign: `LiveClashUpdater.Register failed … AddInId`.
### Where things land
- Presets: `<project>\_BIM_COORD\token_depth_presets.json` · per-view: `view_token_configs.json` · per-category corp: `data\STING_TOKEN_DEPTH_OVERRIDES.json`.
- Propagated tag families: `…\STING_PLACEMENT_GOLD\data\TagFamilies\*.rfa`.
