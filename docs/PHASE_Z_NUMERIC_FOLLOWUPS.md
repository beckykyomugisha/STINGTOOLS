# Phase Z Numeric Follow-Up Prompts — chain after VAT lands

Paste prompts in order. Each is independent — agent can do them in any sequence, but the suggested order minimises overlap and lets you batch the QS review.

The terminal Claude Code session is currently working on **Z-18 (VAT GrandTotal centralization)** based on the user's "fix the VAT GrandTotal issue" message. Once that PR lands + you've Revit-verified the new total appears in XLSX / dashboard / BCC, paste the next prompt.

---

## After Z-18 (VAT) ships — paste this for Z-19 (sand bulking)

```
Z-19 — Sand bulking factors are non-physical. Pure CSV one-line edit
+ regression spot-check. ~15 min.

REQUIRED reading FIRST

git fetch origin audit/numerics-deep-review
git show audit/numerics-deep-review:docs/PHASE_Z_NUMERIC_AUDIT.md | grep -A20 '4.1\|Sand bulking'

Audit found:
  MATERIAL_LOOKUP.csv:219-222 — DRY=1.15, DAMP=1.25, WET=1.10, DEFAULT=1.20
  Physics: dry sand does not bulk (≈1.00); bulking peaks DAMP (~1.25-
  1.30); saturated/wet collapses to ≈1.00-1.05 (per CIRIA, IS 2386).
  Effect: DRY value over-orders dry sand by 15%; WET value treats
  saturated sand as +10% when it should be near 0. Direct quantity
  error on every sand line.

BRANCH

fix/sand-bulking-physical off latest origin/main. No PR.

SCOPE — JUST CSV

1. Open Planscape.Server/.../StingTools/Data/MATERIAL_LOOKUP.csv (or
   wherever it lives — find with `find . -name MATERIAL_LOOKUP.csv`).
2. Lines 219-222 (or whatever the actual sand bulking rows are —
   grep "BULKING" in the file). Change:
     DRY     1.15 → 1.00
     DAMP    1.25 → 1.30   (peaks higher per CIRIA; current 1.25 is low end)
     WET     1.10 → 1.05   (saturated sand collapses; 1.05 is upper end)
     DEFAULT 1.20 → 1.25   (matches expected damp-site average)
3. Confirm column order in the row matches header. Don't shift other
   columns.

CONFIRM IT COMPILES + DOESN'T BREAK FORMULA EVAL

dotnet build StingTools/StingTools.csproj
(CSV isn't compiled but BOQ engine loads it — confirm no schema-shape
assumption broke.)

REGRESSION SPOT-CHECK

Find any unit test that loads MATERIAL_LOOKUP for sand:
  grep -rln 'MATERIAL_LOOKUP\|sand.*bulking\|BULKING' StingTools.Tests/
If a test exists, update its expected values to the new factors. If
no test exists, document in the commit body: "no test coverage on
sand bulking — manual verify via Revit BOQ export."

COMMIT + PUSH

Single commit on fix/sand-bulking-physical. Confirm push:
  git ls-remote origin fix/sand-bulking-physical

REPLY (under 200 words)

- Exact line numbers changed in MATERIAL_LOOKUP.csv
- Before/after values per row
- dotnet build result
- Test coverage status (found/updated test, OR no test exists)
- Commit hash + push confirmation
```

---

## After Z-19 ships — paste this for Z-20 (embodied carbon undercount)

```
Z-20 — Embodied carbon for steel / copper / glass is 4-9× under
ICE v3.0 reference. Plus concrete-carbon cross-file drift (BLE 150
vs LOOKUP 345 kgCO₂/m³). ESG/sustainability reporting impact.

REQUIRED reading FIRST

git fetch origin audit/numerics-deep-review
git show audit/numerics-deep-review:docs/PHASE_Z_NUMERIC_AUDIT.md | grep -A30 '2\.4\|2\.5\|2\.6\|7\.1'

The audit cited:
  MEP_MATERIALS.csv galvanised steel duct: 2500 kgCO₂/m³ (≈0.32 kg/kg)
    vs ICE steel 1.5-2.8 kg/kg → real value ~12,000-22,000 /m³ (~6× under)
  MEP_MATERIALS.csv COPPER PIPE TYPE L: 3500 kgCO₂/m³ (≈0.39 kg/kg)
    vs ICE copper 2.7-3.8 kg/kg → real value ~24-34k /m³ (~7× under)
  MEP_MATERIALS.csv glass: 850 kgCO₂/m³ (≈0.34 kg/kg)
    vs ICE glass 1.4-1.7 kg/kg → real value ~3500-4250 /m³ (~4× under)
  Cross-file drift: BLE concrete carbon 150 kgCO₂/m³ vs LOOKUP 345
    (2.3× mismatch — same material, two answers)

BRANCH

fix/embodied-carbon-ice-v3 off latest origin/main. No PR.

DECISION FIRST — WHICH FILE IS CANONICAL?

Read CLAUDE.md "BOQ" + "MATERIAL_LOOKUP" sections. The architecture
intent (Phase 76+) was MATERIAL_LOOKUP.csv as the single source of
truth. BLE_MATERIALS.csv and MEP_MATERIALS.csv carry per-row props
that the BOQ resolver falls back to ONLY when MATERIAL_LOOKUP has no
matching entry.

So the fix shape is:
  1. UPDATE MATERIAL_LOOKUP.csv with correct carbon factors for
     steel / copper / glass (the values BLE/MEP currently lie about)
  2. Verify the resolver in BOQCostManager.cs prefers MATERIAL_LOOKUP
     over BLE/MEP per-row columns. If it doesn't, fix the resolver.
  3. Don't touch the per-row BLE/MEP carbon columns yet — they'll be
     dead-code after the resolver respects LOOKUP, and removing them
     is its own PR (schema change risk).

UPDATE MATERIAL_LOOKUP.csv

Find rows for: STEEL, GALVANISED_STEEL (or whatever the duct material
key is), COPPER, GLASS_FLOAT (or similar). If rows don't exist yet,
ADD them. ICE v3.0 carbon factors to use (kgCO₂e/kg):

  Steel (virgin, sections)         2.45
  Steel (recycled content 60%)     1.55
  Galvanised steel sheet (duct)    2.85
  Copper (sheet/pipe, virgin)      3.50
  Copper (recycled 25%)            2.70
  Float glass                      1.55
  Toughened glass                  1.80
  Laminated glass                  1.65

Use the most conservative / industry-default value where uncertain.
Cite ICE v3.0 in the commit body — DON'T invent values.

For the concrete cross-file drift (BLE 150 vs LOOKUP 345): keep
LOOKUP's 345 — that matches ICE C30 ready-mix; BLE's 150 is way low
(likely uncured / mortar value mistakenly applied to concrete).

VERIFY RESOLVER PREFERS LOOKUP

Read BOQCostManager.cs ResolveCarbonFactor / CarbonFactorResolver
(~lines 485-551 per the audit). Confirm:
  1. Lookup query tries MATERIAL_LOOKUP.csv first
  2. Falls back to per-row BLE/MEP carbon column only if not in LOOKUP
  3. Logs which source it used (so future audits can trace)

If the resolver order is wrong, fix it. ONE commit covers both the
CSV update + the resolver fix.

REGRESSION SPOT-CHECK

Find any test that asserts on carbon kg values:
  grep -rln 'kgCO2\|EmbodiedCarbon\|CarbonFactor' StingTools.Tests/
Update expected values to match the new ICE-grounded numbers.

Build:
  dotnet build StingTools/StingTools.csproj

COMMIT + PUSH

Single commit on fix/embodied-carbon-ice-v3. Confirm push.

REPLY (under 300 words)

- MATERIAL_LOOKUP.csv rows added/updated (table: material → old → new)
- Resolver order before/after (cite file:line of the lookup chain)
- ICE v3.0 reference values used (cite source)
- Test coverage status
- Concrete cross-file drift resolution (BLE 150 stays for backwards
  compat? OR change BLE 150 → 345 to match LOOKUP? Recommend AND act)
- dotnet build result
- Commit hash + push confirmation
- Open question: timber biogenic carbon column (-900 in BLE) is per
  Z-23 — is the project's whole-life carbon report A1-A3 inclusive of
  biogenic, OR gross? Decision needed before changing timber values.
```

