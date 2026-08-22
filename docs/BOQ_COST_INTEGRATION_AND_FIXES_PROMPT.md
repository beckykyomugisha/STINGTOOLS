# Implementation Brief — BOQ/Cost: integrate P1–P4 into one stack + fix verified bugs

> **For the implementing agent.** Self-contained. Read fully before acting.
> This consolidates four already-built phases into one coherent branch and fixes
> the bugs found in review. Work in two stages with a **hard STOP** between them
> (Stage A integration → human sanity-check → Stage B fixes).

---

## 0. Background (what exists, verified)

A prior agent delivered the BOQ/Cost upgrade across four branches. P1–P3 are
cleanly stacked; P4 is **not** — it was branched off the P0 fix, never saw P1's
model changes, and carries an unrelated stowaway commit.

| Phase | Branch | Tip SHA | Base |
|---|---|---|---|
| P0 (button-revive fix) | `claude/revive-cost-buttons` | `fc44e29e0` | pre-existing |
| P1 takeoff exclusion + aggregation | `claude/boq-p1-aggregation` | `3f7611792` | `main` |
| P2 location column + grouping + print profiles | `claude/boq-p2-grouping` | `b6aa405fb` | P1 |
| P3 QS Excel round-trip | `claude/boq-p3-qs-roundtrip` | `9491c06d9` | P2 |
| P4 valuations/variations/EVM | `claude/boq-p4-cost-control` | `73c3b6efa` | **P0 (not P3)** |
| ⚠️ stray `.rfa` deletion on P4 | — | `0570d989d` | on top of P4 — **DROP** |

**Verified-good (do not "fix" these — they are correct):**
- P1 aggregation: `BOQLineItem` has `SimilarCount`, `ConstituentElementIds = new List<long>()`, `AggregationKey`; `Clone()` copies them; exclusion filter is real + data-driven (`ImportInstance` + `OST_DetailComponents`/`OST_FilledRegion` + config key `COST_TAKEOFF_EXCLUDE_CATEGORIES`).
- P3 round-trip key: `UID:<uniqueId>` for model rows, `MAN:<id>` for manual/QS rows, hidden column, rates persisted as per-element overrides. Stable across rebuilds.
- P4 EVM math (BCWS from planned %, SPI/CPI/EAC/VAC/TCPI with div-zero guards), cert carry-forward, VAT order, JSON persistence/sequencing — all correct.

