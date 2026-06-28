# BOQ & Cost Manager — Close the 9 QS Gaps (implementation prompt)

You are a terminal coding agent on the **StingTools** C# Revit plugin. The
`docs/BOQ_QS_LAYMANS_GUIDE.md` §10 lists **9 honest gaps** in the BOQ & Cost
Manager. This prompt is the brief to close them. Implement in the order below
(value-first), compile-verifying, committing, and deploying each, and **strike
each gap from the guide's §10 as you close it** so the living doc stays true.

---

## 0. Environment, build & deploy — READ FIRST (this bit has bitten us repeatedly)

- **Branch & checkout:** work in the **main checkout `C:\Dev\STINGTOOLS`** on
  **`claude/placement-centre-review-audit`** — the single unified branch (BOQ +
  placement + seed-family all live here). Verify: `git branch --show-current`.
  Do **not** create a worktree; do **not** run `deploy.bat`/`deploy-gold.bat`
  from any other checkout (that repoints Revit's addin away and "loses" the build).
- **The live DLL path:** Revit loads the assembly named in
  `%APPDATA%\Autodesk\Revit\Addins\2025\StingTools.addin`. The team's convention
  is the **GOLD** folder `C:\Dev\STING_PLACEMENT_GOLD\StingTools.dll`, refreshed by
  **`deploy-gold.bat`** (run from THIS checkout). If a deploy "doesn't show up",
  FIRST `grep -i '<Assembly>' "$APPDATA/Autodesk/Revit/Addins/2025/StingTools.addin"`
  and check it points where you deployed — don't assume.
- **Compile-verify (headless works via Nice3point — mandatory before each commit):**
  ```bash
  dotnet build StingTools/StingTools.csproj -c Release -t:Rebuild --nologo -v minimal   # 0 Error(s)
  ```
- **Deploy:** Revit locks the live DLL while open — the human must close Revit,
  then run **`deploy-gold.bat`** from `C:\Dev\STINGTOOLS` (build + copy to GOLD +
  pin addin). Confirm the GOLD DLL timestamp changed; the human restarts Revit.
- **Git:** one commit per gap (imperative subject + `Co-Authored-By: Claude Opus
  4.8 <noreply@anthropic.com>`). **Do NOT push or merge.** Log each gap in
  `docs/CHANGELOG.md` and remove/strike its line in `docs/BOQ_QS_LAYMANS_GUIDE.md`
  §10. State each gap's Revit smoke test (the human runs it).

## 1. Conventions to REUSE (don't reinvent, don't fork)

- **Inline, no popups.** Results render through `StingResultPanel` (its
  `Builder.Show()` auto-routes to the BOQ Actions pane when hosting; it has an
  inline action bar with an **Open file** button via `SetCsvPath`). Pickers go
  through `StingListPicker` (inline when hosted, modal fallback when a transaction
  is open). New tabs render their own inline content in the panel's `_mainTabs`.
- **Persistence:** project-scoped JSON under `<project>/_BIM_COORD/…` (same as
  `boq_links.json`, `boq_ui_state.json`, `boq_schedule.json`).
- **Reuse engines:** `BOQCostManager` (build + rate pipeline), `RateProviderRegistry`
  / `IRateProvider` / `RateResult`, `BOQTenderConfig`, `CarbonFactorResolver`,
  `V6/LabourHoursEngine`, `Core/PaymentCert`, `Core/Variation`, `Core/Evm`,
  `Scheduling4DEngine`, `StingWizardDialog`, `StingProgressDialog`. **Never fork.**
- **Data facts you'll use:** `BOQLineItem` carries `RateUGX`, `RateSource`
  (`CSV|COBie|Default|Manual|Override|Carbon|Interpolated|QS`), `RateConfidence`
  (0–100), `Quantity`, `TotalUGX`, `NRM2Section`, `Source` (`Model|Manual|
  ProvisionalSum|…`), `SourceModel`, `Note`. `BOQDocument.AverageRateConfidence`.
  `BOQTenderConfig` has `PreliminariesPct/ContingencyPct/OverheadProfitPct/VatPct`.

---

## G1 — Rate-gap report (HIGHEST VALUE; do first)
**Why:** auto-rates cover only part of the bill; users must know exactly which
items still need a price before they trust the total or run the QS round-trip.

**Build:** a read-only command `BOQ_RateGapReport` (wire a button into the Actions
tab's **QS ROUND-TRIP (P3)** group, next to Export/Import QS Bill). It scans
`BOQCostManager.BuildBOQDocument(doc).AllItems` and classifies each modelled item:
- **No rate** (`RateUGX <= 0`),
- **Low confidence** (`RateConfidence < threshold`, default 70 — configurable via a
  `COST_RATE_CONFIDENCE_FLOOR` config key),
- **Defaulted** (`RateSource == "Default"`).

Render **inline** via `StingResultPanel`: headline metrics (priced %, value at
risk = Σ TotalUGX of flagged rows, count by reason), a per-NRM2-section table of
gap counts, and a top-N list of the biggest unpriced items by quantity×(best
guess). Add **`SetCsvPath`** to a CSV written to `<project>/_BIM_COORD/` so the
user gets an **Open file** button listing every gap row (Ref, description, qty,
unit, current rate, confidence, reason) — that CSV doubles as the "give this to
the QS" worklist.

**Acceptance:** the report lists exactly the unpriced/low-confidence items, totals
the value at risk, and exports a CSV. Renders inline, no popup.
**Smoke test:** Actions → Rate Gap Report → pane shows priced %, value-at-risk, and
an Open-file CSV of the gaps.

---

## G2 — Provisional-sum reconciliation trail
**Why:** today you just overwrite the PS number; there's no record of estimate →
actual, so cost movement from PS is invisible.

**Where:** `BOQ/MeasuredAddition.cs`, the PS rows (`Source == ProvisionalSum`), the
existing reconcile command (`grep ReconcileProvisional`).

**Build:** give each PS row a persisted reconciliation record in
`<project>/_BIM_COORD/boq_provisionals.json`: `{ id, description, originalSum,
adjustments:[{date,amount,note}], reconciledActual, status: Open|PartlyReconciled|
Closed }`. Upgrade **Reconcile Provisionals** (Actions → COST REPORT P4.4) to an
**inline editable form** (per §1 / the P2.2 ShowInlineForm pattern): list each PS
with its original sum + an editable "actual" + note; on save, persist and stamp the
delta. Surface Σ(actual − original) as a "**provisional-sum movement**" metric and
feed it into Anticipated Final Cost.

**Acceptance:** PS estimate vs actual is recorded, persists across reopen, and the
movement shows in the cost report. **Smoke test:** add a PS → Reconcile → enter an
actual → reopen project → the actual + movement persist.

---

## G3 — Preliminaries schedule (replace the flat %)
**Why:** prelims/contingency/overhead are single percentages — fine for a quick
estimate, not a defensible prelims bill.

**Where:** `BOQ/BOQTenderConfig.cs` (`PreliminariesPct` …), the tender dialog, the
grand-total maths in `BOQModels.cs`.

**Build:** an optional **built-up preliminaries schedule**: a data-driven list of
prelim line items (site setup, management staff, welfare, scaffolding, insurances,
…) seeded from `Data/STING_PRELIMS_TEMPLATE.json` (corporate baseline + project
override at `_BIM_COORD/boq_prelims.json`), each with a value or a %-of-works basis.
Add a **Prelims** affordance (button in the tender flow / a small inline editable
grid) where the user keeps the flat % **or** switches to the itemised schedule; the
grand-total maths uses whichever is active (keep flat % as the default so nothing
regresses). Itemised prelims must export in the XLSX BOQ as their own section.

**Acceptance:** a project can produce an itemised prelims schedule that rolls into
the grand total and the export; flat % still the default. **Smoke test:** switch to
itemised prelims, add 3 lines, see the grand total + export reflect them.

---

## G4 — Labour / plant / material split per rate
**Why:** NRM2 rates ideally break into labour + plant + material; today a rate is
one number, so labour-content and plant analysis are impossible for measured work.

**Where:** `BOQLineItem` (add `LabourUGX`, `PlantUGX`, `MaterialUGX` — nullable, +
`Clone` copy), `V6/LabourHoursEngine.cs` + `Data/Labour/STING_LABOUR_RATES.csv`,
the rate providers (`Rates/`).

**Build:** extend the rate pipeline so a `RateResult` can carry an optional L/P/M
split (where the source provides one — e.g. a rate-card column or a CSV with split
columns). When present, populate the three new `BOQLineItem` fields; when absent,
leave null (rate stays a single number — no regression). Add **optional columns**
(Labour / Plant / Material) to the BOQ grid (toggle via the existing Columns
dropdown) and to the XLSX export. Provide a **labour-content rollup** (Σ labour by
trade/section) using `LabourHoursEngine` — render inline.

**Acceptance:** items with split rates show L/P/M; items without are unchanged; a
labour rollup is available. **Smoke test:** price an item with a split rate → the
L/P/M columns populate → labour rollup totals correctly.

---

## G5 — Carbon: from starter factors toward EPD-grade
**Why:** embodied carbon is currently indicative (ICE DB + overrides), not tied to
real product EPDs.

**Where:** `BOQ/CarbonFactorResolver.cs`, `Data/MATERIAL_LOOKUP.csv`, the carbon
columns/metric.

**Build:** (a) allow a **per-element / per-material EPD reference** (a new optional
param or `_BIM_COORD/boq_epd_map.json` keyed by material) that overrides the ICE
default with a verified A1–A3 figure + source + data-quality flag; (b) surface a
**carbon data-quality** indicator (verified-EPD vs database-default vs missing),
mirroring rate-confidence; (c) a **carbon-gap report** (like G1, but for materials
with no/weak factor). Keep ICE as the fallback. Render inline; export the carbon
breakdown with its data-quality column.

**Acceptance:** EPD overrides apply where mapped; a data-quality flag and carbon-gap
list exist; ICE fallback intact. **Smoke test:** map one material to an EPD → its
row's carbon + quality flag change; carbon-gap report lists the rest.

---

## G6 — Schedule / EVM tab maturity (P2 follow-on)
**Why:** the Schedule tab (phase grid + S-curve + EVM) is new and basic.

**Where:** the P2 Schedule tab in `UI/BOQCostManagerPanel.cs`,
`BIMManager/SchedulingCommands.cs` (`Scheduling4DEngine`), `Core/Evm`.

**Build (incremental):** (a) **per-period EVM** (not just latest) with the S-curve
plotting PV/EV/AC over real periods from the phase grid; (b) **forecast band**
(EAC range) on the curve; (c) **milestone markers** + slippage flags from
`MilestoneRegister`; (d) link **% complete** in the phase grid to the Payment-Cert
%-complete so they're one source of truth. Keep everything inline; persist period
actuals to `_BIM_COORD/boq_schedule.json`.

**Acceptance:** the S-curve shows PV/EV/AC over periods with a forecast band;
milestones render; %-complete is shared with certs. **Smoke test:** enter actuals
for 3 periods → curve + SPI/CPI update; a milestone shows; cert %-complete matches.

---

## G7 — First-run QS wizard (guided onboarding)
**Why:** a Revit-only newcomer has no guided path; the layman's guide is the only
on-ramp.

**Where:** `UI/StingWizardDialog.cs` (reusable multi-page wizard base),
`UI/ProjectSetupWizard.*` (pattern reference).

**Build:** a **"Cost Setup" wizard** launched from a header button (and auto-offered
once per project when the BOQ is first opened with no budget/rates set). Pages:
(1) what this does (1 screen, plain English, links the layman's guide); (2) model
readiness check (rooms? phases? — runs a quick scan and reports gaps); (3) set
budget + currency; (4) choose pricing path (auto / QS round-trip / manual) and, if
round-trip, one-click **Export QS Bill**; (5) markups (flat % or prelims schedule);
(6) finish → Refresh + summary. Persist "wizard completed" in `boq_ui_state.json`
so it doesn't nag. It must **orchestrate existing commands**, not duplicate them.

**Acceptance:** a first-timer can go from open-model to priced-draft + baseline via
the wizard, calling existing commands. **Smoke test:** open BOQ on a fresh project →
wizard offered → complete it → bill is built, budget set, snapshot saved.

---

## G8 — Background the BOQ rebuild (big-model performance)
**Why:** `BOQCostManagerPanel.RefreshAsync` runs `BuildBOQDocument` on the UI
thread; large host models pause Revit on Refresh. (Links are already cached.)

**Where:** `UI/BOQCostManagerPanel.cs` `RefreshAsync`, `UI/StingProgressDialog.cs`.

**Build:** move the heavy compute off the UI thread **safely**: read all Revit data
the take-off needs into plain POCOs **on the API thread** (Revit API must not be
touched off-thread), then run grouping/rate/aggregation on a background `Task` with
`StingProgressDialog` (cancellable), and marshal the finished `BOQDocument` back to
the UI thread for `RefreshDisplay`. If a clean API-thread/compute split proves
infeasible, instead just show the progress dialog during the synchronous build (so
the user sees it's working) and **document why** the full split was deferred — do
NOT call Revit API off-thread.

**Acceptance:** Refresh on a large model shows progress and doesn't freeze the UI
(or, if deferred, shows a progress dialog + a documented reason). No Revit API off
the API thread. **Smoke test:** Refresh a large model → progress shows, UI stays
responsive, result correct.

---

## G9 — QS sign-off + "uncertified" stamp on outputs
**Why:** the tool must not be mistaken for certified QS output; a real QS still
signs off.

**Where:** the export commands (`BOQExportCommand`, `BOQProfessionalExportCommand`),
`BOQTenderConfig`, a new sign-off record.

**Build:** a per-project sign-off record in `_BIM_COORD/boq_signoff.json`
(`signedBy, role, date, scope, snapshotRef`). Until signed, every exported BOQ /
cost-plan / tender carries a clear **"DRAFT — not a certified bill of quantities;
subject to QS verification"** watermark/footer. A **Sign-off** action (Actions tab)
records the QS name/role/date against the current snapshot and removes the draft
mark for exports of that signed snapshot. Surface the sign-off status as a metric.

**Acceptance:** unsigned exports are clearly marked draft; a recorded sign-off (tied
to a snapshot) clears the mark for that snapshot. **Smoke test:** export → see DRAFT
mark → Sign-off → re-export the signed snapshot → mark gone; status metric updates.

---

## Definition of done
- One gap per commit; compile-verify (`0 Error(s)`, `-t:Rebuild`) before each;
  deploy per gap via `deploy-gold.bat` (Revit closed) and give the human its smoke
  test. **No popups** (inline per §1); **no engine forks**; `_BIM_COORD` JSON for
  persistence; `StingLog` for errors.
- After closing each gap, **remove its bullet from `docs/BOQ_QS_LAYMANS_GUIDE.md`
  §10** and add a `docs/CHANGELOG.md` entry. **Do NOT push or merge.**
- Suggested order: **G1 → G8 → G2 → G3 → G4 → G5 → G6 → G7 → G9** (value + risk
  balanced; G8 early because perf affects every other test on big models).