---

## After Z-19 + Z-20 both ship — paste this for Z-21 (waste% on legacy BOQ path)

```
Z-21 — Waste% applied only on the TakeoffRule path; legacy fallback
path applies 0% waste, under-quantifies. ~30 min.

REQUIRED reading

git fetch origin audit/numerics-deep-review
git show audit/numerics-deep-review:docs/PHASE_Z_NUMERIC_AUDIT.md | grep -A10 '6\.3'

The audit cited BOQCostManager.cs:368-372 (TakeoffRule path, waste
applied correctly) vs :380+ (legacy fallback, waste NOT applied).
Elements routed through the fallback are under-quantified.

BRANCH

fix/boq-waste-legacy-fallback off latest origin/main. No PR.

SCOPE

1. Read BOQCostManager.cs lines 368-400 to understand BOTH paths.
2. Identify which property holds the waste% (probably the
   TakeoffRule's WastePercent OR a per-material fallback default).
3. In the legacy fallback path (:380+), apply the same `q *= 1+Waste%`
   step. Source the waste% from the per-material default if no rule
   exists (probably MATERIAL_LOOKUP.csv has a column).
4. If no per-material default exists, apply a conservative project-
   default (e.g. 5%) and DOCUMENT it as a project-config knob.

HARD RULES

- DO NOT change the TakeoffRule path — it's correct.
- DO NOT change MATERIAL_LOOKUP.csv unless you need to add a waste%
  column (in which case keep the diff small).
- Verify NO existing test expected the 0%-waste behaviour on the
  legacy path (if it did, the test was codifying the bug — update +
  document in commit body).

BUILD + VERIFY

dotnet build StingTools/StingTools.csproj
Find a Revit-Tests project that exercises BOQCostManager:
  grep -rln 'BOQCostManager\|class.*BoqTest' StingTools.Tests/ Tests/
If a test exists for the legacy path, update it. Add a new test that
asserts waste% applies on the fallback path.

COMMIT + PUSH

Single commit on fix/boq-waste-legacy-fallback.

REPLY (under 250 words)

- Before/after of the legacy fallback line
- Source of the waste% (per-material default? project config? hardcoded?)
- Test coverage added/updated
- Commit hash + push confirmation
```

---
## After Z-21 merges — paste this for Z-21b (rate × quantity waste double-count)

```
Z-21b — Rate-override waste and quantity-fallback waste double-apply.
Surfaced by the Z-21 agent. ~45 min.

REQUIRED reading FIRST

git fetch origin main
git show origin/main:docs/UI_CLEANUP_CAMPAIGN.md | grep -A6 'Z-21b'

The problem: after Z-21 (commit on fix/boq-waste-legacy-fallback),
an element carrying an explicit StingCostRateOverride.WastePercent
gets waste applied to the RATE in RateProviders.cs:89, AND now also
gets waste applied to the QUANTITY via the legacy-fallback path that
Z-21 just wired. A 5% rate-override + 5% quantity-waste compounds to
~10.25% on line cost. The override path is rare/explicit but the
double-count is a real over-measurement.

BRANCH

fix/boq-waste-dedup off latest origin/main (Z-21 must be merged first
— confirm with `git log --oneline origin/main | grep -i waste`).
No PR yet — push branch, user opens PR.

SCOPE — pick ONE convention and enforce it

Read both waste-application sites:
  1. RateProviders.cs:89 — StingCostRateOverride.WastePercent on the RATE
  2. BOQCostManager.DeriveQuantity legacy fallback — WasteFactor.Apply
     on the QUANTITY (the Z-21 fix)

Decide the canonical convention (recommend OPTION A):

OPTION A — quantity is the single waste surface (recommended)
  - Waste always applies to QUANTITY (Z-21 already does this).
  - When StingCostRateOverride.WastePercent is present, SKIP the
    rate-side waste — OR re-interpret the override's WastePercent as
    the quantity waste% for that element (so the override still wins,
    just on the quantity side).
  - Net: every element wastes exactly once, on the quantity.

OPTION B — rate is the single waste surface
  - When an explicit rate-override WastePercent exists, SKIP the
    quantity-side WasteFactor.Apply for that element.
  - Net: override elements waste on rate; everything else on quantity.
  - More complex (two code paths) — only choose if the QS expects
    rate-side waste semantics.

Implement A unless you find evidence the codebase expects B. Document
the chosen convention in a one-line comment at BOTH sites so the next
reader knows waste is single-surface.

HARD RULES

1. After the fix, NO element can waste on both rate and quantity.
2. Elements WITHOUT a rate-override keep the Z-21 behaviour exactly
   (quantity waste only) — don't regress the common path.
3. Add a test to StingTools.Boq.Tests (the project Z-21 created):
   - element with rate-override + measurable quantity → waste applied
     exactly once (assert the line total, not just one factor)
   - element without override → unchanged from Z-21

BUILD + TEST

dotnet build StingTools/StingTools.csproj
dotnet test StingTools.Boq.Tests

COMMIT + PUSH

Single commit on fix/boq-waste-dedup. Confirm push.

REPLY (under 250 words)

- Which option (A/B) + one-sentence why
- The two waste sites before/after (file:line each)
- The new test case(s) added
- Confirmation no element double-wastes (cite the test assertion)
- dotnet build + test result
- Commit hash + push confirmation
```