**Conventions (read `CLAUDE.md`):** doc acquisition via `ParameterHelpers.GetDoc(commandData)`; `[Transaction]` attributes + named `Transaction`; `StingLog` not silent catches; `TaskDialog` not `MessageBox`; additive model changes with safe defaults (don't break saved snapshots); commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
**Build/verify command (must be 0 errors before every commit):**
`dotnet build StingTools/StingTools.csproj -p:RevitApiPath="C:\Program Files\Autodesk\Revit 2025"`
**Do NOT touch the `.rfa` tag-family files** — they are unrelated work; leave them out of this branch entirely.

---

## STAGE A — Integrate the four phases into one linear stack

Goal: one branch `main → P1 → P2 → P3 → P0-fix → P4`, **without** the `.rfa`
deletion commit, building clean.

### A.1 Create the integration branch off the P3 tip
```
git checkout -b claude/boq-cost-integrated claude/boq-p3-qs-roundtrip
```
This gives you `main + P1 + P2 + P3` as the base.

### A.2 Replay P0 + P4 on top, excluding the `.rfa` commit
Recommended (cherry-pick the two cost commits in order; the range stops *before*
the stray `0570d989d`):
```
git cherry-pick fc44e29e0^..73c3b6efa
```
> Verify first with `git log --oneline fc44e29e0^..73c3b6efa` — it must list
> exactly two commits: `fc44e29e0` (P0 fix) and `73c3b6efa` (P4). If anything
> else appears, adjust. **Never** cherry-pick `0570d989d`.

### A.3 Resolve conflicts (expected in these files)
Conflicts are expected because P4 was built without P1–P3. Resolution principle:
**keep BOTH the P1–P3 changes and the P4 changes** — do not drop either side.
- `StingTools/UI/BOQCostManagerPanel.cs` — P2/P3 added the Location column,
  grouping selector, print profiles, QS round-trip buttons; P4 added the cost-
  control action buttons/sections. Merge so **all** controls survive.
- `StingTools/UI/StingCommandHandler.cs` — keep **all** new command-tag `case`
  entries from both sides (P3 QS tags + P4 cost tags).
- `StingTools/Commands/Cost/CostCommands.cs` — P0 converted doc-acquisition to
  `ParameterHelpers.GetDoc`; ensure the final version uses `GetDoc` everywhere
  **and** keeps P3/P4 additions. No method may revert to
  `commandData?.Application?.ActiveUIDocument?.Document`.
- `StingTools/BOQ/BOQModels.cs` — P4 may reference `BOQLineItem`; ensure P1's
  aggregation fields remain present (`SimilarCount`, `ConstituentElementIds`,
  `AggregationKey`) and any P4 additions are layered on, not over them.
- `docs/CHANGELOG.md` — concatenate both phase entries in order.

After P4 lands, confirm P4's cost engines compile against the **post-P1**
`BOQLineItem`/`BOQDocument` (the aggregation fields and `BOQDocument.Currency`
must be in scope — they are needed in Stage B).

### A.4 Re-assert the P0 doc-acquisition fix across ALL Cost commands
The P0 fix (use `ParameterHelpers.GetDoc(commandData)` instead of
`commandData?.Application?.ActiveUIDocument?.Document`, which is always null when
dispatched from the dock panel → dead buttons) is **not reliably present** in the
current tree — several Cost files still carry the broken pattern. The
cherry-pick may not cover files P4 overwrote. So, after the cherry-pick:
```
grep -rn "commandData?.Application?.ActiveUIDocument" StingTools/Commands/Cost/
```
This **must return 0**. For every hit, replace the doc/uidoc acquisition with
`ParameterHelpers.GetDoc(commandData)` (or `GetApp(commandData)?.ActiveUIDocument`
where the `uidoc` is reused), exactly as P0 did. Check all six files:
`CostCommands.cs`, `CostPlanCommands.cs`, `PaymentCertCommands.cs`,
`VariationAndEvmCommands.cs`, `IfcAndIcmsCommands.cs` (the `MeasurementStandard`
file has no doc dependency). If any button is still wired to a command reading
`commandData.Application` directly, it stays dead.

### A.5 Build + commit the integration
- Run the build command. Fix any compile fallout from the merge (e.g. P4 calling
  a `BOQLineItem` shape that P1 changed). 0 errors required.
- Commit: `chore(boq): integrate P1–P4 into one linear stack (drop stray .rfa commit)`.

### ⛔ A.6 — HARD STOP. Hand back for human review.
Post a summary: the cherry-pick result, every file you had a conflict in and how
you resolved it (one line each), and the clean build output. **Do not start
Stage B until a human confirms the conflict resolution.** This is the riskiest
step; a wrong merge here silently drops a feature.

---

## STAGE B — Fix the verified bugs (only after Stage A is approved)

All line numbers are from the P4 code as reviewed; re-locate after the merge.

### B.1 🔴 CRITICAL — Currency is hardcoded "GBP" but values are UGX
The cert/variation/EVM models stamp `Currency="GBP"` while the numbers come from
`BOQSection.TotalUGX` / `boq.GrandTotalUGX`. `BOQDocument.Currency` already holds
the correct project currency (`"UGX"`). **Thread the BOQ currency through; never
hardcode the literal.**

Sites to fix:
- `StingTools/Core/PaymentCert/PaymentCertEngine.cs:78` (`CreateDraft` `Currency="GBP"`) — set from the snapshot/BOQ currency. Minimum: at the call site in `PaymentCertCommands.cs` (~`:57`), `cert.Currency = boq.Currency;` immediately after `CreateDraft`. Also fix the stale `// GBP …` comments on `SovLine.ContractValue`/`PreviouslyCertified`.
- `StingTools/Core/PaymentCert/PaymentCertModels.cs:69, 142` — default `"GBP"` → `"UGX"`.
- `StingTools/Core/Variation/VariationEngine.cs:65` (`FromDiff`) — add a `string currency` parameter (or read from the diff/BOQ) instead of `"GBP"`; pass `boq.Currency` from all three call sites in `VariationAndEvmCommands.cs`.
- `StingTools/Core/Variation/VariationModels.cs:171, 208` — defaults `"GBP"` → `"UGX"`; `StarRate.Currency` too.
- `StingTools/Core/Evm/EvmCalculator.cs:72` — default `"GBP"` → `"UGX"`.
- `StingTools/Commands/Cost/CostPlanCommands.cs:70-71` — the cost-plan summary prints `GBP {plan.SubtotalLikely}` / `GBP {plan.GrandTotalLikely}`; and the NRM1 benchmark engine (`Core/CostPlan/`) likely carries £/m² rates. Use `boq.Currency` for display, and confirm the NRM1 benchmark CSV (`STING_NRM1_BENCHMARKS.csv`) units match the project currency — if the benchmarks are genuinely £/m² they need a conversion or a UGX benchmark set (flag this in CHANGELOG rather than silently mislabelling).
- `StingTools/Commands/Cost/VariationAndEvmCommands.cs` — the ~7 hardcoded `"GBP"` star-rate labels (~`:338-344`) and the ACWP display (`:571`) → use `rate.Currency` / `boq.Currency`.
- `StingTools/Commands/Cost/CostCommands.cs:513` — the ES-migration `Write(el, rate, unit, "GBP", …)` → use the project currency (`BOQCostManager.BuildBOQDocument(doc)?.Currency ?? "UGX"`).
- `StingTools/Commands/Cost/CostControlCommands.cs` — any cert/AFC XLSX export that prints `Currency` must read the model's currency.

Acceptance: a UGX project shows "UGX" everywhere; grep for `"GBP"` in
`Core/PaymentCert`, `Core/Variation`, `Core/Evm`, `Commands/Cost` returns **0**
hardcoded literals (defaults may be `"UGX"`). (Full currency *conversion* is out
of scope — this is label/consistency correctness only.)

### B.2 🟠 Variations silently drop omissions
`StingTools/Core/Variation/VariationEngine.cs:70-93` — a `DeletedItem` (removed
scope) falls through to the default path producing `Qty=0, Rate=0, Total=0`, so
omissions vanish. `QuantityChanged` records the surviving positive quantity, not
the reduction. Add explicit handling so omissions/reductions carry the correct
**negative** value:
- `DeletedItem` → `Quantity = c.QtyA`, `UnitRate = -c.RateA`, `RateSource="Omission"`.
- `QuantityChanged` → `Quantity = c.QtyB - c.QtyA` (signed), `UnitRate = c.RateB`.
Acceptance: a snapshot diff that removes scope mints a VO line with a negative
`TotalValue`; the VO register net total reflects omissions.

### B.3 🟠 Anticipated Final Cost double-counts
`StingTools/Commands/Cost/CostControlCommands.cs:283` — `afc = grand + agreedVo +
pendingVo`, but `grand` (`boq.GrandTotalUGX`) is the *live* BOQ which already
reflects agreed changes the VOs were minted from → double count, and there is no
frozen original contract sum. Fix: anchor AFC on a **contract-sum baseline**, not
the live total. Use the earliest issued payment cert's contract value (sum of
`Lines[*].ContractValue`) when one exists, else fall back to the current grand
total, then `afc = contractSum + agreedVo + pendingVo`. Document the assumption
in a comment. If no baseline exists yet, surface that in the report rather than
silently using the live total.

