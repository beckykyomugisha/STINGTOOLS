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