---

## Z-24 — MATERIAL_LOOKUP.csv dead-loader (paste anytime — independent)

```
Z-24 — MATERIAL_LOOKUP.csv Tier-1 resolver is dead at runtime. The
loader expects wide-format columns; the shipped file is long-format.
All LOOKUP queries return 0. Architectural fix + tests. ~3-4h.

REQUIRED reading FIRST

git fetch origin main
git show origin/main:docs/UI_CLEANUP_CAMPAIGN.md | grep -A18 'Z-24 —'

Background (discovered during Z-20): MaterialLookupCsv.EnsureLoaded()
(MaterialLookupCsv.cs around line 56) looks for a wide-format CSV with
a "Material" or "Name" column. The shipped MATERIAL_LOOKUP.csv is
long-format: Category,TypeKey,Property,Value. iName resolves to < 0,
the cache loads empty, and GetCarbon/GetCost/GetDensity always return
0. The Phase 76+ "canonical Tier-1 lookup" is non-functional. Real
BOQ values come from per-row PROP_CARBON_KG_M3 etc. in BLE/MEP CSVs.

BRANCH

fix/material-lookup-long-format off latest origin/main. No PR yet.

SCOPE — DECIDE FORMAT FIRST, then loader + resolver + tests

STEP 1 — read both ends
  - MaterialLookupCsv.cs (the loader + GetCarbon/GetCost/GetDensity)
  - MATERIAL_LOOKUP.csv (the actual shipped data — confirm long-format
    Category,TypeKey,Property,Value with `head -5`)
  - CLAUDE.md "MATERIAL_LOOKUP" + "BOQ" sections (the design intent)

STEP 2 — decide format (recommend: teach loader the long format)
  OPTION A (recommended) — pivot long→wide at load time. EnsureLoaded()
    groups rows by (Category,TypeKey), pivots each Property→Value pair
    into a strongly-typed MaterialRow. More extensible: adding a new
    property = new rows, no schema migration.
  OPTION B — convert the CSV to wide format (one row per material,
    a column per property). Matches the existing loader but loses the
    long-format extensibility and is a bigger data churn.
  Pick A unless the data has irregular property sets that make pivoting
  fragile.

STEP 3 — implement
  - Rewrite EnsureLoaded() to parse the actual format.
  - Verify GetCarbon/GetCost/GetDensity return non-zero for known
    materials after load.
  - Keep the public API (GetCarbon(name) etc.) byte-identical so no
    caller changes.

STEP 4 — resolver order (the Z-20 agent flagged this as deferred)
  After the loader works, decide whether to reorder the carbon/cost
  resolver chain so Tier-1 LOOKUP wins over the per-row BLE/MEP columns
  (the documented "material-param wins" intent). CAUTION: the Z-20
  agent deliberately did NOT reorder because reordering would change
  delivered BOQ numbers. ONLY reorder if:
    a) the loader now works (this PR), AND
    b) you've confirmed LOOKUP's values match or supersede the BLE/MEP
       per-row values (no silent number change), AND
    c) tests lock the resolved values.
  If any of those isn't satisfied, leave the resolver order alone and
  document "loader fixed; resolver reorder is a separate PR pending
  value reconciliation."

STEP 5 — tests (mandatory — this regressed silently once)
  Add to StingTools.Boq.Tests (or a new MaterialLookup test fixture):
    - GetCarbon("<known material>") returns the expected non-zero value
    - GetDensity / GetCost likewise
    - unknown material returns 0 (graceful)
    - the long-format pivot handles a material with multiple Property
      rows correctly

BUILD + TEST

dotnet build StingTools/StingTools.csproj
dotnet test StingTools.Boq.Tests

COMMIT + PUSH

Single commit on fix/material-lookup-long-format. Confirm push.

REPLY (under 350 words)

- Format chosen (A long-parse / B wide-convert) + why
- EnsureLoaded() before/after (the bug: what iName resolved to)
- Resolver reorder: done OR deferred (+ which of a/b/c gated it)
- Tests added (list)
- Sample GetCarbon value for one material: was 0, now X
- dotnet build + test result
- Commit hash + push confirmation
- IMPORTANT: if reordering the resolver would change any delivered BOQ
  carbon/cost number, STOP and report the deltas before committing —
  the user must sign off on any number change.
```

---

## Z-22 — 63 formulas in dependency cycles (TWO-STAGE — audit prompt first)

This is a multi-day project. Do NOT brief it as a single fix. Paste
the AUDIT prompt below first; the resolution prompts come after you've
seen the cycle map.

### Stage 1 — paste this AUDIT prompt (read-only, ~2h)