### B.4 🟠 Retention has no cumulative cap
`StingTools/Core/PaymentCert/PaymentCertEngine.cs` (CreateDraft / retention calc)
— retention is taken per-cert with no ceiling, so cumulative withheld can exceed
the contractual cap (JCT 2024 §4.10 / NEC4 X16: cap ≈ `RetentionPercent ×
ContractSum`). Compute headroom = `cap − ledger.Balance` (the ledger already
tracks cumulative withheld) and cap this cert's retention at
`min(GrossValuation × rate, headroom)`. Add a `RetentionCap` field if needed.
Acceptance: across successive certs, total retention never exceeds the cap; once
reached, per-cert retention is 0.

### B.5 🟠 EVM actuals double-count on re-import
`StingTools/Commands/Cost/VariationAndEvmCommands.cs:545-575` — re-importing the
same actuals CSV adds to ACWP again, and the dialog shows the raw CSV sum, not
the persisted cumulative. Use `EvmCalculator.ImportActualsToDate(report, …)`
(which reads the persisted store) and display the merged cumulative
(`report.CurrentPeriod.Acwp`). Guard against importing an already-imported file
(e.g. dedupe by file hash or period date) and warn instead of silently doubling.

### B.6 🟢 Minor (do if quick; otherwise note in CHANGELOG)
- `StingTools/BOQ/BOQCostManager.cs` `AssignBoqLineRefs` — the middle index is
  hardcoded `"1"` (`{section}.1.{n}`). Refs are still unique within a section
  (rowIndex increments), so this is cosmetic, not a collision. If trivial, make
  the group index advance per Category group; otherwise leave + note.
