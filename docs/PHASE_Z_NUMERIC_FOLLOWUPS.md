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

## Z-22 (63 formulas in cycles) — DO NOT brief as a single-PR fix

Z-22 is a multi-day project: identify each cycle, decide algebraic resolution per cycle, wire into FormulaEngine. Recommended approach:

1. **Audit-first PR** — dump the cycle graph as a visualisation (Graphviz `.dot` file) so the cycles are inspectable. Group by domain (concrete-takeoff cycles, plumbing-head cycles, HVAC-flow cycles). Output: `docs/PHASE_Z_FORMULA_CYCLES.md`.
2. **Per-domain resolution PR** — one PR per cycle cluster. Pick the algebraic resolution that minimises BOQ impact (parameter elimination preferred; fixed-point iteration with convergence test acceptable; break-by-construction last resort).
3. **Engine change PR** — once cycles are resolved at the formula level, FormulaEngine no longer needs the "run last with stale inputs" fallback. Remove the fallback + the warning log.

I'll write the Z-22 audit prompt separately when you're ready — it's a 1-2 week project and needs a planning conversation, not a one-shot agent.

---

## Z-23 small findings — defer until the top-3 are verified

The 10 P1 + 11 P2 items in `docs/PHASE_Z_NUMERIC_AUDIT.md` §1-8 (smaller stuff like BLE template-default density for non-concrete rows, softwood density = hardwood, BOQ ProvisionalSum signed credit-vs-overrun, CIBSE velocity max slightly permissive) can wait until the top-3 are merged + Revit-verified. They're isolated single-line fixes that fall into the same "agent + dotnet build + numeric regression check" pattern. Cluster them into one cleanup PR when convenient — not their own campaign.