```
Z-22 Stage 1 — Map the 63-formula dependency cycle set. READ-ONLY
audit. Output a doc + a Graphviz visualisation. No engine changes.

REQUIRED reading FIRST

git fetch origin audit/numerics-deep-review
git show audit/numerics-deep-review:docs/PHASE_Z_NUMERIC_AUDIT.md | grep -A12 '5\.1\|5\.2\|dependency cycle'

The numeric audit found: FORMULAS_WITH_DEPENDENCIES.csv (278 formulas)
has cycles — Kahn's topological sort orders only 215/278; 63 nodes are
in or downstream of cycles. FormulaEvaluatorCommand.cs:530-539 logs
"Formula cycle detected" and runs cycle nodes LAST with stale inputs.
Cycle set includes CST_S_CON_CEMENT_BAGS_NR, CST_S_CON_SAND_VOLUME_CU_M,
CST_CALC_STEEL_KG, PLM_HED_M, HVC_PIPE_FLOWRATE_LPS, FLS_SFTY_DEMAND_LPS.
Effect: concrete/steel-takeoff + plumbing-head can compute against
stale/zero inputs → non-deterministic BOQ numbers between runs.

BRANCH

audit/formula-cycles off latest origin/main. READ-ONLY — no source
fixes. One doc commit.

WHAT TO PRODUCE

1. Parse FORMULAS_WITH_DEPENDENCIES.csv. Each formula has a name +
   the parameters it references (its inputs) + the parameter it
   computes (its output). Build the directed graph: edge A→B means
   "formula for B references parameter A".

2. Run a strongly-connected-components (SCC) algorithm (Tarjan's or
   the engine's own cycle detector). Enumerate every ELEMENTARY cycle,
   not just "is in a cycle". For each cycle list the exact node chain
   (A → B → C → A).

3. Group the cycles by domain (concrete-takeoff, steel-takeoff,
   plumbing-head, HVAC-flow, fire-safety-demand, etc.). Most cycles
   are probably 2-3 nodes; flag any large ones.

4. For each cycle, hypothesise the FIX TYPE:
   - "parameter elimination" — one formula can be algebraically
     substituted into the other, breaking the loop
   - "fixed-point iteration" — genuinely circular (e.g. pipe size
     depends on flow depends on pipe size); needs iterate-to-converge
   - "spurious self-reference" — a formula references its own output
     by mistake (data bug, not a real cycle)
   - "ordering bug" — not actually circular, just mis-ordered in the
     CSV (Dependency_Level column wrong)

5. Output docs/PHASE_Z_FORMULA_CYCLES.md:
   - Table: cycle # | node chain | domain | hypothesised fix type |
     BOQ impact (does it feed a delivered quantity?)
   - A Graphviz .dot file (docs/formula_cycles.dot) of just the cyclic
     subgraph so it's visually inspectable — render instructions in
     the doc.
   - A "recommended resolution order" — which cycles to fix first
     (spurious self-refs + ordering bugs are quick wins; fixed-point
     ones are the hard core).

HARD RULES

1. READ-ONLY. No changes to FORMULAS_WITH_DEPENDENCIES.csv or
   FormulaEvaluatorCommand.cs. This pass only MAPS the problem.
2. Use the engine's actual cycle detector if it exposes one (so the
   audit matches runtime behaviour) — cross-check against an
   independent SCC implementation.
3. dotnet build NOT required (read-only).

REPLY (under 400 words)

- Total elementary cycles found (vs the 63 "nodes in cycles")
- Breakdown by fix type (elimination / iteration / spurious / ordering)
- The quick-win count (spurious + ordering — fixable without engine math)
- The hard-core count (genuine fixed-point cycles)
- How many cycles feed a DELIVERED BOQ quantity (the dangerous ones)
- Commit hash + push confirmation
```

After this audit lands, we'll have a real cycle map and can write
per-cluster resolution prompts (Stage 2) — one PR per domain. Don't
attempt Stage 2 before reviewing the Stage 1 doc.

---

## Z-23 — 21 smaller findings (paste as ONE batch after top-3 verified)

```
Z-23 — Batch-fix the smaller P1/P2 numeric findings. ~3-4h for the
whole batch. ONE branch, but verify EACH change against its reference.

REQUIRED reading FIRST

git fetch origin audit/numerics-deep-review
git show audit/numerics-deep-review:docs/PHASE_Z_NUMERIC_AUDIT.md

Read the FULL audit — sections 1-8. The top-3 (VAT, sand, carbon) are
done. This task is the remaining 10 P1 + 11 P2. They're isolated
single-value fixes; batch them but verify each against its cited
reference (ICE v3.0 / CIBSE Guide A / NRM2 / BS 7671).

BRANCH

fix/numeric-batch-cleanup off latest origin/main. No PR yet.

SCOPE — the audit's P1/P2 items NOT already fixed

From §2 (material constants):
  2.1 BLE template-default rows for non-concrete materials carry a
      generic ρ=2300/k=1.3/C=400 block → wrong for steel ceiling tile
      etc. Fix the per-material-type rows that are obviously wrong
      (steel tile ρ should be 7850 not 2300, etc).
  2.2 Fiberglass/glass-wool tile ρ=2300 → should be 10-100 kg/m³.
  2.3 Cement plaster ρ=2400 → 1900-2100.
  2.7 Softwood skirting ρ=720 (= hardwood) → softwood 420-550.
  2.8 Timber biogenic C=-900 — DO NOT TOUCH unless Z-25 is decided
      (see the open decision). Skip timber in this batch.

From §4 (waste/bulking):
  4.5 Rebar wastage absent — add a cutting/lapping waste % (NRM2 5-7%)
      if the rebar takeoff path supports it.
  4.6 Concrete order +5% over-order factor not baked in — add if the
      concrete takeoff supports a project-config knob.

From §6 (BOQ aggregation):
  6.6 ProvisionalSum reconciliation uses Math.Abs → no signed
      credit-vs-overrun distinction. Change to signed diff so unused-PS
      credits back to the client and overruns show positive.

From §1 + §8 (P2 cosmetic — fix if trivial, skip if risky):
  1.4 Linear-element carbon fallback comment says "~32 mm" but value
      is 1000 mm² (= Ø35.7 mm). Fix the comment OR the value to match.
  8.1 CIBSE supply-main velocity max 10 m/s slightly permissive vs
      ≤7.5-9 noise guidance — tighten to 9 if no test depends on 10.
  8.2 BS 7671 cooker circuit 45A/6mm² borderline — leave (it's clipped
      correct), just add a code comment noting the marginal case.

HARD RULES

1. Verify EACH value against the cited public reference before
   changing it. Cite the reference in the commit body per change.
2. SKIP timber (2.8) — blocked on the Z-25 decision.
3. Each material-constant change risks a delivered-number shift —
   if any change moves a BOQ carbon/cost/mass number, NOTE the delta
   in the commit body. The user must be able to see what numbers moved.
4. Add tests where a clean test surface exists (StingTools.Boq.Tests
   for BOQ-side; material-constant changes are data so may not have
   a test home — document manual-verify-in-Revit in those cases).
5. ONE commit for the whole batch (it's cleanup), but a clear
   commit body listing every change as a bullet with file:line +
   old → new + reference.

BUILD

dotnet build StingTools/StingTools.csproj

COMMIT + PUSH

Single commit on fix/numeric-batch-cleanup. Confirm push.

REPLY (under 400 words)

- Per change: file:line, old → new, reference cited
- Which items you SKIPPED + why (timber on Z-25; anything risky)
- Any delivered BOQ number that moved (table of deltas)
- Test coverage added
- dotnet build result
- Commit hash + push confirmation
```