- P3 XLSX import (`BOQQsRoundtripCommands.cs`) — confirm the importer reads the
  literal **rate** columns (not formula totals) and parses with
  `double.TryParse(…, NumberStyles.Any, CultureInfo.InvariantCulture, …)`, with
  blank/non-numeric cells producing a diff-preview warning rather than a silent 0.

### B.7 Build + commit
- Build clean (0 errors). One commit per fix (B.1…B.5), or grouped logically with
  a clear message. Update `docs/CHANGELOG.md` and add any residual gaps to
  `docs/ROADMAP.md`.

---

## STAGE C — Verification gate (report, do not skip)
The whole stack builds clean but **has never run in Revit**. You cannot click
buttons in CI, so produce a **Revit manual-test checklist** in the PR/commit body
covering, against a real UGX model:
1. Build BOQ → confirm aggregation (similar items collapse, `SimilarCount`/Qty
   correct) and 2D/CAD noise excluded.
2. Location column toggle + regroup by Level/Zone + tender print profile.
3. QS round-trip: export unpriced → edit rates in Excel → import → rates land on
   correct rows, manual rows survive, diff preview correct.
4. Cost: issue interim cert (currency shows **UGX**, retention/MOS/previous/VAT/
   net correct), raise a variation incl. an **omission** (negative line), check
   revised contract sum + AFC (no double-count), run EVM with imported actuals
   (no double-count), export S-curve.
State explicitly what is build-verified vs. what still needs human Revit testing.
**Do not push or open PRs unless asked.**

---

## Definition of done
- Single branch `claude/boq-cost-integrated` = `main→P1→P2→P3→P0fix→P4`, no `.rfa`
  commit, building clean.
- 0 hardcoded `"GBP"` literals in cost code; UGX shown throughout.
- B.2–B.5 fixed with correct signs/caps/dedup; verified-good areas untouched.
- CHANGELOG updated; Revit manual-test checklist provided; honest caveats stated.
- **Hard stop after Stage A respected** — human approved the conflict resolution
  before Stage B began.