---

## Z-25 — timber biogenic carbon: NOT a prompt, a DECISION you make first

Z-25 blocks any timber value change (it's why Z-23 above skips timber).
You must pick a reporting convention before an agent can act. From the
tracker (docs/UI_CLEANUP_CAMPAIGN.md §11 Z-25):

| Path | What | Trade-off |
|---|---|---|
| A | Leave timber at -900, document the asymmetry in release notes | Zero code; reports biased low; QS must compensate |
| B | Strip biogenic from timber (~+100-200 manufacturing-only) | Internally consistent gross A1-A3; loses sequestration story |
| C | Split timber into 2 columns (fossil + biogenic) | Best long-term; matches RIBA 2030/LETI/RICS WLCA; ~1d schema change |

Once you pick, tell me the letter and I'll write the matching prompt:
- Path A → a 10-min docs-only "release note" prompt (or I just do it from cloud)
- Path B → a CSV-edit prompt like Z-19 (strip the biogenic component)
- Path C → a schema-change prompt (new column across MATERIAL_LOOKUP /
  BLE / MEP + resolver awareness + tests) — the biggest of the three

Default if you don't decide: Path A (disclose + move on). Recommend C
when a carbon engineer / QS can confirm the client's expected report
format.

---

# BATCH 2 — prompts added after the Z-22 Stage-1 audit + user decisions

The Z-22 Stage-1 audit (branch `audit/formula-cycles`, doc
`docs/PHASE_Z_FORMULA_CYCLES.md`) found **ZERO genuine cycles** — the
audit's "63" was self-reference noise in the Input_Parameters column.
So Z-22 collapses to a tiny CSV-hygiene PR (Stage 2 below), not the
multi-day project originally feared.

User decisions captured:
- Z-25 timber → **Path C** (split into fossil + biogenic columns)

---

## Z-22 Stage 2 — remove the 19 spurious self-references (tiny CSV hygiene)

```
Z-22 Stage 2 — Clear the 19 spurious self-references from
FORMULAS_WITH_DEPENDENCIES.csv. The Stage-1 audit proved there are NO
genuine cycles — these are self-reference data artifacts the engine
already self-skips at runtime. ~30 min. CSV + relabel only.

REQUIRED reading FIRST

git fetch origin audit/formula-cycles
git show audit/formula-cycles:docs/PHASE_Z_FORMULA_CYCLES.md

Key finding (verified by replicating the engine's actual detector):
  - FormulaEvaluatorCommand.cs:485 already does
    `if (token == ParameterName) continue;` — it self-skips, which is
    why "cycle detected" never fires and Kahn orders all 278/278.
  - The audit's "63 nodes in cycles" = 19 single-node self-loops +
    44 non-cyclic downstream dependents (mis-counted).
  - 18 of the 19 are mis-keyed VALIDATION formulas: if(SELF op
    threshold, "[!WARN]", "") where the formula references its own
    value param in Input_Parameters. Pure data-quality noise.
  - 1 is BLE_STRUCT_CONCRETE_GRADE_TXT — a lookup-keyed text param
    whose self-entry is metadata noise; it roots the concrete take-off
    (17 CST_* downstream nodes that are NOT cyclic).

BRANCH

fix/formula-self-ref-cleanup off latest origin/main. No PR.

SCOPE — Input_Parameters column hygiene + optional relabel

1. Read docs/PHASE_Z_FORMULA_CYCLES.md for the exact list of the 19
   parameters whose Input_Parameters column lists themselves.

2. In FORMULAS_WITH_DEPENDENCIES.csv, for each of those 19 rows:
   REMOVE the self-name from the Input_Parameters column ONLY. Keep
   every other input. Don't touch Revit_Formula, Parameter_GUID,
   Dependency_Level, or any other column.

   Example: if BLE_FINISH_PAINT_WARN_TXT has
     Input_Parameters = "BLE_FINISH_PAINT_AREA_SQ_M,BLE_FINISH_PAINT_WARN_TXT"
   change to
     Input_Parameters = "BLE_FINISH_PAINT_AREA_SQ_M"

3. (Optional but recommended) For the 18 validation formulas, the
   self-reference in the Revit_Formula itself (if(SELF op threshold...))
   is the engine's intended "read my own current value to validate it"
   pattern — that's fine and the engine handles it via self-skip. Do
   NOT change the Revit_Formula. Only the Input_Parameters METADATA
   column was wrong (it shouldn't list the formula's own output as an
   input).

4. Confirm the Dependency_Level column is recomputed correctly. The
   engine computes it by topological sort at load (per an earlier fix),
   so it doesn't trust the CSV column — but if the CSV column is used
   anywhere for display/audit, regenerate it. Check whether
   FormulaEngine.LoadFormulas recomputes or reads the column.

VERIFY

dotnet build StingTools/StingTools.csproj
Add a test (StingTools.Boq.Tests or wherever FormulaEngine is testable):
  - load FORMULAS_WITH_DEPENDENCIES.csv
  - assert the dependency graph has 0 self-loops in Input_Parameters
  - assert topological sort orders all 278 (already true; lock it)

COMMIT + PUSH

Single commit on fix/formula-self-ref-cleanup. Confirm push.

REPLY (under 250 words)

- The 19 parameters cleaned (list or count + the 1 BOQ-critical one)
- Confirmation Revit_Formula expressions are UNTOUCHED (only the
  Input_Parameters metadata column changed)
- Whether Dependency_Level needed regeneration (engine recomputes? or
  CSV column used for display?)
- Test added (assert 0 self-loops + 278/278 ordering)
- dotnet build + test result
- Commit hash + push confirmation
```

---

## Z-24b — populate LOOKUP with all materials + reorder resolver (gated, sequential)

```
Z-24b — Complete the canonical Tier-1 LOOKUP. Z-24 made the loader
work but LOOKUP only carries concrete-grade carbon. Populate the rest,
reconcile values, reorder the resolver, remove dead per-row columns.
GATED — each step needs the prior + sign-off on number movement.
~half-day across STEP 1; STEPS 3-4 are separate sign-off-gated PRs.

REQUIRED reading FIRST

git fetch origin main
git show origin/main:docs/UI_CLEANUP_CAMPAIGN.md | grep -A20 'Z-24b'
git show origin/main:StingTools/UI/MaterialLookupParser.cs | head -60

Background: Z-24 (commit c0337a3d7) made MaterialLookupCsv load the
long-format CSV. GetCarbon("C30") now returns 345 (was 0). But LOOKUP
only has concrete-grade carbon rows — no steel/copper/glass/timber, no
density/cost. The resolver still consults material-params FIRST (not
reordered) so no delivered number changed. To make LOOKUP the true
canonical Tier-1 (Phase 76+ intent) without breaking numbers:

BRANCH

fix/material-lookup-populate off latest origin/main. No PR yet.

STEP 1 — POPULATE (this PR)

Add to MATERIAL_LOOKUP.csv (long-format: Category,TypeKey,Property,Value)
the carbon + density + cost rows for every material the per-row
BLE/MEP columns currently cover. Use the SAME ICE v3.0 values Z-20 put
into BLE/MEP (so LOOKUP matches, not contradicts):

  steel (sections)      2.45 kgCO2e/kg, density 7850
  galvanised steel      2.85, density 7850
  copper                3.50, density 8960
  float glass           1.55, density 2500
  toughened glass       1.80, density 2500
  timber (softwood)     SEE Z-25 — timber is split into fossil+biogenic
                        columns; coordinate with that PR before adding
                        timber carbon rows. SKIP timber here if Z-25
                        hasn't landed.

Match each LOOKUP value to the corresponding BLE/MEP per-row value
EXACTLY (Z-20 already set those to ICE v3.0). The goal is parity so
that STEP 3's reorder is a no-op on delivered numbers.

STEP 2 — RECONCILE + REPORT (this PR's verification)

For every material now in BOTH LOOKUP and a per-row BLE/MEP column,
produce a reconciliation table: material | LOOKUP value | per-row value
| match? If ANY mismatch, STOP and report — a mismatch means STEP 3's
reorder would change a delivered number, which needs sign-off.

STEP 3 — REORDER RESOLVER (SEPARATE PR — only after STEP 1+2 clean)

This is its own branch fix/material-lookup-resolver-reorder. Flip the
resolver in CarbonFactorResolver / CarbonTrackingCommands so Tier-1
LOOKUP wins over per-row columns. ONLY do this when STEP 2 shows
exact parity (no number moves) OR you have explicit sign-off on the
deltas. Lock the resolved values with tests.

STEP 4 — REMOVE DEAD COLUMNS (SEPARATE PR — after STEP 3 verified)

Once the resolver prefers LOOKUP and tests lock the values, the per-row
BLE/MEP carbon columns are dead fallback data. Remove them in a final
schema-cleanup PR.

HARD RULES

1. STEP 1 (this PR) is POPULATE ONLY. Do NOT reorder the resolver here.
   With the resolver unchanged, adding LOOKUP rows changes NO delivered
   number (material-params still win). Safe.
2. Timber: coordinate with Z-25 (fossil+biogenic split). Skip timber
   rows if Z-25 hasn't landed; note it in the commit body.
3. Tests: assert GetCarbon/GetDensity/GetCost return the populated
   values for the new materials (was 0 pre-population).

BUILD + TEST

dotnet build StingTools/StingTools.csproj
dotnet test StingTools.Boq.Tests

COMMIT + PUSH

Single commit on fix/material-lookup-populate (STEP 1+2 only).

REPLY (under 350 words)

- Materials added to LOOKUP (table: material → carbon/density/cost)
- Reconciliation table (LOOKUP vs per-row BLE/MEP, match column)
- Any mismatch found (STOP + report if so)
- Timber: skipped (Z-25 pending) or coordinated
- Tests added
- dotnet build + test result
- Commit hash + push confirmation
- Confirm: with resolver UNCHANGED, zero delivered numbers moved
  (material-params still win) — STEP 3 reorder is the separate
  sign-off-gated PR
```

---

## Z-25 — timber Path C: split into fossil + biogenic columns (USER CHOSE C)

```
Z-25 Path C — Split timber embodied carbon into separate fossil +
biogenic columns so whole-life reports match RIBA 2030 / LETI / RICS
WLCA conventions (show A1-A3 fossil + biogenic separately, sum either
way). Schema change across the material CSVs + resolver awareness +
tests. ~1 day.

REQUIRED reading FIRST

git fetch origin main audit/numerics-deep-review
git show origin/main:docs/UI_CLEANUP_CAMPAIGN.md | grep -A12 'Z-25'
git show audit/numerics-deep-review:docs/PHASE_Z_NUMERIC_AUDIT.md | grep -A8 '2\.8'

Background: BLE_MATERIALS.csv timber rows carry C=-900 kgCO2/m3 — a
biogenic-INCLUSIVE value (the -900 is sequestered carbon). Steel/
concrete (post-Z-20) are A1-A3 GROSS (no biogenic). Mixing -900 timber
with gross steel/concrete makes whole-life totals misleadingly low.
RIBA 2030 / LETI / RICS WLCA all want SEPARATED reporting: fossil
A1-A3 and biogenic A1-A3 as distinct line items.

BRANCH

fix/timber-biogenic-split off latest origin/main. No PR yet.

SCOPE — schema change: 2 carbon columns instead of 1

STEP 1 — define the two-column model
  - PROP_CARBON_FOSSIL_KG_M3  (A1-A3 manufacturing, always >= 0)
  - PROP_CARBON_BIOGENIC_KG_M3 (sequestered, <= 0 for timber, 0 for
    non-bio materials)
  The legacy single PROP_CARBON_KG_M3 = fossil + biogenic (for
  backwards-compat display / existing consumers that want the net).

STEP 2 — populate timber rows
  For each timber material in BLE_MATERIALS.csv (and MEP if any):
    - PROP_CARBON_FOSSIL_KG_M3 = the manufacturing-only A1-A3 value
      (sawn softwood ~ +50 to +200 kgCO2/m3 per ICE v3.0 — cite the
      exact value used)
    - PROP_CARBON_BIOGENIC_KG_M3 = the sequestration value (the -900
      was the biogenic component; refine to ICE v3.0 sawn softwood
      sequestration ~ -1.6 kg/kg × density)
    - keep/recompute PROP_CARBON_KG_M3 = fossil + biogenic (net) for
      any consumer still reading the single column

  For NON-timber materials: PROP_CARBON_FOSSIL_KG_M3 = existing
  PROP_CARBON_KG_M3, PROP_CARBON_BIOGENIC_KG_M3 = 0.

STEP 3 — resolver + model awareness
  - MaterialRow (or the carbon model) gains FossilCarbon + BiogenicCarbon
    properties alongside the existing net CarbonKgCo2e.
  - CarbonFactorResolver / GetCarbon: keep the existing net API working
    (returns fossil+biogenic), ADD GetCarbonFossil / GetCarbonBiogenic.
  - BOQ carbon report / any carbon dashboard: surface the 3 numbers
    (fossil / biogenic / net) where it currently shows 1. If the report
    UI is out of scope for this PR, at minimum expose the data so a
    follow-up report PR can render it — document that.

STEP 4 — MATERIAL_LOOKUP coordination
  If Z-24b is populating LOOKUP, the timber LOOKUP rows must carry both
  fossil + biogenic properties too. Coordinate: either land this PR
  first (so Z-24b adds timber with the split) or note the dependency.

HARD RULES

1. The legacy net PROP_CARBON_KG_M3 must keep working — don't break
   existing consumers that read the single column. Net = fossil +
   biogenic.
2. Cite ICE v3.0 for both the fossil and biogenic timber values —
   don't invent. The -900 was a net/biogenic blend; the split needs
   real A1-A3-fossil and A1-A3-biogenic components.
3. Tests: assert GetCarbonFossil + GetCarbonBiogenic == GetCarbon (net)
   for timber; assert biogenic == 0 for steel/concrete; assert the
   net value for non-timber is unchanged from today (no regression).
4. This MOVES delivered carbon numbers for any report that sums timber
   — that's the POINT (gross totals stop being misleadingly low). NOTE
   the deltas in the commit body for sign-off.

BUILD + TEST

dotnet build StingTools/StingTools.csproj
dotnet test StingTools.Boq.Tests

COMMIT + PUSH

Single commit on fix/timber-biogenic-split. Confirm push.

REPLY (under 400 words)

- The two new columns + the legacy-net relationship
- Timber values used (fossil + biogenic, both ICE v3.0 cited)
- Resolver API additions (GetCarbonFossil / GetCarbonBiogenic)
- Report-surfacing: done in this PR, or data-exposed-for-followup?
- Z-24b coordination (timber LOOKUP rows)
- Tests added
- Delivered carbon deltas (the gross totals that moved — table)
- dotnet build + test result
- Commit hash + push confirmation
```

---

## Z-1 — Photo*.cs services break the server build (terminal, server-side)

```
Z-1 — Planscape.Infrastructure does not compile: 43 CS1061 errors in
Photo*.cs services referencing DbSets that don't exist on
PlanscapeDbContext. Decide: dead feature (delete) or unfinished
(add DbSets). Audit FIRST, then act.

REQUIRED reading FIRST

git fetch origin main
git show origin/main:docs/UI_CLEANUP_CAMPAIGN.md | grep -A4 'Z-1 —'

The errors (from the P1-A build): PhotoChecklistDueJob.cs and related
Photo*.cs in Planscape.Server/src/Planscape.Infrastructure/Services/
reference PhotoChecklistItems / PhotoAlbumPhotos / PhotoPolicies /
PhotoAlbums DbSets that aren't declared on PlanscapeDbContext.

The running webapp uses a different build path (the API project builds;
Infrastructure doesn't) — confirming the Photo* code is orphaned or
half-wired.

BRANCH

First: audit/photo-services off latest origin/main (READ-ONLY audit).
Then a fix branch once you've decided.

STEP 1 — AUDIT (read-only, commit a findings note)

1. dotnet build Planscape.Server/src/Planscape.Infrastructure/Planscape.Infrastructure.csproj
   — capture the exact 43 errors + which Photo* files.
2. For each missing DbSet (PhotoChecklistItems / PhotoAlbumPhotos /
   PhotoPolicies / PhotoAlbums):
   - Is there an Entity class for it? (grep the Entities folder)
   - Is there a Controller / API surface that would use it? (grep
     Controllers for Photo*)
   - Is there a migration that would have created its table? (grep
     Migrations)
   - Is the mobile app calling its endpoints? (the Planscape Expo app
     under Planscape/app/ — grep for photo-album / photo-checklist)
3. Classify: DEAD (no entity, no controller, no migration, no caller —
   delete the orphan services) vs UNFINISHED (entity + controller exist,
   just the DbSet declaration + migration missing — wire it up).

STEP 2 — ACT (separate fix branch, based on the classification)

IF DEAD:
  - Delete the orphan Photo*.cs services + any dead Hangfire job
    registration. Confirm the API project still builds. Confirm no
    controller references the deleted services.
  - branch fix/remove-dead-photo-services

IF UNFINISHED:
  - Add the missing DbSet<T> declarations to PlanscapeDbContext.
  - Generate the migration (dotnet ef migrations add PhotoServices —
    BUT heed the Z-2 stale-snapshot trap: the model snapshot is 55
    entities behind, so auto-migration may emit a monster. Hand-author
    the migration like P1-A did if auto-gen is unsafe.)
  - branch fix/wire-photo-services

HARD RULES

1. Audit before acting. The decision (dead vs unfinished) determines
   the entire fix shape — don't guess.
2. Heed Z-2: do NOT run a naive `dotnet ef migrations add` if the
   snapshot is stale — it'll emit a 55-table monster. Hand-author.
3. Whatever you do, Planscape.Infrastructure MUST compile cleanly after.
4. Confirm push with git ls-remote.

REPLY (under 400 words)

- The 43 errors grouped by Photo* file
- Per missing DbSet: entity exists? controller exists? migration
  exists? mobile caller exists?
- Classification: DEAD or UNFINISHED (+ evidence)
- Action taken (delete vs wire) + branch name
- dotnet build Planscape.Infrastructure result (0 errors required)
- If migration generated: hand-authored or auto-gen? (Z-2 trap)
- Commit hash + push confirmation
```

---

## Z-2 — EF model snapshot is 55 entities stale (terminal, server-side)

```
Z-2 — PlanscapeDbContextModelSnapshot tracks 58 entities;
OnModelCreating configures 113 (~55 stale). 0 of 72 migrations carry
[Migration] attributes — the dev workflow uses CreateTables() not
Migrate(). Future migrations face the trap P1-A hand-authored around.
Decide the workflow + bring the snapshot current OR formalise the
CreateTables-only path.

REQUIRED reading FIRST

git fetch origin main
git show origin/main:docs/UI_CLEANUP_CAMPAIGN.md | grep -A4 'Z-2 —'

Background (from P1-A): the EF model snapshot is severely behind the
actual model. dotnet ef migrations add would emit a ~55-table diff.
The app boots via CreateTables() (Program.cs:1280 era) not Migrate(),
so migrations are half-abandoned infrastructure.

BRANCH

First: audit/ef-snapshot-state off latest origin/main (READ-ONLY).
Then a decision + fix.

STEP 1 — AUDIT (read-only, commit a findings note)

1. Confirm the gap: count DbSets in PlanscapeDbContext vs entities in
   the snapshot. (dotnet ef dbcontext info / read the snapshot file.)
2. Confirm the boot path: does Program.cs call db.Database.Migrate()
   or EnsureCreated() / a custom CreateTables()? Grep both.
3. Confirm migration state: how many migrations, how many have
   Designer/[Migration] files, when was the last real one applied?
4. Determine: is ANY environment using migrations to deploy (prod?)
   or is everything CreateTables()? This decides the path.

STEP 2 — DECIDE (the audit informs this; recommend one)

PATH A — formalise CreateTables-only, retire migrations
  - If nothing deploys via migrations, the migration folder is dead
    weight + a trap. Document the CreateTables workflow as canonical.
    Optionally delete the abandoned migration files (CAUTION: only if
    truly unused — prod schema history may need them).
  - Lowest effort; matches reality.

PATH B — regenerate the snapshot to match the 113-entity model
  - dotnet ef migrations add SnapshotCatchup — but this emits the
    55-table monster. Instead: regenerate the snapshot WITHOUT a
    destructive migration (there are EF techniques to baseline a
    snapshot to current model without a migration that drops/recreates).
  - Higher effort; only worth it if you WANT migrations to work going
    forward.

PATH C — hybrid: baseline now, migrations forward
  - Mark current schema as the baseline (an empty/no-op migration that
    just snapshots current state), then real migrations from here.
  - The "proper" EF answer; medium effort.

STEP 3 — ACT (separate branch per the decision)

HARD RULES

1. Audit + decide before touching migrations. A wrong move here can
   make P1-A's IfcIngest migration (and HealthcarePack, HvacEngine-
   Snapshots) inconsistent.
2. Do NOT run a destructive migration against any environment.
3. If you delete migration files, confirm NO environment replays them
   from scratch (prod that runs Migrate() on deploy would break).
4. Coordinate with Z-1 (if Z-1 adds Photo DbSets + a migration, that
   migration must fit whatever workflow Z-2 establishes).

REPLY (under 400 words)

- Snapshot entity count vs OnModelCreating count (confirm ~55 gap)
- Boot path: Migrate() / EnsureCreated() / CreateTables()? (cite line)
- Migration state: count, how many have Designer files, last applied
- Does ANY env deploy via migrations? (the deciding question)
- Recommended path (A retire / B regenerate / C baseline) + why
- Action taken + branch name
- Commit hash + push confirmation
- IMPORTANT: if any environment runs Migrate() on deploy, FLAG before
  changing anything — schema history is load-bearing there.
```


---

## Z-1c → Z-1e — server + tests build green (MERGED chain)

The Z-1 cascade is fully resolved. Linear branch chain, each build-verified
on Windows before the next:

- **Z-1b** `fix/api-dedupe-build` — deduped 6 merge-artifact duplicate
  definitions in the API (cleared the wall hiding the rest).
- **Z-1c** `fix/wire-photo-dbsets-and-api-bugs` — wired the 5 missing Photo
  DbSets (`PhotoAccessRules` / `PhotoApprovalSignoffs` / `PhotoNdaAcceptances`
  / `PhotoShareLinks` / `PhotoVoiceNotes`; no migration — `CreateTables()`
  builds them, Z-2 trap avoided) **+** repaired the 20 genuine code bugs
  (`IssueAudioNotes` DTO-migration leftover, byte-identical `IssuesController`
  dup, `SeedData` rename repoint). API + Infrastructure + Core build clean.
- **Z-1d** `fix/test-project-build` — removed the divergent duplicate
  `StingBimWebApplicationFactory.cs` (kept canonical `PlanscapeWebApplicationFactory.cs`
  with the correct `UserRole.Contributor`) + added `public partial class
  Program { }` so the test host can see it.
- **Z-1e** `fix/test-config-using` — one-line `using
  Microsoft.Extensions.Configuration;` in `AuditCategoriesControllerTests.cs`.

**Result: `Planscape.sln` builds 0 errors (14 warnings) for the first time** —
server source AND test project. Current server source is now **DEPLOYABLE**,
which is the trigger to revisit **Z-2b** (fresh-prod-DB boot via `CreateTables()`).

Side-effect cleanup: the Z-1c "API bugs" commit had accidentally swept in
unrelated 3D-viewer frontend work via `git add -A`. It was carved back out
onto **`feat/coordination-viewer-orbit-pivot`** (ACC-style orbit-pivot/focus,
dblclick pivot, shift-dblclick frame-fit — `coordination-viewer.js` +
`viewer.html`), so Z-1c carries zero viewer diff. That branch awaits its own
review/merge.
