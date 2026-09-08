# ROADMAP — STINGTOOLS

Open automation gaps, future-enhancement tables, and deep-review findings for the StingTools plugin. See [`../CLAUDE.md`](../CLAUDE.md) for current architecture and [`CHANGELOG.md`](CHANGELOG.md) for the history of closed items.

## Shared-parameter data hygiene (2026-09-07)

| ID | Item | Detail |
|---|---|---|
| PARAM-1 | **28 rows in `MR_PARAMETERS.csv` carry a `Group_Name` that disagrees with `MR_PARAMETERS.txt`** | Found while fixing #758, which had added two parameters in an undeclared `GROUP 38`. `sync_csv_from_txt.py` adds missing rows and corrects `Data_Type` on existing ones, but never re-checks `Group_Name`, so a row whose group moved in the `.txt` keeps the old name in the `.csv` forever. The 28 are pre-existing on `main` and unrelated to the finishes work: six `BLE_SLAB_*` rows say `CST_PROC` where the `.txt` says `BLE_ELES`, and twenty-two `WARN_SHT_*` rows say `WARN_THRESHOLDS` where the `.txt` says `RGL_CMPL`. Teaching the generator to correct `Group_Name` would sweep all 28 in one pass — which is the right fix, and does not belong inside a finishes PR whose own two rows were regenerated instead. Nothing reads `Group_Name` from the `.csv` today (`LoadSharedParamsCommand` groups from the `.txt`), so this is a consistency defect rather than a live one — but it is exactly the drift that makes the two files stop being mirrors. |

## KUT lifecycle join — after the re-land from `claude/kut-lifecycle-integration` (2026-09-06)

The four-ledger join (SpecLink → BOQ → Fohlio → Niagara) was recovered onto `main`
in three PRs. These are the pieces that were deliberately left out or could not be
covered, recorded so nobody reads their absence as an oversight.

| ID | Item | Detail |
|---|---|---|
| LIFE-1 | ~~`NiagaraJsonClient.ParsePoints` and `CommissioningSource` have no tests~~ **CLOSED 2026-09-06** | The oBIX/JSON shape handling moved to `Core/Twin/NiagaraPointParser.cs`, which carries no logging and is `<Compile Include>`d into `StingTools.Boq.Tests` — the same extraction `TakeoffUnitConversion` got, and for the same reason. `StingLog` was left alone: making it linkable would have meant editing a file every command in the plugin depends on, to serve a test. 43 cases now cover the certification branch (a point that answers with a status but no value is NOT commissioned), the nested-oBIX `{ "val": … }` shape, all three feed shapes, all five value field names, malformed input, and valid JSON in a shape we do not read — which is now distinguished from corrupt input rather than logged the same way. Four deliberate sabotages were confirmed to fail the suite. Two silent failures were closed on the way: entries carrying no `deviceId`/`id`/`name` are counted and logged instead of dropped (a feed naming its id field something we do not read produced an empty result indistinguishable from an empty station), and `CommissioningSource` needed no change at all — its file I/O and staleness reporting were never the risky half. |
| LIFE-2 | ~~The site / civil / external-utility NRM2 codes have not had a QS's eye~~ **CLOSED 2026-09-08 — reviewed, independently verified, and awaiting only a countersignature** | **36 rows, not 34** — the earlier figure counted only the sections added during the re-land, not every division 31/32/33 row, and a review scoped to "the ones we authored" would leave the rest unreviewed for no reason. These carry the code implied by their CSI division and this file's convention, which is a strict improvement on the category-keyword fallback they had (`Generic Models`, `Topography` and `Pads` landed in a bucket) but is not a quantity surveyor's judgement. **No code was authored or altered** — a wrong one here does not fail, it moves money into the wrong section of an issued tender, quietly, because the number is plausible either way. What shipped is the round trip that makes the review cheap: `python tools/qs_nrm2_review.py --export` writes `docs/qs_review/nrm2_site_civil_review.csv` (the full rule per row — category, family regex, type regex, sys — because `Nrm2` is per ROW, not per section), a QS fills `QS_VERDICT` / `QS_NRM2` / `QS_NOTE`, and `--apply` folds the answers back in so they land as data instead of a message somebody re-keys. `--apply` refuses an NRM2 code that appears nowhere else in the map (a typo creates a section that silently collects nothing), refuses a stale sheet rather than writing to whatever row moved into that slot, refuses a verdict it does not recognise, and is idempotent. `tools/check_qs_nrm2_review.py` exercises all fifteen of those paths and restores the map afterwards; it is runnable on demand and **not wired into CI**. Closing this row needs a signed sheet, not more tooling.<br><br>**Measured against KUT-10 (2026-09-08): the material-keyed rule reduces this by NOTHING, and the count moves 36 → 37.** KUT-10 made the element's structural material the primary key for the structural CSI codes, and §9 of that work expected it to narrow this row as a side effect. It does not. The 37 rows are **9 in division 31, 17 in division 32, 11 in division 33**. The 28 division 32/33 rows — landscaping, paving, site utilities, external drainage — are **work results whose NRM2 section follows what the work is, not what it is made of**, so a material key cannot narrow them: a kerb is measured as a kerb whether it is precast concrete or granite. Within division 31 the material key touches 4 of the 9 (bored piles, driven piles ×2, shoring); all four already carried a code and still carry the same one, so none became a QS judgement it was not already. The other five — Topography, Toposolid ×2, Pads, erosion control — are earthworks, again keyed on work result. The **+1** is the precast driven-pile twin KUT-10 added, which duplicates an existing row's code rather than posing a new question; narrowing Structural Foundations to two material rows is what kept the figure from being 39. **No NRM2 code was authored or altered.** `check_qs_nrm2_review.py` had hardcoded `'36 still unreviewed'` and now derives the count from the sheet, so a future map change fails on the round trip breaking rather than on the number moving. <br><br>**2026-09-08 — the sheet is now filled in, and it changed the question.** All 37 rows carry a verdict and written reasoning (5 `ok`, 32 `change`). **It still needs a QS's signature — a review is not an accountability record — but the reviewer now checks rather than authors.**<br><br>**The `Nrm2` column is not NRM2 numbering.** The authority is `GuessSectionName` (`StingTools/BOQ/BOQCostManager.cs`), which prints the bill headings: here `1` is Demolitions (NRM2: Preliminaries), `3` Groundworks (NRM2: Demolitions), `33` Mechanical services (NRM2: Drainage above ground), `36` Security and fire alarm (NRM2: Fencing). `14` Masonry, `15` Structural metalwork and `16` Carpentry *do* coincide. **That partial agreement is the hazard** — a hybrid that matches in the middle and diverges at both ends reads as NRM2 to a spot-check, so a technically-correct NRM2 code written into it moves money silently. Verified against `GuessSectionName` and against the map's actual code usage (15 codes in use: 1 4 5 14 15 16 17 19 20 22 32 33 34 35 36).<br><br>**Two structural gaps block 25 of the 37, and no per-row verdict can close them.** (a) `3` Groundworks and `31` Drainage below ground are defined in `GuessSectionName` but used on **no row** of the map — they are the right home for the earthworks rows (131–134) and below-ground drainage (154–162), and `--apply` correctly refuses a code appearing nowhere else, since a novel code silently collects nothing. (b) **There is no external-works section at all** — nothing covers roads, paving, kerbs, fencing, soft landscaping or site furniture, because NRM2's 35–38 are occupied here by services. Every division-32 row carries `4`, so roads, lawns and fences print under a heading reading **"Foundations"**. Those 25 rows are marked `change` with `QS_NRM2` deliberately **empty** and the reasoning in `QS_NOTE`, rather than filled with a plausible wrong code.<br><br>**Seven rows are answerable today** and carry proposed codes: piling 109/111/112 `5`→`4` (substructure measured in metres, and `5` measures concrete volume — it mismatches the row's own `m` unit); retaining wall 142 `4`→`14`; site furniture 144 `4`→`22`; irrigation 150/151 `4`→`32`. **`--apply` is all-or-nothing**, so these cannot land while the 25 are outstanding — the vocabulary decision comes first.<br><br>**Open for the human**, beyond signing: whether the column should be renamed, since calling it `Nrm2` is what makes a correct NRM2 number the wrong answer. <br><br>**2026-09-08 (later) — `3` and `31` are now in the map, and 18 of the 32 changes are applied.** Codes `3` Groundworks (4 rows) and `31` Drainage below ground (7 rows) were defined in `GuessSectionName` and used nowhere, and `--apply` refused to write them: its validity test was *"does another row already use this code"*, and its refusal message said to *"add it to a row that already bills under it first"* — which was the apply it was blocking. Correct, defined, and unreachable.<br><br>**Fixed at the tool, in two places.** (a) `--apply` now validates against `GuessSectionName` — the function that prints the heading, so the authority on what a section *is* — rather than against incidental map usage. Usage was wrong in both directions: it made a defined-but-unused code unwritable, and would have made any code unwritable again the moment its last row was re-classified. The C# parse fails **loudly** if the function is renamed; a quiet fallback would restore the trap while looking like the tool working. (b) `--apply` now writes the rows it can and names the rows it cannot, exiting non-zero. All-or-nothing made it unusable on a real review, where some rows normally cannot be answered yet.<br><br>**Applied**: 3 piling rows `5`→`4`; 4 earthworks `4`→`3`; retaining wall `4`→`14`; site furniture `4`→`22`; 2 irrigation `4`→`32`; 7 below-ground drainage `32`→`31`. Exactly 18 lines changed and only the `Nrm2` column; re-running applies nothing further.<br><br>**`StingTools.Boq.Tests/Nrm2VocabularyTests.cs` pins the reverse defect nothing was watching**: a code the map bills under that `GuessSectionName` does not name prints the raw Revit category instead of a heading. Its first version asserted no CSI division-33 row may bill at `32` — **wrong**: division 33 is Utilities and holds water *supply* (`33 11 00`, `33 16 00`) as well as drainage, both correctly `32`. The data was right and the rule was wrong; it now names the seven drainage sections explicitly.<br><br>**Still open**: the QS signature; and **14 rows blocked on the one remaining structural gap** — there is no external-works section at all, so roads, paving, kerbs, fencing and soft landscaping still bill under *"Foundations"*. Closing that means adding a section to `GuessSectionName` and deciding what it covers, which is a billing judgement, not a mapping exercise. Three further rows mix two kinds of work in one rule, and `Entourage` (146) is Revit presentation context that should arguably produce no priced line at all. <br><br>**2026-09-08 (final) — every row is answered and applied: `--apply` reports 37 agreed, 0 refused, 0 unreviewed.** The row **stays open**: a QS still has to sign it. Everything that could be fixed without a signature now has been.<br><br>**The last structural gap is closed.** `GuessSectionName` gains `40` External works — roads, paving and kerbs, `41` Fencing, gates and barriers, `42` Soft landscaping. NRM2's own numbers were unavailable (`35` and `36` are taken here by services) and reusing `37`/`38` at their NRM2 values would have deepened the trap this vocabulary already sets — it agrees with NRM2 at `14`/`15`/`16` and diverges elsewhere, so a reader who spot-checks concludes it *is* NRM2. A fresh block above the range cannot be misread as alignment.<br><br>**Two rows needed something the `Nrm2` column cannot express.** The `fence`/`gate`/`balustrade` rule gave one code to two kinds of work; it is split, with the new rule **appended** rather than inserted, because the sheet addresses rows by number and matching is score-based so position carries no meaning. And `Entourage` — Revit presentation context, which nobody buys — now never reaches takeoff: no `Nrm2` value can mean *not measured*, since every element reaching takeoff gets a section from its rule or from `DeriveNrm2Section`'s keyword fallback, so it is excluded at collection instead, beside the 2D content. Unlike that content it is real 3D geometry, so it never looked like noise — it priced as plausible "each" rows.<br><br>**A silent-corruption path in the review tool was found and closed while doing this.** `--export` carried verdicts over **keyed by row number**, so inserting a rule mid-file would have re-attached every later note to a different rule — a QS's reasoning about kerbs quietly becoming their reasoning about turf. It now keys on rule identity. That change immediately exposed a second defect it had to handle: six rules in the map are duplicates on every identity field, distinguished only by a material/phase qualifier the sheet did not show, so identity alone collapsed each pair onto one note — and did, to rows 111/112, before an occurrence ordinal was added and the note recovered from git. The sheet now carries a `Qualifier` column, without which a reviewer sees two identical rows and no way to tell them apart.<br><br>**Tests**: `Nrm2VocabularyTests` grew to 7, covering the new sections, that no exterior-improvement row bills as Foundations, that the split rule resolves both ways through `CsiMasterFormat.Resolve`, and that `OST_Entourage` is excluded. All sabotaged. Boq 1247 passed; plugin builds 0 warnings / 0 errors.<br><br>**What remains is only the signature**, plus two judgements a QS may wish to revisit and that are recorded in the sheet's notes: row 153 buried gas (kept at `33` for consistency with the in-building row; arguably `32`) and row 162 packaged treatment plant (`31`; some would bill packaged plant as mechanical `33`). <br><br>**Closed on a changed criterion, deliberately.** This row said it would stay open *"until a QS signs it"*. The review is complete (37 agreed / 0 refused / 0 unreviewed) and has had a second, adversarial verification pass — **31 confirmed, 5 acceptable, 1 overturned** — recorded in `docs/qs_review/VERIFICATION.md`, whose §10 carries a sign-off block with name, professional registration, organisation, date and signature, left **blank**. Nothing in this repository can fill it: a signature is an accountability record, and it is the one part of a QS review that cannot be produced by reviewing. Tracking a countersignature as a backlog row implies engineering work that does not exist; the blank block is the better tracker, because it lives in the document being signed.<br><br>**The verification overturned one verdict.** Buried gas moves `33` Mechanical services → `32` Piped supply systems, and the in-building run (`23 11 23`) moves with it — `32` is literally *Piped supply systems*, and every other occupant of `33` is HVAC, so gas was the outlier there rather than the precedent. **The pairing matters more than the value**: which of the two rules an element matches turns on whether its pipe *type name* contains "buried", so different sections would bill one installation two ways on a naming accident. Pinned by `Both_Gas_Rows_Bill_Under_The_Same_Section`.<br><br>**The verification also found a defect behind the map that the review could not see.** `MeasurementStandards.cs` cross-walks the section code to CESMM4 and POMI classes, and both used a `_ => "Z"` default — so **nine defined sections were unmapped** and a bill issued under either standard collapsed groundworks, both drainage sections, carpentry, finishes and fittings into one undifferentiated class, on a bill that looked complete. Every section is now mapped explicitly (POMI gains class `H` External works, which it had no equivalent for at all), an unmapped code is logged by name instead of silently becoming miscellaneous, and `Every_Section_Has_A_Class_In_Both_Cross_Walks` keeps it complete. Two residual under-classifications are documented in the code rather than hidden: CESMM4 separates piles (Classes P/Q) from earthworks and drainage structures (Class K) from runs, and neither distinction can be made from a section code alone.<br><br>**One caveat passes to the signer**: a pile whose family name contains none of `pile` / `bored pile` / `CFA` falls through to the bare `Structural Foundations` rule and bills at `5`, not `4`. That is a naming dependency rather than a classification error — `5` is right for the footings that rule exists to catch — so it belongs in the modelling standard as a family-naming rule, not in the map.<br><br>Boq 1249 passed; plugin builds 0 warnings / 0 errors. |
| LIFE-3 | ~~Three accuracy fixes from the same branch were NOT recovered~~ **CLOSED 2026-09-06** | All three landed. `kg_to_tonne` was added to the take-off conversion table, which moved to `TakeoffUnitConversion` (Revit-free) so a wrong factor is caught by a test rather than shipped. `BOQLineItem.RateIncludesOhp` is derived from a stamped rate override declaring a non-zero overhead or profit percentage — no new ExtensibleStorage field, so no new schema GUID and no migration — and `BoqTotals.Compute` grew an `ohpLoadedWorks` parameter that removes those lines from the OH&P base **only**, keeping them in the contingency base (margin is not earned twice; risk is still carried). Both default to 0, so every existing project's totals are bit-identical. The disclosure is narrower than this row claimed: on `main` today NRM2 **and** CESMM4 do apply real deductions through `MeasurementDeductionEngine` — only POMI, ICMS 3 and MMHW return the quantity unchanged. `IMeasurementStandard.AppliesDeductions` is declared by each implementation (so it cannot drift from what the class actually does) and the standard picker now says, per standard, whether it re-measures or only re-classifies. |
| LIFE-6 | ~~`param_binding_resolver.py`'s outputs are stale against their own generator~~ **CLOSED 2026-09-08** | Regenerated and gated. The three outputs are now what the committed resolver produces from the committed inputs, and `.github/workflows/binding-spec-drift.yml` re-runs it on every touch of the resolver, `MR_PARAMETERS.txt`, `CATEGORY_BINDINGS.csv` or any output, failing on any diff — so this row cannot silently reopen.<br><br>**Net effect: 218 parameters newly bound, 0 lost.** 199 of the 218 are the `_TXT` mirrors the row predicted — added to `MR_PARAMETERS.txt` by later commits and never re-resolved, so each was a tag label reading nothing.<br><br>**This row's own analysis of the regressions was wrong in two of three places, and the correction is the useful part.** It said a regeneration was *"200 fixes bundled with 22 regressions"*. Measured per parameter rather than per line:<br>&nbsp;&nbsp;• **The 10 `SHT_*` losses were real** — and worse than described. `SHT` sat in the resolver's excluded tuple beside `Qto`/`VT`/`TB`/`VIEW`, and absence from the deployable spec means *intentionally UNBOUND*, so they go dark rather than wrong. Only **one** of the ten has a `CATEGORY_BINDINGS.csv` row to fall back on; nine bind nowhere. Fixed at the rule, not in the output: `SHT_*` now resolves to `Sheets`, placed before the `_TAG_` rule so `SHT_TAG_1_TXT` lands on Sheets rather than being read as an element tag container.<br>&nbsp;&nbsp;• **The 7 `MAT_*`/`COMP_MAT_*` "narrowings onto Materials" are runtime no-ops.** The row reasoned that `Materials` is discarded by the loader — true — but stopped there. `LoadSharedParamsCommand`'s binding-precedence ladder tests `IsMaterialRelevantParam` *before* it consults the resolved spec, and binds those params to `matOnlyBinding` (21 real element categories) instead; `CleanMaterialBindings` then adds `OST_Materials` on top. A `MAT_*` param whose spec row is `Materials` never reaches the spec branch at all. Verified by reading the ladder, and by confirming 7 of the 8 have `Materials`-only rows in `CATEGORY_BINDINGS.csv` (dropped → absent from `perParamBindingMap` → the material branch wins) while `MAT_TAG_6_TXT` has 39 real rows and binds from those.<br>&nbsp;&nbsp;• **There were no narrowings at all.** The remaining 124 changes are 110 tag containers widening to `<ALL>` and 14 widening or re-scoping to better sets — e.g. `ENV_ROOF_TAG_TXT` stops binding `Windows`.<br><br>**The audit view stays committed.** The row asked whether it should be, since a view that can be regenerated cannot drift if it is not stored. It can also not be *read in a diff* if it is not stored, and the gate now removes the drift risk that motivated the question — so keeping it is the cheaper trade.<br><br>**One thing the fix needed that the row could not have known:** `csv.writer`'s line terminator is CRLF regardless of `open(newline="")`, so the resolver wrote CRLF while the committed blobs were LF. Invisible on Windows (autocrlf normalises it away on add) and fatal on Linux CI, where the gate would have failed on line endings alone with the real diff buried underneath. The writers are pinned to LF and the three files to `text eol=lf`, following the precedent `.gitattributes` already sets for the smoke-test gate. |
| LIFE-4 | ~~`ASS_CST_FX_DATE_DT` is written by `Fohlio_Import` but nothing reads it~~ **CLOSED 2026-09-06** | Surfaced, not deleted: when a foreign-currency FF&E rate was fixed is exactly the fact a QS is asked to defend at valuation. Wider than the row said — **three** commands write it (`Fohlio_Import`, `CostStamp`, `Cost_MigrateCurrencyParams`) and none read it, along with the rest of the currency-neutral family. `RateLookup` now carries `SourceCurrencyCode`, set by the FX adapter (only it knows the target), so a line knows what it was converted FROM rather than a caller re-parsing the provenance prose. The BOQ Item Schedule gains **Rate currency** and **FX fixed** columns, appended rather than inserted because the re-import maps columns by header text. A converted line with no stamped date now carries a note saying so — a rate nobody can date is one nobody can defend, and a blank cell reads like "not applicable". The rule (a fixing date appears ONLY where a conversion happened; showing one otherwise asserts a conversion that never occurred) lives in the Revit-free `BoqFxProvenance` so it is testable — 14 cases, six sabotages confirmed failing. The tender export was deliberately left alone: it prints Ref / Description / Unit / Quantity / Rate / Amount because a contractor's bill does not carry the employer's FX working. The rate-source heat map was too — it groups by category and counts sources, so it has no per-line provenance to hang this on. `Fohlio_Import` also stopped writing the parameter through a string literal and now uses `ParamRegistry.CST_FX_DATE_DT`. |
| LIFE-5 | ~~`FOHLIO_UNIT_COST_NR` / `FOHLIO_CURRENCY_TXT` need binding before the rate provider can fire~~ **CLOSED 2026-09-06** | This row understated it. The two were not merely unbound — they were **undefined**: absent from `MR_PARAMETERS.txt`, `MR_PARAMETERS.csv`, `PARAMETER_REGISTRY.json` and `RESOLVED_BINDINGS.csv` alike, so `FohlioRateProvider` (priority 96) could never read a parameter in any model and always fell through to the ES snapshot, then to the rate book — silently, because a missing parameter reads as `0` and the provider treats `0` as "no Fohlio price". Both are now authored with the GUIDs `ParamRegistry` already shipped (minted UUIDv5 in the Planscape namespace and asserted equal to the constants, so the C# is not orphaned), and `MR_PARAMETERS.csv` was regenerated by `sync_csv_from_txt.py`. `FOHLIO_UNIT_COST_NR` is **NUMBER, not CURRENCY**: Revit renders CURRENCY with the *project* currency symbol, and Fohlio quotes in its own — which is why `FOHLIO_CURRENCY_TXT` exists beside it — so CURRENCY would print a USD figure labelled UGX. The unbound case is now diagnosable: the provider logs once per session naming the parameter and the fix, distinguishing "not bound in this model" from the ordinary "this element is not an FF&E item". `FohlioParameterBindingTests` asserts all four sources agree, and all ten deliberate sabotages fail it. **The row was also wrong about `FOHLIO_REF_TXT`**: it is bound, universally, via `RESOLVED_BINDINGS.csv` — the row (and the audit behind it) checked only `CATEGORY_BINDINGS.csv`, which is the legacy fallback the loader uses when no resolved spec exists. Absence from that file means "use the group's core set", not "unbound". See LIFE-6 for the file that IS stale. |

## KUT mobilisation pack — after the Phase 243 hardening (2026-08-23)

Numbered **MOB-n** because an older section further down this file, *KUT project-readiness — open after Phase 228*, already owns `KUT-1`…`KUT-11` for unrelated items. `KUT-OPEN-n` below does not collide and keeps its name.

**The four items in the first table are NOT ours to close.** They belong to the
Appointing Party, to the Lead Appointed Party, or to engineering judgement. Resolving them in the repository would
look like progress while removing a decision from the people who own it. They are
recorded so nobody mistakes them for oversights.

| ID | Item | Whose decision |
|---|---|---|
| KUT-OPEN-1 | **Originator code — awaiting the register from the Lead Appointed Party; likely `SMB`** — `PLNS` was only ever a placeholder, so the earlier framing of this item as "`PLNS` vs the 3-character rule" was a stale reading of the problem. The Information Manager will most likely operate under **Symbion rather than as Planscape**, making the code likely `SMB` and the register **Symbion's to issue**, not the Appointing Party's. Two consequences. **(a) The 3-character rule is probably fine as written** — it only ever conflicted because `PLNS` is four characters, and a register of three-letter firm codes needs no change at all. **Do not widen the regex pre-emptively.** **(b) If the Information Manager is Symbion, the documents change more than the code does**: `Planscape` appears **11 times** across the three generators (BEP ×3, playbook ×5, MIDP ×3) and **5 distinct `KUT-PLN-…` references** exist — three are the pack's own document references (`RP-Z-0001` BEP, `RP-Z-0002` playbook, `SC-Z-0001` MIDP) and two are worked examples in the playbook (`M3-Z-0001`, `CR-Z-0007`) that carry the originator code without naming a real document. That is a generator edit plus a regeneration, never a hand-edit; `tools/check_kut_documents.py` will prove the set stays internally consistent afterwards. **No container may be numbered until the register is issued** — renumbering after Deliverable A touches every issued document. Stated in BEP §4.2.1, playbook §4.1, and left empty in every MIDP `Originator` cell. **Action nothing until Symbion confirms.** | Symbion, as Lead Appointed Party, issuing the originator register |
| KUT-OPEN-2 | **Clash clearances and tolerances** (BEP §7.2, playbook §12.3) | Engineering judgement; the Owner may specify |
| KUT-OPEN-3 | **Every `[FILL]`** — 66 in the BEP, 40 in the playbook, 10 in the MIDP. Names, dates, project number, procurement route. Legitimate at Rev P01. The gate counts them and fails only on a **rise**, against `docs/examples/KUT/placeholder_baseline.json`; lower the baseline in the same commit when one is closed, to lock the gain in. | The project team, as the information arrives |
| KUT-OPEN-4 | **Whether Tier A/B/C/FF&E membership is right** | A project decision recorded in the overlay. Argue it here before moving a category — the gate now holds the documents and the overlay to each other, so moving one without the other fails |

| ID | Item | Detail |
|---|---|---|
| MOB-1 | **The monthly asset-data completeness report does not exist as code** | MIDP row Z-514 promises an "Asset data completeness report — monthly, by tier and by volume", and the BEP makes a tier below 95 % at the Deliverable D gate a **gate failure**. Nothing produces it. `WORKFLOW_KUT_MonthlyReport.json` chains tag/naming completeness, model health and the KPI dashboard — none of which is asset data by tier. The closest existing surface is `LOD_Verify`, which checks the same rung-500 fields but reports pass/fail per category at the gate, not completeness per tier per volume monthly. The date-format validator was therefore wired into LOD verification, which is where those fields are actually read. Building the monthly report is a new command surface and was deliberately not invented; when it is built, the tier/volume rollup and the date-format non-conformance count belong in it. |
| MOB-2 | **`BIM Coordinator` is assigned deliverables but never appears in the playbook** | The register makes it responsible for `Z-010`, `Z-210` and `Z-211`; the BEP project-team table defines the role; the playbook — the document a task team actually works from — does not mention it once. The gate reports this as an **advisory, not a failure**: adding a role to an issued document is an editorial decision, not a gate's to force. Either add it to playbook §3.1 or reassign those three rows to a role the playbook does define. |
| MOB-3 | **`COM_INSTALL_DATE_TXT` is a legacy alias that pickers still offer** | The Phase 243 picker filter keys on a description beginning `DEPRECATED`; this one begins `LEGACY`, so it is not caught. It must stay **readable** — the COBie export fallback depends on it — but offering it for a new mapping or a new write is the same trap the deprecation filter exists to close. Either re-word the registry description, or widen the rule to `LEGACY` as well and confirm nothing writes to it. |
| MOB-4 | **The document gate cannot see whether the requirements are RIGHT** | Stated here so a green run is not over-read. It proves the pack does not contradict itself and matches the LOD overlay. It cannot judge whether a tier is sensible, whether a stage LOD is achievable, or anything at all about a real Revit model. The manual smoke test remains the only thing that touches Revit. |
| MOB-5 | **`--apply` on the TIDP merge leaves the register hand-edited** | `merge_tidp.py --apply` writes rows into a **generated** workbook, so `check_kut_documents.py` will immediately and correctly report it as edited since generation. The tool says so on completion. The clean path is to fold accepted rows into `tools/build_midp.py` and regenerate before issue; `--apply` is for the Information Manager's working copy between issues. A `--emit-python` mode that prints the rows as `build_midp.py` tuples would close the loop properly. |
| MOB-6 | **`merge_tidp.py` cannot correct an existing row** | `--overwrite-conflicts` is accepted and then declines to act, printing why: a conflicting `Ref` needs the register row corrected in place, which the tool does not do, and two parties disagreeing about one deliverable is a question for a person rather than a merge rule. Either implement in-place correction with an audit line, or drop the flag. Leaving a flag that explains itself instead of working is better than one that silently overwrites, but it is not the end state. |
| MOB-7 | **`ViewStylePack.Checksum`-style asymmetry: the pack's `.docx` are gated, the internal playbook is not** | `KUT_BIM_MANAGER_PLAYBOOK_INTERNAL_STINGTOOLS.docx` is hand-maintained and exempt from the leakage check by name — correctly, since it is ours and may name the tooling. But nothing checks it is current, and it duplicates procedure from the playbook. If it drifts, the internal reader is the one misled. Generating it from a source, the way the other three are, is the obvious fix. |
| MOB-8 | **COBie `Type.ReplacementCost` is imported nowhere** | The old map pointed it at `ASS_REPLACEMENT_COST_TXT`, which does not exist, so the column was already being discarded. It is now absent from `CobieFieldMap.TypeColumns` rather than pointed at a wrong target. The only bound candidate is `PER_REPLACEMENT_COST_UGX`, which names a currency the COBie column does not carry; writing an unknown-currency figure into a UGX-labelled field would turn missing data into wrong data, and a wrong number in a cost field is the more expensive failure. Closing this needs either a currency-neutral replacement-cost parameter, or a decision that the project's costs are UGX. That is a project decision, not a mapping one. |
| MOB-9 | **`COM_WARRANTY_START_TXT` is bound to five categories, so COBie warranty start still cannot be imported onto plant** | It reaches Data, Communication, Telephone, Security and Nurse Call Devices only. A warranty start date imported onto a chiller goes nowhere — the same silent discard as a missing parameter, one level narrower. It is kept because it is the only warranty-start parameter that exists and the primary import already used it, and it is now **declared** in `CobieFieldMap.NarrowlyBound` with a test pinning the declaration to `RESOLVED_BINDINGS.csv`, so the hazard is visible. The KUT pack does not depend on it: BEP §14.3 records warranty EXPIRY plus duration precisely because those are bound to every category. Widening the binding is a registry change and was not made here. |
| MOB-10 | **The COBie Type EXPORT is only partly routed through the shared map** | `ModelNumber` now reads through `CobieFieldMap.TypeReadOrder` because the import and export disagreed on it (`ASS_MODEL_REF_TXT` vs `ASS_MODEL_NR_TXT`) and fixing one side alone would have broken the round-trip in the other direction. The remaining Type export fields still name their parameters inline. They currently agree with the map, and `EveryTypeColumnWithAReadOrderIsReadBackByTheExport` only checks the columns that have a read order — so agreement is asserted where it is routed and merely true elsewhere. Routing the rest closes the gap properly. |
| MOB-11 | **THE SHAPE: a parameter named by string, written with `SetString`, and never checked against the shipped data** | Recorded as a shape rather than a list, because the list is not the point and will be incomplete. **The failure:** code names a parameter as a literal; `ParameterHelpers.SetString` returns `false` when the parameter is absent from the element, not bound to its category, or not `TEXT`; the caller does not check the return; the value is discarded with no log line and no error. **Why it survives review:** every symptom is an absence. A missing value looks exactly like a value that was never supplied, so it is attributed to incomplete source data rather than to the writer. It also survives a round-trip test, because a map compared only against itself is consistent whether or not either side exists. **Why it matters beyond tidiness:** it silently defeats downstream gates. The COBie instance was not that warranty data was untidy — it was that a handover file could not satisfy the close-out LOD gate by import, because eleven columns were read and thrown away. **The check, which is cheap and static:** for every parameter named in shipped configuration, assert it is present in `PARAMETER_REGISTRY.json`, resolves in `RESOLVED_BINDINGS.csv`, and has `DATATYPE=TEXT` in `MR_PARAMETERS.txt`; and require any target bound to fewer than all categories to be declared with the categories it reaches, so the narrow case is visible rather than silent. Reference implementations: the binding gate in `tools/build_kut_lod_overlay.py` and `CobieFieldMapTests`. **Known instances, none audited:** ExLink link profiles (`ExLinkDefaultLinks`, 12 preconfigured profiles), the Fohlio finishes map (`_BIM_COORD/fohlio_map.json`), the ISB schedule commands, the COBie `Attributes`/`Job`/`Spare` CSV maps under `Data/COBIE_*.csv`, and `NativeParamMapper`'s 30+ built-in mappings. A single shared assertion over all of them is a better fix than auditing each. Scoped out of Phase 243 deliberately: the COBie map was the one blocking the handover gate. |

## Product-code automation — after the Phase 255 alignment pass (2026-09-08)

| ID | Item | Detail |
|---|---|---|
| PROD-1 | **BHD and BHT are two PROD codes for one product** | Bedhead trunking, under two disciplines — `BHD` (E, HTM 08-03) and `BHT` (H, HBN 00-01). Both spell `*Bedhead*`, so they tie on specificity and the tie falls to file order; no ranking can separate them because there is nothing to separate. Retiring one is a healthcare-catalogue decision, not a mechanical one, so Phase 255 named it instead of deciding it in a commit. `ProdCodeDataTests.KnownUnresolved_Bedhead_Trunking_Has_Two_Codes` fails the day it is settled, and it is the one exclusion in the "every rule wins on its own name" gate. |
| PROD-2 | **A negated material name still takes the material's code** | "Lead-Free Solder" resolves to `-PB`. This is NOT the boundary defect Phase 255 fixed — the word *is* "lead", correctly bounded. It is a negation the override table has no way to express, and a `(?!-free)` special case invites tin-free, chrome-free and every other one after it. Needs a real mechanism (an exclusion column, or a rule that can say "not") or a decision that it does not matter. Pinned by `KnownUnresolved_A_Negated_Material_Name_Still_Matches_The_Material`. |
| PROD-3 | **Four of the seven columns in `STING_PROD_CODES.csv` are read by nothing** | `TagConfig.LoadProdCsv` takes columns 0-2 only: `PROD_CODE`, `CATEGORY`, `FAMILY_PATTERN`. `DESCRIPTION`, `DISCIPLINE`, `SYSTEM` and `STANDARD_REF` are documentation. That is defensible, but it is *silent* — the VIE/ZVB/AAP discipline disagreement Phase 255 found had sat there because nothing could ever notice it. Either wire `DISCIPLINE` into the tag's discipline segment (it is the obvious next consumer, and `No_Prod_Code_Declares_Two_Disciplines` now keeps it honest enough to trust) or say in the file header that those columns are reference only. |
| PROD-4 | **`ProdPatternMatcher`'s bare-substring path is an unanchored `Contains`** | An alternative with no `*`, `?` or `[` takes a fast path that is exactly the defect class of #863 and Phase 255 — `nameUpper.Contains(a.Sub)`, so a short pattern matches inside a longer word. **All 376 shipped `FAMILY_PATTERN` alternatives are globs**, so nothing live goes through it today. A PROJECT overlay can, and `Prod_GenerateRules` seeds overlays from live family names, so a hand-curated short pattern is a plausible route in. Cheap to close by routing that path through `Core/MaterialSchedule/PatternMatch.Contains`; deliberately not done in Phase 255, which was already changing resolution order. |
| PROD-5 | **The twelve moved resolutions are unverified in Revit** | Phase 255 changed which rule wins for twelve alternatives — fire damper `DMP → FSD`, boiler feed `BCH → BFP`, heat pump `CIR → HPU`, fume hood `HED → FHD`, mop sink `SKT → MOP`, thermostatic radiator `TSV → TRV`, and plain pumps `CIR → PMP`. Every one moves from a generic code to the specific one its author asked for, and each is proved against the shipped CSV in `Every_Shipped_Rule_Wins_On_Its_Own_Name` — but no model has been tagged with the new build. `Prod_CoverageAudit` (CREATE TAGS tab, read-only) is the check. **Its headline "specific %" will not move**: all twelve already counted as specific, with the wrong rule matching. The evidence is the `PROD` column of its per-element CSV. Whether `FSD` is the right code for a fire damper on a given job is a catalogue question this work does not answer. |
| PROD-6 | **PROD codes and commodity keys do not meet — recorded so nobody "harmonises" them** | 185 PROD codes, 30 commodity keys, **zero overlap**, and nothing under `Core/MaterialSchedule/` references `ProdMap` or `ProdResolver`. This is correct: a PROD code names *what an element is* for a tag, a commodity key names *what you buy*, and one roof yields several commodities. Unifying the vocabularies would break both. The seam where they legitimately do meet is `BOQCostManager` / `Rates/RateProviders`, which already read PROD — anything that needs to cross should cross there. Logged as a decision, not a gap. |

## Material Schedule export — after Phase 246 (2026-09-06)

| ID | Item | Detail |
|---|---|---|
| MATSCHED-1 | **Partly closed by four real exports; the Revit-side READERS remain unexercised** | What real runs have now established: materials are produced, wastage applies exactly once (all eight conversions re-derived by hand), the unit guard holds, and the block/mortar wall area reconciles exactly — 174.60 + 389.62 = 564.22 m², plaster 1,128.45 = 2 × 564.22. Memoranda, stage routing, the exclusion protection, the scan diagnostics and the workbook notes have all been seen working in an export. **What has NOT run once:** the tile finish-LAYER reader (#780), the room-finish reader (#786) and the entire baseline minter (#789). Each found nothing or was never invoked on the only model available, so their failure modes are untested — and every bug this feature has had lived in the Revit-bound half. `Baseline_Audit` is read-only and is the cheapest first contact with that risk. |
| MATSCHED-7 | ✅ **Fixed (#716)** | The command now detects `COST_COMPOUND_TAKEOFF` being off **before** it builds and offers to enable it for that export only, via a thread-static session override. The override drops `BOQCostManager`'s host take-off cache on entry **and** on exit; without the flush it changed nothing, because the cached non-compound rows came straight back. Scoped to one document in #719. |
| MATSCHED-8 | ✅ **Fixed (#716, extended #724)** | Three buckets now, not two: converted commodity, material awaiting a rate, and excluded-not-a-material — the last **counted and reported on the workbook**, never silently dropped. Doors, windows and fixtures are deliberately not excluded; they are bought. #724 added description-pattern exclusion after the worst offender turned out to be an OPENING sold 1,187 times under category `Generic Models`, which elsewhere holds real building elements — so the category alone could not be excluded. |
| MATSCHED-2 | **`ViewSchedule.CreateKeySchedule` is unproven in this codebase** | Used nowhere; key-schedule columns are project parameters bound to the key category, so the builder must create parameters before writing a row. Plan Task 15 is a 2-hour timeboxed spike: create key schedule → bind text/number parameters → create key instances → populate → place on a sheet via `ScheduleSheetInstance.Create`. If it fails, the named fallback is a Generic Annotation family carrying the commodity fields as type parameters, scheduled normally. Task 16 (the view builder) is deliberately blocked until the spike records an outcome, so guessing at the API costs nothing downstream. |
| MATSCHED-3 | ✅ **Tiling measured, twice over (#780, #786)** | Two independent sources, neither a guess. The **layer** source reads a type's compound structure: a `Finish1`/`Finish2` layer whose material reads as tile IS the tiled area. The **room** source reads `ROOM_FINISH_FLOOR`/`_WALL`/`_BASE` and measures from `Room.Area` and `Room.Perimeter`, because most architects never layer finishes — a real model had ONE finish layer across ten types, and it was Gypsum Wall Board. They can never measure the same surface: if any type carries a tiled layer, room tiling is skipped whole and the export says so. **Skirting is now measured** — recorded here as out of reach because it needs a perimeter, which `Room.Perimeter` is. Wall tiling also deducts tiled faces from the painted area; a tiled face is plastered as backing but never painted. Nails and hoop iron remain absent. |
| MATSCHED-3b | **Two rules shipped unreachable** | `aggregate` (since #700) and `tile-adhesive` (#709) matched neither a constituent kind nor a category, so neither could ever fire. Both removed. `ShippedDataIntegrityTests.Every_Rule_Is_Reachable` now fails the build on a third. Dead config is worse than absent config: it advertises coverage the export does not have. |
| MATSCHED-3c | **The two data files were never compared** | `STING_SUPPLIER_UNITS.json` and `STING_MATERIAL_STAGES.json` were each valid and each tested, yet disagreed — category rules inherited the ELEMENT's stage, filing wall paint under SUPERSTRUCTURE. Fixed structurally: a category rule must now declare its own `stageId`, and `ShippedDataIntegrityTests` asserts that, that the stage exists, and that no paint/tile commodity sits in a structural stage. Same class as the "valid JSON + green build + runtime-dead" risk already recorded for this codebase. |
| MATSCHED-4 | **The commodity price list is indicative, not tendered** | `STING_COMMODITY_RATES.csv` is seeded from the PATMAC sample's own figures (Kampala, mid-2026). Every live project must re-price via the project override at `_data/coord/commodity_rates.csv` before the output is used for tender. An unpriced commodity is honestly reported — rate 0, source `unpriced`, flagged by reconciler rule R3 and highlighted in the workbook — rather than borrowing a neighbour's rate. |
| MATSCHED-5 | **The XLSX Amount formula diverges from the model once a QS edits a rate** | The workbook writes a live `=F*G` per row while the model derives `AmountUGX`. They agree at generation; if a QS edits a rate in Excel the formula updates and the model does not. That is intended — the workbook is the QS's to edit — but **re-importing an edited workbook is not supported** and no round-trip exists. If one is wanted later, it needs the same reconciliation gate the export has. |
| MATSCHED-6 | ✅ **Renamed to "Measured by Material" (#799)** | `BOQExportCommand` emitted a sheet called "Material Schedule" that is BOQ rows filtered to m²/m³/kg in MEASURED units. Defensible while nothing else claimed the name; actively dangerous once a real material schedule existed, because two documents answering to one name is how somebody prices square metres of blockwork as though they were blocks. The banner now says what the sheet is and points at the real export. |
| MATSCHED-9 | ✅ **Site tools quantified (#736)** | The schedule opens with a tools section derived `work → trade-days → gang → tools`. **There is no standard for this**: NRM2 prices tools in preliminaries and no measurement standard publishes a wheelbarrows-per-mason ratio, so it ships as an editable table (`STING_SITE_TOOLS.json`, 14 rules) calibrated against the reference schedule — and says so in the JSON, the class header and a banner on every export. With no programme duration it emits **nothing** rather than inventing a crew; duration reads `PRJ_DURATION_DAYS` with a dialog fallback, and storeys are counted from the model's own Levels. Rates seeded into `STING_COMMODITY_RATES.csv`. |
| MATSCHED-10 | ✅ **Unlike units can no longer be added together (#728)** | A real export read `Bricks · No. · 364.31`, which was 364 **square metres** of brickwork relabelled as a brick count; separately `blockwork` (m²) and `units` (nr) both mapped to commodity `block`, so an area was added to a piece count. The aggregator now refuses to convert when a rule's `sourceUnit` and the row's measured unit denote different dimensions, and flags the row instead of guessing. Area measures are no longer commodities. |
| MATSCHED-11 | ✅ **Wastage lives in exactly one place (#728)** | Engine and supplier-unit rule both applied it — blocks carried ~10% instead of 5%, plaster cement ~23% instead of 2.5%. It now belongs to the supplier-unit rule alone, which is what the converter's own documentation already claimed. `UnitWastePct` and `PlasterWastePct` survive as retired no-ops so callers compile, with a test asserting they change nothing. Three existing tests encoded the double-counted behaviour and were **corrected, not loosened**. |
| MATSCHED-12 | ✅ **Columns and foundations decompose (#725)** | `CompoundTakeoffBuilder` handled Walls, Floors and Structural Framing only, so RC columns arrived as unpriced m³ lumps and no SUB-STRUCTURE section appeared at all. Formwork is the part that needed care: a column shutters four faces with no soffit, a round column uses its circumference (treating Ø152 as a square over-orders shuttering by ~27%), a pad shutters only its sides because it bears on the ground, and blinding carries no rebar. |
| MATSCHED-13 | ✅ **The rounding contract now holds for every value, not only ties (#777)** | `MidpointRounding.AwayFromZero` decides TIES, yet the comment claimed a divisible order could never round below the measured net. R4 caught a real 174.6044 → 174.60 under-order and was mistaken for a false alarm because both printed at 2 dp. Ceiling at 2 dp, message widened to 4 dp. **The test written to catch this compared the order against `Math.Round(net, 2)` — the rounding under test — so it could not fail.** It now compares against the raw net. |
| MATSCHED-14 | ✅ **Intermediate measures cannot be priced (#777)** | `Blockwork wall` 175 m² sat beside the 2,292 blocks derived from it, with an R3 saying it had no rate — an instruction to pay for the same wall twice. Such rows are memoranda: quantity kept for checking, `AmountUGX` hard-zero, no rate cell, no formula, skipped by R3. Marking is **conditional on the children being present**: turning a double-count into an omission is worse, because a duplicated line is arguable and a missing one is invisible. |
| MATSCHED-15 | ✅ **The export explains itself, in the workbook (#783, #788)** | Zero tiling rows and no errors fit two incompatible causes, and nothing separated them. The tiling and room scans now report their own denominator and name every finish material they rejected. Those notes were then recoverable only from the post-export dialog — the same mistake one level up — so they travel on the document and are written to the Validation sheet above the issues they explain. The XLSX writer proved Revit-free, so its tests write a real workbook and read it back. |
| MATSCHED-16 | ✅ **Baseline built, run and repaired in Revit (#789, #792, #794, #798)** | `STING_PROJECT_BASELINE.json` + `Baseline_Audit` (read-only) / `Baseline_Apply` (audit → confirm → mint). Shipping it took three reachability fixes — a dispatch case with no button, a button with no working app handle — and running it found two more: `CreateSimpleCompoundStructure` returns a wall-shaped EndCapCondition that Revit refuses on floors and ceilings, and a failure after `Duplicate` had committed left five types NAMED for the baseline carrying the source's layers, which the next audit would read as conforming. Half-made types now roll back. **Verified: 24 of 24 created, 0 failed, re-audit reports 32 conforming.** The 2 outstanding items are family-backed and reported as guidance. |
| MATSCHED-18 | ✅ **38 dead dispatched commands, and a gate (#796)** | `RunCommand<T>` calls `cmd.Execute(null, …)` by design; a command reading `commandData?.Application?…` returns on its first line and logs a clean start/done. Two were worse than dead buttons: **Master Setup's step 20 skipped Healthcare Pack setup on every panel run**, and WorkflowEngine's plugin-hook fallback could never resolve. The defect had already been fixed twice (28 sites in `Commands/Drawing`, then `Commands/Cost`), so the deliverable is `tools/check_command_doc_acquisition.ps1` — in CI, baseline zero, and **verified failing** on a deliberately re-broken site before being kept. |
| MATSCHED-T1 | ⚠ **Screed emitted; the layer read that feeds it is still unexercised (#801)** | `CompoundTakeoffBuilder` inspected `Finish1`/`Finish2` only, and only for tile materials, so the 40 mm `Cement Screed` **Substrate** layer on `STING RC Slab 150 - Ceramic Tiled` contributed nothing to any export — no cement, no sand, no warning. The walk is now a primitive (`HostLayerCache`) returning every layer with its function, material and thickness; screed rides it and so will T2/T3. Emits `screed` (m², memorandum), `screed_cement` (bag) and `screed_sand` (m³) off the FLOOR/ROOF path only. The thickness is the driver and is **never assumed**: a zero-width screed layer emits nothing and the scan counts it separately from a rejection. `IsScreed` is disjoint from `IsTile` by construction and refuses plaster/render/mortar/skim, each already measured elsewhere. **Verified headlessly:** 520 tests green (was 465), all four gates pass, and 14 deliberately wrong inputs were each shown to fail the matching gate before it was kept. **NOT verified:** nothing Revit-side has run. The layer reader has never executed against a real model — the same open risk as MATSCHED-1, which this feature now shares in full. |
| MATSCHED-T2 | ⚠ **Ceilings decompose; the layer read that feeds them is still unexercised (#802)** | `CeilingType` appeared in NO branch of `TryBuild` — not mishandled, absent — so a suspended gypsum ceiling produced no boards, no furring and no skim, silently. Now emits `ceiling_board` (m² → Sheets at 2.88 m²) and `ceiling_furring` (m → 3.6 m Lengths), and reuses the existing `plaster` kinds for a wet coat rather than minting ceiling-only twins. Board and plaster are two findings (a skimmed plasterboard ceiling carries both) that can never accept the same layer. **Furring is ratio-derived** — the model does not state grid spacing — and is qualified on the row, on the commodity rule and by a banner, conditional on any furring having been derived; it rides on the BOARD, since a skim on a soffit has no grid. Ceiling paint is not emitted: nothing states whether a ceiling is painted. **Two classifier defects were caught by tests before shipping** (bare `gypsum` classified a wet skim as sheets; bare `acoustic` would have rejected real acoustic plasterboard), and **two gates were found BLIND** in the break-it pass — one where a narrowed positive pattern meant the exclusion list never ran, one where the category route made the kind route untestable. Both fixed and re-verified. **Verified headlessly:** 581 tests green (was 520), all four gates pass, 21 wrong inputs each shown to fail. **NOT verified:** nothing Revit-side has run — `ReadCeilingFinish` and the `Ceilings` branch have never executed against a real model, the same open risk as MATSCHED-1 and MATSCHED-T1. |
| MATSCHED-T3 | ⚠ **Membranes measured; the layer read that feeds them is still unexercised (#803)** | `MaterialFunctionAssignment.Membrane` layers were ignored entirely, so a ground slab's DPM and a roof's underlay produced nothing, silently. Now emits `dpm` (m² → Rolls of 100 m²) and `roof_underlay` (m² → Rolls of 45 m²), both with a 15% lap allowance in the supplier rule — a DPM laps at every joint and turns up at the perimeter, so without it every order under-buys. Layers are counted, not collapsed. The NAME decides the commodity when unambiguous, otherwise the HOST does (roof → underlay, floor → DPM), which the model states. Routing: `dpm` → substructure (its preamble is already "up to and including the ground floor slab"), `roof_underlay` → roof. **Two things are deliberately not priced and the export says so:** insulation (bought by thickness — counted and named, with a test that no commodity exists to absorb it into) and wall membranes (a horizontal DPC occupies one course of a wall face; measuring the face would over-order by an order of magnitude). **A blind gate exposed a corrupted regex in shipped code:** the `\b` word boundaries in the insulation exclusion had been eaten into literal backspace bytes, leaving bare `eps|xps|pir|pur` — and bare `pur` matches "Purlin", so "Purlin Underlay" would have been discarded with no row and no warning. Repaired to `\b(...)\b` (the intended `eps\b` was also wrong — it still matches "Steps") and pinned in both directions. **Verified headlessly:** 644 tests green (was 581), all four gates pass, 25 wrong inputs each shown to fail. **NOT verified:** nothing Revit-side has run — the same open risk as MATSCHED-1, T1 and T2. |
| MATSCHED-T4 | ⚠ **Ratio-derived consumables shipped; the DRIVERS they multiply are still unverified (#805)** | Hoop iron (1.2 m per m² of walling), binding wire (1.25% of rebar mass), formwork nails (0.20 kg/m² of contact area) and roofing fasteners (11 per m² of covering). Unlike T1–T3 **none of this is stated in the model** — the numbers exist because a table says so. Every ratio lives in the new `STING_CONSUMABLES.json` with a `sourceNote` giving its derivation (tested for presence AND for length — "industry standard" explains nothing), overridable at `_BIM_COORD/consumables.json`; none is hardcoded in C#, pinned by a test that empties the table against huge drivers. **No driver, no row** — never a minimum or a default, and the scan says an absent driver is intended behaviour rather than a fault. The qualification appears in three places: the row description, the commodity rule's description (what the workbook prints) and a conditional banner that quotes the actual driver value and ratio so the figure can be checked without opening the JSON. Derived rows re-enter the ordinary aggregator, so they are staged, unit-guarded, converted and priced by the same code as measured commodities. **Five gates were found BLIND** in the break-it pass — three where a unit check masked a kind check, one where the converter was called instead of the guard, and one where a stage matched on the row's DESCRIPTION and then on the DEFAULT stage — all retargeted and re-verified. **Verified headlessly:** 689 tests green (was 644), all four gates pass, 35 wrong inputs each shown to fail. **NOT verified:** T4 adds no Revit reader of its own, but it multiplies driver totals produced by take-off paths that have never run against a real model — see MATSCHED-T-VERIFY. |
| MATSCHED-T5 | ⚠ **Fascia measured; ridge and barge refused out loud; a NEW Revit reader is unexercised (#806)** | Of the three roof-edge accessories, only the fascia has a length the model states: it runs along the eaves, an eave is horizontal, so the footprint's plan length IS its length. Eaves are identified by `FootPrintRoof.get_DefinesSlope` — a fact the sketch records, not an inference from form — and only the outer profile loop is measured, because an opening's edge is not an eave. Emits `fascia_board` (m → Lengths of 4.2 m). **Barge board is NOT emitted:** it runs up the rake, longer than the gable's plan length by 1/cos(pitch), and `get_SlopeAngle` returns a value whose units could not be confirmed without running Revit; the scan reports the gable PLAN total hard-labelled as not the answer. **Ridge cap is NOT emitted:** it is an internal line the footprint does not carry at all. Deriving either from the roof AREA is refused explicitly in the export text. The scan also states that rafters and purlins are measured only as Structural Framing, so their absence is a scope boundary; and separates concrete roofs, footprint-less roofs and roofs with openings, because each has a different fix. A shipped-data test asserts **no ridge or barge commodity exists** to be quietly filled in later. **Verified headlessly:** 716 tests green (was 689), all four gates pass, 24 wrong inputs each shown to fail; one mutation did not move a test and is reported as redundant-by-design rather than blind. **NOT verified:** `ReadRoofEavesLengthM` is a NEW Revit reader and has never executed against a real model — see MATSCHED-T-VERIFY, which this makes more urgent, not less. |
| MATSCHED-T6 | **Proposed, not built (#808, corrected by #809)** | Five TEXT parameters, no dimensional ones: leaf area and frame length are derivable from the width and height every conforming family already carries, and two sources for one number always drift. Ironmongery is a SET CODE resolved against a corporate table, so changing the standard schedule is one file edit rather than re-issuing every door family — and it is declared data, not a ratio, so it must not carry T4's heuristics banner. The door becomes a memorandum once its parts are listed, or the schedule prices the door AND its parts. **Five decisions still open**, including whether to measure glazing at all (recommendation: leave it in the window unit rate — an approximate area beside measured quantities is the mixing the proposal opens by warning against). |
| MATSCHED-B1 | ⚠ **Family types mint; `FamilySymbol.Duplicate` has never run (#811)** | A type is MISSING only when a loaded family can host it; otherwise GUIDANCE, so Apply cannot promise a mint that must fail. Half-made types roll back (`Duplicate` commits before the parameter set — #798 one level down), and same-name-different-size is a CONFLICT. Nothing in this codebase duplicated a `FamilySymbol` before this. |
| MATSCHED-B2 | ⚠ **Family parameters add; the `EditFamily` round trip has never run (#813)** | SHARED parameters via `ExternalDefinition`, never the local overload, which produces GUID-less parameters that cannot be scheduled together. `Document.EditFamily` cannot run inside an open transaction, so layer 3 sequences after the mint commits. Unresolvable names block Apply before a family is opened; in-place, vendor and workshared-elsewhere families are a reporting case, not an exception. |
| MATSCHED-B3 | ⚠ **Catalogue packs ship adopted by nobody (#814)** | Opt-in by id, additive, overridable per type, versioned in the id. `EA-RESIDENTIAL-V1` is provisional and its sourceNote says its sizes demonstrate the SHAPE and are to be replaced by harvest, not edited. A test asserts the corporate baseline does not adopt its own pack — that would be the corporate default the mechanism exists to avoid. |
| MATSCHED-B4 | ⚠ **Harvest writes a pack; the pass has never run (#815)** | Reads PLACED types only — a type nobody used is evidence somebody loaded a family. Refuses to harvest our own minted types (the catalogue would become a record of itself) and uses the family's own name as the only pattern (inferring "Door" from "M_Single-Flush" is the #710 inference). Read-only: writes one JSON file. Has a handler case AND a button. |
| MATSCHED-B5 | **Layer 3 is inert by default: `familyParameters` ships with ZERO sets** | `STING_PROJECT_BASELINE.json` declares no parameter sets, so `Baseline_Apply` augments nothing unless a project writes its own in the override. That is consistent with `familyTypes` and with the adopted-by-nobody rule for catalogue packs — but the two cases are not alike. A type list is a **market opinion** and rightly opt-in; the STING material parameters are **our own contract**, and T6's emitter cannot read a material that no family carries. The verified names exist and resolve: `BLE_DOOR_MAT_TXT`, `BLE_DOOR_FRAME_MAT_TXT`, `BLE_DOOR_HARDWARE_SPECIFICATION_TXT`, `BLE_WINDOW_FRAME_MAT_TXT`, `BLE_WINDOW_GLAZING_TYPE_SINGLE_DOUBLE_TRIPLE_TXT`. Note that the spec's own examples (`BLE_DOOR_LEAF_MATERIAL_TXT` and companions) are **not** in `MR_PARAMETERS.txt`, so anyone adding sets must take the resolving names, not the spec's. Decide whether layer 3 should ship the door/window sets on by default; it is a one-line data change either way, and shipping empty is defensible only while nothing downstream depends on it. |
| MATSCHED-T-VERIFY | 🟡 **Material matching is now PROVEN in Revit; five things still have never run** | **Proven:** the T1–T5 readers, B4’s harvest, B6’s per-category fix, the rate editor opening, and — new this phase — material matching end to end (`GetMaterialIds` → resolver → conversion → priced, 80 bundles from a real roof) plus the material scan naming what it could not place. **Still never executed:** the Ctrl+V paste path, the mapping dialog and `supplier_unit_patches.json` round trip, `FamilySymbol.Duplicate`, the `EditFamily` round trip, and catalogue adoption. **Two now have no route to proof on the test project at all:** the tile-size band (the model has no tiling — its only finish material is Gypsum Wall Board) and the By Type sheet (only one of three roofs converts, so nothing splits). Both need a different model, not a different run. |
| MATSCHED-T7 | ✅ **A roof that produced only a fascia no longer deletes its own covering (#TBD)** | Regression found by reading the 17:32 export against the 14:39 one: three `Generic - 225mm` roof rows totalling **856 m²** were gone, and their two unpriced-item flags with them. Cause — T5 gave every roof a fascia line, a non-empty decomposition REPLACES the composite row, and the fascia measures an edge rather than the roof. The covering left the schedule with no warning anywhere, and took `roof_covering_m2` down with it, so T4’s roof-fastener rule printed “driver is zero” — a considered refusal in the place where a missing measurement belonged. **Fix:** a constituent kind either measures its host or sits on it; `fascia_board` is the only accessory today, an unknown kind measures its host (a wrong “measures” double-counts, which a reader sees; a wrong “accessory” deletes a quantity, which nobody sees), and the caller keeps the composite row when the decomposition was accessories-only — clearing the accessory rows’ element writeback so two rows do not fight over one CST_* stamp. A test asserts `RoofAccessories` emits only accessory kinds, so a future row that DOES measure the roof fails rather than repeating this silently. 13 tests, both mutations verified failing. |
| MATSCHED-T8 | ✅ **A zero driver now says WHICH zero it is (#TBD)** | The 20:14 export proved #819 (856 m² of roof restored, issues 22 → 28) and exposed the next layer: the roof-fastener driver still read zero, and the export said “that is the intended behaviour — with no driver the quantity would be invented”. True of a model with no roof; that model has 856 m² of it. `SupplierUnitTable.Resolve` returns `CategoryTypeMismatch` with a NULL rule for `Generic - 225mm`, and the driver sum only reads `res.Rule`. **The quantity is still not derived, and that is correct** — 11 fasteners/m² is corrugated sheeting fixed at every second corrugation, and this roof carries Eagle high-profile TILES, which take clips at another rate; multiplying an unidentified covering by a product-specific ratio is a confident number for the wrong roof. What changed is the sentence: unattributed area is tracked separately from attributed, never added to it, and the export now names the 856 m², says the product is unknown, and gives the two fixes (name the type so it matches a pattern in `STING_SUPPLIER_UNITS.json`, or count the specified fixing pattern by hand). 9 tests, three mutations verified failing — including the tempting wrong fix of routing unattributed area into the driver. |
| MATSCHED-T9 | ✅ **Commodity rates are editable in a grid; the CSV is storage, not the editing surface (#TBD)** | The reconciler told users to add a row keyed `RD_Breeze Block 01_Panel — Concrete` to a file **nothing in the codebase had ever written**. 21 of the 25 keys the 20:14 export asked for carry a non-ASCII em dash, because an unmatched row’s key IS its display name, and `Resolve` is an exact `OrdinalIgnoreCase` hit — a hyphen typed in its place misses and the row stays unpriced with no error. `MaterialSchedule_PriceCommodities` seeds a grid FROM the schedule, so the user types only a number. Storage stays a plain CSV — diffable, hand-editable, and copyable to the next project, which Extensible Storage would not be. Refusals carry tests: a memorandum is never offered a rate (its amount is hard-zero; pricing it beside its own parts pays twice), a blank cell never deletes a price, a zero is never written (the resolver ignores a zero project row, so the file would look like a decision and behave like an absence), and a rate for something not in today’s model is kept. Atomic save, project override only — never the corporate baseline. `ParseCsv` gained quote handling in the same pass: it had never been asked to read back anything it wrote, and a QS writes commas in descriptions. 29 tests, five mutations verified failing. **One mutation proved nothing and was replaced** — see the changelog. |
| MATSCHED-T10 | ✅ **Excel navigation and paste in the rate grid (#828)** | Typing 58 numbers is the work the grid was meant to remove. Paste column (Ctrl+V), Fill down (Ctrl+D), Clear (Del), Copy keys, Find, Unpriced-only; cell selection, single-click edit, Enter commits and moves down. Three parsing decisions carry tests because each is a silent-failure route: a BLANK line consumes its row (skipping it shifts every rate below a gap onto the wrong commodity, and each then looks deliberate); a line with NO number is reported, never zeroed; a multi-column paste takes the LAST numeric field, because Qty+Rate is an ordinary copy and first-numeric-wins would price cement at 93. Save reads the unfiltered set, so filtering then saving cannot discard a rate typed into a hidden row. |
| MATSCHED-T11 | ✅ **The rate grid says what each row IS (#833)** | `ConstituentInput` carried `Category` and `TypeName` all along and aggregation dropped both, so `Generic - 225mm · 610 m² · unpriced` gave no way to tell it was a ROOF. Carried through as SETS — cement comes from walls AND floors. A **From** column, a type tooltip, and four collapsible detail columns (CSV key, constituent kind, backing rows, why-measured-units). **Deliberately no tag column**: a commodity is an aggregate and no single ISO 19650 tag identifies one; a single backing row shows its reference, several show a COUNT. Two alignment defects fixed: Source read "baseline" while the export said "Indicative" for the same row, and sorting compared 610 m² against 29 each. |
| MATSCHED-T12 | ✅ **Type → commodity mapping, in a file that cannot say more (#836)** | `supplier_units.json` replaces a rule WHOLESALE by key, so a file written to add one type pattern would also reset `SourceUnitsPerSupplierUnit` to 1.0 and `DefaultWastagePct` to 0 — "2.4 m² per sheet, 10% waste" silently becoming "1 m² per sheet, none" across the schedule. **A UI promising not to do that is a promise; a file shape that cannot express it is a guarantee.** `supplier_unit_patches.json` carries a key, a pattern and a reason and nothing else — a test asserts by REFLECTION that the type has no conversion field. Corporate corrections still reach every project because the rule is never copied, only appended to. Both roof commodities are offered and every roof mapping warns that sheet and tile both convert cleanly. Apply is disabled until the preview says it can apply; a pattern under 3 characters BLOCKS. **The rename is offered first** — mapping is the escape hatch for a linked model or vendor family, and a dialog offering only the escape hatch has every project accumulate patches instead of fixing anything once. |
| MATSCHED-T13 | ✅ **Coverings match on MATERIAL, and five East African roofing commodities (#840)** | Every layer reader filters on Finish1/Finish2/Substrate/Membrane — **none accepts STRUCTURE** — so a shingle on a structure layer was invisible and the row fell back to being named after its TYPE. Order is now kind → material → type: decreasing reliability, because a material is chosen deliberately and a type name is free text. **Verified in Revit:** 224.44 m² of `Asphalt Shingle` → 80 bundles, ROOF 2,790,000 → 11,590,000. A hazard was closed in the same pass: a material-patterned rule no longer falls through to whole-category matching, which would have re-routed every roof in the model. |
| MATSCHED-T14 | ✅ **The export says whether the material reached the resolver (#841)** | Material matching had a failure indistinguishable from success-minus-one-pattern. The scan publishes its denominator and NAMES what it could not place — on the first run that was `Default Roof` and `Eagle_Roofing-Tile-as-Specified`, which is how we learned two of three roofs carry no decided material. Three outcomes, three sentences: plumbing, data, or nothing needed. |
| MATSCHED-T15 | 🟡 **The same constant is declared in two files, and five pairs disagree (#846)** | `MATERIAL_LOOKUP.csv` has stated roofing coverage per profile since before the schedule existed; `STING_SUPPLIER_UNITS.json` states it too. **All five shared pairs disagree** — corrugated 2.7 vs 2.4, box profile 3.2 vs 0.86, clay 0.04 vs 0.077, concrete 0.03 vs 0.10, default 3.0 vs 0.09. Four were added in #840 without checking the file that already held them. **The gate does NOT resolve them** — 11% of a roof order needs a supplier’s coverage table, not a judgement from whoever is editing. It makes them impossible to forget, forbids new duplication, and fails on a recorded pair that stops disagreeing so the list shrinks only by reconciling. **BLOCKED ON: a supplier coverage table.** |
| MATSCHED-T16 | ✅ **Fasteners counted per covering; tiles are nailed (#848)** | A flat 11/m² quoted a tiled roof screws it does not use — wrong by KIND, not degree, and the lookup has said `CLAY_TILE 0` all along. The mixed case is the one no flat ratio can express: 100 m² of sheet at 8 plus 100 m² of tile at 0 is 800, and a flat 11 over 200 m² gives 2,200. Density lives on the covering rule; the driver is a COUNT. −1 (unstated) and 0 (nailed) stay distinct. |
| MATSCHED-T17 | ✅ **A By Type sheet — sheets and tiles for EACH roof (#855)** | Aggregation is right for ordering and destroys checking, phasing and subcontract splitting. The sheet is ADDITIVE and the order line is untouched. Parts are **apportioned, never re-converted**: rounding each type up separately would order more than the schedule says, and two documents in one workbook disagreeing about a total is worse than no breakdown. Not written when nothing splits. |
| MATSCHED-T18 | ✅ **Tile size — the fourth member of a set that already had three (#857)** | Brick bond, block size and plaster type each resolve a `BLE_*` parameter through a canonicaliser with an inference fallback. `BLE_TILE_SIZE_TXT` had been declared in `MR_PARAMETERS.txt` **with a GUID all along and read by nothing**, so a mosaic and a 600 mm porcelain floor both got 10% while the lookup banded them 20 and 8. A dimension bands on its SMALLER edge — a 300×600 plank is cut on its short side. A name with no size returns EMPTY, not a band, so `InferOrCanon` records that it was defaulted. **Adopting it changes no existing number**; value arrives as projects fill the parameter. **Untestable on the current project — it has no tiling.** |
| MATSCHED-B6 | ✅ **Harvest reads per-category dimensions (#TBD)** — found by running B4 on a real model | One flat parameter list asked `Width`/`Height` of a `Concrete-Rectangular-Column` and got **4500 × 12000** — extents, beside the 450 × 450 section in `b`/`h`. The pack proposed minting a column 4.5 m wide, and a pack is DATA, so nothing downstream would have complained; harmless only because packs are adopted by nobody. `HarvestDimensionRules` is now per category and an unknown category harvests **nothing rather than guessing** — a pack with no dimensions is visibly incomplete, one with confident wrong dimensions is not. The builder re-applies the rule so a pack built by anything else cannot smuggle extents in as a section. Two model problems are now reported instead of swallowed: a type with no recognised dimension (kept and named, since minting it renames without defining), and a NAME whose numbers appear in none of its own parameters (three columns named 200x200 measuring 175/450/500; a vendor door named 1740x2595mm measuring 1500×2400). The name check refuses to guess which parameter a number refers to, and ignores numbers below 10 so "type 2" does not bury the real cases. 12 tests, five mutations verified failing. |
| MATSCHED-17 | **The model has no prices for what it does describe** | 47 door and window units, three roof types and one breeze-block panel total zero. This is the largest remaining gap between a schedule and a tender document, and no code closes it: every unpriced row now names its exact key and `_BIM_COORD/commodity_rates.csv`. Related but distinct from MATSCHED-4, which is about the baseline rates being indicative rather than absent. |

## Licensing — after the Phase 234 self-serve pass (2026-08-16)

| ID | Item | Detail |
|---|---|---|
| ~~LIC-1~~ | ~~**`issue.ts` has never been exercised by a signed-in user in production**~~ | **CLOSED 2026-08-16.** Issued from the `/licences` page by `mayanjadavis@gmail.com` (tenant `exo`). A `wrangler pages deployment tail` on deployment `6daa6e53` captured `POST /api/license/issue` → **200, outcome ok**, and `licenses` gained its first row ever: `4681-584E-784F-0868-4E48`, expiring `2036-08-03T09:23:49.531Z` — exactly `trial_ends_at` + `TRIAL_GRACE_DAYS`, so the computed path is the one reasoned about. Root cause of the earlier silent failures was **not** the endpoint: the page refused clipboard-damaged input before the fetch, so no request was made and the only feedback was small red text. Fixed in #698. |
| LIC-2 | **The authenticated `/licences` view is now exercised, but not automatically** | Downgraded 2026-08-16 — the issue path has been driven for real in production (LIC-1), so this is no longer "never run". What remains is that there is **no UI harness**: the four status branches and the #677 expiry guard were verified in a browser against fixtures, and the logged-out path for real, but nothing re-checks any of it on change. A home for page-level tests is blocked on #691 (`marketing-site/tests/` is published, so adding files there worsens an open issue). |
| ~~LIC-7~~ | ~~**`last_seen_at` has never been moved by the plugin itself**~~ | **CLOSED 2026-08-17.** The issued `.lic` was installed at `C:\ProgramData\Planscape\StingTools\` (old one backed up) and Revit 2025 launched. `LicensePresenter` posted on startup and the row was stamped: `last_seen_at` `null` → `2026-08-17T05:06:01.266Z`, `last_seen_plugin_version` **2.2.0.0** (real assembly version), `last_seen_revit_version` **2025**. Plugin log agrees to the second: `License presented: licensee=exo expires=08/03/2036 inUse=1/10`. `updated_at` stayed at `2026-08-16T21:31:55` — the "being observed is not a change to the licence" rule verified, not just documented — and the audit action was `license.first_seen`, not the routine `license.presented`. The full chain now runs on real components: browser → `issue.ts` → `.lic` on disk → Revit plugin → `present.ts` → D1, with no curl standing in for any hop. |
| LIC-3 | **Revoke does not exist** | `licenses.revoked_at` is read by `issue.ts` and `present.ts` and **written nowhere in the codebase**. A dead machine's seat can only be freed by hand in D1, which is what `issue.ts`'s own "contact us to move a licence" message quietly depends on. Deliberately out of Phase 234's scope: it is a destructive billing action needing role gating and a confirm step. Additive when wanted — the page already renders a `revoked` status pill for rows that carry the date. |
| LIC-4 | **A lapsed trial still passes entitlement and mints a dead licence** | Tracked as #677. `expireTrialIfNeeded` exists and is correct but is called **only** on `/api/auth/me`; every other path reads `getTenantById`, a raw `SELECT` with no expiry applied. So `issue.ts` and both download endpoints see a stale `"trial"`. The page guards against handing over the expired licence, but the row is still written and the downloads are still ungated. Fix belongs at the read, not in the UI. |
| LIC-5 | **Merging licensing changes ships nothing** | Tracked as #651. `marketing-site` has no git-connected Pages build, so `/licences` and every Function change reach production only when a human runs `npm run deploy`. Compounded by the deploy-time binding behaviour corrected in #674: a secret that `wrangler pages secret list` shows is stored, not bound. |
| LIC-6 | **Preview and production share one D1** | Tracked as #652 (#644 closed as its duplicate). A preview deployment can issue, revoke and stamp rows in the production `licenses` table — now the billing artefact, since #626 made D1 the sole owner of seat entitlement. |

## Model publishing — after the federation pass (2026-08-22)

| ID | Item | Detail |
|---|---|---|
| PUB-1 | **Store the element map gzipped** | Measured on a real federated site: 12.28 MB of JSON gzips to **0.40 MB — 31×**. The cap is 25 MB and cannot go much higher because `ModelsController.DownloadElementMap` reads the whole map into a string to merge the cost sidecar, on a 512 MB free-tier instance. Compressing at rest removes the ceiling problem entirely, but needs the serve path AND the cost merge to decompress, plus a magic-byte check so plugins still sending plain JSON keep working. |
| PUB-2 | **The map can still over-collect on a user-picked file** | When the publish exports the GLB itself, the map is narrowed to the keys the exporter actually wrote — which took a real model from 37,110 entries to ~1,407. When the user picks an existing `.glb`/`.ifc` we cannot know its contents, so the map keeps its full document scope and the old bloat returns. Reading the element list back out of a picked GLB would close it. |
| PUB-3 | **Nested-link metadata is collected, its visibility is not filtered** | `CollectLinksRecursive` filters top-level link INSTANCES by the active view, because that is a host element the view can answer for. Deeper links, and per-element visibility inside any link, cannot be filtered by a host view id. The PUB-2 narrowing hides the consequence today; it would reappear on the picked-file path. |

## Planscape hosting — after the connect pass (2026-08-20)

| ID | Item | Detail |
|---|---|---|
| HOST-0 | **The free Postgres expires ~2026-08-29/30 and the API dies with it** | **Deadline, not a backlog item.** Render: free Postgres expires **30 days** after creation (reduced from 90), then a **14-day** grace, then deletion with all data. At expiry the database is *inaccessible*, so `planscape-api-free` stops serving entirely. Blueprint committed 2026-07-30, stack curled live 2026-07-31 → expiry ~08-29/30, deletion ~09-12/13. Fix is one dashboard action: change the instance type off Free (in-place, data preserved, a few minutes unavailable). **Then update `render.free.yaml`'s `plan: free`** or a blueprint sync can pull the upgrade back and re-arm the clock. |
| HOST-1 | **`api.planscape.build` does not resolve** | Tracked as #705. The plugin's baked default now points at `planscape-api-free.onrender.com` because that is the host that answers; `PlanscapeServerClient.IntendedProductionServerUrl` is the one place to change when the custom domain is attached. Note the service is `planscape-api-free` — `planscape-api`, the name in `render.yaml`, 404s. |
| HOST-2 | **The API instance sleeps, and waking it takes minutes** | Measured 2026-08-20 after ~2h idle: first request no answer within **180s**, next **66.6s**. The plugin now absorbs this with a pre-login wake probe and says so in the error, but that is mitigation. An always-on plan is the fix, and it pairs naturally with HOST-1 since both are one dashboard visit. Until then, the first Connect of the day is slow and may need a second press. |

## Entitlement plumbing — after the project-ceiling pass (2026-08-20)

The project cap now resolves in one place (`ProjectCeilingPolicy`): D1's tier grants where
present, otherwise the local `BillingPlan`, and `Tenant.MaxProjects` may only tighten. What
that pass deliberately did **not** try to settle:

| ID | Item | Detail |
|---|---|---|
| ENT-1 | **Two plan taxonomies still exist** | planscape.build sells Solo / Studio / Practice / Firm / Large / Enterprise; the server's `BillingPlan` enum is Trial / PluginOnly / Studio / Practice / Network / Enterprise. There is no Solo, Firm or Large in the enum, and Network sits where two sold tiers are. `BillingTierMap` is the seam and is keyed by the **sold** names, because those are what a customer paid against. Unifying them is a commercial decision, not a refactor — and it needs the D1 side to agree, since `plan_tier` is written there. |
| ENT-2 | **`BillingPlanLimits.For(PluginOnly)` grants unlimited projects** | PluginOnly is documented as "$15/mo — Revit plugin only, local storage, **no cloud sync**", yet its `MaxProjects` is `int.MaxValue` — more cloud projects than Studio or Practice, which cost more. Nothing assigns PluginOnly today (both creation paths write Trial or Network), so it is latent. Left alone deliberately: correcting it downward changes what a priced plan includes, which is a commercial call. |
| ENT-3 | **Tier changes reach the mirror only at sign-in** | `PlanTier` is refreshed on every handoff, so an upgrade lands the next time the customer signs in through planscape.build. A customer who upgrades and stays logged in keeps the old cap until their next handoff. Full reconciliation (a webhook, or a periodic pull) remains out of scope per `docs/PLANSCAPE_IDENTITY_HANDOFF.md`; this is the honest bound on how stale the mirror can be. |
| ENT-4 | **Storage is still capped by the local plan alone** | `CheckCanUploadBytesAsync` reads `BillingPlanLimits.For(tenant.Plan).StorageMb` and knows nothing about the tier. It is less visible than projects because the handoff now mirrors onto Network (50 GB) rather than Trial (5 GB), so nobody is squeezed — but it is the same shape of bug, one axis over. `BillingTierMap` would need a storage column and D1 would need to agree on the numbers. |

## Visibility Center — after the Phase 233 enhancement pass (2026-08-16)

| ID | Item | Detail |
|---|---|---|
| VIS-1 | **Nothing in the Revit-bound half has been run in Revit** | The Phase 233 read-back, category metadata reads, badge push and Hub-button capture all compile and are covered *only* where they are Revit-free (102 unit tests). `VisibilityStateReader` itself is unreachable from any test by design. Three separate bugs on this feature have already lived in exactly this layer. **Before merge, on a model with real MEP content — not a 97-element architectural view:** hide a category → reopen → reads back unticked; hide by ZONE in Saved mode → reopen → the ZONE row reads back unticked; Reset → footer and badge show nothing hidden; the category list shows no Cameras/Views and nests Runs under Railings. |
| VIS-2 | **Apply only ever hides — re-ticking a row does not bring it back** | The read-back now shows a hidden row unticked, which invites the user to re-tick it. `VisibilityEngine.Apply` is additive: `HideElementsTemporary` adds to the temporary set and `AddAndHide` adds filters; neither removes anything for a re-ticked row. The row tooltip says so honestly ("use 'Reset all' and re-apply"), but the natural gesture is unsupported. A declarative apply — disable the temporary mode, then hide exactly the unticked set — would fix it, **except** that it would also clear individual elements the user hid with Revit's own HH, which our rows represent only partially (they show as a part-hidden, therefore ticked, row). Needs a way to preserve element-level hides before it is safe. |
| VIS-3 | **A view template that hides many categories inflates the badge** | The badge counts every element the view is hiding, whoever hid it — including a view template's own category visibility. That is true, and it is the honest answer to "why can't I see my ducts", but on a heavily-templated view the number will be large and not actionable. If it proves noisy in use, the fix is to report template-controlled category hides separately rather than to stop reading them. |
| VIS-4 | **The document-scoped pass is unbounded** | When anything is hidden, the reader harvests the whole document (`TokenValueHarvester.Harvest(doc, null)`) to recover rows for hidden elements — a hidden element is by definition absent from a view-scoped collector. On a 50k-element federated model that is a full token read per dropdown open (30 s cache, per-scope). Not measured on a large model. If it bites, scope the widening pass to the categories and token values the view's own filters and hidden-category set name, rather than the whole document. |
| VIS-5 | **Deliberately not built, per runner §5** | Live hover-highlight of matching elements (expensive per tick; the live count already answers it). Category **isolate** in Saved mode (structurally impossible — a view filter only acts on the categories it binds to — and already reported as a clear blocker; do not attempt a workaround). Worksets / phases / design options as visibility axes. Any use of `Autodesk.Windows` / `AdWindows` (undocumented, version-fragile, and it can corrupt the user's saved QAT layout; the one legitimate need is already met by the supported `UIApplication.MainWindowHandle`). |
| VIS-6 | **`View.IsElementVisibleInTemporaryViewMode` under temporary ISOLATE over-reports** | Under isolate, everything outside the isolated set answers "not visible" — including elements the view never drew. The out-of-scope guard cannot separate those, so the hidden count under an active isolate is an upper bound. Hide mode is exact. |
## KUT smoke test + checklist tooling — after the reconciliation (2026-08-08)

| ID | Item | Detail |
|---|---|---|
| SMK-1 | **`origin/claude/session-8tl9ga` is SUPERSEDED — do not merge it** | It carries the hand-generated `KUT_Revit_Smoke_Test_Checklist.docx`, `tools/kut_preflight.py` (779 lines) and a `CATEGORY_BINDINGS.csv` change, and is ~100 commits behind `main`, predating #623 / #635 / #638. Everything still true was reconciled **by content** into `claude/kut-smoke-test-reconciliation`: the pre-flight's substance is now `tools/check_smoke_test.py` driven from `smoke_test.json`; the `WorkflowEngine` alias fix landed in two passes — `Owner_`/`KUT_` with the KPI rename, and the `ACC_`/`Acc` + `Lite_ComCheck`/`ComCheck_Export` dual-accepts only when the branch was re-read immediately before deletion, where they were found still missing despite this row previously claiming otherwise (a reconciliation is not done because the summary says it is); the README prose (ACC read path, `ffe-fohlio-ref` severity) was folded in. **Its `CATEGORY_BINDINGS.csv` change was deliberately NOT ported** — it scopes `LTG_HOIST_*` to Generic Models as well as Lighting Fixtures, but `PARAMETER_REGISTRY.json` declares `"binding": "LightingFixtures"` and `RESOLVED_BINDINGS.csv` (the file `SharedParamGuids` treats as the source of truth) lists Lighting Fixtures alone. The branch copied the pattern of the sibling `LTG_FIX_*` params, which are genuinely `universal`. Merging the branch now would re-introduce a 100-commit-stale checklist and a wrong binding. **Delete it.** |
| SMK-2 | **The Revit smoke test itself has still never been run** | 33 steps, all wiring-checked offline, none exercised against a model. That is the BIM Manager's session, and it is what the CI gate explicitly cannot substitute for — the gate proves the checklist is *answerable*, not that the answers are right. Until the session happens, keep the "verify in Revit" caveat on the Phase 192 CHANGELOG blocks. |
| SMK-3 | **Two presets read as read-only but do not declare it** | `tools/check_smoke_test.py` proves `"readOnly": true` and prints a non-fatal advisory for prose that sounds like the claim without the field. Currently advisory on `WORKFLOW_KUT_MonthlyReport.json` and `WORKFLOW_PlumbingAudit.json`. Both descriptions were corrected to state what is true, so neither is a lie any more — but neither can declare `readOnly` while it contains `Manual` steps. The real question is whether `CompletenessDashboard` should build its legend inside the reporting chain at all; if the legend build were split out, MonthlyReport could declare the flag and be enforced. |
| SMK-4 | **`Plumb_ScanFixtures` writes from inside an "audit" preset** | `WORKFLOW_PlumbingAudit`'s first step stamps `PLM_DRN_DU` / `PLM_SUP_LU` / `PLM_SUP_WSFU` onto every fixture (`FixtureUnitScanner.Scan(writeBack: true)`). The description now says so, but a scan-and-report variant with `writeBack: false` would let the audit preset be genuinely read-only and would be a small change to an existing call site. |
| SMK-5 | **`docs/examples/` has one owner** | The tooling is owner-agnostic and globs `docs/examples/*/smoke_test.json`, but only KUT exercises it. The second owner pack is the real test of whether the abstraction holds; expect the panel/tab/section assertions to be the part that needs loosening, since a different pack may drive the same commands from different panels. |

## Coordination viewer — after the zoom fixes (2026-08-06)

| ID | Item | Detail |
|---|---|---|
| VIEW-1 | **The original "model blinks out on zoom-out" is bounded, not explained** | [#618](https://github.com/beckykyomugisha/STINGTOOLS/pull/618) made near/far track the camera every frame and is provably live (served bytes byte-identical to `main`, `Cache-Control: no-store`) — yet the model still vanished **while staying centred**, which rules out both far-plane clipping and the `zoomToCursor` drift theory. [#622](https://github.com/beckykyomugisha/STINGTOOLS/pull/622) / [#627](https://github.com/beckykyomugisha/STINGTOOLS/pull/627) then capped perspective and ortho zoom-out so the camera cannot reach the range where it happened. **The mechanism is still unknown.** Ruled out by reading the served file: no `scene.fog`, no LOD, no distance- or zoom-driven `visible = false`, no camera-dependent clipping (every `renderer.clippingPlanes` writer — section box, clash section, section plane — is user-invoked), and ortho never clips on zoom-out (span stays symmetric, `near -98.6` / `far 98.6`). What is left is GPU/driver-level and only observable live: most plausibly depth-buffer behaviour under `logarithmicDepthBuffer: true`. **Diagnose only if it recurs** — the signal is the model vanishing *inside* the new bounds. The probe, run in the `viewer.html` frame (the viewer is an iframe; switch the DevTools console context), logs `STING_VIEWER.camera.near/far` against `position.length()` and `modelBounds` size while scrolling out; `far` frozen means the loop is not running, `far` growing with the model gone means the camera was never the cause, empty bounds means the fallback branch is sizing the frustum off the orbit distance. |
| VIEW-2 | **The viewer cannot be exercised headlessly** | Unauthenticated hits on `/viewer.html` bounce to `/index.html` after ~1.5 s (`coordination-viewer.js` on the 401), so no CI or agent can drive the real viewer. Diagnosis of VIEW-1 needed a mirror of the deployed assets served locally — workable but manual, and it silently fails until every runtime-fetched vendor file is present (`vendor/three/addons/utils/BufferGeometryUtils.js` is fetched lazily and is easy to miss). Worth a token-gated or `?diag=1` boot path that skips the redirect, so viewer regressions can be caught by something other than a person scrolling. |

## Button + preset wiring — after Phase 230

| ID | Item | Detail |
|---|---|---|
| WF-5 | ~~26 dock-panel buttons dispatch to nothing~~ **NOT A DEFECT — measured 2026-08-06** | Recorded so the claim is not re-raised a third time. 26 `Cmd_Click` button tags have no `case` in any handler, but **all 26 are reachable**: 23 through `Cmd_Click` suite runners in `StingDockPanel.xaml.cs`, 3 through the `CommandRegistry` modules (`Folder_CloudSync`, `HC_HbnAutoPopulate`, `Tags_MigrateStyleCode`), which `StingCommandHandler.cs:173` consults *before* its switch. **Zero of 1,323 button tags are dead.** Any audit counting against the switch alone will over-report — this is the second time (`SILENT_BUTTONS_TODO.md` records the earlier "141" figure being ~96% false-positive). Now enforced as Tier 4 of `tools/check_workflow_wiring.ps1` with a deliberately empty baseline. |
| WF-6 | **No gate enforces `order` against array position** | The Phase 230 brief assumed the wiring gate had a Tier 3 failing any preset whose `order` values disagree with array order. It does not — the gate has Tiers 1, 2 and 4. `order` is an unbound key (WF-4) used 40 times across existing presets, so adding such a tier would fail them until each is corrected or baselined. Either delete the `order` keys and add the tier, or leave both alone; what must not happen is someone believing the check exists. New presets authored in Phase 230 omit `order` entirely. |
| WF-7 | **`docs/UNREACHABLE_COMMANDS_TRIAGE.md`'s 126 figure predates the three-layer correction** | It counts `IExternalCommand` classes not referenced from `StingCommandHandler.cs`, ignoring the 661 `CommandRegistry` names and the 38 code-behind runners. The true number of unreachable *commands* is unknown and is probably materially lower. Re-derive it across all three dispatch layers, as the button-side audit now is, before acting on any entry in that file. |

## Workflow wiring — after Phase 229

**KUT-1** (59 steps keyed `"tag"` across 11 presets, each preset entirely inert) and
**KUT-2** (66 unresolved `commandTag`s, 96 once KUT-1 exposed the inert presets' tags) are
**CLOSED — Phase 229**. Both were raised during the KUT readiness review (PR #623) and
closed here. A CI gate (`tools/check_workflow_wiring.ps1`) now blocks both regressions.
What remains:

| ID | Item | Detail |
|---|---|---|
| WF-1 | **`WORKFLOW_DailyFieldWalk.json` is a manual checklist, not a workflow** | All 7 steps are BIM Coordination Center **SITE PHOTOS tab** interactions (`UI/SitePhotosTab.cs`) — `OpenSitePhotos`, `RefreshPhotos`, `PhotoChecklistAudit`, `BulkApprovePending`, `DigestPreview`, `PushDeliverableRegister`, `WorkflowComplete`. None is an `IExternalCommand`, so none can ever run from `WorkflowEngine`. They are marked `optional` with `(MANUAL - ...)` labels and baselined, so the preset no longer claims to have done the work. Decide: either promote the tab actions to real commands, or move this out of `WORKFLOW_*.json` into documentation, because a "workflow" that skips every step is theatre. |
| WF-2 | **`BOQ_DriftCheck` and `BOQ_ExportErp` were never written** | `WORKFLOW_BOQ_CostLifecycle.json` describes a drift check against the latest snapshot and an "ERP-ready CSV + P6 XML" export. No source in the tree implements either. Both steps are marked `optional` with `(NOT IMPLEMENTED - ...)` labels and baselined. `BOQSnapshotSave` already computes the snapshot checksum, so the drift check is the smaller of the two. |
| WF-3 | **`Hvac_AutoSizeDuct` cannot be resolved from a workflow** | The handler case is a strategy dispatcher: it snapshots the STING HVAC panel's header radio and runs a different command per strategy (velocity / equal-friction / static-regain, with constant-pressure deliberately reporting "not implemented"). There is no single `IExternalCommand` to return, and resolving it would silently pick one strategy and hide that choice. Marked `optional` with a `(MANUAL - ...)` label. Fix would be a `WorkflowStep` parameter block so a preset can name the strategy explicitly. |
| WF-4 | **Nine more step keys are silently dropped by `WorkflowStep`** | Phase 229 fixed the five that mattered (`tag`→`commandTag`, `description`/`name`→`label`, `continueOnFail`/`allowSkip`→`optional`). Still unbound and therefore ignored: `order` (40 uses), `id` (33), `_notes` (23), plus `skipIfFamilyLoaded`, `scheduleNameFilter`, `drawingTypeId`, `sheetNumberFilter`. `order`/`id`/`_notes` are harmless documentation. The last four express real filtering intent the engine has no support for — either implement them or delete them, because as written they read as configuration and do nothing. The new gate does **not** catch these; extending it to warn on unbound step keys is the cheap follow-up. |
## KUT project-readiness — open after Phase 228

Found during the Phase 228 KUT readiness pass. Everything here was **flagged, not fixed** —
either explicitly out of that phase's scope, or discovered adjacent to it.

| ID | Item | Detail |
|---|---|---|
| KUT-1 | ~~59 workflow steps use `"tag"` instead of `"commandTag"` and are silently dead~~ | **CLOSED — Phase 229 (PR #630).** All 59 steps renamed to `commandTag`; four more silently-dropped keys (`description`/`name`→`label`, `continueOnFail`/`allowSkip`→`optional`) fixed in the same pass. A CI gate (`tools/check_workflow_wiring.ps1`) now fails the build on any step keyed `"tag"`. See "Workflow wiring — after Phase 229" above. |
| KUT-2 | ~~66 workflow `commandTag`s do not resolve in `WorkflowEngine.ResolveCommand`~~ | **CLOSED — Phase 229 (PR #630).** The count rose to 96 once KUT-1 exposed the inert presets' tags: 72 were commands that already existed and only lacked a `ResolveCommand` case (now added, 583 → 655 labels), 14 were stale tags corrected in the presets, and 10 have no command behind them — those steps are marked `"optional": true` with honest labels and baselined in `tools/workflow_wiring_baseline.txt`. The same CI gate blocks regressions; the residue is tracked as WF-1…WF-3 above. |
| KUT-3 | ~~Project-scope LOD verification only sees categories with an explicit rule~~ | **CLOSED — Phase 228 (PR #623).** Project scope now collects every taggable model category (`CategoryType.Model` ∩ `TagConfig.DiscMap`, minus a documented `NonPhysicalCategories` set and the project skip list), so the `"*"` rule is genuinely reachable; 14 new explicit category rules were added for the categories the KUT deliverables are judged on; and all three output forms now carry a scope-disclosure block naming what was **not** scanned. Measured 879 → 2,789 elements in scope on a synthetic model. |
| KUT-4 | ~~LOD 100 is a rung that cannot fail~~ **CLOSED 2026-09-08** | **Made explicitly NON-GATING rather than given a requirement**, and the choice is the whole answer. The BIMForum LOD specification defines 100 as CONCEPTUAL — an element "may be graphically represented with a symbol or other generic representation" and its information "may be derived from other Model Elements" — so there is genuinely nothing per-element to verify. Inventing a check would be inventing a standard, and the obvious candidate the row suggested (existence + a category assignment) **passes vacuously anyway**: every Revit element has a category by construction, so it would be the same green-over-nothing in a new costume. A rung whose checks assert nothing now reports **NOT ASSESSED**, never a pass: those elements are counted in `LodTally.NotAssessed` and kept **outside `Total`**, exactly as `SkippedNoRule` already is, so they cannot inflate a percentage — and they are not counted as failures either. `LOD_Stamp` **refuses** on such a rung instead of writing `ASS_LOD_VERIFIED_TXT` off it: that parameter is a claim that an element MET a standard, and "Stamped 0 passing element(s)" reads like a model that failed rather than a milestone that asks nothing. **The flag is DERIVED from the emptiness of the resolved check, never hardcoded to the number 100** — give 100 a real check and it starts gating on its own, and, more usefully, **empty a higher rung by accident** (a bad merge, or a mistyped key Newtonsoft leaves at its default) and that rung starts reporting NOT ASSESSED instead of a perfect score. `RungAssertsNothing` is kept distinct from `NoElementsInScope`: "there was nothing to check" is a matrix problem and "there was nothing here" is a model or filter problem, and a reader needs to know which. A new `HasMeaningfulResult` is the one flag a report should branch on; `OverallPct`'s 100.0-over-zero trap is left exactly as it was, documented, so no existing caller changes behaviour. All four output surfaces say it — TaskDialog headline, text report, CSV header and the JSON gate report (`rungAssertsNothing`, `notAssessed`, `notAssessedByCategory`, and `overallPct` now null in this case too). A MIXED run — some categories gating at a rung, others not — names the not-assessed elements per category, because they sit outside the denominator and the percentage would otherwise silently describe a subset. `StingTools.Tags.Tests` +14, including one asserting **every shipped milestone binds a gating rung** (none binds 100 today). Four sabotages confirmed failing. **No Revit runtime path was exercised.** |
| KUT-11 | ~~`GetFamilyName` has no system-element fallback~~ **CLOSED 2026-09-08** | `ParameterHelpers.GetFamilyName` now falls back to `ElementType.FamilyName`, so it answers the SYSTEM family name ("Basic Wall", "Rectangular Duct", "Wall Foundation") where it used to answer `""`. **The row understated the harm and located it in the wrong place.** The visible cost was not CSI: it was PROD. `ProdResolver.Resolve` gates its ENTIRE project + corporate rule lookup on a non-empty family name, so the **eleven corporate PROD rules shipped on Ducts, Floors and Structural Foundations** — `*Concrete Slab*`→SLB, `*Fire Damper*`→FSD, `*Pile*`→PIL and eight more — had never once fired, and every floor, duct and wall footing silently took its category default. Fixing the family side alone would not have reached them either: those patterns match the TYPE name, and `GetFamilySymbolName` was empty for the same elements. So `GetElementTypeName` was promoted out of `CsiMap.TypeName` (which held the only copy of that fallback, benefiting only the CSI resolver) and `GetFamilyAwareProdCodeCore` now reads both. `GetFamilySymbolName` itself is untouched — 55 callers, each of which can opt in on its own evidence. Callers that read `""` as "not a loadable family" were made to say so: `Prod_GenerateRules` uses the new `GetLoadableFamilyName` (a rule keyed on "Basic Wall" is a category default in a family rule's clothes), and the tag-map import now treats an unrecorded family as "no opinion" so a map exported before this change still matches. `BOQCostManager` was passing its LOCAL helper's answer — the TYPE name — into the CSI resolver's FAMILY slot, so the same map gave two answers down two paths; that call now uses the shared helper, while the two private helpers stay (and now say why: a bill and a paragraph want the type name, a rule wants the family name). The CSV header's absolute claim was corrected, and the audit behind it is now an executable test. `StingTools.Tags.Tests` +4 (307→311): the eleven system-category rules resolve with a family name and fall through without one, and no shipped FamilyRegex row sits on a system category — which is what makes the change provably inert for CSI. Three sabotages confirmed failing. **No Revit runtime path was exercised**; what `ElementType.FamilyName` returns per category is asserted by the API contract, not by a run. |
| KUT-5 | ~~Division 02 is UNSUPPORTED — naming-based rows withdrawn; classified MANUALLY~~ **CLOSED 2026-09-08** | Phase state now reaches `CsiMasterFormat.Resolve` through an optional `Phase` qualifier, and Division 02 is **one rule** keyed on it: any element the works REMOVE is demolition, whatever it is made of. `ElementPhaseState` (Revit-free, therefore testable) derives New / Existing / Demolished / Temporary from `Phase Created` and `Phase Demolished`; `CsiMap.PhaseState` only turns two `ElementId`s into positions in the document's phase sequence. **The weighting is the load-bearing decision**: a matched phase scores 10, more than every product qualifier combined, because MasterFormat Division 02 classifies by the STATE OF THE WORK, not by what the thing is — a demolished precast beam is demolition, the contractor is removing it, not casting it. At any lower weight the rule would fire only where nothing else matched, which is the withdrawn naming rows' failure in a new costume. **Three traps encoded rather than discovered later**: a single-phase project has NO existing elements (that phase IS the works, and calling it Existing would silently mark a whole building out of scope); a non-phase-aware element is UNKNOWN, not New, so a grid can never satisfy a phase rule; and **Existing and Temporary are deliberately left unclassified** — existing-to-remain is not work, and temporary works are NRM2 preliminaries and a QS's judgement. Both states are now matchable, so a project can add its own rule. NRM2 **1 = Demolitions**, read from this repository's own vocabulary in `BOQCostManager.GuessSectionName`, **not** from published NRM2 — the in-house scheme has 4 Foundations and 5 In-situ concrete where NRM2 has 5 Excavating and filling, and guessing across that gap moves money. The parser hazard the row named is handled: the row widens to 10 columns and 6, 7, 8 and 9 all still load AND still match (asserted, not assumed) — **column 9 is RESERVED for KUT-10's Material qualifier** so the two changes compose under either merge order. **§7's "re-run the resolution harness" had no harness to re-run**, so one was written: 51 categories × 5 phase states = 255 probes against the shipped map. Measured **0 → 51** probes landing in Division 02. It immediately found a **pre-existing gap left deliberately unfixed and baselined instead**: `Generic Models` and `Pipes` have no bare-category fallback (every rule on them carries a family or SYS qualifier), so an unqualified element in either resolves to nothing and its CSI section is blank — authoring a catch-all would put a confident number on exactly the elements nobody has classified. The rule is **appended, not inserted**: `qs_nrm2_review.py` keys its sheet on a row's ORDINAL, so adding a rule higher up re-points every ROW below it and invalidates an in-progress QS review (the staleness guard catches it, but the sheet must be re-exported). Position is irrelevant to matching here. `StingTools.Tags.Tests` +27; five sabotages confirmed failing. **No Revit runtime path was exercised** — `CreatedPhaseId` / `DemolishedPhaseId` cannot be read from a terminal. |
| KUT-6 | ~~Unit system: Owner wants CFM/GPM, engines are l/s and Pa~~ **CLOSED 2026-09-08 (partial by design — surfaces listed)** | Answered as a **display/entry** concern, not a calculation one: SI stays the internal representation and no solver, sizing table or stored parameter was touched. `Core/Units/FlowDisplayUnits.cs` (Revit-free) converts at the edge; `MepDisplayUnits` decides whether to, **by reading the `ProjectStandardsManager.UnitSystem` that already existed** — already set by the regional preset (the USA preset carries `Imperial`), already synced per-document from `PROJECT_REGION`, and simply never read by anything that formats an engineering value. **A second parallel "units" preference would have been the KUT-8 defect again**: two settings for one question and a header that disagrees with the calculation. Defaults to metric, where every conversion is the identity, so an existing project's output is bit-identical. **The round trip is the whole risk and it drove the design.** The usual implementation uses two published constants — 0.4719 l/s per CFM and 2.119 CFM per l/s — which are **not reciprocals**: their product is 0.99995610, so a typed value comes back changed, invisibly on a terminal and by half a CFM at 12,345 CFM. Every conversion here multiplies or divides by **one exact factor** derived from a definition (1 ft = 0.3048 m exactly → 1 CFM = 0.4719474432 l/s; 1 US gal = 3.785411784 L exactly), so `ToSi(FromSi(x)) == x` to double precision. **Air and water are separate quantities** even though both are l/s in SI, because a single "flow" conversion would silently render an air flow in GPM. Pressure uses in.w.g. **at 60 °F (248.84 Pa)**, named rather than assumed — the 4 °C definition differs by 0.1 %, invisible on one duct and a whole fan selection across a system. **CONVERTED**: the HVAC block-load report (per-zone OA and DCV average → CFM); the plumbing sizing report (BS EN 806 Qd CW/HW, and the pipe audit line's Qd and ΔP); the pump duty report (peak demand, design flow, rated flow); roof drainage Q_r; and the Hardy-Cross duty flow. **NOT CONVERTED, and deliberately**: DN designations (nominal, not a length), velocities in m/s (no imperial counterpart in the sizing tables these read), the `HVC_OA_LS` / `PLM_*` model stamps (SI by their own names, and every solver reads them that way), the BOQ and COBie exports (a bill's units are the measurement standard's, not the viewer's), and roughly a hundred other `l/s` strings across the tree — this is an edge layer, not a sweep. `ExLink/FohlioFinishesCommands.cs`'s hard m² conversion is untouched: it is an area, not a flow, and Fohlio's own schema is metric. `StingTools.Tags.Tests` +32, including the two-constant trap reproduced and shown failing so the reason for the single-factor design is recorded rather than assumed. Five sabotages confirmed failing. **No Revit runtime path was exercised** — no panel grid or dialog was driven; what is proven is the conversion and the round trip. |
| KUT-7 | ~~Electrical standards are hardcoded BS 7671; `NEC2023` is unreachable~~ **CLOSED 2026-09-08** | **The row was right about the symptom and never named the cause, and the cause is the interesting part.** Layer by layer: the PANEL was fine — `StingElectricalPanel.xaml` has offered a `NEC2023` combo item and an `rbNEC2023` radio since Phase 178, and both write `SelectedStandard`. The COMMAND layer was nearly empty — exactly one command (`DemandFactorReportCommand`) branched on it. The ENGINE layer never saw it, because **the panel emits `"NEC2023"` and `CableSizerEngine` tested `standard == "NEC"`**. Two vocabularies, no translation. **The obvious repair was the worst available change**: the only two places testing for `"NEC"` were a breaker lookup and `VoltageDropEngine.FormatCsa`, which returned the *"closest AWG approximation by CSA"* — so normalising the token would have printed an **AWG designation on a conductor chosen from BS 7671 Appendix 4**. The mismatch was the only thing preventing that. `ElectricalStandardId` (Revit-free) is now the one vocabulary, accepting every spelling that has ever reached an engine including the legacy `"NEC"`. `CableSizerEngine.Calculate` routes on it: **NEC 2023 goes to `StingTools.Standards/NEC2023`**, which already held Table 310.16, the 310.15 corrections and 240.6(A) and which nothing had ever called — Table 310.16 at the 75 °C column, 310.15(B)(1) ambient, 310.15(C)(1) bundling, 240.6(A) ratings, 210.19(A)(1) 125% continuous, 240.4(D) small-conductor cap — and reports a real AWG/kcmil trade size plus its true mm² area, so the AWG comes from the AWG table. **BS 7671 and IEC 60364 share the Appendix 4 path, stated rather than assumed** (BS 7671 is the UK implementation of IEC 60364; the factors are harmonised with IEC 60364-5-52 Annex B). **AS/NZS 3000 REFUSES**: AS/NZS 3008.1.1 is not in this tree, and the refusal names the standard, the missing tables and what to do instead. `FormatCsa` lost its `standard` parameter entirely — the approximation has nothing left to do, and restoring the argument would re-open the seam. `CableSizeResult` gained `Sized`, `StandardId` and `StandardBasis`, and **the callers that wrote a refused zero into the model now honour it**: `WireParamSync` had omitted the standard from `CableSizeInput` altogether (silently BS 7671 whatever the panel said) and now counts and REPORTS refusals instead of writing `0` — a zero CSA reads as "not sized yet", not as "we declined". `WireElementAnnotation` had it hardcoded to `"BS7671"`. `ElectricalStandardsValidator` became standard-aware where the codes genuinely differ: **NEC has no maximum conduit run length at all** (344.26 / 352.26 / 358.26 constrain total bend degrees, not distance), so the BS 7671 §522.8.4 draw-in check no longer fires under NEC, and the bend cap becomes a flat four quarter bends instead of the IET GN1 size-aware three. Its **fill percentages needed no branch** — the 53/31/40 figures already shipped ARE NEC Chapter 9 Table 1, which is now said out loud instead of looking like an oversight. `StingTools.Tags.Tests` +32, including the bug as a test (panel tag and engine token must land on the same id) and the refusal with its reason. Four sabotages confirmed failing. **No Revit runtime path was exercised.** |
| KUT-8 | ~~`ProjectStandardsManager` regional presets never reach the engines~~ **CLOSED 2026-09-08** | **The row was stale in one direction and understated in another.** Stale: two consumers outside `Commands/Standards*` already read the presets — `MepDesignCommands` MEP-A-01, which sizes cables through `StandardsAPI` on the project electrical standard and **writes the result to the model**, and `MaterialLocaleManager`, which drives MAT-tab units and currency. Understated: the engines that DO have a region read a **different region, in a different vocabulary**, and nothing reconciled them. HVAC sizing reads `StingHvacCommandHandler.CurrentRegion` (`UK_SI` / `US_IP` / `EU_SI` / `DE_SI` / `SE_SI`, selecting the duct size ladder and pipe bores) and LPS reads `StingLpsCommandHandler.CurrentRegion` (`UK` / `EU` / `US` / `TROPIC` / `AFRICA`, selecting a **ground flash density band**); both were pinned to a hardcoded constant. So choosing Uganda left duct sizing on the UK ladder and lightning risk on Ng ≈ 0.5–1.0 — for a country among the highest measured anywhere, a BS EN 62305-2 input wrong by more than an order of magnitude, silently. **Three dead wires were found on the way**: `ProjectStandardsManager.StandardsChanged` had **zero subscribers**; `StingDockPanel.RefreshRegionIndicator` had **zero callers**, so the header's region chip kept whatever it was built with for the life of the session; and two comments (`MaterialLocaleManager`, `StingDockPanel`) asserted that the HVAC/Plumbing surfaces and the chip "pick up the change automatically". They now do. `EngineRegionMap` (Revit-free, in `StingTools.Standards`) translates the project region into both engine vocabularies with a cited reason per row, and `EngineRegionSync` is the event's first subscriber: it seeds at startup and on document open, re-seeds on change, moves the panel COMBOS as well as the handler statics (the panels re-post their combo on every raise, so seeding the statics alone would be undone by the first click), and repaints the chip. **It never overrides a region the engineer selected mid-session** — the preset supplies the starting selection, the panel keeps the last word. The chip tooltip now names the derived size ladder and Ng band, so the region's effect is visible rather than inferred. `StandardsAPIExtensions` was split out of `ProjectStandardsManager.cs` unchanged — it was the only thing in that file referencing `StandardsAPI`, which made the preset table impossible to test without dragging 1,400 lines of calculation code along. **Uganda→`UK_SI` is deliberate** (EAS/UNBS/KEBS/SANS schedules are BS-derived and metric); **Australia is deliberately left on the panel default for LPS**, because AS spans Ng ≈ 0.1 to above 10 and no single band is defensible. AS/NZS duct sizes are **not** represented in `STING_MEP_SIZING_RULES.json`; an AU project should add an `AU_SI` region rather than rely on the `UK_SI` approximation. `StingTools.Tags.Tests` +26 (311→337), including three that hold the map to the shipped data rather than to itself: every preset mapped by name, every mapped MEP region present in the rules file, every mapped LPS region offered by the panel. Four sabotages confirmed failing. **No Revit runtime path was exercised** — the event subscription, the combo marshalling and the chip repaint are not reachable from a terminal. |
| KUT-9 | ~~`sheet-kut-number-pattern` is provisional~~ **CLOSED 2026-09-08** | **Owner decision: the pattern is 100% ISO 19650, and the ISO container convention IS the register — there was nothing left to wait for.** The pattern is now DERIVED from `tools/kut_naming.py` by the new `tools/build_kut_owner_standards.py`, which is the same table the BEP and the Document Control Standard render their naming sections from, so the rule a gate enforces and the tables a drafter reads cannot fork. The old pattern was ISO-*shaped* but not ISO: `[A-Z0-9]{2}` for Volume, Level and Type **accepted volume 47 and type QQ**, so an invalid container name passed validation and could reach a transmittal looking checked. It is now an enumerated alternation, generated. **Two traps the derivation encodes**, both of which a hand-written alternation would have got wrong: `LEVELS` lists `01` as *"first floor, and upward in sequence"*, so enumerating the six literal codes would have **rejected every floor above the first** — numeric levels are a `\d{2}` class beside the literals; and alternation is first-match-wins, so role codes are emitted longest-first, defensively (no shipped pair collides today, which is exactly why the day one does would be missed). **The originator stays `[A-Z0-9]{3}`** — the register is Symbion's to issue and has not been (KUT-OPEN-1), so enumerating a guess would reject the real codes on the day they arrive; only its length is single-sourced. `discipline-code-valid` is derived from the same table (`ROLES`, deliberately **without** `Z`, which BEP 4.2.2 keeps as a container role and never an element discipline). **Severity stays WARN, recommended not defaulted**: the pattern is authoritative now, which is the usual argument for BLOCK, but no KUT container can legitimately be numbered until the register is issued — so "every existing sheet conforms" is true only because there are none, and **an empty set is not evidence** (the same vacuous-pass shape as KUT-4). Raise to BLOCK once the register is issued and a first full sheet set has been validated. Drift is now impossible in three places: the generator has a `--check` mode, `check_kut_documents.py` fails if the file and the derivation disagree (80 → 82 assertions), and `StingTools.Tags.Tests` +30 exercises the SHIPPED pattern **through .NET's `Regex`** — the generator verifies in Python's `re` but `OwnerStandardsPack` runs `new Regex(rule.Pattern)`, and that the two engines agree on this syntax was an assumption nothing asserted. Five sabotages confirmed failing. **The KUT document gate stays green and no issued document was touched** — one observation for the team, not acted on unilaterally: the BEP and the Document Control Standard display the example in a SPACED form (`KUT - SMB - 01 - GF - M3 - A - 0001`) which is presentation only and does not match the machine pattern; `build_bep.py` already renders the unspaced form alongside it, and the generator asserts that form matches. |
| KUT-10 | ~~CSI map defaults for structural framing/columns are judgement calls~~ **CLOSED 2026-09-08** | **Owner decision: follow a well-known standard — CSI MasterFormat 2020 — which resolves the judgement rather than deferring it.** MasterFormat divides by **work result and material**, not by authoring-tool category (03 Concrete, 04 Masonry, 05 Metals, 06 Wood), and a Revit `Structural Framing` element can be any of those — which is exactly why a category-keyed default was a judgement call. `CsiRule` gained a 9th `Material` column read from `STRUCTURAL_MATERIAL_PARAM` on the instance then the type — **the declared parameter, never the type name**, because "W310x39" is a naming convention and not a fact. **`PrimaryMaterial.Resolve` was deliberately not reused**: it leads with dominant-by-volume, which is right for pricing a compound assembly and wrong for classifying a member (on a clad element the largest volume can be a finish, and Division 05 steelwork would bill as Division 07). **The scoring is where the real work was.** Making material simply outrank everything broke MasterFormat's own hierarchy — a bored pile is made of concrete and is still measured under 31 63 00, not 03 30 00. So qualifiers score 2 and a matched material 3, giving category (2) &lt; name guess (4) &lt; declared material (5) &lt; named work result + material (7). **Two shipped-map defects surfaced from the tests, both pre-existing**: `"Masonry - Concrete Blockwork"` matched both the masonry and concrete patterns and tied (masonry now leads, since a concrete block is Division 04); and `(?i)pile` was listed **before** `(?i)shoring|underpin|sheet pile`, so a SHEET PILE already resolved to 31 62 00 Driven Piles instead of 31 40 00 Shoring — the reorder is a fix, not a side effect. `Structural Foundations` deliberately gets only two material rows: a concrete row would restate the bare-category fallback (same section, same NRM2) and a steel row would outscore sheet-pile shoring for no gain, so only the driven-pile row needed a precast twin. The file header's "common approximations to be confirmed against the project's RIB SpecLink spec" is retired, and the surviving category rows are labelled **fallback, not answer** — a project whose members carry no structural material is being classified on the weakest available evidence, and the fix is to set the parameter. `StingTools.Tags.Tests` +24 driving the shipped map; four sabotages confirmed failing. One Boq test pinned the absolute score `1` and now asserts `CsiRule.QualifierWeight` — every production caller discards the score (`out _`), so the scaling is inert. **LIFE-2 impact (see §10 there): this does NOT reduce the 36.** It adds one division-31 row (the precast driven-pile twin), so the QS sheet is 36 → 37; the narrowing to two Foundations material rows kept that from being 39. No NRM2 code was authored or altered. `check_qs_nrm2_review.py` had hardcoded `'36 still unreviewed'` and now derives the count from the sheet, so the next map change does not fail a check that says nothing about the round trip. **No Revit runtime path was exercised** — `STRUCTURAL_MATERIAL_PARAM` cannot be read from a terminal. |

## ISO information-management spine — open after Phase 200 remediation

| ID | Item | Detail |
|---|---|---|
| IM-1 | **`ExtractIfMissing` runs before consolidation** | `EmbeddedTemplates.ExtractIfMissing` fires on `DocumentOpened` (`Core/StingToolsApp.cs`), before any user-driven consolidation. On a project whose customised templates still sit in a legacy folder, extraction seeds the destination with stock templates first; a later `Folders_Consolidate` then hits the collision guard and renames the user's customised copies aside. Nothing is lost on disk, but the registry keeps loading the stock versions, so the customisation silently stops taking effect. Fix: have extraction skip a project with pending legacy content, or have consolidation prefer the incoming legacy file on collision in `templates/`. |
| IM-2 | **139 hand-rolled `_BIM_COORD` paths remain** | Tier 2 of `tools/check_path_discipline.ps1` is ratcheted, not zero: 139 sites across 118 files still build `Path.Combine(..., "_BIM_COORD", ...)` by hand instead of calling `StingPaths.Meta` / `CoordStores`. They mostly land in the right place today, so this is layout coupling rather than a live defect — but every one is a place the layout can fork again. Burn the baseline down file-by-file; the gate blocks any increase. |
| IM-3 | **`PlanscapeProjectLink.ConfigPathForModel(string)` cannot resolve canonically** | It takes a bare model path, which carries no project root, so it falls back to the pre-consolidation sibling (marked `path-discipline: legacy-fallback`). The `WarningsManager` callers were switched to the `ConfigPathFor(Document)` overload, but the BCC still calls the string form with `_data.FilePath` (6 sites in `UI/BIMCoordinationCenter.cs`), so the BCC may read a link config from the old location. Fix: give the BCC a `Document` at those call sites, or resolve the root from a model path. |
| IM-4 | **Issue schema fork: `issue_id` vs `id`** | **CLOSED — Phase 201.** One canonical identifier (`issue_id`); reads accept `id` / `IssueId` via `IssueSchema.IdOf`; legacy rows upgrade in place on load. See CHANGELOG Phase 201. |
| IM-5 | **Four forked warning→issue escalation paths** | **CLOSED — Phase 201.** All four collapsed into `IssueEscalationEngine`; identifiers mint through `IssueIdMinter` (per-type high-water mark, reserved across a batch). Note: the `Count + 1` mechanism recorded here was mis-diagnosed — the collision is with *live rows*, not within a batch. Corrected analysis + reproduction in CHANGELOG Phase 201. |
| IM-7 | **Six issue writers bypassed `IssueStore`'s audit + server push** | **CLOSED — Phase 202.** `CreateIssuesFromWarningsCommand`, `AutoRaiseComplianceIssues` and the four BCF sites now run through `IssueStore.Begin(doc)` batches. Importers whose record IS the mapping work use the new `IssueBatch.Adopt`, which migrates the row on the way in so an importer cannot re-fork the schema. |
| IM-8 | **`GetNextIssueId` reserved nothing between calls** | **CLOSED — Phase 202.** Retired outright — zero callers remained after IM-7. Replaced by `IssueBatch.MintId` / `IssueBatch.Create`, which hold one minter for the batch. Red/green pair documents the batching failure mode the helper would have had. |
| IM-9 | ~~`IssueStatusNormalizer` has no kind for `RESPONDED` / `ACCEPTED`~~ **CLOSED 2026-09-08** | Both now have their own kinds, so `Canonical()` returns `"RESPONDED"` and `"ACCEPTED"` instead of `"UNKNOWN"`. **The row offered two options and the second one was wrong.** It suggested folding `RESPONDED` into `Resolved`. `Resolved` makes `IsOpen` false, and every other reader treats RESPONDED as still open — `BIMManagerCommands` counts an issue open as `OPEN || IN_PROGRESS || RESPONDED`, and the platform bridge maps it to `"Active"`. Folding it would have flipped `has_open_issues` and hidden exactly the population the status names. Distinct kinds were also required rather than merely preferable: both are **exact-match filtered in seven places** (`status == "RESPONDED"` drives a *Bulk: Close All RESPONDED* action), so canonicalising a stored row into another kind's spelling would silently unmatch rows those filters used to find. **A behaviour change worth knowing: `ACCEPTED` is no longer counted as open.** It previously normalised to `Unknown`, and `IsOpen` treats Unknown as open so the gate fails safe — so an accepted issue kept `has_open_issues` red. Seven call sites already disagreed: three skip lists group ACCEPTED with CLOSED and VOID, a fourth returns `s == "CLOSED" || s == "ACCEPTED"` for *is closed*, both the BCF and platform bridges map it to `"Resolved"`, and its own description reads *"Response accepted, issue closed"*. **One existing test was changed, deliberately, because it encoded the defect.** `Migration_does_not_destroy_a_status_it_does_not_recognise` asserted `IsOpen == true` for **both** spellings, commented *"not closed ⇒ still needs attention"*. That was measuring the Unknown fallback, not the status. Its real point — a stored status survives migration byte-for-byte — is kept and now also covers a genuinely unrecognised spelling, since RESPONDED and ACCEPTED are recognised from here on. Two things fell out on the way. `IssueSchema.ApplyStatus` set `date_closed` on `Closed || Void`, written before ACCEPTED existed, so an issue closed by acceptance never got one and read as still-running in any report measuring age from that field; that hand-written terminal test now lives once as `IssueStatusNormalizer.IsTerminal`, which the codebase had spelled out by hand in four places. And `DocumentManagementDialog` can offer RESPONDED / ACCEPTED as filters again — it had dropped them precisely because the normalizer never emitted them, so those nodes could not match a stored row. `StingTools.Cost.Tests` 110 → 144, `StingTools.Tags.Tests` 718 → 719. **Five sabotages confirmed failing**, including the row's own folding suggestion. |
| IM-10 | **Watch: closed-without-fixing warnings may ping-pong** | Phase 202 scoped escalation dedup to still-OPEN issues, so a recurrence after closure is reported again — deliberate, and the point of closing an issue. The failure mode to watch for: a coordinator closes an auto-raised warning issue WITHOUT fixing the underlying warning (won't-fix, accepted-risk, false positive). The next scan re-raises it, they close it again, forever. **Do not pre-build this.** If it shows up in practice, the fix is a suppressed / won't-fix status that still participates in dedup — i.e. `FindOpenByDedupKey` gains a "or suppressed" arm so a suppressed finding blocks re-raising without counting as open. Precedent for the storage and the expiry semantics is `Core/Storage/StingValidatorSuppressionSchema` (suppressed codes + reason + expiry). Related: IM-9 — a `SUPPRESSED` status would need its own `IssueStatusKind` rather than falling through to `Unknown`, which `IsOpen` treats as open. |
| IM-11 | **`POST /warnings/report` does not feed `GET /warnings/trend`** | Verified against a live local server in Phase 203. `PushReport` only updates `Project.WarningCount` and broadcasts `WarningsReported`; it writes no row. `GetTrend` reads `ComplianceSnapshots`, which only `SaveBaseline` creates. So a project that scans continuously but never saves a baseline shows a current warning count and an **empty trend chart** — including on the mobile warnings screen. Not fixed here: EXPECTED SCOPE for Phase 203 was plugin-only, and the consolidation branch's server code predates PR #448. Fix is server-side and one of: have `PushReport` also persist a snapshot (cheap, but inflates `ComplianceSnapshots` at scan cadence — needs a dedupe/interval guard), or add a dedicated warning-report entity. Decide alongside #448. **UNBLOCKED 2026-08-03** — #448 has landed, so this is no longer queued behind it; it is ready to pick up as a short-lived PR off `main`. |
| IM-12 | **`GetTrend` filters `WarningCount > 0`, hiding a clean model** | `WarningsController.GetTrend` has `&& s.WarningCount > 0`. A project that reaches zero warnings drops out of its own trend series at exactly the point worth celebrating, and a genuinely clean model shows an empty chart indistinguishable from "never reported". Phase 203 deliberately snapshots zero-warning scans locally (the clean-model early return now caches + records), so the local store is correct; only the server view is lossy. One-word server fix (`>= 0`), deferred with IM-11 for the same scope reason. **UNBLOCKED 2026-08-03** — #448 has landed; ready to pick up with IM-11 as a short-lived PR off `main`. |
| IM-16 | ~~PR #448 is parked behind DEP-7~~ **DONE — DEP-7 cleared, #448 landed** | The park is over. Its own defect (`SeedTestData` running twice against one in-memory store, `System.ArgumentException: An item with the same key has already been added. Key: 11111111-...` out of host construction, surfacing as every test in a class failing) was fixed in `804fe22da` by making the seed idempotent — it returns early when the fixed test tenant is already present (`IgnoreQueryFilters`, since the tenant filter falls back to `Guid.Empty` there). Duplicate-key failures went **16 -> 0**. The remaining blocker was never this PR: it was DEP-7, the process-global `Hangfire.JobStorage.Current` that made the victim set random (12 / 16 / 3 failures across three runs of the same tree), which is why baselining it was refused and re-running for luck was refused. DEP-7 was fixed on `main` (recurring jobs now registered through the injected `IRecurringJobManager`) and closed by #549. On that base the server suite is **deterministic**: three independent runs across three different trees — `main` `097b75a94` (540 tests), #549 `435cb8432` (541), #448 `8a737626a` (542) — all report **0 failed**, 1 skipped (the deliberate `Real_LiveKit_*` test), and `check-new-failures.sh` reports "No new failures." A residual `ObjectDisposedException` on `Hangfire.InMemory.State.Dispatcher` still appears in the logs, but it is a *teardown* artefact — Hangfire deregistering a background server in `BackgroundServerProcess.ServerDelete` after the run, logged WRN and swallowed. Its volume is nondeterministic (1052 / 1346 / 2 across those same three runs) but it fails no test, and it predates #448 on `main`. Distinct from DEP-7, which failed tests at host-build time. Worth a separate cosmetic cleanup, not a merge blocker. **Unblocks IM-11 / IM-12** (server warnings-trend gaps), which were queued behind this PR and are now the next short-lived PRs off `main`. |
| IM-6 | **`StingTools.Clash.Tests` does not build** | Pre-existing on `claude/iso19650-consolidation`: 14 `CS0246` errors because linked "pure-logic" clash files now reference `Autodesk.Revit.*`. The project's Revit-free premise has drifted. Coord-log unit tests were hosted in `StingTools.Tags.Tests` instead. |
| IM-13 | **Three document-identity resolvers differ on trimming** | **CLOSED — `b76438483` (branch `claude/iso19650-consolidation`).** One shared rule now lives in `Core/DocumentIdentity.cs` (trim + first-non-blank); all three resolvers route through it, each keeping its existing empty sentinel so no caller contract changed. The register's row mapping + id-keyed merge were split into the Revit-free `Core/DocumentRegisterMerge.cs` so the dedup is provable headlessly, and `Merge` re-normalises at key time so no other call site can reintroduce the split namespace. Regression test: `DocumentRegisterMergeTests.TrailingSpaceDocNumber_DedupsWithDeliverable` — verified load-bearing (reverting the trim fails 10 of 14 merge tests). Original finding: `DocumentRegister.First(...)` and `CoordStores.RowId(...)` return the raw first-non-blank candidate key; `DeliverableLifecycle.DeliverableKey`/`RowKey` `.Trim()` first. So a `DocNumber` carrying a trailing space keys as `"PRJ-001"` in `deliverables.json` (RowKey, trimmed) but `"PRJ-001 "` in the register reader (untrimmed `First`), and the two stores fail to dedup in `DocumentRegister.BuildUnified` — the same deliverable shows twice. Low severity (doc numbers rarely carry whitespace), deferred because unifying the key rule changes how existing project files collapse and needs its own verification pass. Fix: one shared `NormalizeId` (trim + candidate-list) used by all three. |
| IM-14 | **Suitability/status vocabulary is not single-sourced** | **CLOSED — `8ac700189` (branch `claude/iso19650-consolidation`).** `Iso19650Vocabulary` became the canonical source (rather than adding a fourth parallel class — `DocStatusCodes.SuitabilityCodes` already delegated to it): added `SharedSuitabilityCodes` (S0–S7), `TerminalStatuses`, `CdeStates` (aliasing `StingPaths.CdeStates`) and `CdeStatesWithTerminal`. The three exact-duplicate inline arrays in `UI/BIMCoordinationCenter.cs` now read from it. **Deliberately left alone:** the bespoke-label positional `suitCodes[,]` grid, `TitleBlockCommands.ValidSuitabilityCodes` and `DocumentLookups.SuitabilityCodes` (different supersets carrying `A6/A7/B7` and `C1–C3/D1–D2`), and the typed transition maps in `BIMManagerCommands` / `Phase75Enhancements` — none are literal duplicates and repointing them would change behaviour. Original finding: the four CDE containers are now single-sourced (`StingPaths.CdeStates`), but suitability (S0–S7) and status/terminal codes (SUPERSEDED/WITHDRAWN/OBSOLETE) are still spread across `Core/Drawing/Iso19650Vocabulary`, `BIMManager.DocStatusCodes.All`, and inline arrays in `UI/BIMCoordinationCenter.cs`. No canonical suitability enum, so the sets can drift. Fix: promote one `Iso19650Codes` source (states + suitability + status) and repoint the inline arrays, as was done for `CdeStates`. |
| IM-15 | **P→C revision scheme is hard-coded** | **CLOSED — `f71b98ee3` (branch `claude/iso19650-consolidation`).** Driven from the manifest's `revision_scheme`, which `ProjectManifestBlock` already declared but nothing ever read. New Revit-free `Docs/Templates/RevisionScheme.cs` parses it into preliminary/contractual prefixes and owns `Bump` + `PromoteToContractual`; a single-stage scheme makes promotion a no-op rather than inventing a `C` series. Unset/blank/prefix-less falls back to P/C so existing projects are unchanged. 14 tests in `RevisionSchemeTests`. Original finding: `DeliverableLifecycle.BumpRevision` / `PromoteToContractual` bake in the `P01`→`C01` preliminary→contractual scheme. Appointments that mandate a different revision convention need code changes. Fix: drive the scheme from project config (`PRJ_ORG_*` or the manifest), defaulting to P/C. |

## Drawings-production deep review (2026-07-20)

Full-surface review of the Drawing Type engine, corporate catalogue, View Style Packs + AEC
filters, title blocks, annotation/legends/match lines, sheet production pipeline, and command
wiring: **~85 findings (5 Critical / 27 High)** with a prioritised P0–P2 remediation plan.
See [`DRAWINGS_PRODUCTION_REVIEW.md`](DRAWINGS_PRODUCTION_REVIEW.md).

### P0 — CLOSED (see CHANGELOG Phase 223)

| Finding | Status |
|---|---|
| C-1 unbound style-pack JSON keys (`filterRules`, short-form vgOverrides) | ✅ fixed — POCO aliases; filter rules bound 19 → 97 |
| C-2 / E-1 both `ResolveExtends` folds strip fields | ✅ fixed — generic default-aware overlay |
| C-3 producer sheet key ignores context | ✅ fixed — `STING_SHEET_CONTEXT_TXT` in the sheet key |
| W-1 two penetration workflow presets inert | ✅ fixed — step keys + 3 `ResolveCommand` cases |
| A-11 `LABEL_DEFINITIONS.json` mojibake | ✅ fixed — 530 strings / 1,171 chars repaired |
| V-3 material-class filter AND-ed instead of OR-ed | ✅ fixed — `LogicalOrFilter` |
| V-7 / V-8 dead filter definitions | ✅ fixed — 12 filters (8 `Family Name`, 2 ops, 2 compound schema) |

Found while fixing P0, **not** in the review:

- **`surfFgColor` (15×) and `projLinePattern` (5×) were also unbound** on `StyleFilterRule` —
  the Phase 139 alias pass covered the four line keys and stopped short of these. Fixed with C-1.
- **`plumb-high-pressure-zone` and `plumb-backflow-risk` use a third rule schema**
  (`{"kind":"compound","op":"or","operands":[…]}`) that binds to neither `IsLeaf` nor
  `IsCompound`, so `BuildFilter` returned null silently. Fixed with V-8.
- **`DrawingType.extends` could never inherit any field with a non-null POCO default**
  (`PaperSize` "A1", `Discipline`/`Phase` "*", `Purpose` "Plan", `Orientation` "Landscape",
  `Scale` 100, `DetailLevel` "Medium", both sheet patterns): the old `!IsNullOrEmpty` guard
  treats a child's default as a value, so an A0 parent always resolved back to A1. Fixed with
  E-1. Known residual limitation: a child that explicitly restates a default is
  indistinguishable from one that omits it and will inherit the parent's value.
- **`ViewStylePack`'s six already-working scalars keep their original guards** — that path runs
  on every `Get()` and changing `lineWeightScale` merge semantics would alter rendering. The
  same default-clobbers-parent issue therefore remains open for style packs specifically.
- **Duplicated `OST_Sheets` insertion** in `LoadSharedParamsCommand` (two identical try blocks,
  ~lines 333–349). Harmless, not fixed.

### P1 — managed-template hardening (done, prerequisite of C-2)

| Finding | Status |
|---|---|
| E-3 `CopyElement` on a non-template seed → junk views re-minted each run | ✅ fixed — `View.CreateViewTemplate()` + cleanup on failure |
| E-4 hardcoded `VIEW_DISCIPLINE` ints | ✅ fixed — reads `ViewDiscipline` members |
| E-6 discarded managed parameter-id list | ✅ fixed — `SetNonControlledTemplateParameterIds` complement |

The review mis-stated E-4: `Coordination = 4095` was already correct and `Mechanical = 4096` was
not. `VIEW_DISCIPLINE` is a bit-flag parameter (Architectural 1, Structural 2, Mechanical 4,
Electrical 8, Plumbing 16; Coordination 4095 = all bits). The genuine defects were Mechanical,
Electrical and Plumbing.

### P1 — issued-output correctness (done)

| Finding | Status |
|---|---|
| D-1 / P-2 / P-10 producer token substitution + numbering | ✅ fixed |
| T-1 composer never called `ToConcreteFamily` | ✅ fixed |
| T-2 blank-name match-anything + silent arbitrary fallback | ✅ fixed (two defects) |
| T-3 `PRJ_TB_LOCK_BOOL` unread by the declarative pipeline | ✅ fixed (skip-and-report) |
| T-12 issue summary clobbered by revision description | ✅ fixed (write removed) |
| C-4 AnnotationRunner had no idempotency | ✅ fixed (tags / dims / decorative) |
| A-6 dog-leg pair key never matched on re-run | ✅ fixed |
| A-7 captions stacked every sync | ✅ fixed |
| A-8 silent tag-family fallback | ✅ fixed (warns) |
| A-9 `GetElementCentre` null passed to `IndependentTag.Create` | ✅ fixed (bbox centre) |
| A-3 legend refresh minted a blank twin | ✅ fixed (in-place, both branches) |
| P-5 section frames wrong and divergent | ✅ fixed (one shared builder) |
| E-2 crop assigned in model space | ✅ fixed (converted to crop frame) |

Corrections to the review found while fixing these:

- **E-4 was wrong in both directions.** `VIEW_DISCIPLINE` is a bit-flag parameter —
  Architectural 1, Structural 2, Mechanical 4, Electrical 8, Plumbing 16, Coordination 4095
  (all bits). So the shipped 4095 the review flagged as invalid was correct, and the 4096 it
  called "the only correct one" was not. Real defect: Mechanical / Electrical / Plumbing.
- **A-6's second clause is incorrect.** `PruneOrphans` does not need the segment-key
  discipline: it takes the scope-pair GUID from the first colon-delimited field, identical in
  the stamped and collapsed forms, so orphan segments pruned correctly before the fix.
- **T-2 is two defects.** The `IsNullOrEmpty(tbFamily) ||` clause matched any loaded title
  block and returned before the documented silent-fallback path could run.
- **T-7's `Family Name` filters carry no `kind` field at all**, rather than `kind: builtin`.

Deliberately deferred, with reasons:

- **Dimension idempotency uses reference-category detection, not a stamped marker.** A marker
  needs a new parameter bound to Dimensions (4-file provisioning). Residual: a dimension
  reporting `AreReferencesAvailable == false` is treated as unknown, so a duplicate remains
  possible if every dimension in a view is unreadable AND a prior chain exists.
- **Match-line captions are identified by (view + type + caption-template shape + proximity)**,
  since `STING_MATCH_LINE_GUID_TXT` does not bind to Text Notes. Template-shape matching (not
  exact text) is what makes the post-renumber case work — the caption embeds the sheet number,
  so exact text would miss every stale caption after a renumber, which is precisely when
  `MatchLine_Sync` is run. Residual: a caption whose boundary geometry moved is orphaned rather
  than deleted.
- **P-5 / E-2 could not be verified outside Revit.** Both take Revit geometry types and
  RevitAPI.dll is mixed-mode, so it will not load in a bare host. Testing a re-implementation
  would assert against a copy, not the shipped code, so they rest on structural argument plus
  the Revit smoke test.
- **`IFamilyLoadOptions` `overwriteParameterValues` disagreement** (resolver false,
  TitleBlockSlotCommands true) — untouched, since no loading behaviour changed.

### P2 — tracks A + D CLOSED (see CHANGELOG Phase 224)

| Finding | Status |
|---|---|
| E-5 / E-13 SheetSequenceStore wipe, silent write failure, Peek off-by-one | ✅ fixed |
| A-4 / A-10 grid + level dimension chains never placed | ✅ fixed |
| P-6 / P-13b / P-13c scope-box production | ✅ fixed |
| P-8 producer schedule placement + per-slot viewport types | ✅ fixed |
| P-9 re-run warnings + double counting + ctx.Tag divergence | ✅ fixed |
| E-7 / E-8 / E-9 / E-10 / E-12 / E-14 / V-9 engine small-bore | ✅ fixed |
| A-12 / A-13 / A-14 legend placement + match-line validate | ✅ fixed |
| T-8 / T-9 / T-11 / T-13 title-block loose ends | ✅ fixed |
| V-4 / V-5 / P-12 / P-13a performance | ✅ fixed |
| A-5 GridDimensioner axes + drainage invert | ✅ fixed |
| W-2 MatchLine suite unreachable | ✅ fixed (5 buttons + 5 ResolveCommand cases) |

Corrections to the review found in this pass (bringing the running total to six):

- **W-5 was wrong and acting on it would have been destructive.** The instruction was to delete
  the inline handler copies for `DrawingTypes_SyncStyles` / `DrawingTypes_FromScopeBoxes` as
  "reimplementations". They are not: the inline versions are read-only (an audit and an
  advisory) while the command classes WRITE. Deleting them would have turned two read-only
  buttons into model-writing operations. Resolved by naming instead — the advisories moved to
  `DrawingTypes_AuditStyleRefs` / `DrawingTypes_SuggestFromScopeBoxes`, the canonical tags now
  mean the command class everywhere, and new buttons expose the writing commands.
- **P-7's conclusion does not follow from its evidence.** The two slot data sets are authored to
  DIFFERENT conventions, each self-consistent with its consumer: `STING_DRAWING_TYPES.json` is
  bottom-left (pipe-spool tiles 0.05→0.45, 0.55→0.95; every x+w ≤ 0.98) while
  `SheetTemplateEngine`'s built-ins are centre (normX 0.47/w 0.80, 0.72/w 0.38, 0.72/w 0.40 give
  right edges of 1.27, 1.10, 1.12 under a bottom-left reading — three of four off-sheet). The
  defect is the adapter's claim that they share a convention. Still open; see below.
- **A-5's drainage magnitude is wrong.** The error is one wall thickness, not two: that would
  require `Diameter` to be the outside diameter, and Revit exposes no inside/outside distinction
  on `Pipe` or `MEPCurve`.
- **E-10 is worse than described** — the `Validate` loop sat outside the `try`, so the snapshot
  clear was skipped on any throw, not merely un-stamped.

### P2 — still open

**Track B — CLOSED.**

| Item | Status |
|---|---|
| B1 grid-dimensioning convergence | ✅ `DimGrids` survives; `GridDimensioner` reduced to `IsDimensionable` |
| B1 `dimensionStrategy` | ✅ wired into `DimGrids` via `DimensionStrategy.ResolveType` |
| B1 `condition` | ✅ wired (evaluator had zero call sites) |
| B1 per-rule `tagFamily` | ✅ wired, warns when the named family is absent |
| B1 `minSizeMm` | ✅ was never inert — `MEPDimensioner` reads it (review error) |
| B1 `orientation` / `tag7Depth` | ✅ Phase 225 — wired in `AnnotationRunner` |
| B1 `densityMode` | ✅ Phase 225 — deleted; semantics were unrecoverable |
| B2 MatchLine reachability | ✅ 5 buttons + 5 ResolveCommand cases |
| B3 `${MAT_*}` tokens | ✅ wired; usage scan memoised per doc |
| B4 checksums | ✅ Phase 225 — all 93 stamped by `tools/StampDrawingTypeChecksums`; packs deliberately unlocked |
| B5 composer numbering | ✅ routed through `SheetSequenceStore` |
| B6 unmintable `iso-status-*` filters | ✅ Phase 225 — removed; filter library 298 → 290 |

**Track C (catalogue data) — CLOSED in Phase 225.** C1–C6 all landed. Two review claims did
not survive verification and are recorded here so they are not re-raised:

- **C2 indices were off by one.** The review named rules #82/#89/#91/#95/#101 as the shadowed
  literals; those are the *regex* rules doing the shadowing. The unreachable ones were
  #83/#90/#92/#96/#102. Also: the two encodings mostly use different docType spellings
  (hyphens vs underscores), so only the five identically-spelled rules were ever shadowed —
  not the whole literal block.
- **C6 put `struct-pt-tendon` in the wrong file.** It is a filter id in
  `STING_AEC_FILTERS.json`, not a drawing type. `OST_StructuralRebar` appears once, there.

**Found while verifying Track C — now fixed (Phase 225):**

- Two routing rules could never fire: `(*, HANDOVER, *)` and `(*, PRESENTATION, ELEVATION)`
  sat below the phase-wildcard production block. Moved up to join the other phase-scoped rules.
- `duct-spool` carried the same copy-pasted `01/DR` isoNaming the review flagged on
  `elec-spool`; both now type as `02/SP`.
- `DrawingTypeRegistry.MakeFabSpool` carried the same overlapping ISO/BOM slot geometry as the
  JSON, so fixing only the data would have left the built-in fallback broken.

**Found while verifying Track C — still open:**

- **Viewport naming has two sources of truth.** The catalogue now says
  `"STING - Standard Viewport"` on all 93 types, but
  `SheetManagerEngineExt.DefaultViewportTypeRules` independently names `"STING Viewport"`,
  `"STING Section Viewport"`, `"STING Elevation Viewport"` and `"STING 3D Viewport"` for the
  `AutoAssignVPTypes` path. Those strings may match types in existing project templates, so
  they were not renamed blind. Needs a decision on the canonical set, then one edit.
- **`Schematic` and `Clarification` produce a floor plan.** Both are now canonical
  `DrawingPurpose` constants, but `DrawingProducer.SynthesizeSingleRule` still falls through to
  `"FloorPlan"` for them. Choosing the Revit view family for a schematic (Drafting? Section?)
  is a design decision, not a rename.
- **ISO 19650 suitability colouring has no mechanism.** The 8 `iso-status-*` filters were the
  attempt and could never work — Revit view filters cannot target `OST_Sheets`. A title-block
  parameter or a view-template route would work; a view filter never will.

- **`ViewStylePack.Checksum` is declared but never computed.** Drawing types were locked in
  Phase 225; packs were deliberately left unlocked (reasoning in `CLAUDE.md`). The field should
  either be wired to a `ViewStylePackRegistry.ComputeChecksums` or dropped, rather than left as
  a property that looks like a lock and is not one.
- **`tools/StampDrawingTypeChecksums` is not gated by CI.** It has a `--check` mode that exits
  non-zero on a missing or stale checksum, but the plugin workflows name specific `.csproj`
  paths so the tool is never built. Until it is wired, a change to `STING_DRAWING_TYPES.json` or
  to the `DrawingType` model can ship with stale hashes — which surfaces in Revit as every type
  reporting drift.

- **`AnnotationRulePack.TagDepths` is inert in exactly the way `tag7Depth` was.** The
  per-category depth dictionary is edited by `DrawingTypeEditorDialog` (a real UI, with
  add / rename / set-depth) and read by no engine — `AnnotationRunner` resolves depth from
  the *ViewStylePack's* `CategoryDepths`, not from the pack's `TagDepths`. It was left in
  place rather than deleted alongside `densityMode` because deleting it removes a working
  editor surface, which is a bigger call than dropping an unreferenced field. Either point
  `AnnotationRunner` at it or retire the UI with it.
- **`minSizeMm` applies to dimension rules only.** `MEPDimensioner` honours it; the tag
  path ignores it, so a tag rule declaring `minSizeMm` silently has no effect. Either
  honour it when collecting taggable elements or reject it on tag rules in the validator.

**SURFACED AND NOT ACTED ON — needs a drainage engineer.** `DrainageInvertDimensioner` places
the invert one wall thickness low. The geometry fix is understood, but the corrected value is an
engineering number on a drainage deliverable and is not an autonomous call. Left unwired.

**P-7 slot convention** — ✅ closed in Phase 225. `TemplateViewSlot` converged on the
bottom-left convention: 16 built-in slots re-authored, placement and save maths aligned
with `SheetPlacementBridge`, and pre-2.0 user libraries migrated on load by
`MigrateSlotOrigin` (version-guarded, in-memory until the next save).

**Still open — a third slot convention.** `LayoutSlotPreset` (`SheetManagerEngineExt`) is
centre-anchored. Its comments claimed bottom-left and have been corrected, but the format
itself was not converted: it is a separate saved preset format that never exchanges slots
with `DrawingSlot` or `TemplateViewSlot`, so converging it means a second user-data
migration for no current correctness gain. Worth doing when that area is next touched.
Everything else in the review remains open, notably: D-1/P-2/P-10 token substitution and
numbering; T-1/T-2/T-3 title-block resolution, silent fallback and `PRJ_TB_LOCK_BOOL`;
C-4/A-6/A-7 annotation and match-line idempotency; A-3 legend in-place refresh; P-5/E-2 section
frame and crop coordinates; C-5 inert corporate-lock checksums; W-2 unreachable MatchLine suite;
W-3/W-4/W-5/W-6 wiring and documentation drift.

## Revision system — deferred items (Phase 199)

Recorded while aligning the revision subsystem (see CHANGELOG Phase 199).

- **Rebind title-block revision labels to built-in parameters, then delete the TB half of the
  syncer.** The revision box currently reads STING shared params (`PRJ_TB_REVISION_NR_TXT` /
  `_DATE_TXT` / `_DESCRIPTION_TXT`) that a command must keep in sync. Revit exposes built-in
  **Current Revision / Current Revision Date / Current Revision Description** parameters on
  title blocks that it maintains itself. Rebinding the catalogue's revision-box labels to those
  built-ins makes the drawing correct with **zero sync** — no command run, no drift window, no
  stale box if someone forgets to click. Once rebound, `TitleBlockRevisionSyncer` can drop its
  title-block writes entirely and keep only the `SHT_REV_TXT` / `SHT_REV_DATE_TXT` sheet stamps
  (which feed schedules and exports, and have no built-in equivalent). Scope: a catalogue
  migration across the affected families plus a factory change — deliberately out of scope for
  Phase 199, which did not mass-edit the 206-family catalogue.
- **Consolidate the three revision-cloud implementations.** `AutoRevisionCloudCommand`
  (`BIMManager`), `DocAutomationExtCommands.RevisionCloudAuto` (`Docs`), and
  `MaterialRevisionCloudJob` (`Core`) each independently decide what "changed" means, how clouds
  are grouped, and which view they land in. Fold them onto one shared engine (change-set in →
  clouds out) so the three entry points stay behaviourally identical and a fix to cloud grouping
  lands once.
- **Data-drive the LG-03 per-discipline auto-revision thresholds.** `AutoRevisionOnTagChangeCommand`
  carries a hardcoded per-discipline threshold dictionary. Move it into a JSON data file with a
  project override (same corporate-baseline + `_BIM_COORD` override pattern the drawing types and
  sizing rules already use), so a project can tune how many tag changes trigger an auto-revision
  without a code change.

## MEP print-readiness — deferred items (Phase 198)

Recorded while making the MEP drawing types print-ready (see CHANGELOG Phase 198).

- **Fire suppression drawing types (fast-follow).** Only fire *detection* exists today. There are no
  sprinkler / suppression **layout**, **section**, or **detail** drawing types (corporate DrawingType +
  routing + a fire style pack). Author them as a fast-follow so a fire-suppression package is
  drop-a-view print-ready like M/E/P. Scope: a `corp-standard-fire` style pack (sprinklers + fire-alarm
  bold `#C00000`, other MEP + arch/struct halftone), `fire-sprinkler-layout` / `fire-section` /
  `fire-detail` types with `${PRJ_ORG_*}` title-block params + `tagFamilies` (`STING - Sprinkler Tag`,
  `STING - Fire Alarm Device Tag`), and `F/*/SPRINKLER|SECTION|DETAIL` routing rules.
- **SEQ zero-pad is project-global by design (not per-DrawingType).** SEQ pad lives in
  `TagConfig.SeqPadWidth` / `EffectiveSeqPad`, set from the Tag Studio → Tokens & Depth dock tab, and
  applies project-wide. It was **deliberately not** made an `AnnotationTokenProfile` field, so a
  DrawingType cannot override it per drawing. If per-type SEQ pad is ever wanted, add a `seqPad` field to
  `AnnotationTokenProfile` and push it on the produce path (mirroring how paragraph depth already flows),
  with the project-global value as the fallback.
- **Arch / structural / health `tagFamilies` debt (NOT fixed — separate scope).** The Phase-198 punch-list
  cross-check found the same key-alignment + family-existence defects the MEP fix cleared, still present on
  **non-MEP** drawing types: ~87 `tagFamilies` key↔AutoTag-category mismatches (camelCase keys like
  `StructuralColumns` that never match the display-name rule category), **19 `STING_TAG_*`
  non-existent family values across ~14 arch/structural types**, and `STING - Generic Tag` (should be
  `STING - Generic Model Tag`) on ~22 healthcare types. So "MEP print-ready" ≠ "whole file fixed". Real
  target families exist (`STING - Door/Window/Room Tag`, `STING - Structural Column/Rebar Tag`,
  `STING - Generic Model Tag`), so the same mechanical fix applies — do it only when explicitly scoped as
  an arch/struct/health pass (out of scope for the MEP runner).

## Ambiguous parameter bindings — needs SME confirmation (Phase 196)

The Phase 196 binding-accuracy fix narrowed every confidently-classifiable discipline family and
preserved the universal set. The parameters below remain bound to the **broad core set** (they had no
per-param `CATEGORY_BINDINGS.csv` row and no group override) and are logged as coverage **GAPs** by
`LoadSharedParamsCommand`. They are semantically mis-located (space/zone/landscape/structural-cost
params filed under the `COM_DAT` communications group) — narrowing them requires an SME decision on the
correct element domain, so they were **not** guessed. Recommended categories noted for review; once
confirmed, add rows to `CATEGORY_BINDINGS.csv` (+ `PARAMETER_CATEGORIES.csv`) and re-run
`Bindings_PruneToSpec`.

| Param(s) | Group | Recommended domain (needs confirmation) |
|---|---|---|
| `SPC_GROSS_AREA_M2`, `SPC_CEILING_HEIGHT_M`, `SPC_MIN_HEADROOM_M`, `SPC_FINISH_FLOOR/CEILING/WALLS_TXT`, `SPC_PUBLIC_ACCESSIBLE_BOOL` | COM_DAT | Rooms, Spaces |
| `ZON_CATEGORY_NAME/CODE_TXT`, `ZON_NET/GROSS_AREA_M2`, `ZON_VOLUME_M3`, `ZON_OCCUPANTS_NR` | COM_DAT | Areas, Spaces, HVAC Zones |
| `GEN_SPECIES_TXT`, `GEN_HEIGHT_AT_MATURITY_M`, `GEN_ROAD_TYPE_TXT`, `GEN_TAG_7_PARA_MISC_TXT` | COM_DAT | Planting, Site, Roads |
| `CST_S_FRM_FORMWORK_AREA_SQ_M`, `CST_S_MAS_BLOCKS_NR`, `CST_S_MAS_NET_WALL_AREA_SQ_M`, `CST_S_REI_LAP_LENGTH_MM`, `CST_S_REI_TOTAL_WEIGHT_KG`, `CST_S_SUPPLIER_TXT`, `CST_FORMWORK_TYPE_TXT`, `CST_PLYWOOD_SIZE_TXT`, `CST_SAND_MOISTURE_TXT` | COM_DAT | Structural Framing/Columns/Foundations, Walls, Floors |
| `SLV_TAG`, `SLV_TAG_7_PARA_TXT` | SLV_SLEEVE_PARAMS | Generic Models (matches rest of `SLV_*` family) |
| `COMP_TAG_1_TXT` | COM_DAT | universal composite tag — likely keep broad |

Additionally, the healthcare families `MGS_` / `RAD_` / `CLN_` / `CEQ_` were bound to reasoned
discipline domains (medical-gas piped set, radiation-shielding set, clinical room/equipment set) that
exclude all cross-discipline conveyance categories, but the **precise per-param category refinement**
(e.g. which exact device vs. room vs. equipment category each WARN_/DESIGN_/TAG param belongs to)
should be SME-confirmed against HTM/HBN modelling conventions. See `docs/binding_audit_report.csv`.

## Universal Tag pivot — Task 4 legacy cleanup (DEFERRED, branch `feature/universal-tag-system`)

Tasks 1-3 of the universal-tag pivot landed (propagation command, status gates, tag-expander
schedules). **Staged cutover steps 1-3 are now done** — step 1-2 in Phase 196
(`claude/universal-tag-finalize`) and the **teardown in Phase 197** (`claude/universal-tag-teardown`):
`TagFamilyCreatorCommand` ("Create Tag Fams") was gutted of its CSV tier-authoring path (labels now
come from `Propagate_UniversalTag`), which removed the last live callers of `FamilyLabelAuthor` and
`TagConfigPlanResolver`; both files were then **deleted** (0 callers verified). **Steps 4-5 remain**
(repurpose `HandoverModeHelper` DC/HO; deprecate the colour-scheme commands) — those still have live
callers and are separately gated.

| Candidate | Status | Notes |
|---|---|---|
| ~~`Tags/FamilyLabelAuthor.cs`~~ | **DELETED (Phase 197)** | 0 code callers after the Create Tag Fams gut. Nested `Options`/`ModePlan` went with it. |
| ~~`Tags/TagConfigPlanResolver.cs`~~ | **DELETED (Phase 197)** | 0 code callers after the gut. `TierPlan`/`TierState` live in `Core/PerFamilyTierMap.cs` (retained). |
| `Core/TagConfigCsvReader.cs` + `Data/STING_TAG_CONFIG_v5_0_*.csv` | **RETAINED** | The reader's `TierPlan` API (`LoadFile`/`LoadFiles`/`Parse`) is now caller-less (its consumer `TagConfigPlanResolver` was deleted), but the **v5.0 CSV data** it parses is the canonical *synced* source (per `reference-tag-config-sources`) and is still read by `ParamRegistry` / `TagConfig` / `HandoverModeHelper` / `PresentationModeCommand` / `FamilyParamCreatorCommand` / `LpsValidator` via their own paths. Marked `LEGACY(universal-tag)` in-file; a future pass can rewire a reader onto the typed parser or retire it with the CSVs together. **Do NOT delete the data files — still parsed.** |
| `Core/HandoverModeHelper.cs` (DC/HO) | **RETAINED** | Live callers: `StingToolsApp.GetAllTagConfigCsvs`, `TagConfig.GetTagConfigCsv` (×3), `ApplyParagraphPresetCommand.GetSelectorBool`/`ModeSelectorBool`; `GetActiveMode` used internally. It's a mode/CSV resolver, not pure-legacy. Step 4 (repurpose DC/HO → `PARA_STATE` view preset) still open. |
| `Core/TagStyleCatalogue` colour dims + `Tags/TagStyleEngine.cs` + `Tags/TagStyleCommands.cs` | **RETAINED** | `TagStyleEngine.ResolveTagTypeForPlacement` used by `StingAutoTagger` + `SmartTagPlacement` (6 sites); colour commands wired to live buttons. Keep DEPTH-variant logic (also in `TagTypeVariantWriter`). Step 5 (colour-scheme deprecation) is a separate surgical refactor. |
| ~~`MigrateTagFamiliesCommand` tier-authoring path~~ | **DONE (Phase 196)** | Trimmed to param + type-variant migrator. |
| ~~`TagFamilyCreatorCommand` tier-authoring path~~ | **DONE (Phase 197)** | Gutted to mint (shell + params + variants); label via `Propagate_UniversalTag`. Dead alias helpers (`CsvFamilyNameCandidates`/`TryGetTierPlan`/`ContainsPlanForFamily`) removed. |

**Prerequisites now in-repo (tracked):**
- `docs/UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md` — the consolidated manual walkthrough: what to
  DELETE (T3 + discipline + warning rows), how to BUILD the 65 rows, and the status-badge system.
- `docs/UNIVERSAL_TAG_LABEL_BUILD_SHEET.md` — the authoritative 62-row master-label build guide
  (human-authored in the Family Editor; the API can't do it).
- `docs/UNIVERSAL_TAG_DUCT_SMOKE_TEST.md` — the precise Duct smoke-test checklist (the step-1 gate).
- `docs/UNIVERSAL_TAG_TASK4_STEP2_PATCH.md` — the ready-to-apply step-2 trim of
  `MigrateTagFamiliesCommand` (staged; apply only after the smoke test passes).

**Staged cutover (do in order, each gated):**
1. ~~Prove the universal path in Revit (Duct smoke test for `Propagate_UniversalTag`)~~ —
   **DONE.** Recategorise preserves labels/formulas/breaks (proven live on Duct).
2. ~~Retire the OLD authoring ENTRY POINTS: trim `MigrateTagFamiliesCommand`'s tier-authoring
   call~~ — **DONE (Phase 196).** Applied `docs/UNIVERSAL_TAG_TASK4_STEP2_PATCH.md`; build +
   Tags.Tests green. The remaining entry point is `TagFamilyCreatorCommand` (step 3).
3. ~~Once nothing calls them, delete the `FamilyLabelAuthor` / `TagConfigPlanResolver` cluster~~ —
   **DONE (Phase 197).** `TagFamilyCreatorCommand` was gutted off the CSV path first (removing the
   last callers), then both files deleted (0 callers verified). The v5.0-CSV **reader** and **data**
   are RETAINED (still the canonical synced source, still parsed elsewhere) — see the table above.
4. Repurpose `HandoverModeHelper` DC/HO → `PARA_STATE` view preset (Task-3-adjacent); remove
   the dual-CSV authoring path only. **Still open** (helper has live callers).
5. Deprecate the colour-scheme commands separately (keep depth-variant creation everywhere).
   **Still open** (live buttons + placement).

**Consistency findings (Phase 196 sweep) — both APPLIED in Phase 197:**
- ~~**`FamilyParamCreatorCommand` injects STATE/style params as INSTANCE.**~~ **FIXED (Phase 197).**
  `InjectSharedParams` now uses an `IsTypeParam` predicate: `TagFamilyConfig.VisibilityParams` ∪
  `StyleParams` ∪ `{ TAG_DEPTH_TIER, TAG_BOX_*, TAG_LEADER_*, TAG_POS }` bind as TYPE; container
  values (`ASS_TAG_*`, tokens, description, label params) stay INSTANCE. `TAG_POS` preserved as type
  (drives its offset formula). Depth-setting on "Inject Params"-built families now works.
- ~~**SEQ zero-pad has two sources of truth.**~~ **FIXED (Phase 197).** New `TagConfig.EffectiveSeqPad`
  (`SeqPadWidth > 0 ? SeqPadWidth : ParamRegistry.NumPad`); `BuildSeqString` and `BuildAndWriteTag`
  route through it. Panel still writes `SeqPadWidth` (live driver); `NumPad` write kept in
  `ApplyTagFormatOverrides` (fallback + `num_pad` export). Single accessor — can't desync.


## Universal Tag badge/gate ↔ drawing-pipeline integration (branch `feature/universal-tag-system`)

Follow-on to the badge/gate system: wired the stamped status gates into the AEC filters,
View Style Packs, and QA workflows (see CHANGELOG "Universal-tag badge/gate integration").
Open items surfaced while doing it — recorded rather than force-fixed:

**QA-filter rationalization (runner Task 4 — DOCS ONLY, nothing deleted).**
The stamped **data gate** (`STING_GATE_DATA_STATUS_INT`) now computes the same completeness
signal the old ad-hoc completeness filters derive per-view. Once `coord-qa` + the four
`qa-gate-*` filters are proven in Revit, deprecate the overlapping completeness filters in
favour of the gate-based ones (single source of truth = the stamped gate, not a re-derived
per-view rule). Overlapping legacy ids in `STING_AEC_FILTERS.json`:
`qa-untagged` (⊆ data-gate red), `qa-incomplete-tag` (⊆ data-gate amber "TAG INCOMPLETE"),
`qa-missing-disc` / `qa-missing-loc` (⊆ data-gate amber container/ISO reasons),
`qa-stale-element` (orthogonal — keep; staleness is not a gate input). These have live
callers/packs, so **not deleted** — deprecate only after the Revit smoke test.

**Drawing-Type tag-binding audit (runner Task 5 — reported, no change made).**
`STING_DRAWING_TYPES.json` annotation rule packs bind `tagFamilies` per category. 22 bindings
reference `"STING - Generic Tag"`; the rest reference specific `STING_TAG_*` families. Finding:
`"STING - Generic Tag"` is a **placeholder generic-tag family name** (spaces / "STING - " prefix),
NOT the universal master — the universal label is propagated *into* the 206 named `STING_TAG_*`
families, so the specific bindings already carry it. Repointing the 22 generic bindings to a
"universal master" would therefore be wrong. **No bindings changed.** Revisit only if a single
universal `.rfa` master is ever loaded per-category and the names are proven to resolve.

**Integration follow-ups (need Revit validation before merge — repo norm):**
1. **View Style Pack catalogue now loads at runtime.** `ViewStylePackLibrary` gained a `stylePacks`
   alias (it previously bound only `viewStylePacks`, so the 31-pack corporate file + editor-written
   project overrides were runtime-dead and the registry used 3 hard-coded `BuildDefaults` packs).
   This activates the full corporate catalogue at runtime for the first time — **validate drawing
   production in Revit** (managed-template minting + authored vgOverrides now apply). Treat like the
   Duct smoke test: prove before merge.
2. **Managed issue packs still show badges.** `corp-standard-plan`, `corp-fabrication-shop`,
   `corp-coordination` are `templateMode: managed` with `managedFields` that exclude `vgOverrides`,
   so their `STING_TagStatus: {visible:false}` hide does not apply through the managed path. Either
   add `"vgOverrides"` to those packs' `managedFields` (note: this also applies their existing
   authored vgOverrides, a visible appearance change) or leave as-is (badges are opt-in —
   `TAG_WARN_VISIBLE_BOOL` defaults off — so this is belt-and-braces). Deferred pending the Revit
   validation in (1).
3. **Style-pack `routing[]` is not consumed by `ViewStylePackRegistry`** (resolution is by explicit
   pack id). The added `{purpose:QA → coord-qa}` entry (and all existing routing entries) are
   declarative until routing consumption is wired. Low priority.
4. **Guide edits live on a different branch.** The runner's Task 6.1 (rename badge visibility params
   to UPPERCASE `VIS_DATA_*` / `VIS_QA_*`; document the message labels + view-driven control) targets
   `docs/UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md` + `docs/UNIVERSAL_TAG_BADGE_GLYPH_GUIDE.md`, which do
   **not exist on `feature/universal-tag-system`** — they live on branch `claude/tag-tier-review-94c78a`
   (worktree `awesome-kirch-94c78a`), together with the runner itself. This branch implemented the
   *enabling* code (the `STING_GATE_*_MSG_TXT` params + message computation + stamping, and the
   filters/packs/workflows); the guide edits must be applied on that branch (or when the two branches
   merge). Not forked here to avoid divergent guide copies.

**Smoke-test survival (carry forward):** when the Duct smoke test runs, additionally verify the
badge subsystem survives recategorise-propagation — the 6 family-local `VIS_DATA_GREEN/AMBER/RED` +
`VIS_QA_GREEN/AMBER/RED` Yes/No params, the `STING_TagStatus` annotation subcategory, and the coloured
glyphs must all survive `Propagate_UniversalTag`, and `Gate_StampStatus` must repopulate the four
`STING_GATE_*` params (2 INT + 2 MSG) so badges + message labels render.


## MEP visual-tag declutter — remaining coverage (branch `claude/mep-tag-declutter-advice`)

Phase 197 shipped one-tag-per-run for the batch **Smart Place Tags** path
(`SmartTagPlacementCommand.PlaceTagsInView`) via `Core/Mep/MepRunGrouper.cs`, and suppressed
real-time per-segment visual tags for PerRun/None categories in `StingAutoTagger`. Remaining visual
placement paths that still emit one tag per element (not yet policy-aware):

1. **`SmartTagPlacementCommand.PlaceTagsInLinkedViews`** — tags every linked-model element, no run
   grouping. Linked elements have no writable tokens, so run keys (system/size) read from the linked
   element directly; the grouper would need a linked-aware overload (its own connector walk in the link
   doc). Niche path — deferred until someone tags a federated MEP link and hits the clutter.
2. **`TagSelectedCommand.PlaceVisualTag`** (Organise) — deliberately **left per-element**: an explicit
   user selection of N segments should yield N tags (the drafter chose them). If a "dedup my selection
   to runs" affordance is wanted later, add it as an opt-in mode on that command, not a default.
3. **Reactive vs preventive** — `ClusterTags` still exists as the post-hoc merge into `[×N]` badges.
   With PerRun now preventive on the main path, `ClusterTags` is mostly redundant for linear MEP but
   still useful for dense **equipment/fixture** tags (policy `All`). Keep; no change.

**Plumbing smoke-test watch-items** (when the plumbing model test runs): verify sloped drainage
(1–2 %) is treated as horizontal (grouped) while stacks (vertical) each keep their own tag; verify
pipes with **no assigned MEP system** still separate physically-distinct runs (connector traversal, not
attribute grouping); verify `RBS_CALCULATED_SIZE` reads on the plumbing pipe types in use so size
changes break runs correctly.


## PM / Cost-Control — remaining (branch `claude/pm-cost-control`)

PM-1 landed (the §2 correctness bugs + the do-once shared helpers
`IssueStatusNormalizer` / `MoneyRound` / `ContractSumResolver`, with
`StingTools.Cost.Tests`, 41 green). The full catalogue with file:line anchors is in
`docs/PROJECT_MANAGEMENT_COST_CONTROL_PROMPT.md`. Still open:

- **PM-2 — remainder.** Done (`claude/pm2-onward`): CostPlan→`PROJECT_BUDGET_UGX`
  seeding, cert cumulative keyed on stable `SectionKey`, FinalAccount certified-to-date,
  AFC/FinalAccount/EVM unified on `ContractSumResolver`, retention-MoS basis
  configurable. Still open: approved VO → next-cert "Adjustments/Variations" SOV
  section; clash → tracked issue (via the normalizer) into `issues.json` with SLA +
  reconcile `clashes.json`.
- **PM-3 — remainder.** Done: index-linked fluctuations engine (feeds AFC +
  FinalAccount); schedule-driven cash-flow S-curve (`Core/Schedule/CashFlowSCurve.cs`
  — real time-phased PV off per-task start/finish, consumed by EVM with the manual-%
  path kept as fallback); contractor CVR, loss & expense, line-level cost-to-complete
  and commitments register (`Commands/Cost/Pm3LifecycleCommands.cs`); dayworks capture
  + build-up + final-account pricing (`Core/Variation/DayworkModels.cs`,
  `Commands/Cost/DayworkCommands.cs`). Still open: NRM1 elemental cost plan + OCE;
  QuickBooks/Sage/Excel export.
- **PM-4 — remainder.** Done: the pure `CpmEngine` (forward/backward pass → critical
  path + total/free float, cycle detection). Still open: converge the two MSP/XER
  parsers + read P6 `TASKPRED`/MSP links to feed the engine; model-driven % complete;
  Uganda working-calendar into generation.
- **PM-5 — carbon data.** Per-material/category waste table; UG clamp-kiln brick
  factor; Kampala/UG green-baseline rows; stainless factor. (B6→GridCarbonRegistry +
  benchmark consolidation done in PM-1.)
- **PM-6 perf / PM-7 hygiene / PM-8 delivery layer** — multicategory filters on the
  heavy sweeps + one cached `BuildBOQDocument` per command run; the WorkflowEngine
  name-collision rename, dup `ContractForm`/`SuggestLiability`/`MapProviderIdToLegacySource`,
  sidecar-root unification; MIDP/TIDP engine + real KPI time-series + risk register.

## BOQ & Cost Manager — remaining after Round 3

Branches `claude/boq-master-impl`, `claude/boq-measurement-qs` and
`claude/boq-round3` (WP-C carbon convergence, WP-M measurement parity 2, WP-A
automation, WP-Q QS fills) landed. Round 3 closed: the fossil/biogenic carbon
convention on every surface (off real volume, with waste), shaft-void deduction,
aggregated MeasureQuantity write-back, the could-not-measure export gate, unified
solid-reader detail level, debounced incremental auto-refresh, frozen contract sum,
retention release and the sign-off guard. Still open:

- **WP-M — remainder.** The full per-material COST-row split (multiple bill rows per
  material — carbon already splits, cost still bills the whole element); the optional
  `matchSystem` (SYS) token in takeoff/measurement matching (discipline preference is
  done); a regional/per-category waste override (currently the flat
  `COST_DEFAULT_WASTE_PCT`); remaining magic unit literals → `UnitUtils`.
- **WP-C — remainder.** Per-row EN 15978 A4/A5/C module surfacing on the BOQ (the
  V6 `CarbonStageTracker` computes them and now shares the A1-A3 basis; the BOQ row
  is explicitly scoped "A1-A3 upfront"); repoint/relabel the dead legacy
  `CARBON_FACTORS.csv` loader.
- **WP-Q — remainder.** Itemised contra-charges register behind the flat
  `OtherDeductions`; Materials-on-Site capture into `SovLine.MaterialsOnSite` with a
  vesting trail + statutory payment/pay-less dates; distinct PC-sum mechanism (NRM2
  defined/undefined) separate from provisional sums; adjudication hardening
  (IQR/std-dev outliers). **CVR fusion deltas** — `CvrReportCommand` fuses BOQ total,
  latest cert, VOs and EVM, but does not yet (a) break retention out as its own line,
  (b) track PS movement into the CVR, or (c) read Value off the S-curve — provisions
  remain a manual `CVR_PROVISIONS_UGX` knob. (Fluctuations engine, contractor CVR,
  time-phased EVM BCWS curve and the Dayworks build-up sheet are all done — see the
  PM-3 bullet above.)
- **WP-A — remainder.** Off-thread rate-feed pre-warm on document open; a low-frequency
  scheduled server-baseline drift check (surface `SyncState=Conflict` proactively).
- **WP6 — remainder.** Snapshot one FX rate per build + make currency mandatory on
  every rate source (validate FX presence, no silent default). (USD-from-UGX at
  summary and BCIS-off-sync-path are done.)

## Placement Centre — residual gaps (post review/hardening)

- **BOQ quantity handoff.** Placed-element counts (`PlacementResult.PlacedIds`,
  keyed by provenance) still don't feed `StingTools/BOQ/`. A "Send placed
  quantities to BOQ" action keyed off the provenance stamp would close the
  loop. Deferred from the review (large BOQ API surface; unverifiable here).
- **Drawing-Type preset *apply* during placement.** "Save view preset" writes
  `StingViewPresetSchema` (works), but nothing applies a preset during a run
  and it bypasses `DrawingTypePresentation.Apply`. Either route through the
  Drawing Template Manager or drop the half-feature. Deferred.
- **Wall/ceiling/floor-follow router.** The legacy `WALL_FOLLOW` /
  `CEILING_FOLLOW` / `FLOOR_FOLLOW` rule tokens are normalized to the nearest
  drop engine with an honest warning (Part C.5). A genuine follower router
  (route along wall/ceiling faces, not a drop) is net-new work.
- ~~**Data-driven Auto-place category checklist.**~~ **Closed (Phase 225)** —
  the checklist is generated from `PlacementCategoryRegistry`
  (`STING_CATEGORY_TO_SEED_MAP.json` v2 + the loaded rules). Non-placeable
  categories render disabled with the registry's own reason; ruleless ones
  render muted. `Ducts` and `Cable Trays` added to the map.
- ~~**PlacementRule push-to-family-types fields.**~~ **Closed (Phase 225)** —
  audit found six of the ten fields were already consumed on the routing /
  lighting-grid paths. `Material` / `GlazingSpec` / `ToughenedGlazingRequired` /
  `InsulationThicknessMm` / `NominalDiameterMm` / `MaintenanceClearance` are now
  pushed to family types (writing `STING_MAINT_CLEAR_TXT` activates
  `MaintenanceAccessValidator`, which had no writer), and
  `PlacementAdvisoryValidator` reports fields that cannot take effect on a
  rule's configuration. Two underlying defects fixed: `MaintenanceClearance` was
  typed `double` against a class-code ComboBox so every selection was discarded,
  and the glazing / clearance editors had no commit handler at all.
- ~~**StandardRef ↔ ApplicableStandards.**~~ **Closed (Phase 225)** —
  `ApplicableStandards` backfilled across 128 rules in 8 packs (structured
  coverage 206/408 → 334/408) and matching moved to `StandardsTokenMatcher`,
  which normalises spacing, edition years and `/`-separated citations. The
  warn-when-inert fallback is retained and now also reports how many rules
  bypass the gate untagged.

  ~~Still open in this area: 74 rules carry neither `ApplicableStandards` nor
  `StandardRef`.~~ **Closed (Phase 226)** — by then 117 rules were untagged; all
  are now tagged with the governing standard for their category (coverage
  451/451). Note the behaviour change: those rules used to pass *every* profile,
  so a single-standard profile now sees far fewer rules (`["BS 7671"]`: 175 → 79).
  Profiles should list all applicable standards.

  Phase 226 also cleaned the token vocabulary (155 → 121 distinct, 45 → 0 glued-
  prose tokens) and fixed a `StandardsTokenMatcher` false positive the mangled
  tokens had been masking: any standard numbered 1800–2199 (`BS EN 1838`, the
  `EN 1990`–`1999` Eurocodes) collapsed to its prefix and matched every sibling.

- ~~**Editor cards with no commit handler (beyond glazing / clearance).**~~
  **Closed (Phase 226)** — a systematic audit of the per-rule sync block found 16
  further controls populated from the rule with no write-back (including
  `txtStandardsCsv`, the `ApplicableStandards` editor itself). All wired via
  `CommitField`; only the two intentional read-only displays (`txtSourcePack`,
  `txtRuleError`) remain uncommitted.

- **Placement Centre in-Revit verification.** The Phase 225/226 UI work has never
  been exercised in Revit: no interactive session was available and
  `StingTools.Headless` is a Design Automation library with no local entry point.
  Outstanding: checklist rendering and grouping, disabled/muted states (the
  `ItemsControl` binding + `ToolTipService.ShowOnDisabled` are the highest-risk
  untested pieces — a binding typo renders an empty checklist), All/None, tick
  preservation across a rule reload, the maintenance-clearance and glazing
  round-trips, "Push to Families" writes, and the `MaintenanceAccessValidator`
  type-fallback read path with both instance- and type-scope bindings.

- **Wall/ceiling/floor-follow router (stretch, still open).** `EffectiveRoutingMode`
  still normalises every `WALL_FOLLOW` / `CEILING_FOLLOW` / `FLOOR_FOLLOW` token to
  the nearest drop engine with an honest warning. A genuine face-follower is
  net-new engine work that cannot be verified without a Revit runtime.

## BOQ / Cost — residual gaps (post Stage-B integration)

- **Currency conversion (not just labels).** Stage-B B.1 made currency labels
  consistent (UGX), but there is no FX conversion. Mixing a GBP rate-card entry
  with a UGX BOQ would still sum face values. A single project-currency knob +
  per-provider FX (the `RateProviderRegistry` already converts GBP/USD→UGX for
  rates) extended to certs / variations / EVM is the real fix.
- **NRM1 cost-plan UGX benchmarks.** `STING_NRM1_BENCHMARKS` are genuinely
  £/m² GIFA (UK BCIS-style). For a UGX project the cost plan is shown in GBP
  (honest, via `CostPlanDocument.Currency`) — a UGX benchmark set, or an FX
  projection at plan time, is needed for a UGX-native concept estimate.
- **B.6 — `AssignBoqLineRefs` group index.** Middle index is the literal `1`
  (`{section}.1.{n}`). Cosmetic (refs unique within a section); a per-Category
  group index would change the ref format and risks the write-once
  `ASS_BOQ_LINE_REF` stamp — deferred.
- **B.6 — QS import non-numeric rate warning.** A blank rate is treated as
  "unpriced / no change"; genuinely non-numeric rate text (e.g. "TBC") is
  currently read as 0 silently. Surface it as a diff-preview warning row.
- **EVM BCWS from a real schedule.** BCWS still comes from a QS-entered planned
  %; no 4D / cost-loaded-schedule wiring yet.
- **QS import per-row accept/reject.** The import diff is whole-batch
  Apply/Cancel; per-row checkboxes would let a QS accept a subset.

## StingBridge — remaining gaps (post 0.1.0-beta.2)

Shipped self-serve on planscape.build/downloads. **0.1.0-beta.2 is live** (SB-2, SB-3 and SB-4 closed; PATs added). Remaining gaps, roughly in value order:

| # | Gap | Detail |
|---|---|---|
| SB-1 | **Live-ArchiCAD verification** | Two documented-but-unverified assumptions need one session against real ArchiCAD (AC 28/29): (a) `_ifc_global_id_from_acguid` presumes ArchiCAD derives the IFC-export GlobalId from the JSON-API element GUID — if wrong, the live-sync and IFC-watcher paths mint two mapping rows per element; (b) zone labels read from `Zone_ZoneNumber`/`Zone_ZoneName` built-ins. Both degrade gracefully today. |
| SB-2 | ~~SEQ minting~~ **DONE Phase 202** | New atomic `POST /api/projects/{id}/seq/reserve` (INSERT … ON CONFLICT … RETURNING) plus `StingBridge/sync/seq_minter.py`, which ports `SeqAssigner.BuildSeqKey` exactly so both hosts draw from the same per-key counters. Wired into live sync + the IFC watcher, batched per run, idempotent, degrades to 7-segment rather than failing. Verified collision-free under 8-way concurrency. **Phase 211** closed the two holes the review found: the IFC path never adopted an already-written `ASS_SEQ_NUM_TXT`, so every re-drop re-minted (fixed + covered by a full-pipeline round-trip test), and the Revit plugin gained a server-side block-reservation **mechanism** (`SeqBlockReservation`). **The mechanism is NOT wired** — see SB-2b. **Remaining:** the Revit cross-host duplicate window is **still open in all cases**, not just offline ones (SB-2b). By design, even once wired, an unconfigured or unreachable server falls back to local allocation and the window stays open for that session — refusing to number offline would be worse than numbering optimistically and reconciling on the next `/seq/sync`. Also **not verified in Revit** — the reservation logic is unit-tested and the plugin builds clean, but no in-Revit runtime run was possible. |
| SB-2b | **Revit SEQ reservation is scaffolding only — not wired** | Phase 211 landed the mechanism and its unit tests; nothing calls it, so Revit still allocates purely locally and the online cross-host duplicate window (Revit vs StingBridge minting the same number on the same key) is **OPEN**. Two wiring points: (1) `PlanscapeServerClient.ReserveSeqBlocksAsync` (`StingTools/BIMManager/PlanscapeServerClient.cs:691`) has **zero callers** — a tagging run must pre-compute its per-key counts and reserve one block per key; (2) `TagConfig.BuildAndWriteTag` (`StingTools/Core/TagConfig.cs:2317`) calls `SeqAssigner.AssignNext` without the optional `reservation` argument, so it defaults to `null` → local allocation. Both are single-line-ish changes; the work is not the edit. **Why it waits:** this is the hot path for every tag the plugin writes, and a mistake renumbers a live model. It needs in-Revit runtime verification against a real document — unit tests and a clean compile do not cover the failure modes that matter (transaction scope, partial-run rollback, counter drift after a cancelled command). No Revit runtime is available to the agent that wrote it. Suggest gating behind a config flag on first release so a site can fall back without a redeploy. |
| SB-3 | ~~Token inference single-sourcing~~ **DONE Phase 205** | ArchiCAD vocabulary moved to `stingtools_core/hosts/archicad.py`; the bridge modules are re-export shims. The two level-derivation functions disagreed on 13 of 31 storey names and the ArchiCAD one collapsed every numbered basement to `B1` — now one implementation, union of both, bug fixed. `ArchiCadHostAdapter` implements the HostAdapter contract. **Follow-on:** the IFC watcher still uses its own extraction rather than `IfcFileHostAdapter`; swapping it needs equivalence coverage first. |
| SB-4 | ~~Hot-folder contract mismatch~~ **DONE Phase 204** | The Python watcher now follows the C# `processing/ → done/YYYYMMDD_<name>` \| `failed/` + `.log` contract (`StingBridge/watch/hot_folder.py`), keeping the sidecars and moving the `_sting.ifc` output with the source. Also added a start-up sweep of the inbox and `processing/` orphan recovery, both only safe once processed files leave the root. Failure routing reads `result["errors"]`, not just exceptions — routing on exceptions alone archived unopenable files as successes. |
| SB-5 | **Multi-host Phase B/C** — **tag-slice** engine DONE Phase 207 (corrected Phase 214), wiring open | The change feed (`GET /api/projects/{id}/changes`), `PullClient`/`CursorStore` and the `ReconcileEngine` are landed and verified two-way against real Postgres. Remaining: **SB-5a** wire the IFC-watcher path to pull→reconcile→push (testable today without ArchiCAD — the sensible first cut); **SB-5b** wire the live-ArchiCAD path and delete the 60-second grace heuristic in `sync/engine.py` (needs a licence to exercise the local-index read, so blocked behind SB-1). Also open: §1.4.4 client-side push chunking, §1.4.5 the GlobalId-stability CI fixture, and the Part 2 LoGeoRef coordinate engine. **Scope correction (Phase 214):** what landed is the **TAG slice** of §1.4.1–§1.4.2. The feed carries `kind="tag"` only — **issues / BCF / clash payloads are not implemented** — and **§1.4.3's "surface the loser as a Planscape issue" is not implemented**: conflicts are reported to an `on_conflict` callback that nothing consumes. The seams (`kind` field, callback) exist; the wiring does not. **Two further documented limitations:** rows with a null `LastModifiedUtc` never appear in the feed, so pre-existing elements stay invisible until next edited (a backfill may be needed); and there are **no delete tombstones**, so a deletion in one host never propagates. |
| #338 | **Native-type parameter migration — MEASURED, recommended against (Phase 224)** | Regenerated on main with a compiler in the loop; the mechanical migration was deliberately NOT performed. 157 datatype flips + 175 new `_TXT` mirrors, of which **123 target params are already bound**. Revit will not redefine an existing GUID with a different data type — `LoadSharedParamsCommand` already detects this and skips — so in existing projects the flip binds 175 empty mirrors and migrates nothing, while the C# side writes native doubles into still-TEXT params. No unbind→rebind→repopulate command exists. See [`NATIVE_TYPE_MIGRATION_ANALYSIS.md`](NATIVE_TYPE_MIGRATION_ANALYSIS.md). **Recommended:** adopt native types for NEW params only; keep the `_BOOL`→`YESNO` track (#337/#479); ship a verified binding-migration command before attempting any flip. |
| SB-5 | **Multi-host Phase B/C** — **tag-slice** engine DONE Phase 207 (corrected Phase 214), wiring open | The change feed (`GET /api/projects/{id}/changes`), `PullClient`/`CursorStore` and the `ReconcileEngine` are landed and verified two-way against real Postgres. Remaining: ~~**SB-5a** wire the IFC-watcher path to pull→reconcile→push~~ **DONE Phase 225** — `StingBridge/sync/ifc_reconcile.py` drains the feed from a **per-document** cursor (`<drop-root>/.sting_sync_cursor.json`, survives restarts), reconciles into the extracted token map *before* SEQ minting so a remote SEQ is adopted rather than re-minted, and the reconciled values reach both the write-back and the push. **SB-5b** wire the live-ArchiCAD path and delete the 60-second grace heuristic in `sync/engine.py` (needs a licence to exercise the local-index read, so blocked behind SB-1). Also closed in Phase 225: ~~§1.4.4 client-side push chunking~~ (`sync/push_chunker.py` — configurable chunk size, retry-with-backoff on transient failures, 413 splits the chunk) and ~~§1.4.5 the GlobalId-stability CI fixture~~ (`test_ifc_globalid_stability.py` — same IFC twice ⇒ zero new mapping rows). Still open: the Part 2 LoGeoRef coordinate engine. **Scope correction (Phase 214):** what landed is the **TAG slice** of §1.4.1–§1.4.2. The feed carries `kind="tag"` only — **issues / BCF / clash payloads are not implemented** — and **§1.4.3's "surface the loser as a Planscape issue" is still not implemented**. Phase 225 consumed the `on_conflict` callback on the IFC path — every conflict is now written to a `<name>.conflicts.jsonl` sidecar (one row per differing token: guid, key, local, remote, winner, applied, reason) and logged — so a conflict is no longer *silent*, but no Planscape issue is raised and the live-ArchiCAD path still consumes nothing. The remaining work is server-contract, owned by the server lane. **Two further documented limitations:** rows with a null `LastModifiedUtc` never appear in the feed, so pre-existing elements stay invisible until next edited (a backfill may be needed); and there are **no delete tombstones**, so a deletion in one host never propagates. |
| SB-6 | **macOS notarized binary** | `any` zip covers macOS today; a signed native build is deliberate future work. |
| SB-7 | **Beta feedback loop** | Optional `download_log` table (D1) on the gated endpoint so beta testers can be followed up without the old request-by-email list. |

## Planscape Server — deployment gaps (Phase 200)

The blueprint is validated and the owner package is written; what remains is
owner-side or follow-on work. Status verified 2026-07-20.

| # | Gap | Detail |
|---|---|---|
| DEP-1 | **Server is not deployed** | `api.planscape.build` does not resolve. Owner-only: apply the Render Blueprint, paste secrets, add the custom domain + registrar CNAME. Prep is complete — see [`SERVER_GO_LIVE.md`](SERVER_GO_LIVE.md) and the local package at `C:\Dev\planscape-render-golive\`. |
| DEP-2 | **`PLANSCAPE_HANDOFF_SECRET` unset on Cloudflare** | `wrangler pages secret list --project-name planscape-marketing` returns 12 secrets and this is not one of them, so cloud→server handoff cannot work in production yet. It is a *shared* secret: set the identical value on Render (`planscape-api` **and** `planscape-worker`) and on Cloudflare Pages. Rotate both sides together. |
| DEP-3 | ~~Handoff provisions no Project~~ **DONE Phase 201, hardened Phase 212** | `EnsureStarterProjectAsync` now creates a project + `ProjectMember` when the tenant has none. Idempotent (gate is "zero projects"), best-effort so a failure never costs the session. Phase 212 made "never costs the session" actually true: the catch now detaches the failed `Added` entities, which otherwise poisoned the next `SaveChangesAsync` (the refresh-token one) and turned a provisioning failure into a 500 at login. |
| DEP-7 | ~~Test infra: `Hangfire.JobStorage.Current` is process-global~~ **DONE — recurring jobs moved to injected `IRecurringJobManager`; HTTP test restored** | The blocker: Program.cs's static `RecurringJob.AddOrUpdate` calls read the process-wide static *during host build*, and each `WebApplicationFactory` pointed it at a container-owned storage disposed with the factory — so any host built after a sibling's teardown died with `ObjectDisposedException: Hangfire.InMemory.State.Dispatcher`, and **no test could reliably stand up an extra factory** (Phase 212 had to drop its HTTP-level provisioning-failure test for a SQLite mechanism test; suite-wide serialisation made it worse, 136 failures). Resolved: the recurring-job registrations now go through the injected `IRecurringJobManager` (this host's own storage), so nothing reads the global during build and a second factory is safe. On that foundation the dropped test is **restored** — `HandoffProvisioningFailureHttpTests` proves end-to-end through `POST /api/auth/handoff/exchange` that a starter-project provisioning failure (injected as the real `(TenantId, Code)` `DbUpdateException` via a `SaveChangesInterceptor`) still issues a session and detaches the failed entities. Pinned to a non-parallel collection with its own in-memory factory. |
| DEP-4 | ~~Handoff accounts have no headless credential~~ **DONE Phase 201** | Personal access tokens: `POST/GET/DELETE /api/auth/tokens` + `POST /api/auth/token/exchange`, wired into StingBridge as `STING_PLANSCAPE_TOKEN`. A PAT is exchanged for a normal JWT, never accepted as a bearer token, so the API stays single-scheme. The unusable password hash on handoff accounts remains, by design. |
| DEP-5 | ~~`/api/auth/license/activate` is unrate-limited~~ **DONE — test-health pass** | `[EnableRateLimiting("auth")]` added. The original framing was wrong on one detail: it was not *the only* endpoint without the attribute — ten lacked it. Eight of those are `[Authorize]`d and fall back to the global `"api"` policy, which is defensible; the two anonymous ones were `license/activate` **and `/api/auth/refresh`**, which DEP-5 did not mention. `refresh` is arguably the worse of the pair (unauthenticated token exchange). **Follow-up (review of #460):** putting both on `auth` was too tight — that bucket is a single 5-req/5-min sliding window keyed by IP *only* and shared across every auth endpoint, so behind a shared egress IP an automatic endpoint (`refresh`) or a multi-user one (`license/activate`, called pre-session by the Revit add-in) would 429 legitimate traffic. `refresh` moved to the `api` policy (100/60s; the refresh token is a 128-bit secret, so strict brute-force limiting adds little), and `license/activate` moved to a dedicated per-IP `license` policy (20/5min) so it stays a throttled oracle without starving login. |
| DEP-6 | **Handoff single-use check fails open** | The `jti` replay guard is a Redis `SET … When.NotExists`; when Redis is unavailable the exchange logs a warning and proceeds, so a captured ticket could be replayed within its 120 s TTL during a Redis outage. Acceptable given the TTL, but it is an availability-over-integrity choice worth making deliberately. |
| DEP-6b | **Rate-limiter Production gate is verified by review, not by a test** | PR #439 made `RateLimiting:Enabled=false` inert when `IsProduction()`. F5 got a Production-environment test host building (`UseSetting("environment", "Production")` — `UseEnvironment` after the base factory does not stick) and **confirmed the gate fires**: startup prints `[rate-limit] RateLimiting:Enabled=false IGNORED - the environment is Production and the auth limiter is not optional there.` The remaining leg — asserting an actual **429** — could not be landed: the `auth` policy is a **Redis-backed** sliding window (`RedisRateLimitPartition.GetSlidingWindowRateLimiter`, Program.cs:816), and in the in-process test host it does not trip even with the docker Redis reachable at the default `localhost:6379`. Asserting on a console line is too brittle to keep, so no test shipped. **To close:** either expose an in-memory limiter for the test host behind config, or stand the check up as an out-of-process smoke test against a running container. |
| DEP-6a | ~~No automated test for handoff jti replay~~ **DONE — test-health pass** | `IReplayGuard` seam added (`Core/Interfaces`, Redis impl in `Infrastructure/Services`), faked by `TestReplayGuard` in the factory. Four tests in `HandoffReplayGuardTests` now cover replay-blocked, distinct-tickets-independent, fail-open-when-the-guard-throws, and guard-actually-consulted. Fail-open semantics are unchanged, and the exception filter at the call site stays transport-specific on purpose — widening it to `catch Exception` would silently convert a logic bug in the guard into a fail-open too. The warning emitted on the degraded path is **not** asserted; see DEP-13. |
| DEP-6a | ~~No automated test for handoff jti replay~~ **DONE Phase 226** | `HandoffProvisioningTests.Handoff_SameTicketReplayed_SecondRedemptionIsRejected` redeems one ticket (same jti) twice and asserts the second is `401` and mints no second account. It is a `[SkippableFact]` gated on `PLANSCAPE_TEST_REDIS`, mirroring the `PLANSCAPE_TEST_PG` convention: **Skipped** (never falsely Passed) when no Redis is present, because the guard **fails open** by design and the proof would be vacuous. Rather than the `IReplayGuard` seam, it uses the factory's real `IConnectionMultiplexer` (default `localhost:6379`), so the guard is exercised end-to-end through the HTTP endpoint. A dedicated CI job (`redis-single-use` in `planscape-server.yml`) stands up a `redis:7` service and runs it with the flag set — isolated in its own job so the service does not perturb the `build` job's known-failing baseline. Bite verified: with the flag set but Redis unreachable the test goes red (fail-open lets the replay through), which is exactly the behaviour it guards. The **fail-open on Redis-down** choice (availability over integrity, bounded by the 120 s ticket TTL) is deliberate — see DEP-6. |
| DEP-6b | **Rate-limiter Production gate is verified by review, not by a test** | PR #439 made `RateLimiting:Enabled=false` inert when `IsProduction()`. F5 got a Production-environment test host building (`UseSetting("environment", "Production")` — `UseEnvironment` after the base factory does not stick) and **confirmed the gate fires**: startup prints `[rate-limit] RateLimiting:Enabled=false IGNORED - the environment is Production and the auth limiter is not optional there.` The remaining leg — asserting an actual **429** — could not be landed: the `auth` policy is a **Redis-backed** sliding window (`RedisRateLimitPartition.GetSlidingWindowRateLimiter`, Program.cs:816), and in the in-process test host it does not trip even with the docker Redis reachable at the default `localhost:6379`. Asserting on a console line is too brittle to keep, so no test shipped. **To close:** either expose an in-memory limiter for the test host behind config, or stand the check up as an out-of-process smoke test against a running container. **Phase 223 note:** CI now runs a `redis:7` service, so "no Redis in CI" is no longer the obstacle — but this item is unaffected, because the limiter already failed to trip *with* a reachable Redis. The two remedies above remain the only routes. |
| DEP-6a | **No automated test for handoff jti replay** | The single-use guard is a Redis `SET … When.NotExists` that **fails open** when Redis is down, and the integration-test host registers no Redis — so a replay test would pass or fail depending on whether a docker Redis happened to be running. Needs either a Redis test double registered in `PlanscapeWebApplicationFactory` or an `IReplayGuard` seam that can be faked. Until then the guard is covered by review only (Phase 215). |
| DEP-7 | **`PLANSCAPE_IDENTITY_HANDOFF.md` status line is stale** | It reads "design agreed 2026-07-18, not yet implemented"; the feature is in fact implemented on all three sides (Cloudflare Pages Function, `AuthController`, Next.js `/handoff` page). The doc's own line references still resolve, so only the status line drifted. Its role table also says `project_lead` → `ProjectLead`, but `UserRole` has no such member and the code maps it to `Manager`. |
| DEP-8 | ~~StingBridge token-expiry constant is wrong~~ **DONE Phase 201** | Was 55 min against a 30-min server token, so the proactive refresh only fired ~25 min after expiry. Now 30 min with a 5-min margin, pinned by a regression test. |
| DEP-9 | **No UI for minting access tokens** | The API exists (`POST /api/auth/tokens`) and the guides document the `curl`, but the cloud app has no screen for it. A subscriber currently needs a terminal to get a StingBridge credential. |
| DEP-10 | ~~Integration-test suite has 73 pre-existing failures~~ **73 → 9, test-health pass** | The premise that these were all drifted assertions turned out to be wrong: **five were real production bugs** (see DEP-12 and the CHANGELOG), and one of them — `ProjectVisibility.IsTenantAdmin` reading a claim that JWT inbound mapping had renamed — accounted for ~28 failures on its own. The rest were fixture rot (global tenant query filter, unset `Tenant.Plan`, missing project membership, per-context InMemory store names). The remaining entries are diagnosed and annotated in `known-failing-tests.txt`: originally 9 (3 Postgres-only + 6 confirmed `DeliverableStateMachine` defects deferred to DEP-14). **Follow-up (build review of #475):** the 3 Postgres-only `CoreApiTests` are now `[Fact(Skip=…)]` — they report as Skipped, not Failed, on the always-InMemory test host (they never ran there regardless of `PLANSCAPE_TEST_PG`), so the baseline shrank to the 6 DEP-14 defects. |
| DEP-11 | ~~`Jwt__Key` is an undocumented prerequisite for running the tests~~ **DONE** | The factory already defaulted the key via `UseSetting` (it must not go through `ConfigureAppConfiguration` — Program.cs reads the value while the host is still being built). What was missing was the documentation half: `tests/Planscape.Tests/README.md` now covers it, the env-var override, and every other production/test substitution. |
| DEP-12 | **`FindFirst("role")` is unmapped at ~24 call sites** | The JWT bearer options set no `RoleClaimType` and never disable `MapInboundClaims`, so the minted short `role` claim is rewritten to the `ClaimTypes.Role` URI before any handler sees it. Every site reading only `"role"` gets null. `RequireRoleAttribute` and `TenantContextMiddleware` carry a `?? ClaimTypes.Role` fallback; ~24 others do not — `grep -rn 'FindFirst("role")' src/` lists them. The test-health pass fixed the two that gated failing tests (`ProjectVisibility.IsTenantAdmin`, `DocumentsController.GetUserRole`) with the same fallback. **The remaining sites are still wrong.** Audited as fail-*closed* (an empty role denies; the one `?? "Admin"` default, `ProjectMembersController.cs:608`, is a logging label reached only after the admin check already passed) — so this degrades function rather than opening access, which is why it was not hot-patched across the board. Real fix is central: set `MapInboundClaims = false` **and** `RoleClaimType`/`NameClaimType` on the bearer options, which repairs all sites at once but touches every claim reader in the codebase and needs its own security review. `Auth/PlanscapeJwtHandler.cs:64` already does this and is dead code on the live path. |
| DEP-13 | **Test infra: Serilog's `Log.Logger` is process-global** | Sibling of DEP-7. `UseSerilog` assigns the static logger, so when the suite runs many `WebApplicationFactory` hosts concurrently they race over it and a controller's `ILogger` can resolve against a different host's pipeline. An `ILoggerProvider` attached to your own factory then captures **nothing**: such a test passes in isolation and fails in the full suite. Measured directly while writing DEP-6a — the warning reached Serilog's console sink but zero records reached the provider. Consequence: **log output cannot currently be asserted from an integration test**, which is why `Handoff_ReplayGuardUnavailable_FailsOpen` asserts behaviour only. Real fix is the same shape as DEP-7: stop depending on process-global state during host build (`preserveStaticLogger: true` per host, or a DI-resolved sink). |
| DEP-14 | **`DeliverableStateMachine` role inference has 5 confirmed defects** | Six tests fail because the engine is wrong, not the tests. (1) `"BLOCKED".Contains("LOCKED")` is true and the terminal scan precedes the working scan, so every `BLOCKED*` state is classified terminal. (2) `AcceptKeywords` `"APPROV"` is scanned before `SubmitKeywords` `"FOR_APPROVAL"`, so `ISSUED_FOR_APPROVAL` reads as already approved — note the code comment at the submit scan claims the opposite ordering was intended. (3) The `if (roles.ContainsKey(declared)) continue;` guard in `LoadOrDefault` skips the tenant-keyword layer for any state named in `states[]`/`transitions[]`, so tenant overrides only work for states the schema never mentions — the inverse of the documented contract. (4) `MergeKeywordLayers` merges per role bucket in isolation, so a token claimed by both project and platform layers survives in both and `RolePriority` lets the platform layer win, inverting "project entries win on collision". (5) `hasRolesBlock` is a local in the loader and never persisted, so `RoleOf`'s runtime memoisation falls through to built-in inference for machines that explicitly opted out. Deliberately **not** fixed in the test-health pass: each remedy changes how every tenant's custom vocabulary is classified. Wants its own PR. |

## Sub-system reviews

- [`PLACEMENT_CENTRE_GUIDE.md`](PLACEMENT_CENTRE_GUIDE.md) — plain-English user guide to the Placement Centre: every button, every editor field, background concepts (anchors, regex, mounting reference, provenance, standards), worked walk-throughs, troubleshooting and a cheat-sheet (2026-04-25).
- [`PLACEMENT_CENTRE_REVIEW.md`](PLACEMENT_CENTRE_REVIEW.md) — flexibility / functionality / automation gap audit of the Placement Centre with PC-01..PC-25 backlog and a recommended ≈ 25-category baseline catalogue (2026-04-25).
- [`HEALTHCARE_PACK_DESIGN.md`](HEALTHCARE_PACK_DESIGN.md) — multi-phase design document for the Healthcare / Hospital Design pack covering HTM / HBN / FGI / NFPA 99 / NCRP 147 / ASHRAE 170 / ISO 14644 / USP 797-800 / SFG20-Healthcare integration. Defines ~140 new shared parameters, 60 filters, 16 drawing types, 4 ViewStylePacks, 8 validators, COBie-Healthcare overlay, RDS template engine, MGPS package, radiation calc, adjacency analyser, anti-ligature pack, behavioural-health pack, digital-twin / IoT bridge, mobile commissioning app and server APIs. Phased H-1..H-22 with file-by-file integration map (2026-05-08).

## How to use this file

- Items are grouped by the review that surfaced them (Phase 74 5-agent review, Phase 76 DWG review, Phase 77 review, Phase 78 triage, etc.). The grouping is preserved so you can trace each gap back to its origin.
- Items marked `~~strikethrough~~` with `**DONE**` are completed — they stay here as a record of what the review covered. When closing a new item, either strike it through in place or move it to `CHANGELOG.md` under the appropriate phase.
- When adding a new gap, either extend an existing section's table or add a new `### Future Enhancement Gaps — <topic> (Phase N Review)` section at the end.

## Branching + merge discipline (adopted 2026-07-24)

Adopted after `claude/iso19650-consolidation` (PR #453) — a 137-file, ~55-commit
long-lived branch — went green, dirty, re-synced and re-reviewed for roughly 14
rounds before it could land. Every round cost a full re-verification, and none of
it was caused by the work itself: a large branch simply cannot stay in sync with a
`main` that several sessions push to daily. The branch landed on 2026-07-24 and was
deleted the same day. The rules below exist so nothing like it is built again.

- **No long-lived stacks.** Each item is a short-lived PR branched fresh off `main`
  and merged within about a day. Remaining ISO IM work follows this: Phase 4
  (meetings round-trip), IM-1 / IM-3, IM-11 / IM-12 (server warnings trend),
  and any defect the tester finds in a deployed build.
- **Every session gets its own git worktree.** Never work directly in the shared
  `C:\Dev\STINGTOOLS` checkout — several sessions have it open at once, so an edit,
  a branch switch or a `reset --hard` there lands under another session's feet.
  Branch off `main` into a fresh worktree and remove it once the PR is merged.
- **One session per branch.** Never point two sessions at the same branch. During
  the #453 landing, three separate pushes arrived from another session mid-merge,
  each re-triggering CI and invalidating the head that had just been verified.
  That is the mechanism behind most of the churn, not the code.
- **Runtime verification is post-deploy.** The owner ships a compiled build to a
  tester. Findings come back as small fix-PRs off `main` — they do not gate work
  that has already merged on green CI.
- **A branch that has landed gets deleted.** History is preserved in `main`; a
  surviving branch is only a re-conflict magnet.

---

## Unreviewed branches — triage of 2026-08-22

40 remote branches had **never had a pull request opened on them**. Not drafts in
review — nobody was looking at them at all. Re-measured with `git cherry
origin/main origin/<branch>`, which is the only honest measure: a "3 commits
ahead" count lies across a rebase, and one branch in this pool
(`claude/document-manager-iso-review-dbb595`) reports `+2` while `main` already
holds both its files verbatim plus a superseded banner.

Result: **17 carried no unique patch at all**, 6 were opened as PRs
(#755-#759, #761), 3 are landable but were left unopened against a self-imposed
6-PR cap, 2 need a human decision, and the 12 below are **rotted** — real work
that no longer applies. Each row is a resting place, not a plan. The branch still
exists; nothing here has been deleted.

**Reviving any of these means re-implementing against today's `main`, not
rebasing.** The conflict counts are from `git merge-tree` against `main` at
`10c9d45ed`.

| Branch (tip) | Unique / behind | What it was trying to do | Why it cannot land as-is |
|---|---|---|---|
| `claude/tb-w1w5-fold` (`557bd2d04`) | 33 / 597 | Title-block workstream W1-W5, folded: cover-A1 spec rebuilt to v8 layout, 10 new `PRJ_*` params bound, `category_enum_map` fix | 15 conflicts centred on `ParamRegistry.cs`, `MR_PARAMETERS.txt/.csv` and `PARAMETER_REGISTRY.json`. The parameter registry has moved substantially since. Its "Param rename A" was back-ported to `main` separately in PR #565, so the branch is *partly* already landed — which is exactly what makes a blind rebase dangerous. |
| `claude/tb-w1w5-impl` (`1468c0b30`) | 30 / 620 | The same title-block work, before the fold | **Subsumed by `tb-w1w5-fold`**, which merged it. Triage this one only through the fold; landing both would double-apply. |
| `claude/render-deploy-merge-504` (`64acdc56f`) | 19 / 379 | Render deploy hardening: a **health check pointed at an endpoint that 403s in production**, `PLANSCAPE_HANDOFF_SECRET` is per-service not env-group, plus a real-PostgreSQL test harness with 0 skips (DEP-7) | 12 conflicts in `Program.cs`, `PlanscapeDbContext.cs`, `PlanscapeWebApplicationFactory.cs`. **The production health-check fix and the Postgres harness are worth re-doing on their own merits** — they are the highest-value single items in this whole rotted set. Treat this row as a specification, not a branch to rescue. |
| `claude/symbol-sld-only` (`db68f8cea`) | 18 / 906 | Symbol-library and SLD hardening F4-F9: traceable filled-region fallback, orientation-variant audit, guard against switching to an unbuilt library, DWG-to-MEP stage 2 | 6 conflicts across `SymbolConceptRegistry`, `SymbolDefinition`, `SymbolLibraryCreator`, `SymbolOverlayManager`, plus `WorkflowEngine` and `StingCommandHandler`. 906 commits behind; the symbol engine has been reworked underneath it. |
| `claude/boq-accuracy-hardening` (`80be9d916`) | 16 / 1283 | Ten BOQ production-review accuracy findings; also removes 137 stale pre-Phase-188 tag-family seeds | 15 conflicts in every core BOQ file (`BOQCostManager`, `BOQModels`, `BOQExportCommand`, `BOQProfessionalExportCommand`, add/add on `BoqTotals`/`BoqUnits`). 62 days old, 1,283 behind, and PR #761 rewrites parts of the same surface. |
| `claude/boq-p4-cost-control` (`0570d989d`) | 2 / 1164 | BOQ P4 — valuations, variations and EVM usable end-to-end; revive dead Cost Manager buttons | 6 conflicts including add/add on `CostControlCommands.cs` and `StarRateBuilderDialog.cs`. Touches 216 files. Same era and same rot as the row above. |
| `claude/bonsai-installable` (`4c75781bd`) | 5 / 1367 | Make StingTools-for-Bonsai a real installable Blender extension — vendor core + substrate into the `.zip`, correct the archive layout | 2 conflicts, but in `stingtools-bonsai/__init__.py` and the vendor README, and **1,367 commits / 72 days** behind. The Bonsai tree has been through the MBALWA compliance work since (PR #639). |
| `eager-dirac-check` (`cb1e7bec5`) | 4 / 279 | Expand the Bonsai extension 16 → 29 operators; repair silent pset writes; restore the adapter status API | 2 conflicts, one an **add/add on `stingtools-bonsai/ops/spatial_ops.py`** — the same file was created independently on `main`, so this is divergence, not drift. |
| `claude/m-pass-deploy` (`2ebb10fcc`) | 3 / 308 | Write the Planscape project link where the readers actually read it (#570, #571); refuse to launch Revit against an unidentifiable plugin | 9 conflicts across the whole `SitePhotos*` UI surface. Overlaps `claude/sitephotos-d1-guards` (it merged it). Its Revit-launcher half already landed — `tools/Start-RevitLocal.ps1` is on `main` in a **later, larger** form (177 lines vs this branch's 106, with `-Prod`, `-Force` and a running-Revit guard). |
| `claude/sitephotos-d1-guards` (`736e3e334`) | 1 / 309 | Un-fake the site-photo suite: real HTTP, and failures that look like failures | 6 conflicts, the same `SitePhotos*` surface. Subsumed by `m-pass-deploy` above; triage the two together or not at all. |
| `claude/repo-review-hardening` (`994df8079`) | 3 / 1528 | Repo review A1-A3: `StingLog` midnight-rotation fix, 5 param-GUID conflicts in `PARAMETER_REGISTRY.json`, tenant-isolation pass with `ITenantScoped` + migration + backfill | Only 1 conflict (`REVIEW_LOG.md`, add/add) — but **1,528 commits behind**, the oldest in the pool at 77 days, and it carries an **EF migration and a data backfill**. A migration authored against a 1,528-commit-old schema is not something to rebase; see also the standing rule that this repo's migrations are largely inert (ADR 0001). |
| `claude/document-manager-iso-review-dbb595` (`711baa905`) | 2 / 776 | ISO 19650 document-manager + folder-structure review, plus a fix-agent prompt | **Not rotted — superseded.** `main` contains both files byte-for-byte plus a `⛔ SUPERSEDED (2026-08-05)` banner. `git cherry` says `+2` only because the banner changed the patch. Safe to delete. |

### Needs a human decision (not triaged here)

| Branch (tip) | Unique / behind | Why it is not a bucket |
|---|---|---|
| `claude/kibale-np-bim-modeling-f5e653` (`ade468cfa`) | 141 / 193 | The single largest pool of unreviewed work in the repo: 141 commits, 580 files, 8 days old. It merges with only **2 conflicts** (`StingHvacPanel.xaml`, `docs/INDEX.md`), so it is not rotted — it is simply too large to land on an agent's judgement. It also holds `GUIDES/STINGTOOLS_GAPS_KIBALE_REVIEW.md`, the review document that PR #761 implements items 1-4 of, so **that PR is unreviewable from `main` alone** until this is dealt with. Deciding what to do with it is the highest-value branch decision available. |
| `claude/kut-lifecycle-integration` (`ce5c98553`) | 42 / 1265 | Phase 199b-f: a full OmniClass table registry, Table 23 + Table 41 maps researched from source, an in-Revit OmniClass selector, optional MasterFormat stamped on tags. 26 conflicts, 223 files, 61 days. The question is strategic — *is owner-facing classification still wanted?* — and no conflict count answers it. |

### Landable, left unopened against the 6-PR cap

Each has exactly one conflicting file. Open these next, in this order:

| Branch (tip) | Unique | Conflict | Note |
|---|---|---|---|
| `claude/distgroups-server-canonical` (`5778f596d`) | 1 | `StingTools.csproj` (comment block + `NoWarn` list) | Highest value per line in the pool. It **removes `CS0472` from `NoWarn`** because the suppression was hiding exactly one real bug — `SitePhotosAdminSubTab.cs:171`, where a failed distribution-group create could never reach its error branch — and un-suppressing it is what mechanically stops the bug class returning. 2 files. |
| `p517-resolve` (`a645403e6`) | 4 | `SitePhotosAdminSubTab.cs` | BCC correctness: assign issues to real people rather than job titles, address transmittals from the project roster, resolve a distribution group by id not name, null-means-failed throughout. |
| `claude/export-center-layout-issues-557d8e` (`4c1883467`) | 1 | `StingExportCenterDialog.cs` | Export Centre imports Revit's own layouts for `AllInOneMultiLayout`. 4 files. |

## SMK-3 follow-on — `readOnly` proves less than it reads (2026-08-22)

| ID | Item | Detail |
|---|---|---|
| SMK-3a | **`"readOnly": true` means "no Revit model write", not "no side effects"** | `check_smoke_test.py` proves the claim by checking every step's command carries `[Transaction(TransactionMode.ReadOnly)]`. That says nothing about the file system. Four presets have every step resolving to `ReadOnly` **and still write to disk**: `WORKFLOW_HTM-01-06-EndoReprocess` and `WORKFLOW_HTM-04-01-Annual` (both run `Healthcare_BatchRDS` → `RdsRenderer.Render`, which writes `.docx`), `WORKFLOW_HealthcareCommissioning` (same, plus `COBieExport`), and `WORKFLOW_MgasVerification` (`MgasVerifyCommand` → `MgasVerificationLog.Persist` → `File.WriteAllText`). They were deliberately **not** declared, because a user reading `readOnly` will not read it as "writes documents to your project folder". Either widen the gate to prove file I/O too, or rename the field to something as narrow as what it checks (`noModelWrite`). Until then, declaring a preset that renders documents would be technically true and practically misleading. |
| SMK-3b | **A step whose command cannot be resolved silently passes the read-only check** | `check_readonly_claims` skips a step when `tag_to_class` has no entry (`if mode and mode != "ReadOnly"`). `WORKFLOW_DailyFieldWalk` has **7 of 7 steps unresolvable** (`OpenSitePhotos`, `RefreshPhotos`, `PhotoChecklistAudit`, `BulkApprovePending`, `DigestPreview`, `PushDeliverableRegister`, `WorkflowComplete` — none is a `WorkflowEngine.ResolveCommand` case label). If that preset ever declared `readOnly`, CI would pass it while proving nothing, including for the step named *BulkApprove*. The gate should treat "declared read-only but a step is unresolvable" as a failure, not a pass — an unknown is not a clean bill of health. |

---

### MEP-from-DWG — V2 / V3 backlog

V1 (MEP fixtures from DWG blocks) shipped — see `CHANGELOG.md`. Remaining:

| Id | Item | Notes |
|---|---|---|
| ~~MEPDWG-V2-1~~ | ~~**Straight runs** — line → Duct/Pipe/Conduit/CableTray; size from layer suffix or default.~~ | **DONE (V2)** — `MepRunBuilder` + `MepRunClassifier`. |
| ~~MEPDWG-V2-2~~ | ~~**System assignment** + run elevation from level + per-layer offset.~~ | **DONE (V2)** — system TYPE at `Create`; per-kind / per-layer offset. |
| ~~MEPDWG-V2-3~~ | ~~**Fixture host-snapping** — nearest wall/ceiling; fall back to unhosted.~~ | **DONE (V2)** — best-effort, hosted-family only. |
| ~~MEPDWG-V2-4~~ | ~~**`MepCadWizard`** — per-layer mapping UI.~~ | **DONE (V2)** — per-layer include / run-kind / level / offset. |
| MEPDWG-V2-5 | **Native mounting-height param** — wire the numeric `MNT_HGT_MM` stamp once its exact name + unit (Length vs Number) is confirmed against Placement-Center output (height is encoded in the instance Z; only `MOUNTING_REFERENCE_TXT` stamped). | `TODO-VERIFY-API` in `MepFixtureBuilder.StampMetadata`. |
| MEPDWG-V2-type | **Per-layer family/run TYPE selection** in the wizard — V2 uses the first available type per category/kind. Add a per-layer type combo (enumerate types per category/kind) threaded into the builders. | |
| MEPDWG-V2-host2 | **Ceiling face-hosting fidelity** — V2 uses the host-element overload; face-based (WorkPlaneBased) families may fall back to unhosted. Use a real face `Reference` for true face hosting. | `TODO-VERIFY-API` in `MepFixtureBuilder.PlaceWithHost`. |
| ~~MEPDWG-V3-1~~ | ~~**Fittings** — junction detection → elbows/tees/crosses.~~ | **DONE (V3)** — `MepFittingBuilder`, best-effort + guarded. |
| ~~MEPDWG-V3-2~~ | ~~**Risers** — UP/DN/RISER blocks → vertical run segments.~~ | **DONE (V3)** — span to adjacent level / ±3 m. |
| ~~MEPDWG-V3-3~~ | ~~**Drainage slope** — sanitary/drainage pipe fall.~~ | **DONE (V3)** — End dropped by length × slope (1:80 default). |
| MEPDWG-V3-fit2 | **Fitting robustness** — `New*Fitting` needs the type's routing preferences to carry a fitting family and matching size/system; mismatches fall to the guarded skip. Pre-flight routing prefs + a transition fitting on size change would raise the hit rate. | `TODO-VERIFY-API` in `MepFittingBuilder`. |
| MEPDWG-V3-flow | **Drainage flow direction** — V3 drops the line's End end deterministically (no flow data from a 2D plan). Infer direction from connected stacks / gullies, or expose a per-run flip. | |
| MEPDWG-V3-riser2 | **Riser connection** — risers are created as standalone vertical segments; auto-connect them to the horizontal run at the same XY (the fitting pass joins coincident ends but the riser base may sit at a different Z than the run). | |
| MEPDWG-V1-note | V1 only captures fixture blocks whose layer/block name is recognised as MEP (the extraction whitelist). Fixtures on mislabelled layers with non-MEP block names are not captured — extend the layer mapper or rename layers. | Documented V1 limitation. |

---

### Current Automation Gaps

#### A. Gaps That Hinder Full Automation

| Gap | Location | Problem | Impact |
|-----|----------|---------|--------|
| ~~**No tag collision detection**~~ | `TagConfig.cs` | **DONE** — `BuildAndWriteTag` accepts `existingTags` HashSet for O(1) collision detection; auto-increments SEQ on duplicate. `BuildExistingTagIndex()` builds the index once per batch. All callers updated. | Done |
| ~~**No progress reporting**~~ | `BatchTagCommand`, `MasterSetupCommand` | **DONE** — BatchTag shows element count upfront, logs every 500 elements, reports duration. MasterSetup reports per-step timing. | Done |
| ~~**No cancellation support**~~ | All batch commands | **DONE** — `StingProgressDialog` provides modeless progress window with Cancel button and Escape key detection. `EscapeChecker` utility for Win32 key state. `WorkflowEngine` checks cancellation between steps. | Done |
| ~~**Hardcoded category bindings**~~ | `SharedParamGuids.cs`, `ParamRegistry.cs` | **DONE** — Discipline bindings derived from `PARAMETER_REGISTRY.json` container_groups (data-driven). `CATEGORY_BINDINGS.csv` loaded by `TemplateManager.LoadCategoryBindings()` and used by `LoadSharedParamsCommand` Pass 2 to augment JSON bindings. `FAMILY_PARAMETER_BINDINGS.csv` loaded by `BatchAddFamilyParamsCommand`. | Done |
| ~~**No error recovery**~~ | `MasterSetupCommand.cs` | **DONE** — Wrapped in `TransactionGroup` for atomic rollback. If critical step 1 (Load Params) fails, user can rollback immediately. Per-step timing reported. | Done |
| ~~**Fixed tag format**~~ | `ParamRegistry.cs`, `TagConfig.cs` | **DONE** — Tag format (separator, num_pad, segment_order) loaded from `PARAMETER_REGISTRY.json`, with project-level overrides via `project_config.json` TAG_FORMAT section. `ConfigEditorCommand` displays and saves tag format settings. | Done |
| ~~**Partially unused data files**~~ | `Data/` directory | **DONE** — All data files now loaded: CATEGORY_BINDINGS.csv (LoadSharedParams Pass 2), FAMILY_PARAMETER_BINDINGS.csv (BatchAddFamilyParams), MATERIAL_SCHEMA.json (SchemaValidate), BINDING_COVERAGE_MATRIX.csv (DynamicBindings), VALIDAT_BIM_TEMPLATE.py (ported to ValidateTemplate). | Done |

#### B. Enhancement Opportunities

| Enhancement | Why Needed | Status |
|-------------|-----------|--------|
| ~~Pre-tagging audit~~ | **DONE** — `PreTagAuditCommand` performs complete dry-run predicting tags, collisions, ISO violations, spatial detection, and family PROD codes. Exports CSV. | Done |
| ~~Tag collision auto-fix~~ | **DONE** — `BuildAndWriteTag` auto-increments SEQ on collision. User can choose Skip/Overwrite/AutoIncrement via `TagCollisionMode` enum. | Done |
| ~~LOC/ZONE auto-detection~~ | **DONE** — `SpatialAutoDetect` class auto-derives LOC and ZONE from room data and project info. Integrated into TagAndCombine, AutoPopulate, TagNewOnly, FamilyStagePopulate. | Done |
| ~~Family-aware PROD codes~~ | **DONE** — `TagConfig.GetFamilyAwareProdCode()` inspects family name for 35+ specific PROD codes (Mechanical, Electrical, Lighting, Plumbing, Fire Alarm). | Done |
| ~~TagAndCombine writes only 6 containers~~ | **DONE** — Now writes ALL 182 containers (6 universal + 30 discipline-specific). | Done |
| ~~No incremental tagging~~ | **DONE** — `TagNewOnlyCommand` pre-filters to untagged elements. Much faster for adding new elements. | Done |
| ~~CompoundTypeCreator material properties~~ | **DONE** — Applies color, transparency, smoothness, shininess from CSV. | Done |
| ~~**No template automation**~~ | **DONE** — `TemplateManagerCommands.cs` with 17 commands and `TemplateManager` intelligence engine: 5-layer auto-assignment, compliance scoring, VG diff, style definitions. `ViewTemplatesCommand` expanded to 23 template definitions with VG configuration. | Done |
| ~~**No dockable panel UI**~~ | **DONE** — WPF dockable panel (`UI/` directory, 6 files) with 7-tab interface (SELECT/ORGANISE/DOCS/TEMP/CREATE/VIEW/MODEL), `IExternalEventHandler` dispatch for thread safety, ~521 buttons, colour swatches, bulk parameter controls. | Done |
| ~~Cross-parameter validation~~ | **DONE** — `ISO19650Validator` validates all tokens, cross-validates DISC/SYS against category, validates tag format. `FixDuplicateTagsCommand` auto-resolves duplicates. | Done |
| ~~Formula evaluation engine~~ | **DONE** — `FormulaEvaluatorCommand` + `FormulaEngine` reads 199 formulas from CSV, evaluates in dependency order (levels 0-6), supports arithmetic, conditionals, string concat, and Revit geometry inputs. | Done |
| ~~Family-stage pre-population~~ | **DONE** — `FamilyStagePopulateCommand` pre-populates all 7 tokens before tagging (DISC/LOC/ZONE/LVL/SYS/FUNC/PROD). | Done |
| ~~Leader management commands~~ | **DONE** — 14 leader management commands: Toggle/Add/Remove Leaders, Align Tags, Reset Positions, Toggle Orientation, Snap Elbows, Auto-Align Leader Text, Flip Tags, Align Text, Pin/Unpin, Nudge, Attach/Free, Select by Leader. | Done |
| ~~Tag register export~~ | **DONE** — `TagRegisterExportCommand` exports comprehensive 40+ column asset register (tags, identity, spatial, MEP, cost, validation) to CSV. | Done |
| ~~Full auto-populate pipeline~~ | **DONE** — `FullAutoPopulateCommand` runs Tokens, Dimensions, MEP, Formulas, Tags, Combine, Grid in one click with zero manual input. | Done |
| ~~Native parameter mapping~~ | **DONE** — `NativeParamMapper` maps 30+ Revit built-in parameters to STING shared parameters. | Done |
| ~~Document automation~~ | **DONE** — `DeleteUnusedViewsCommand`, `SheetNamingCheckCommand`, `AutoNumberSheetsCommand` for view cleanup and ISO 19650 sheet compliance. | Done |
| ~~Schedule field remapping~~ | **DONE** — `ScheduleHelper.LoadFieldRemaps()` loads SCHEDULE_FIELD_REMAP.csv; `BatchSchedulesCommand` auto-remaps deprecated field names. | Done |
| ~~Port VALIDAT_BIM_TEMPLATE.py (45 checks) to C# ValidateTemplateCommand~~ | **DONE** — `ValidateTemplateCommand` in `DataPipelineCommands.cs` performs 45 validation checks (data file inventory, parameter consistency, material completeness, formula dependencies, schedule definitions, cross-references). | Done |
| ~~Dynamic category bindings from BINDING_COVERAGE_MATRIX.csv~~ | **DONE** — `DynamicBindingsCommand` in `DataPipelineCommands.cs` loads bindings from CSV, replacing hardcoded `SharedParamGuids.AllCategoryEnums`. | Done |
| ~~Color By Parameter system~~ | **DONE** — `ColorCommands.cs` with 5 commands: ColorByParameter (10 palettes, `<No Value>` detection), ClearOverrides, SavePreset, LoadPreset, CreateFiltersFromColors. Full `OverrideGraphicSettings` support. | Done |
| ~~Smart Tag Placement~~ | **DONE** — `SmartTagPlacementCommand.cs` with 9 commands: SmartPlace (8-position collision avoidance), Arrange, RemoveAnnotation, BatchPlace, LearnPlacement, ApplyTemplate, OverlapAnalysis, BatchTextSize, SetCategoryLineWeight. `TagPlacementEngine` with scale-aware offsets and 2D AABB collision detection. | Done |
| ~~View automation commands~~ | **DONE** — `ViewAutomationCommands.cs` with 6 commands: DuplicateView, BatchRename, CopyViewSettings, AutoPlaceViewports, CropToContent, BatchAlignViewports. | Done |
| ~~Annotation color management~~ | **DONE** — 5 commands in `TagOperationCommands.cs`: ColorTagsByDiscipline, SetTagTextColor, SetLeaderColor, SplitTagLeaderColor, ClearAnnotationColors. | Done |
| ~~Schema validation~~ | **DONE** — `SchemaValidateCommand` validates BLE/MEP CSV columns match MATERIAL_SCHEMA.json (77-column schema). | Done |
| ~~Schedule management system~~ | **DONE** — `ScheduleEnhancementCommands.cs` (1,579 lines) with 9 commands: Audit, Compare, Duplicate, Refresh, FieldManager, Color, Stats, Delete, Report. Plus ScheduleAutoFit, MatchWidest (functional), ToggleHidden inline operations. `ScheduleAuditHelper` engine loads CSV definitions for cross-reference. | Done |
| ~~Configurable tag format in project_config.json (separator, padding, segments)~~ | **DONE** — TAG_FORMAT section in project_config.json with `ParamRegistry.ApplyTagFormatOverrides()`. ConfigEditorCommand displays and saves tag format. | Done |
| ~~Batch command chaining / workflow presets~~ | **DONE** — `WorkflowEngine` with JSON presets, 3 built-in workflows, cancellation, TransactionGroup rollback | Done |
| ~~Cancellation support~~ | **DONE** — `StingProgressDialog` + `EscapeChecker` for batch operations | Done |
| ~~Real-time auto-tagging~~ | **DONE** — `StingAutoTagger` IUpdater for zero-touch tagging on element placement | Done |
| ~~Live compliance dashboard~~ | **DONE** — `ComplianceScan` cached RAG status for status bar | Done |
| ~~IFC/BEP/Clash pipeline~~ | **DONE** — 6 new DataPipeline commands (IFC, BEP, clash, Excel import, keynote sync) | Done |

---

### Remaining Underutilized Data Files

| File | Rows | Current Status | Proposed Usage |
|------|------|----------------|----------------|
| ~~`MATERIAL_SCHEMA.json`~~ | 77 cols | **DONE** — loaded by `SchemaValidateCommand` | Validates BLE/MEP CSV columns match schema |
| ~~`BINDING_COVERAGE_MATRIX.csv`~~ | Large | **DONE** — loaded by `DynamicBindingsCommand` | Replaces hardcoded category bindings |
| ~~`CATEGORY_BINDINGS.csv`~~ | 10,661 | **DONE** — loaded by `TemplateManager.LoadCategoryBindings()`, used in `LoadSharedParamsCommand` Pass 2 | Augments JSON-derived discipline bindings with CSV-based category mappings |
| ~~`FAMILY_PARAMETER_BINDINGS.csv`~~ | 4,686 | **DONE** — loaded by `TemplateManager.LoadFamilyParameterBindings()`, used in `BatchAddFamilyParamsCommand` | Data-driven family parameter binding with GUID validation |
| ~~`VALIDAT_BIM_TEMPLATE.py`~~ | 45 checks | **DONE** — ported to C# `ValidateTemplateCommand` | 45 validation checks now in `DataPipelineCommands.cs` |

---

### Implementation Priority Matrix

#### Known Gaps — Tagging Pipeline Deep Review (Phase 34)

Critical review of the tagging workflow identified the following logic, automation, and flexibility gaps across tagging, BIM/BEP/COBie systems:

**Critical Priority:**

| ID | Gap | Location | Status |
|----|-----|----------|--------|
| GAP-001 | WriteContainers in RunFullPipeline | `ParameterHelpers.cs` | **DONE** — `WriteContainers` retry call added at lines 2804-2811 after `BuildAndWriteTag`, ensuring all 182 containers are written even if `BuildAndWriteTag` only writes TAG1. |
| GAP-008 | PreTagAudit token validation | `PreTagAuditCommand.cs` | **DONE** — Phase 36: Predicted token values (DISC/SYS/FUNC/PROD/LOC/ZONE) now validated against `ISO19650Validator.ValidateToken()` code lists before tag simulation. Invalid codes reported as `ISO_PREDICTED_TOKEN` audit issues with grouped counts in report. |
| ERR-002 | Read-only parameter binding | `ParameterHelpers.cs` | **DONE** — Phase 35: `SetString` logs first 5 + every 100th read-only skip with `_readOnlySkipCount` throttle. `ResetReadOnlySkipCount()` for batch operation boundaries. |

**High Priority:**

| ID | Gap | Location | Status |
|----|-----|----------|--------|
| GAP-002 | TOKEN_LOCK_TXT timing | `ParameterHelpers.cs` | **DONE** — Lock snapshot taken after `TypeTokenInherit` (line 2665), restore runs AFTER both `CategoryForceSys` and `CategoryTokenOverrides` (line 2727-2748). Locked tokens are correctly restored after all overrides. |
| GAP-006 | Formula context timing | `FormulaEvaluatorCommand.cs` | **By design** — Formulas intentionally evaluate post-population state so they can reference derived token values (DISC, SYS, etc.). NativeParamMapper runs before formulas, providing Revit native values. This is the correct order: raw data → token population → native mapping → formula evaluation. |
| ERR-003 | Collision detection atomicity | `TagConfig.cs` | **Accepted risk** — In worksharing environments, tag index is built at batch start. Collision detection is best-effort; Revit's own worksharing conflict resolution handles multi-user scenarios. Adding distributed locking would require a central server, which is outside the plugin's scope. |

**Medium Priority:**

| ID | Gap | Location | Status |
|----|-----|----------|--------|
| FLEX-001 | No custom token validators | `TagConfig.cs` | **DONE** — Phase 35: `CUSTOM_VALID_DISC/SYS/FUNC/LOC/ZONE` arrays in `project_config.json` merged with built-in ISO 19650 code lists. |
| FLEX-003 | No post-population hooks | `ParameterHelpers.cs` | **Mitigated** — `CATEGORY_TOKEN_OVERRIDES` in `project_config.json` provides per-category token overrides without source code changes. `CATEGORY_FORCE_SYS` provides SYS overrides. For PROD rules, `GetFamilyAwareProdCode()` handles 35+ family-name patterns. |
| FLEX-005 | SEQ counter isolation | `TagConfig.cs` | **DONE** — Phase 35: `BuildAndWriteTag` tracks `preIncrementValue` and rolls back on TAG1 write failure. Sidecar persistence only saves after successful commit. |
| HC-001 | Hardcoded 10 ft proximity | `ParameterHelpers.cs` | **DONE** — Phase 35: `TagConfig.ProximityRadiusFt` configurable via `PROXIMITY_RADIUS_FT` config key. |
| HC-003 | Hardcoded 500-element batch | `ResolveAllIssuesCommand.cs` | **DONE** — Phase 35: `TagConfig.ResolveBatchSize` configurable via `RESOLVE_BATCH_SIZE` config key. |

### Future Enhancement Gaps (Phase 74 Deep Review — 5-Agent Analysis)

**Model Tab (Agent 1 — 18 gaps):** Missing auto-tagging after model creation (INT-01), DWG layer-to-parameter mapping (CAD-01), geometric cleanup after DWG import (CAD-02), regional LCA factors (CONFIG-01), custom fitting loss database from JSON (CONFIG-02), one-way shear check (PUNCH-01), wind height profile (WIND-01), Voronoi edge case guard (EDGE-01).

**Tagging/BIM (Agent 2 — 47 gaps):** Config key preservation on LoadDefaults (CONFIG-01), ReadOnlySkipCount auto-reset (CONFIG-02), DocumentManager tab persistence to disk (CONFIG-03), ComplianceScan concurrent -1 sentinel check (CRASH-01), PopulationContext ActiveView validity (CRASH-02), ProjectTeamRegistry graceful degradation (CRASH-03), sidecar directory creation guard (CRASH-04).

**Workflows/Coordination (Agent 3 — 29 gaps implemented in Phase 75):** All 29 remaining gaps from Agent 3 have been implemented. See Phase 75 above.

**Docs/Schedules (Agent 4 — 11 gaps):** ViewScheduleLinkEngine missing (DOC-01), schedule template library (DOC-02), document package only 2 of 8 deliverables (DOC-03), PrintQueue O(n²) performance (DOC-04), COBie export only 7 of 11 sheets (HO-01), document versioning/supersession (DOC-06).

**UI/Dispatch (Agent 5 — 6 gaps):** 4 missing command classes (BimKnowledgeBase, CommandSuggestion, ConfigurableTagFormat, CommissioningChecklist), 5 TagStudio stubs with misleading names, 170 dispatch-only entries undocumented.

### Future Enhancement Gaps — DWG-to-Structural Auto-Modeling (Phase 76 Review)

| ID | Gap | Priority | Description |
|----|-----|----------|-------------|
| DWG-FUT-01 | Structural detail reading | High | Read reinforcement schedules, bar marks, curtain lengths from DWG text/tables and populate Revit rebar parameters |
| DWG-FUT-02 | Multi-storey propagation | High | Detect repeating floor patterns and auto-replicate structural layout to upper levels with column continuity |
| DWG-FUT-03 | Section drawing interpretation | Medium | Parse DWG sections/elevations to extract beam depth, slab edge detail, and connection types |
| DWG-FUT-04 | Block-to-family mapping | Medium | Map DWG blocks to Revit families (door/window/equipment blocks → family instances) with attribute transfer |
| DWG-FUT-05 | Hatch-to-material mapping | Medium | Interpret DWG hatch patterns to assign materials (45° hatch → concrete, cross-hatch → masonry, etc.) |
| DWG-FUT-06 | Dimension text extraction | Medium | Read dimension strings near elements to override auto-detected sizes (e.g., "300x600" near a beam) |
| DWG-FUT-07 | Transfer beam schedule | Medium | Parse tabulated beam schedules from DWG (beam mark, size, span, reinforcement) and apply to created beams |
| DWG-FUT-08 | Curved wall support | Low | Detect arc segments in wall layers and create curved Revit walls |
| DWG-FUT-09 | Opening detection | Low | Detect gaps in wall lines as door/window openings and place appropriate family instances |
| DWG-FUT-10 | Retaining wall detection | Low | Identify retaining walls from ground level context and apply appropriate structural properties |
| DWG-FUT-11 | Connection detail extraction | Low | Read structural connection details (base plates, splice connections) and create corresponding elements |
| DWG-FUT-12 | Point cloud integration | Future | Combine DWG structural layout with point cloud scan for as-built verification |
| DWG-FUT-13 | ML-based element recognition | Future | Train element classifier on DWG geometry patterns for improved auto-detection accuracy |
| DWG-FUT-14 | IFC structural import | Future | Import IFC structural models as alternative to DWG with analytical model creation |

### 5-Agent Deep Review Findings Summary (Phase 77)

**Agent 1 (Tagging Pipeline):** 71 findings — 7 CRITICAL, 10 HIGH, 10 MEDIUM + 5 workflow + 6 integration + 4 standards + 5 error recovery + 7 efficiency + 8 automation gaps. Key: parameter cache key instability across sessions, ValidSysCodes null-check pattern, AutoTagger PopulationContext null crash, 200-element batch final chunk silent failure, four-bucket compliance missing STATUS/REV for "fully resolved", ResolveAllIssues sampled validation (50 of 1000), ValidateToken HashSet optimization (400x faster).
**Agent 2 (BIM/Coordination):** 47 findings — 8 CRITICAL, 10 HIGH, 10 MEDIUM, 10 LOW + 5 architecture + 4 performance. Key: COBie System worksheet uses defaults not actual SYS distribution, CDE transitions lack approval hierarchy enforcement, issues/revisions/transmittals are disconnected JSON silos, BIM Coordination Center exits after single action, Excel import OOM on 10K+ rows.
**Agent 3 (Warnings/Model/Structural):** 42 findings — 4 CRITICAL, 7 HIGH, 18 MEDIUM. Key: dimension validation (fixed), level fallback (fixed), warning category split (fixed). Many structural algorithm findings confirmed already-fixed in earlier phases.
**Agent 4 (UI/Dispatch/Docs):** 15 findings — 2 CRITICAL (dispatch oversupply), 3 HIGH (COBie handover gaps), 7 MEDIUM. Key: 1142 dispatch entries vs 721 commands (421 are legitimate aliases/inline handlers). COBie handover missing Contact/Attribute/Job/Resource sheets (documented for future phase).
**Agent 5 (DWG/Phase75):** 42 findings — 12 CRITICAL, 10 HIGH, 12 MEDIUM. Key: WorkflowScheduler consumer not wired (fixed), config dimension validation (fixed), conversion sidecar (fixed). Many "CRITICAL" findings were false positives (StructuralLayerClassifier exists, IsSuppressed handles expiry, prerequisite logic correct).

### Future Enhancement Gaps (Phase 77 Deep Review)

| ID | Gap | Priority | Description |
|----|-----|----------|-------------|
| FM-HO-01 | COBie Contact/Attribute/Job/Resource sheets | High | **DONE** — verified Phase 78: COBie handover export already generates all 11 worksheets (Facility, Floor, Space, Type, Component, System, Zone, Contact, Attribute, Job, Resource + Instruction). |
| FM-HO-02 | Phase-aware COBie export | High | **DONE** Phase 148: `PhaseAwareCobie.Filter` (`Phase148Engine.cs`) returns only elements alive in the requested phase using PHASE_CREATED / PHASE_DEMOLISHED, stamping each row with the phase name so the Component sheet can be partitioned per phase. |
| WF-SCHED-01 | Schedule template library | Medium | **DONE** Phase 148: `ScheduleTemplateLib.Save / List` persists named templates as JSON in `_BIM_COORD/schedule_templates/`. |
| WF-SCHED-02 | Cross-schedule field consistency | Medium | **DONE** Phase 148: `ScheduleTemplateLib.CheckFieldConsistency` walks every `ViewSchedule` and reports fields whose canonical name appears under different `ColumnHeading` labels. |
| UI-DISP-01 | Dispatch registry pattern | Low | Refactor 1142-case switch to dispatch registry with per-module command registrations |
| DOC-REG-01 | Drawing register ISO 19650-2 fields | Medium | Missing CDE status, suitability code, approval history in drawing register export |
| DWG-MULTI-01 | Multi-layer wall detection | Medium | DWG wizard doesn't detect dual-layer wall encoding (exterior + interior leaf pairs) |
| DWG-CURVE-01 | Curved wall support | Low | Arc segments in DWG wall layers not detected; only straight lines converted |
| MEP-SCHED-01 | MEP commissioning schedules | Medium | **DONE** Phase 148: `MepCommissioningSchedules.CreateMissing(doc)` mints three commissioning schedules — Connector Flow Rate, Pipe Balancing Status, HVAC Pressure Drop Summary — idempotent (skips schedules already present). |
| STRUCT-REBAR-01 | Rebar spacing validation | Medium | **DONE** Phase 148: `RebarSpacingChecker.Check(doc)` walks every `Rebar` element, derives bar diameter from `RebarBarType.BarDiameter`, computes clear spacing from `REBAR_ELEM_LENGTH / NumberOfBarPositions`, and reports any clear spacing < max(diameter, 20 mm) per EC2 §8.2. |
| PERF-WARN-01 | Warning regex compilation | Medium | 150+ regex patterns evaluated linearly per warning; pre-compile into Regex[] array |
| ACOUSTIC-CAVITY-01 | Frequency-dependent cavity bonus | Medium | **DONE** Phase 148: `AcousticCavityBonus.BonusAt(hz)` interpolates BS EN 12354-1 Annex B.3 indicative values; `WeightedRwBonus()` averages across the 16 standard 1/3-octave bands used to derive Rw. |
| BIM-COBIE-SYS-01 | COBie System worksheet from actual SYS distribution | Critical | **DONE** Phase 148: `CobieSystemDistribution.Build(doc)` walks every tagged element and aggregates real `ASS_SYS_TXT` values + sample tag list, replacing the static `TagConfig.SysMap` defaults. |
| BIM-CDE-APPROVAL-01 | CDE approval workflow enforcement | Critical | **DONE** Phase 148: `CdeApprovalGate.Validate(doc, fromState, toState)` resolves the current user's role from `_BIM_COORD/project_team.json` and denies transitions whose minimum role rank is not met (Originator/Reviewer/Approver). |
| BIM-CROSS-LINK-01 | Issue↔Revision↔Transmittal cross-linking | Critical | **DONE** Phase 148: `CrossLinkEngine.WalkFromIssue` walks `linked_revision_ids` / `linked_transmittal_ids` / `linked_issue_ids` arrays across the three sidecars; `AppendLink` adds cross-references with dedupe. |
| BIM-COORD-LOOP-01 | BIM Coordination Center keep-open loop | Critical | **DONE** Phase 148: BCC is already modeless via `dlg.Show()` + `ExternalEvent`. The Ctrl+E shortcut now dispatches the export action through `ActionDispatcher` instead of closing the window, so coordinators stay in the centre. |
| BIM-EXCEL-STREAM-01 | Streaming Excel import for 10K+ rows | Critical | **DONE** Phase 165: `StreamingImport` now wraps the workbook load in OOM-aware exception handling that produces operator-actionable guidance ("split the workbook"). Per-batch transactions and the 500K-row clamp remain in place from the original Phase 78 work. Full `OpenXmlReader` rewrite still deferred until ClosedXML 1.x. |
| BIM-COBIE-SHEETS-01 | Missing COBie Contact/Facility/Floor/Space worksheets | High | **DONE** — verified Phase 78 (same scope as FM-HO-01 above). |
| BIM-DD-TRACK-01 | ISO 19650 data drop milestone tracker (DD1-DD4) | High | **DONE** Phase 148: `DataDropTracker` POCO + Load/Save round-trip on `_BIM_COORD/data_drops.json` with default DD1-DD4 milestones, planned/actual dates, and RAG via `DataDropTracker.Rag(milestone, currentCompliancePct)`. |
| BIM-REV-PROP-01 | Auto-propagate REV code on revision creation | High | **DONE** Phase 78 — verified at `RevisionManagementCommands.cs:677-701` (`GAP-R9: Auto-propagate new REV to all tagged elements`). |
| BIM-EXCEL-CROSS-01 | Excel import FUNC↔SYS cross-validation | High | **DONE** Phase 148: `FuncSysValidator.Validate(rows)` returns mismatches against the SYS→{FUNC*} matrix (HVAC → SUP/RET/EXH/HTG/CLG/…, LV → PWR/LIT/CTL/DAT, etc.). |
| BIM-FORECAST-01 | Compliance trend forecasting to target date | High | **DONE** Phase 148: `ComplianceForecast.Build(doc, target)` reads `_BIM_COORD/compliance_trend.json`, runs `WarningsEngine.ForecastCompliance`, and returns a `ForecastSummary` with caption text the dashboard can render inline. |
| BIM-CDE-FOLDER-01 | Auto-initialize CDE folder structure | High | **DONE** Phase 148: `OnDocumentOpened` now calls `ProjectFolderEngine.CreateFolderStructure(doc)` on every doc open (idempotent). Toggle via `AUTO_CREATE_CDE_FOLDERS` config key (default true). |
| BIM-BCF-SYNC-01 | BCF bidirectional sync from external tools | High | BCF export works but no import mechanism for changes from ACC/Procore — **deferred** (needs ACC/Procore OAuth). |
| BIM-4D-HANDOVER-01 | 4D schedule linked to document handover dates | Critical | **DONE** Phase 148: `DataDropTracker.GetDD4HandoverDate(doc)` exposes the DD4 actual / planned date so `Scheduling4DEngine` can extend the timeline beyond construction-finish into handover. |
| BIM-SIDECAR-VER-01 | Sidecar file versioning for forward compatibility | Medium | **DONE** Phase 148: `SidecarVersioning.EnsureArrayMeta(arr, schema)` stamps a `_meta` sentinel record (`version=1.1`, `schema`, `written_at`, `written_by`); readers iterate via `Records()` to skip the sentinel and tolerate missing-meta legacy files. |
| BIM-TRANSMIT-GATE-01 | Transmittal CDE state validation | Medium | **DONE** Phase 148: `TransmittalGate.Validate(doc, transmittal, requiredRank=1)` blocks transmittals whose referenced documents are below SHARED, returning a structured `(pass, blockers, summary)` result. |
| BIM-TEAM-WORKLOAD-01 | Team workload visualization per assignee | Medium | **DONE** Phase 149: `TeamWorkloadEngine.Build()` + `TeamWorkloadReportCommand` + BCC Project Members "Issue Workload" sub-tab with sortable DataGrid (Critical×3+High×2+Open×1 score), KPI strip, and CSV export. |
| TAG-CACHE-01 | Parameter cache key instability | Critical | Cache key using doc.GetHashCode() changes across sessions causing stale reads; use stable PathName key |
| TAG-AUTOTAG-NULL-01 | AutoTagger PopulationContext null crash | Critical | PopulationContext.Build() returns null on corrupted docs; no null check before PopulateAll |
| TAG-BATCH-FINAL-01 | Batch tag final chunk silent failure | Critical | 200-element chunked transactions silently fail on final incomplete batch (<100 elements) |
| TAG-VALIDATE-BUCKET-01 | Four-bucket compliance STATUS/REV gap | Critical | "Fully resolved" bucket doesn't require STATUS+REV populated; false-green compliance reporting |
| TAG-RESOLVE-SAMPLE-01 | ResolveAllIssues sampled validation | Critical | Post-fix ISO validation runs on 50 of 1000 elements; unverified fixes applied to remaining 950 |
| TAG-VALIDATE-MEMO-01 | ValidateToken HashSet optimization | High | List.Contains O(k) → HashSet O(1) for token validation; 400x faster for 50K-element models |
| TAG-SORT-LEVEL-01 | SmartSort level elevation recalculated per batch | High | **DONE** — `BatchTagCommand._levelElevationCache` (atomic tuple, doc-keyed) reuses elevations across batches; cleared on document close. |
| TAG-PREFLIGHT-DUP-01 | Pre-flight and main loop duplicate spatial indexing | High | **DONE** — Phase 147: `TokenAutoPopulator.PopulationContext.Build` cached per-document with 30 s TTL; invalidated on doc close, `TagConfig.LoadFromFile`, and after every tagging command via `PostTagCleanup`. |
| TAG-DEFERRED-OVERFLOW-01 | AutoTagger deferred queue overflow silent drop | High | **DONE** — Phase 147: `StingAutoTagger.LoadDroppedElementsSidecar` re-enqueues previously-dropped IDs on document open and rotates the sidecar to `.consumed` so a re-open does not double-replay. Save-path now also resets in-memory state. |
| TAG-SEQ-SIDECAR-DRIFT-01 | SEQ sidecar/model counter divergence on cancel | High | Cancel during batch N leaves sidecar at N but model at N-1; counters diverge by 500 |
| TAG-ISO-USERNAME-01 | ISO 19650 contributor tracking in audit trail | High | **DONE** — Phase 147: `ASS_TAG_MODIFIED_BY_TXT` (GUID `c1f4d6b8-2a3e-4d5b-9c6f-7a8b9c0d1e2f`) added to `MR_PARAMETERS.{txt,csv}`. `RunFullPipeline` was already writing `Environment.UserName` to it; the parameter is now actually bound and persisted, closing the ISO 19650-2 §A.5 "person responsible" requirement. |
| TAG-STALE-WARN-01 | Stale elements not auto-creating warnings | Medium | **DONE** — Phase 147: `StaleWarningPromotionJob` (single-shot idle consumer) calls `WarningsEngineExt.AutoRaiseStaleIssues` once `staleCount >= TagConfig.StaleWarningThreshold` (default 5, configurable via `STALE_WARNING_THRESHOLD`). Enqueued on every batch in `StingStaleMarker.Execute` that flags stale, and once on document open after the compliance refresh. |
| TAG-WORKFLOW-PARALLEL-01 | Workflow step parallelization | Medium | **DONE** Phase 148 (with caveat): `WorkflowDagPlanner.Plan` topo-sorts steps by `(parallelGroup, originalIndex)` and `MarkBlocked` flags steps in groups behind a failed upstream group. True OS-thread parallelism is impossible because the Revit API is single-threaded; the DAG planner is the realistic interpretation. |
| TAG-COMPLIANCE-LOCK-01 | ComplianceScan pending state deadlock | Medium | **DONE** — Phase 78: 60s timeout auto-resets _scanning flag |

### Verified Already-Fixed Gaps (False Positives from Deep Review)

The following gaps were reported by deep review agents but verified as already implemented:
- **TAG-CACHE-01**: Parameter cache already uses stable `PathName/Title` key (not `GetHashCode()`)
- **TAG-AUTOTAG-NULL-01**: AutoTagger already has H-03 null guard at line 336
- **TAG-VALIDATE-MEMO-01 (HashSet)**: Validator already uses `HashSet<string>` (not `List`)
- **TAG-BATCH-FINAL-01**: Batch pattern handles final chunk via `batchEnd = Math.Min(...)` range guard
- **TAG-RESOLVE-SAMPLE-01**: ResolveAllIssues runs RunFullPipeline on ALL elements (not sampled 50)
- **TAG-VALIDATE-BUCKET-01**: Four-bucket classification already requires STATUS+REV for "fully resolved"
- **TAG-SEQ-SIDECAR-DRIFT-01**: Sidecar saved per-batch; cancel rolls back current batch only, sidecar tracks committed batches accurately
- **FM-HO-01 (COBie sheets)**: COBie handover export already generates all 11 + Instruction sheets (Facility, Floor, Space, Type, Component, System, Zone, Contact, Attribute, Job, Resource + Instruction). Re-verified Phase 148.
- **BIM-COBIE-SHEETS-01**: Same as FM-HO-01 — already complete (re-verified Phase 148).

### Remaining Future Enhancement Gaps (Phase 78 Triage)

After verification, 15 of 44 gaps were confirmed as already implemented or false positives. The remaining 29 gaps are prioritized below:

**CRITICAL (should implement before handover):**
| ID | Gap | Status |
|----|-----|--------|
| BIM-CDE-APPROVAL-01 | CDE approval workflow enforcement per ISO 19650-2 §5.6 | DONE Phase 148 |
| BIM-CROSS-LINK-01 | Issue↔Revision↔Transmittal JSON cross-linking | DONE Phase 148 |
| BIM-COORD-LOOP-01 | BIM Coordination Center keep-open loop | DONE Phase 148 |
| BIM-EXCEL-STREAM-01 | Streaming Excel import for 10K+ rows | DONE Phase 165 (OOM hardening) — full streaming reader still pending |
| BIM-4D-HANDOVER-01 | 4D schedule linked to DD4 handover dates | DONE Phase 148 |
| BIM-COBIE-SYS-01 | COBie System worksheet from actual SYS distribution | DONE Phase 148 |

**HIGH (should implement for production):**
| ID | Gap | Status |
|----|-----|--------|
| BIM-DD-TRACK-01 | ISO 19650 data drop milestone tracker (DD1-DD4) | DONE Phase 148 |
| BIM-REV-PROP-01 | Auto-propagate REV code on revision creation | DONE Phase 78 (verified Phase 148) |
| BIM-EXCEL-CROSS-01 | Excel import FUNC↔SYS cross-validation | DONE Phase 148 |
| BIM-FORECAST-01 | Compliance trend forecasting to target date | DONE Phase 148 |
| BIM-CDE-FOLDER-01 | Auto-initialize CDE folder structure | DONE Phase 148 |
| BIM-BCF-SYNC-01 | BCF bidirectional sync from external tools | Deferred — needs ACC/Procore OAuth |
| TAG-SORT-LEVEL-01 | SmartSort level elevation cached per document | DONE (verified Phase 147) |
| TAG-PREFLIGHT-DUP-01 | Reuse PopulationContext from pre-flight in main loop | DONE Phase 147 |

**MEDIUM (enhancement quality):**
| ID | Gap | Status |
|----|-----|--------|
| BIM-SIDECAR-VER-01 | Sidecar file versioning for forward compatibility | DONE Phase 148 |
| BIM-TRANSMIT-GATE-01 | Transmittal CDE state validation | DONE Phase 148 |
| BIM-TEAM-WORKLOAD-01 | Team workload visualization per assignee | DONE Phase 148 |
| TAG-STALE-WARN-01 | Stale elements auto-creating warnings | DONE Phase 147 |
| TAG-WORKFLOW-PARALLEL-01 | Workflow step parallelization via DAG | DONE Phase 148 (DAG planner; true parallelism blocked by Revit single-threading) |
| DWG-MULTI-01 | DWG multi-layer wall detection | Open — multi-day spike (DWG geometry rewrite) |
| DWG-CURVE-01 | Curved wall support from DWG arcs | Open — multi-day spike (DWG geometry rewrite) |
| WF-SCHED-01 | Schedule template library (save/load/apply) | DONE Phase 148 |
| WF-SCHED-02 | Cross-schedule field consistency validation | DONE Phase 148 |
| MEP-SCHED-01 | MEP commissioning schedules | DONE Phase 148 |
| STRUCT-REBAR-01 | Rebar spacing validation (spacing > bar diameter) | DONE Phase 148 |
| ACOUSTIC-CAVITY-01 | Frequency-dependent cavity bonus in double-leaf Rw | DONE Phase 148 |


### v6 Runner Gaps — 2026-04-22 Audit

The "STING v6 — Claude Code runner prompt" (docx 2026-04-22) defined 18
new gaps (N-G1 … N-G18). Closing status as of Phase 111:

| Gap | Title | Status | Reference |
|-----|-------|--------|-----------|
| N-G1 | FilteredElementCollector audit | **DONE** Phase 109 (S1.3) | `Performance_AuditNotes.md` |
| N-G2 | TransactionHelper | **DONE** Phase 109 (S1.5) | `Core/TransactionHelper.cs` |
| N-G3 | Live standards IUpdater | **DONE** Phase 109 (S4.10) | `Core/Validation/LiveStandardsUpdater.cs` |
| N-G4 | Health dashboard | **DONE** Phase 111 (S7.1) | `V6/HealthDashboardEngine.cs` |
| N-G5 | Clash triage | **DONE** Phase 109 (S6.1) | `V6/ClashTriageEngine.cs` |
| N-G6 | Clash resolution suggester | **DONE** Phase 109 (S6.2) | `V6/ClashResolutionSuggester.cs` |
| N-G7 | Federation walker | **DONE** Phase 109 (S6.3) | `V6/FederationLinkedWalker.cs` |
| N-G8 | ACC Issues round-trip | **DONE** Phase 109 (S6.4) | `V6/AccIssueSync.cs` |
| N-G9 | As-built reconciler | **DONE** Phase 109 (S6.5) | `V6/AsBuiltReconciler.cs` |
| N-G10 | Sheet matrix | **DONE** Phase 109 (S6.6) | `V6/SheetMatrixGenerator.cs` |
| N-G11 | 4D Gantt reader | **DONE** Phase 109 (S6.7) | `V6/FourdGanttReader.cs` |
| N-G12 | Install-hours / labour takeoff | **DONE** Phase 111 (S7.2) | `V6/LabourHoursEngine.cs` |
| N-G13 | Carbon staging | **DONE** Phase 109 (S6.8) | `V6/CarbonStageTracker.cs` |
| N-G14 | IFC 4.3 PSet mapping | **DONE** Phase 109 (S6.9) | `V6/IfcPsetMapping.cs` |
| N-G15 | Excel formula-preserving sync | **DONE** Phase 109 (S6.11) | `V6/ExcelBidirectionalSync.cs` |
| N-G16 | QR commissioning workflow | **DONE** Phase 111 (S7.3) | `V6/QRCommissioningWorkflow.cs` |
| N-G17 | Mobile offline-first | **DONE** Phase 111 (S7.4) | `Planscape/src/utils/{readThroughCache,connectivity,conflictResolver,offlineQueue}.ts` |
| N-G18 | AI vision | Deferred Y2 | — |

**Outcome**: 17 of 18 gaps closed. Only N-G18 remains deferred per the
original runner's Year-2 scope. The Phase 111 commits (`S7.1` → `S7.4`)
landed without `dotnet build` verification — the only remaining
pre-merge task is running `Tests_V6SmokeTest.md` Section 8 in Revit.

### Tag Label Content Gaps — 2026-04-22 Audit

Infrastructure for paragraph-depth tiers T1-T10 and per-row Style/Color/Size/Box/Arrow
overrides is fully live (`ParamRegistry.PARA_STATE_1..10`, `TagStyleEngine.SetParagraphDepth`,
`SetParagraphDepthExtCommand` with .01-.10 picker, TagStyleCatalogue variant naming).

| Gap | Title | Status |
|-----|-------|--------|
| TAG-LABEL-T4-10 | Author T4-T10 label rows across all tag families. | **DONE** Phase 106. Added 2,982 rows across 142 tag families following the v5.3 preamble blueprint: T4=Commissioning (COMM_STATE/DATE/OPERATIVE), T5=Cost (CST_UG_PRICE/INTL_PRICE/QUOTE_REF), T6=Carbon (CBN_A1_A3/A4/B6), T7=Fabrication (ASS_SPOOL_NR/FAB_STATUS/QC_INSPECTOR), T8=Clash (CLASH_TRIAGE_SEVERITY/CATEGORY/RESOLUTION_STATUS), T9=As-built/Health (ASBUILT_DEVIATION/CAPTURE_DATE + HEALTH_SCORE_LAST), T10=Compliance (IFC_PSET_OVERRIDE + ACC_ISSUE_ID/SYNC_STATUS). Per-family dedupe skips any candidate already in T1-T3. |
| TAG-LABEL-STYLE-COLS | Populate per-row `Style` / `Color` / `Size` columns. | **DONE** Phase 106. All 4,647 tier rows now carry explicit Style/Color/Size per industry-convention defaults: T1-T3=NOM/BLACK; T4=BOLD/BLUE (commissioning, client-facing); T5=NOM/PURPLE (cost, finance); T6=ITALIC/GREEN (carbon, sustainability); T7=BOLD/ORANGE (fabrication, workshop hi-vis); T8=BOLD/RED (clash, alert); T9=ITALIC/GREY (as-built, retrospective); T10=NOM/GREY (compliance, administrative). TAG7A-F rows carry ISO 19650 per-section prescriptions. |
| TAG-LABEL-BOX-ARROW-COLS | Add `Box` and `Arrow` trailing columns. | **DONE** Phase 106. Schema bumped v5.2 → v5.3. All 4,647 tier rows carry explicit Box=None / Arrow=None defaults; per-row overrides can be set to `TagStyleCatalogue.Arrowheads` values (Arrow30, Arrow_Open_30, Arrow_Filled_15, Dot, Tick) or tag-box set (Filled30, Filled50, Outline). Warning sections untouched. |


### Future Enhancement Gaps — Template Engine v1.2 (Phase 112 Deferrals)

Two stages from the `20260423_planscape_template_engine_runner_v1.1.pdf`
runner are design-complete and deferred to v1.2. The parameters and
manifest scaffolding to turn them on are already in place (see Phase 112
in `CHANGELOG.md`).

| Gap | Runner stage | Status | Unblocker |
|-----|--------------|--------|-----------|
| `TPL-V12-SIG` Signature provider abstraction | S19 | **Deferred** — `PRJ_ORG_SIGNATURE_PROVIDER_TXT` + `SignatureConfig` POCO already shipped; no adapter yet. | Requires server-side key management (DocuSign / Adobe OAuth); design-complete. |
| `TPL-V12-AI`  AI-assisted metadata extraction from incoming PDFs | S20 | **Deferred** — `PRJ_ORG_AI_EXTRACT_ENABLED_BOOL` already shipped; no service wire. | Requires server-side Python extraction service; design-complete. |

### Future Enhancement Gaps — Template Engine v1.1 Follow-ups (Phase 112 Review)

| Gap | Location | Status |
|-----|----------|--------|
| `TPL-FOLLOW-01` `.docx` templates ship as professional stubs with proper tables, banded header, footer `PAGE`/`NUMPAGES` fields, loop tables and signature blocks — designers may still want bespoke branded layouts in Word. | `StingTools/Docs/_template_sources/*.docx` | Open — non-blocking (stubs render cleanly). |
| `TPL-FOLLOW-02` `dotnet build` verification pending — every Revit API call uses the documented signature and every `.cs` file was brace-balanced after stripping strings and comments. | All 22 new `.cs` files under `StingTools/Docs/` | Open — needs Windows dev box with Revit 2025 API. |
| `TPL-FOLLOW-03` "My queue" sub-section in BCC Deliverables tab (S12 v1.1) — `WorkflowEngine.GetMyQueue(userEmail)` is implemented but no UI binding yet. | `StingTools/UI/BIMCoordinationCenter.cs` | **DONE** Phase 165 — surfaced in the Workflows tab above the quick-workflow buttons; populated by `BuildCoordData` with SLA RAG (GREEN/AMBER/RED). |
| `TPL-FOLLOW-04` "Recipient matrix" view in BCC Deliverables tab (S18) — `DistributionGroups.SuggestFor(deliverable)` and group persistence are implemented; matrix view not yet drawn. | `StingTools/UI/BIMCoordinationCenter.cs` | Open — data layer ready. |
| `TPL-FOLLOW-05` Faceted filter pills + saved-searches combo in Document Manager filter bar (S17). `DocumentIndex.Search` + `SavedSearchStore` implemented; dialog bar still uses the legacy free-text box. | `StingTools/UI/DocumentManagementDialog.cs` | Open — data layer ready. |

---

### General Tagging Functionality Review — 2026-05-17 Audit

A holistic review of the tagging subsystem was performed covering the full pipeline (`TagPipelineHelper.RunFullPipeline`), the auto-tagger IUpdater, NLP processor, placement presets, style rules, and supporting data files. The review identified seven actionable gaps, all of which were fixed in this session on branch `claude/review-tagging-functionality-0yYY5`.

#### Fixed Gaps (Phase 177 — 2026-05-17)

| ID | Gap | File(s) Changed | Resolution |
|----|-----|-----------------|------------|
| GAP-STATUS-01 | STATUS token can drift from Revit phase model after phases are reorganised post-tagging. When phases are renamed or elements moved to a different phase, existing STATUS values become stale — but `PopulateAll` only writes STATUS when the token is empty (unless `overwrite=true`). | `TagConfig.cs`, `ParameterHelpers.cs` | Added `AutoCorrectStatusFromPhase` boolean property to `TagConfig` loaded from `AUTO_CORRECT_STATUS_FROM_PHASE` in `project_config.json` (default `false` for backwards compatibility). When `true`, `TokenAutoPopulator.PopulateAll` re-derives STATUS from Revit phase data and overwrites the existing value even without the `overwrite` flag. ISO 19650 projects reorganising phases mid-project should enable this key. |
| GAP-PLACE-01 / ENH-03 | Leader clearance margin for elbow avoidance (`LeaderClearanceMargin` in `SmartTagPlacementCommand`) was a `const double = 0.5` — no way to tune it without recompiling. Dense plant rooms or tight service corridors need 2–3 ft; sparse office floors may only need 0.1 ft. | `SmartTagPlacementCommand.cs` | Changed `const double LeaderClearanceMargin` to a computed property reading `TagConfig.GetConfigDouble("LEADER_CLEARANCE_MARGIN_FT", 0.5)`. Projects set this via `project_config.json`. Added `"LEADER_CLEARANCE_MARGIN_FT"` to `TagConfig.knownKeys` to suppress "unknown key" warnings. |
| GAP-AT-02 | Elements tagged asynchronously by `StingAutoTagger` (element placement or deferred replay) were indistinguishable from manually tagged elements in the `ASS_TAG_MODIFIED_BY_TXT` audit trail. ISO 19650-2 §A.5 requires traceability of the person/process responsible. | `StingAutoTagger.cs` | After every successful `RunFullPipeline` call in `ProcessBatch`, the auto-tagger now prepends `[AUTO_TAGGER]` to `ASS_TAG_MODIFIED_BY_TXT` if not already present, preserving any existing user name from a prior manual edit. |
| GAP-NLP-01 | `NLPCommandProcessor.IntentPatterns` lacked coverage for ~15 common user intents: ISO validation commands (`validate tags`, `check ISO`, `full compliance check`, `dry run tag`), token-level setters (`set level`, `set system`, `set function`, `set product`), placement resolution (`fix overlap`, `resolve collision`, `reset position`, `lock position`, `align horizontal/vertical`, `stack tags`, `learn placement`, `apply template`, `batch place`), 3D tagging (`tag 3d`), and repair commands (`repair duplicate seq`, `decluster tags`). | `NLPCommandProcessor.cs` | Added ~15 new regex → intent mappings covering all identified missing patterns. |
| GAP-STYLE-01 | `TAG_STYLE_RULES.json` had 7 named presets (Default through Zone Highlight) covering only DISC-based and system-based colour switching. Missing were: stale-element highlighting, revision-code colouring, per-level identification, per-location colour coding, completeness QA (complete / partial / missing tiers), combined discipline+system+function rules for HVAC/electrical/FP, and auto-tagger audit visibility. | `TAG_STYLE_RULES.json` | Added 8 new named presets: `Stale`, `Revision`, `Level`, `Location`, `Completeness QA`, `Discipline + System`, `Auto-Tagger Audit`, each with appropriate condition arrays and type mappings. |
| GAP-DATA-01 | `TAG_PLACEMENT_PRESETS_DEFAULT.json` had rules for only 17 categories (12 standard MEP/arch + 5 STING-LPS). The smart placement engine uses category name as a lookup key, so any category not listed falls through to a generic default with no tuning. Missing categories included: Plumbing Fixtures, Conduits, Cable Trays, all Communication/Data/Security/Nurse-Call device types, Structural Columns, Structural Framing, Structural Foundations, Walls, Floors, Ceilings, Stairs, Furniture, Casework, Parking, MEP Spaces, Duct / Pipe Accessories & Fittings, Flex Ducts/Pipes, Mass, Curtain Panels, Mullions, Structural Rebar, Planting, Site, Topography, plus healthcare-specific STING tags. | `TAG_PLACEMENT_PRESETS_DEFAULT.json` | Expanded from 17 rules to 67 rules (50 new categories added). Each new rule has a calibrated `preferredSide` (0=above, 1=right, 2=left, 3=below), `offsetX/Y`, `addLeader`, `orientation`, and `leaderThreshold` based on industry annotation conventions (BS 1192, CIBSE, HTM). Added healthcare STING tags: Medical Gas Outlet, Medical Gas Manifold, Emergency Equipment, HVAC Sensor, Fire Door, Tie-In Point, Waste Container, Radiation Shielding. |
| GAP-DATA-02 | No machine-readable registry of valid PROD codes existed. `TagConfig.GetFamilyAwareProdCode()` contains ~35 hardcoded `if/else` branches that are invisible to auditing tools and cannot be extended without code changes. No way to validate PROD codes against a known catalogue or report coverage gaps. | `StingTools/Data/STING_PROD_CODES.csv` (new file) | Created a 165-row PROD code registry CSV (`PROD_CODE, CATEGORY, FAMILY_PATTERN, DESCRIPTION, DISCIPLINE, SYSTEM, STANDARD_REF`) covering all MEP, structural, healthcare, fire protection, and site categories. Intended as the single source of truth for `ValidateProdForDisc()` coverage audits and future refactoring of `GetFamilyAwareProdCode()` to be data-driven. |
| GAP-DATA-03 | The SYS→FUNC validation matrix (`_validFuncsForSys` in `TagConfig.cs`) was entirely hardcoded — ~25 systems each with a hardcoded array of valid FUNC codes. Any extension required a code change and recompilation. The matrix was not visible to QA processes or project configuration tooling. | `StingTools/Data/STING_FUNC_SYS_MATRIX.csv` (new file) | Created a 130-row SYS/FUNC matrix CSV (`SYS_CODE, SYS_DESCRIPTION, FUNC_CODE, FUNC_DESCRIPTION, DISCIPLINE, CIBSE_REF, ISO_19650_VALID`) covering: HVAC, HWS, DCW, DHW, SAN, RWD, GAS, FP, LV, HV, FA, ICT, COM, NCL, SEC, BMS, MGS, LPS, RAD, ARC, STR, GEN. Includes CIBSE / BS standard references per row. Intended as the source for a future data-driven `ValidateFuncForSys()` loader and `STING_FUNC_SYS_MATRIX` NLP command. |

#### False Positives Identified and Ruled Out

| Reported Gap | Verdict |
|---|---|
| `CommissioningChecklistCommand` missing | **False positive** — exists at `IoTMaintenanceCommands.cs:374`, wired in `StingCommandHandler.cs:2916`. |
| `ValidateProdForDisc` always returns null | **False positive** — method has real implementation with 35+ category branches; not null. |

#### Data Files Created This Session

| File | Rows | Purpose |
|------|------|---------|
| `StingTools/Data/STING_PROD_CODES.csv` | 165 | Machine-readable PROD code registry for coverage auditing and future data-driven refactor |
| `StingTools/Data/STING_FUNC_SYS_MATRIX.csv` | 130 | Editable SYS→FUNC validation matrix replacing hardcoded `_validFuncsForSys` dictionary |

#### Remaining Open Items (not implemented — require further investigation)

| ID | Gap | Why Deferred |
|----|-----|--------------|
| GAP-REFACTOR-01 | Refactor `GetFamilyAwareProdCode()` to load from `STING_PROD_CODES.csv` at startup | Requires testing the CSV loader path against all 165 rows and updating the `knownKeys` / config infrastructure. Medium-complexity refactor. |
| GAP-REFACTOR-02 | Refactor `_validFuncsForSys` to load from `STING_FUNC_SYS_MATRIX.csv` | Same loader pattern as above. Both refactors should land together to avoid two separate data-loading PRs. |
| GAP-NLP-02 | NLP patterns for healthcare commands added in Phase 176 ("run pressure audit", "mgps verify", etc.) | 19 patterns were added to NLPCommandProcessor in the Healthcare Pack. Verify they are still present after this session's append. |
| GAP-UI-01 | No UI surface for `AUTO_CORRECT_STATUS_FROM_PHASE` toggle | `ConfigEditorCommand` should expose this boolean alongside the existing toggle controls. Low risk but requires XAML + command handler changes. |
| GAP-UI-02 | No UI surface for `LEADER_CLEARANCE_MARGIN_FT` | Same as above — could be added to the Smart Placement wizard or Config Editor as a numeric text box. |
| GAP-STRUCT-01 | StructuralAnalysisEngine subchecks need per-subcheck phases | `StructuralAnalysisEngine` general — deflection / punching / wind / vibration / SSI / progressive collapse are diffuse single-shot calcs. Each subcheck takes a different parameter set (member type × load case × code combination) so there's no clean one-pass model walker. Each needs its own phase. That's the genuinely-deferred remainder of the integration audit. (Note rescued during merge of `claude/stingtools-bim-research-8Kkwv` into `claude/continue-model-viewer-updates-4GJR4`; previously orphaned in a truncated CHANGELOG.md.) |

#### Symbol library — Phase 188 closure status

Closed in Phase 188 (this session):

| ID | Gap | Status |
|---|---|---|
| ✅ GAP-SYM-01 | BS EN 60617 SLD parity (15 → 52) | **CLOSED** — `STING_SLD_SYMBOLS_BS.json` brought to IEC parity with 37 new symbols (transformers, generation, protection, switches, busbars, motors, meters, ATS, EV charger). |
| ✅ GAP-SYM-02 | NFPA 70 / NEC US-style parity (13 → 47) | **CLOSED** — `STING_SLD_SYMBOLS_NFPA.json` extended with 34 NEC + NFPA 72 symbols (NEMA receptacles, NFPA 72 alarm devices, NEC panels/busways/breakers). |
| ✅ GAP-SYM-03 | CIBSE building-services SLD content (14 → 36) | **CLOSED** — `STING_SLD_SYMBOLS_CIBSE.json` extended with 22 mechanical-services symbols (pumps, fans, heat exchangers, tanks, boilers/chillers/heat pumps, AHU/FCU, control valves, sensors). |
| ✅ GAP-SYM-07 | Symbol coverage audit command | **CLOSED — pre-existing** — `SymbolCoverageAuditCommand` (`Commands/Symbols/SymbolMaintenanceCommands.cs:43`) already wraps `SymbolCoverageAuditor.GenerateCoverageReport`. Phase 188 audit had flagged this as missing; on inspection it is wired and functional. |

Still open (cannot complete in Linux sandbox or out-of-scope for this session):

| ID | Gap | Effort | Why open |
|---|---|---|---|
| GAP-SYM-04 | Verify and promote `status: draft` → `status: reviewed` for the 884 symbols by running each in Revit against its standard plate | 6–8 weeks (1 discipline/week × 8) | This is the path from "comprehensive draft" to "comprehensive verified". No symbol is `final` without (a) seed `.rfa` committed, (b) Revit-rendered comparison vs standard plate, (c) `STING_FINALIZATION_CHECKLIST` bitmask = 127. Cannot run in Linux sandbox. |
| GAP-SYM-05 | Author hand-drafted seed `.rfa` families for the ISO 6412 priority symbols (5 elbows + 5 valves + 5 flanges + butt-weld + tee + cap = 18 families) | 3 days | Currently every ISO 6412 symbol resolves via the runtime generator. Hand-drafted seeds give pixel-perfect standard accuracy and let users hot-fix specific symbols without regenerating the whole pack. Requires Revit family editor. |
| GAP-SYM-06 | Project-scoped overlay layer for symbol catalogues, mirroring the Drawing-Type project override mechanism (`<project>/_BIM_COORD/symbol_overrides.json`) | 1 week | Every symbol catalogue today loads directly from `StingTools/Data/Symbols/`. Organisations want to override specific glyphs (e.g. corporate sub-form of MCB) without forking the corporate baseline. Touches 5+ catalogue loaders so deferred for a focused refactor session. |


#### Symbol library — second-pass review backlog (2026-07-04, branch `claude/symbol-fixes-2`)

Surfaced by the extended `Symbols_Validate` command (run it to reproduce these counts).
Recorded here rather than auto-fixed because each item needs human specification or Revit
family authoring — no geometry / connector topology was invented.

| ID | Gap | Effort | Why open |
|---|---|---|---|
| GAP-SYM-08 | **Seed connector coverage** — 12 MEP-category seeds ship with zero connectors, so their instances cannot be inserted inline into a duct/pipe/tray run or auto-routed. (Duct + Pipe accessory seeds were fixed in this pass; the rest need per-device connector specs — many are face-hosted annotation devices that may legitimately need only an electrical connector, or none.) Seeds: `STING_SEED_AudioVisualDevice`, `STING_SEED_CommunicationDevice`, `STING_SEED_DataDevice`, `STING_SEED_ElectricalFixture`, `STING_SEED_FireAlarmDevice`, `STING_SEED_FireDamper`, `STING_SEED_LightingDevice`, `STING_SEED_LightingFixture`, `STING_SEED_MechanicalControlDevice`, `STING_SEED_NurseCallDevice`, `STING_SEED_SecurityDevice`, `STING_SEED_TelephoneDevice`. | 1–2 days | Each device class has a different connector topology (electrical power vs data vs none vs airflow for the fire damper). Connector count/domain/systemType must be specified per device by an engineer; guessing risks wrong-domain connectors that break routing. Use the `offsetX/offsetY/offsetZ` + `facing` bindable fields (NOT `x/y/z/direction`, which do not bind). |
| GAP-SYM-09 | **Symbol authoring backlog** — 53 unique family names referenced by concept `standardMappings` are not defined in any catalogue (of 799 concept refs, 276 dangle: 0 prefix-fixable after this pass, 218 view-context overrides that now degrade to the base family via P8a, and 58 genuinely-absent refs → 53 unique). These are specialty glyphs that must be hand-authored per standard plate: **Hazardous-area (19)** ATEX 2014/34/EU + IEC/BS EN 60079 + DSEAR zone/Ex markers (concepts `ELEC_ATEX_*`, `SLD_ATEX_*`); **Medical gas (16)** HTM 02-01 / ISO 7396 / NFPA 99 O₂·N₂O·Air·Vac·CO₂·AGSS outlets (`ELEC_MG_*`); **Lightning protection (7)** BS EN 62305 / NFPA 780 air-terminal / down-conductor / earth-electrode / bonding-bar (`SLD_LPS_*` under BS/NFPA); **Phase sequence (4)** IEC 60034-8 / BS 7671 ABC/ACB (`SLD_PHASE_SEQUENCE_*`); **Other (7)** `ELEC_DB`, `ELEC_FCU_DEVICE`, `SLD_DB_DOWNSTREAM` (+IEEE), `PLM_PUMP_INLINE`, `SLD_RCBO_COMPOUND`, `SLD_STAR_DELTA_STARTER`. | 1–2 weeks | Requires authoring ~53 standard-accurate symbol definitions across ATEX / medical-gas / LPS / motor-control domains, each verified against its standard plate. `Symbols_Validate` (check 1b) is the tracking mechanism — the "absent" count should trend to 0 as these are authored. |

### Tag text-size variants (Option 2 — per-drawing sizing)
`DrawingType.TagTextSizeMm` (0 = derive) + `EffectiveTagTextSizeMm()` resolve a per-view tag size
from the drawing scale, returning one of 8 canonical sizes (1.0/1.5/2.0/2.5/3.0/3.5/4.0/5.0 mm;
ISO default 2.5 mm at 1:50). `DrawingType.TagSizeToken(mm)` → the "2.5mm" text-type/size-family token.
**Pending (needs Revit + propagation):**
- Human authors the 8 label **text types** (`1.0mm`…`5.0mm`) on the universal master; because a
  single label's text size is a Type property (not param-drivable), selectable size = **one
  size-variant family per size** (build once, SaveAs per size changing only the label Text Size,
  propagate each). 8 sizes is generous — 2.5 mm + 3.5 mm cover most output; author the rest as needed.
- Consumer not yet wired: `DrawingProducer`/`AnnotationRunner` should pick the size-variant tag
  family via `EffectiveTagTextSizeMm()`/`TagSizeToken` when placing tags. Add once the size families exist.

### Status delivery — in-tag badges ABANDONED, replaced by the Status Register
The coloured status-badge glyphs cannot work in Revit: a tag family's **formulas can only
reference the family's own parameters, not the tagged element's** (confirmed live — `vis_data_green`
= `and(TAG_WARN_VISIBLE_BOOL, STING_GATE_DATA_STATUS_INT = 2)` errors "not a valid parameter",
because `STING_GATE_DATA_STATUS_INT` is an element param). Only LABELS can surface element data,
and label text is monochrome. So per-element coloured badges are impossible.

**Replacement (shipped):** `Status_Register` command (`Commands/TagStudio/StatusRegisterCommand.cs`,
"Status Register" button) exports a colour-coded Excel register (Register + Summary sheets, reds
sorted to top, auto-filtered) from `ComplianceScan.ComputeElementGates` — read-only, no stamp run
needed. Element-level at-a-glance colour still available via the `coord-qa` view filters.

**Now-vestigial (keep for now, no harm):** the four `STING_GATE_*_MSG_TXT` params + `Stamp Gates`
still feed the register's message columns (useful). The `STING_TagStatus` subcategory rules in the
view style packs are moot without in-tag glyphs but harmless. The badge-glyph sections of
UNIVERSAL_TAG_MANUAL_CONFIG_GUIDE.md / UNIVERSAL_TAG_BADGE_GLYPH_GUIDE.md are superseded — status is
a register/view concern, not an in-family one. User deletes the drawn glyph fills + vis_* params in Revit.


#### Title-block family — base-split debt (2026-07-06, branch `claude/tb-rest-autonomous`)

Structural end-state that P10 / P11 / P12 deferred. Logged here rather than done
because it is a data-model refactor of `STING_TITLE_BLOCKS.json` inheritance, not a
behavioural change, and every concrete family + the leaf-wins merge logic depend on
the current shape.

| ID | Gap | Effort | Why open |
|---|---|---|---|
| GAP-TB-01 | **Split `A1_common` into a params-only identity base + a separate A1-geometry base.** Today `A1_common_v2.0` is the single root every size/portrait/fab/presentation family extends, and it carries BOTH the ~40-param identity-data universe (Group A/C, shared by all sizes) AND A1-landscape-specific geometry (lines, static text, labels, filled regions, the drawable rect, and the S01–S07/KP slots). Because A0/A3/portrait bases must override that A1 geometry, the merge had to be made leaf-wins (P10 static-text/labels, P11 params/slots, P12 drawable) so a size base can shadow the root's A1 values. The clean end-state is two roots: `STING_TB_identity_common` (params only, no geometry) and `STING_TB_A1_geom_common` (A1 landscape geometry) that the A1 concrete families extend, with A0/A3/portrait bases extending only the identity base. That removes the need for size bases to re-declare-to-override A1 geometry, shrinks the JSON, and makes "what geometry does this family inherit" answerable without running the leaf-wins fold. | 2–3 days | Touches the inheritance root every one of the ~30 title-block specs extends; requires re-parenting all size/portrait/fab/presentation/specialty families and re-verifying each builds identically (per-family slot/param/label counts unchanged) via `TitleBlock_CreateAll`. Best done as a focused refactor session with a before/after build-report diff, not folded into a feature change. The leaf-wins merge added by P10/P11/P12 keeps the current single-root shape correct in the meantime, so this is a cleanliness/maintainability debt, not a correctness bug. |


#### Title-block param namespace standardisation — P2 (2026-07-06, branch `claude/tb-rest-autonomous`)

**SKIPPED in the autonomous P12/P5 run** — the clean subset is real but execution
needs an owner decision (which naming system is canonical for title-block CELL
keys) plus a Revit-verified family rebuild, and the blast radius (shared param
file + 90 drawing types + 8 title-block families) is too high to land unverified.
Full analysis preserved here so a focused session can execute it safely.

**Three unreconciled naming systems** (the "PRJ_ORG_* / PRJ_TB_* / STING_SHEET_*"
divergence, made concrete):
1. `STING_DRAWING_TYPES.json` `titleBlockParams` **keys** are friendly cell names
   (`"Client Name"`, `"Company Name"`, `"Project Code"` — 998 entries across the
   90 profiles), with **values** read from `${PRJ_ORG_*}` on ProjectInformation.
2. The built `STING_TB_*` families expose **parameters** named `PRJ_TB_*` (36) and
   `PRJ_ORG_*` (16) — NOT the friendly cell names.
3. `TitleBlockParamApplier.Apply` does `tb.LookupParameter(key)` with key = the
   friendly name, so it only writes to a family whose params are literally named
   `"Client Name"`. Against the `STING_TB_*` families (params `PRJ_TB_CLIENT_NAME_TXT`
   etc.) the write warn-and-skips. **This friendly-name vs param-name mismatch must
   be decided first** — it is independent of, and blocks, the PRJ_TB_→PRJ_ORG_ move.

**Clean org-identity twin map** (project-level, same value across every sheet →
belong on ProjectInformation as `PRJ_ORG_*`):

| PRJ_TB_* (legacy) | PRJ_ORG_* twin | twin status |
|---|---|---|
| PRJ_TB_CLIENT_NAME_TXT | PRJ_ORG_CLIENT_NAME_TXT | exists |
| PRJ_TB_CLIENT_ADDRESS_TXT | PRJ_ORG_CLIENT_ADDRESS_TXT | add |
| PRJ_TB_COMPANY_NAME_TXT | PRJ_ORG_COMPANY_NAME_TXT | exists |
| PRJ_TB_COMPANY_ADDRESS_TXT | PRJ_ORG_COMPANY_ADDRESS_TXT | exists |
| PRJ_TB_CONTRACTOR_NAME_TXT | PRJ_ORG_CONTRACTOR_NAME_TXT | add |
| PRJ_TB_CONTRACTOR_ADDRESS_TXT | PRJ_ORG_CONTRACTOR_ADDRESS_TXT | add |
| PRJ_TB_MEP_CONSULTANTS_NAME_TXT | PRJ_ORG_MEP_CONSULTANTS_NAME_TXT | add |
| PRJ_TB_MEP_CONSULTANTS_ADDRESS_TXT | PRJ_ORG_MEP_CONSULTANTS_ADDRESS_TXT | add |
| PRJ_TB_STRUCTURAL_CONSULTANTS_NAME_TXT | PRJ_ORG_STRUCTURAL_CONSULTANTS_NAME_TXT | add |
| PRJ_TB_STRUCTURAL_CONSULTANTS_ADDRESS_TXT | PRJ_ORG_STRUCTURAL_CONSULTANTS_ADDRESS_TXT | add |
| PRJ_TB_LOGO_PATH_TXT | PRJ_ORG_LOGO_PATH_TXT | add |

**NOT twins — leave on `PRJ_TB_*` (legitimately per-sheet / workflow, not org identity):**
`PRJ_TB_SHEET_NR_TXT`, `PRJ_TB_PAPER_SZ_TXT`, `PRJ_TB_SCALE_OVERRIDE_TXT`,
`PRJ_TB_TOTAL_NO_SHEETS_TXT`, `PRJ_TB_VARIANT_TXT`, `PRJ_TB_DISCIPLINE_TXT`,
`PRJ_TB_REVISION_NR_TXT`, `PRJ_TB_REVISION_DATE_TXT`, `PRJ_TB_REVISION_DESCRIPTION_TXT`,
`PRJ_TB_DRAWN_BY_TXT`, `PRJ_TB_CHECKED_BY_TXT`, `PRJ_TB_APVD_BY_TXT`,
`PRJ_TB_DATE_DRAWN_TXT`, `PRJ_TB_DATE_CHECKED_TXT`, `PRJ_TB_DATE_APVD_TXT`,
`PRJ_TB_DELIVERABLE_*` (4), `PRJ_TB_LAST_TRANSMITTAL_*` (2), `PRJ_TB_LAST_SYNC_*` (2),
`PRJ_TB_ISSUE_SUMMARY_TXT`, `PRJ_TB_DESIGN_STAGE_TXT` (ambiguous vs PRJ_ORG_PHASE),
`PRJ_TB_SHOW_*_BOOL` (4), `PRJ_TB_LOCK_BOOL`, `PRJ_TB_NOTES_LEGEND_REF_TXT`,
`PRJ_TB_SCHEMA_VERSION_TXT`.

**De-risked:** the `PRJ_ORG_*` GUID scheme is deterministic —
`uuidv5(namespace = a7c0b2e4-4d91-4a55-9c7e-7f6e5d4c3b2a, "PRJ_ORG_<NAME>_TXT")`
(verified against PRJ_ORG_CLIENT_NAME_TXT / _COMPANY_NAME_TXT / _PROJECT_CODE_TXT).
So the 8 new twins' GUIDs can be generated correctly-by-construction.

**Focused-session plan:** (1) decide the canonical `titleBlockParams` cell-key
convention (recommend: keys = the family param name, e.g. `PRJ_ORG_CLIENT_NAME_TXT`,
so LookupParameter hits directly), and rekey the 998 entries; (2) add the 8 new
`PRJ_ORG_*` twins to MR_PARAMETERS.txt (uuidv5, GROUP 13 PRJ_INFORMATION, TEXT);
(3) rebind the 11 org-identity labels in STING_TITLE_BLOCKS.json from `PRJ_TB_*` to
`PRJ_ORG_*`; keep `PRJ_TB_*` as deprecated aliases; (4) add a `TitleBlock_MigrateParams`
command copying `PRJ_TB_* -> PRJ_ORG_*` on ProjectInformation (SetIfEmpty);
(5) regenerate STING_TITLE_BLOCK_PARAMETERS.txt; (6) run TitleBlock_CreateAll +
verify the stamp fills from PRJ_ORG_* in Revit.

## Plumbing tag pipeline — audit follow-ups (branch claude/plumbing-tag-fixes)

FIX 1–4 from the plumbing ISO-19650 tagging audit **landed** on
`claude/plumbing-tag-fixes` (soil/vent pipes → SAN, hyphen-free seed PROD codes,
validator PROD allow-list additions, Plumbing Equipment + Pipe Insulation containers).
The two items below were deliberately left as follow-ups — coupling / ambiguity risk:

- **BUG-2 — live auto-tagging skips pipe/duct curves.** `StingAutoTagger`'s live
  category list (`Core/StingAutoTagger.cs`, ~line 1106-1131) omits `OST_PipeCurves`,
  `OST_FlexPipeCurves`, and `OST_DuctCurves`, so pipe/duct/flex curves are not
  real-time auto-tagged. Adding them is entangled with the MEP run-policy declutter
  guard on branch `claude/mep-tag-declutter-advice` (PR #395): adding the categories
  WITHOUT that guard would re-introduce one-tag-per-segment clutter live. Do it as a
  follow-up on top of PR #395, not in the plumbing-tag-fixes branch.

- **BUG-3 — unassigned pipes fall back to the disc-default SYS.** A pipe with no
  assigned Revit piping system falls back to the discipline-default SYS; for the "M"
  default that is "HVAC", so unconnected domestic-water / drainage pipes tag as
  Mechanical. No safe automatic fix (the pipe categories are shared between mechanical
  and plumbing) — treat as "assign piping systems before tagging". Advisory only.
---

## ISO 19650 consolidation — deferred work (branch `claude/iso19650-consolidation`)

Work packages WP0-WP5 and WP7 (partial) landed on that branch; the evidence base is
[`ISO19650_DOC_FOLDER_REVIEW.md`](ISO19650_DOC_FOLDER_REVIEW.md), the work order is
[`AGENT_FIX_PROMPT_ISO19650_CONSOLIDATION.md`](AGENT_FIX_PROMPT_ISO19650_CONSOLIDATION.md),
and per-package status is in [`CONSOLIDATION_PROGRESS.md`](CONSOLIDATION_PROGRESS.md).
Nothing below was attempted-and-reverted; these are packages the session did not reach.

### Dispatch drift — 183 unreachable panel tags (from WP7)

A parity sweep found **183 panel command tags that resolve in neither
`Core/WorkflowEngine.ResolveCommand` nor `UI/StingCommandHandler`** — mostly the
`Hvac_*`, `Elec_*`, `Circuit_*`, `Lite_*`, `Photo_*`, `Rprt_*`, `PlumbSym_*` and
`Plumb_*` families. They work from their own panel button, but a workflow preset naming
one resolves to null and the step is reported failed.

They are recorded in `tools/dispatch_parity_baseline.txt`, and
`tools/check_dispatch_parity.ps1` fails only on NEW drift so the number cannot grow
silently. Closing the gap needs a per-command decision (alias vs genuinely panel-only),
not a blanket alias pass. Remove a tag's line from the baseline when you wire it.

### WP6 — `Core/StingPaths.cs` path service + path-discipline gate — **COMPLETE**

`Core/StingPaths.cs` is the single path API (`Cde` / `Meta` / `Data` / `Staging` / `Recycle`
/ `Export` / `ExportFile`) delegating to `ProjectFolderEngine`. All 43 hand-built
`<projDir>/_BIM_COORD` sibling occurrences across 37 files were migrated onto `StingPaths.Meta`;
`tools/check_path_discipline.ps1` + `path_discipline_baseline.txt` (now zero) hard-ratchet the
build against any new sibling. The cross-document `_rootPath` cache, the `OptionFolderManager`
and `DesignOptionRegistry` `20_MISC/_BIM_COORD` nesting, `CorporateLibrary.Push`'s relative-path
bug, and the sustainability-cluster relocate/recreate bug were all fixed along the way — the two
sustainability POCOs stayed Revit-free (they now take the resolved dir; callers resolve via
`StingPaths`). See `docs/CONSOLIDATION_PROGRESS.md` → "WP6".

**Still open (separate seam):** `ClashManagerDialog` reads `clashes.json` from `_data/_BIM_COORD`
but the clash writers (`ClashRunCommand`, `ClashXlsxExportCommand`) write to the `20_MISC` export
dir — a read/writer mismatch that predates WP6 and needs both sides aligned (not just a path move).

### WP7 remainder — shared `Run<T>` helper

Five copy-paste `Run<T>()` helpers across the HVAC / Plumbing / LPS / Sustainability /
Electrical handlers should collapse into one shared internal helper in `UI/`.

### WP8 — Document Manager unification (the ISO 19650 core)

**Done:**
- **One vocabulary (WP8.2).** `Iso19650Vocabulary.SuitabilityLabels` is the single source
  of suitability codes; `BIMManagerEngine.SuitabilityCodes` derives from it. It was
  extended to the exact union of the two old tables (added S5/CR/AB with register-context
  meanings) so it is a strict superset — no register row is orphaned — and the register
  now offers the A/B authorization codes it lacked. `DocStatusCodes` was intentionally
  *not* merged: it mixes issue-purpose codes (IFC/IFT/…) with suitability and gives "AB" a
  conflicting meaning, so folding it in would mislabel data. `MidpEngine.SuitRank` (a
  code→rank map) and `TemplateManifest.SuitabilityScheme` (a config string) are different
  shapes and stay as-is.
- **Role gate now real (WP8.3).** `WorkflowEngine.Transition` enforces the transition's
  `allowed_roles` (resolved via `RoleBasedAccessControl.GetCurrentUserRole()`; K/C are CDE
  admins; empty = any). Denials are audit-logged (`wf.transition_denied`) and throw.

**Landed (additive):** read-only **unified register view** — `Core/DocumentRegister.cs`
normalises both stores into one de-duplicated `RegisterEntry` list, exported via the
`DocRegister_Unified` command (BIM tab → "Unified Register"). Touches neither write path.

**Landed:** the register merge ships as **`Register_Consolidate`** (dry-run → canonical
`_data/register.json`, sources intact). The **Document Manager** now reads the LIVE unified
register (both stores merged fresh) once `register.json` exists, so it shows one register
without going stale. **Still open:** the **BCC** stays deliverable-focused on purpose — its
grid runs deliverable-lifecycle bulk actions, so register-only rows must not be injected into
it; a read-only "all documents" surface in BCC would need its own UI + Revit verification.
Eventually the two source stores retire once the UIs write through the canonical one.
- **Run the deliverable state machine end-to-end — DONE.** `DeliverableLifecycle` now
  drives `Planscape.Docs.Workflow.WorkflowEngine`: `Issue` starts the instance (at WIP),
  every CDE-changing action walks the role-gated `WIP→Shared→Published→Archived` machine one
  `Transition` per hop, `Cancel` jumps to `Archived` via a new `cancel` transition, and
  `P→C` revision promotion fires on publish-to-PUBLISHED. A genuine role denial blocks the
  lifecycle change (returns `Ok=false`); undefined paths / an unstarted engine never block,
  so the workflow is a tracking overlay that ENFORCES only when a transition declares
  `allowed_roles` — the default `deliverable_issue_default.json` leaves them empty (permissive)
  so existing publishing is not broken; organisations opt into strict Check→Review→Approve
  gates by adding `allowed_roles` in their project workflow override. `Supersede`/`Replace`
  still mark status directly (they don't map to a CDE state) and are left out of the drive.
- **Close the physical loop — DONE.** `TemplateEngine.RenderToCde` renders a deliverable into
  its CDE container `<state>/<discipline>/Documents/` (via `StingPaths.Cde`);
  `LifecycleCommandHelper` registers the rendered file (`AutoRegisterExport`) so the register's
  `file_reference` equals the physical location; and **move-on-transition** is wired —
  `TemplateEngine.PurgeStaleRenders` removes the deliverable's render from the other CDE states
  (and stale-dated copies) after each (re-)render, so a Publish no longer leaves an orphaned WIP
  copy — the document lives in exactly one CDE state. Transmittal/notice renders stay in
  `generated/` (not discipline-scoped deliverables). **Minor remaining:** `AutoRegisterExport`
  adds a register row per render, so a deliverable's lifecycle accrues rows whose old
  `file_path`s point at purged files — a dedup/update-in-place on the register would tidy that;
  and the discipline subfolder uses the raw code (`A`) not the setup folder name
  (`A_Architectural`).
- **Acknowledgement capture** for transmittals (drives the workflow `acknowledge`
  transition — now that the role gate is enforced).

WP5 partially de-risked this: auto-registration now has one method and one schema, and
lifecycle transitions mirror to the server event-driven.

### WP9 — CDE-first tree + ES root identity

States at the top of the tree with content types inside them, dropping the
`05_MODELS...20_MISC` numbered folders and the per-folder project-code suffixes; an
Extensible-Storage root-identity stamp replacing the 8-char filename-prefix multi-model
heuristic; and a `Folders_ConsolidateAll` migration wizard with a dry-run report.

**Sequencing note:** WP9 changes the physical tree, so it should follow WP6 — with
`StingPaths` in place the layout change is a change to one resolver (`ProjectSetup`
defaults + `ExportRoutes`) rather than to every writer.

**Landed (additive):** the ES root-identity **stamp** — `StingProjectRootSchema` stores the
resolved root (relative to the .rvt) on `ProjectInformation`; `GetRootPath` prefers it as
step 0, so a project-number rename no longer forks a new `<CODE>` tree. Read is
transaction-free with graceful fallback; `EnsureStamped` writes best-effort from
`OnDocumentOpened`. Multi-model guid-sharing is deferred.

**Landed:**
- **CDE-first tree + routing.** `ProjectFolderMode.CdeFirst` + `ProjectSetup.CreateCdeFirst`
  nest content types inside the CDE states; the shared `ResolveRoutedFolder` understands the
  `STATE|ContentType` route encoding (BIM/Mini routes have no `|`, so they resolve exactly as
  before — zero change for existing projects). A greenfield gate in `LoadOrBootstrapSetup`
  adopts it only for brand-new projects (no ES stamp / no root / no legacy folders), overridable
  via `CDE_FIRST_LAYOUT=false`.
- **Migration wizard.** `Folders_ConsolidateAll` — `ScanLegacy` dry-run preview + confirmation
  before `MigrateFromLegacy` runs. Writes a report CSV; never auto-runs.

**Multi-model guid-sharing — assessed, intentionally not shipped as an auto-adopt.** Robust
sibling sharing already comes from `LoadOrDetectSetup`'s sibling scan (folder-based: a sibling
model adopts a neighbour's root via its `_data/project_setup.json`) plus the ES stamp (per-model
root stability). A blanket "adopt any sibling root with a matching guid" would either duplicate
that scan or *risk merging two genuinely-separate projects that happen to share a folder* — the
hard part is a reliable project-grouping signal, not the guid. So the safe path is: keep the
setup-scan + ES stamp for the common case, and add explicit guid-adopt only behind a real
grouping signal (shared project number, or user-declared grouping). The fragile 8-char
filename-prefix heuristic (`ProjectFolderEngine.cs` sibling block) is now superseded by the
subdir scan and could be removed in a focused cleanup.

**In-Revit verification** of the CDE-first routing + the two migration commands + the register
repoint is required before merge — the full runnable checklist is
[`docs/ISO19650_INREVIT_VERIFICATION.md`](ISO19650_INREVIT_VERIFICATION.md). Note: discipline
order under CdeFirst is `<state>/<contentType>/<disc>`; revisit if `<state>/<disc>/<contentType>`
is preferred.

### WP10 — HTTP + storage hygiene

Pooled client for `PluginTelemetry` (currently `new HttpClient()` per call), routing the
ad-hoc unauthenticated clients through `PlanscapeServerClient`, and resolving the
workflow-state dual storage (ES `StingWorkflowStateSchema` vs `workflow_state.json`) —
pick one, migrate, document the storage-ownership rule in CLAUDE.md.

### Visibility Center — open items (Phase 232)

Delivered in Phase 232 (see [`CHANGELOG.md`](CHANGELOG.md)); these are the gaps left behind.

- **Show-only by category in Saved mode is a blocker, not a feature.** A `ParameterFilterElement`
  can only act on the categories it binds to, so it cannot hide elements of *other* categories.
  The engine reports this and points at Temporary mode. Implementing it properly means binding an
  inverse filter to every other filterable category — viable, but heavy enough to want a real user
  asking for it first.
- **The isolate filter does not round-trip.** Show-only in Saved mode emits one combined
  `STING VIS - NOT (isolate)` filter whose rule tree is the negation of the whole set, so
  `TryParseFilterName` returns false for it. It is still found and deleted by prefix, so purge and
  reset are unaffected; only reconstructing a set *from* an existing filter would miss it.
- **`VisibilityTarget.SelectedViews` and `ViewTemplate` are declared but only partly wired.**
  `ActiveView` and `AllViewsOnSheet` are reachable from the dropdown; `ViewTemplate` is reachable
  only via the separate `Vis_ApplyToTemplate` command, and `SelectedViews` has no UI at all.
- **No in-Revit verification yet.** The runner's §4 checklist (hide by category, hide by ZONE, two
  tokens combined, isolate, Reset-all clearing both mechanisms, preset save/reload,
  `Vis_PurgeFilters` leaving the project clean, and the view-template-locked blocker) has not been
  run against a real model.
- **Out of scope by decision, logged here as asked:** worksets and phases as visibility axes;
  design-option visibility (already `DesignOptions_LockView`); per-element graphic overrides beyond
  show/hide (that is `RevitVgEditor`'s job); syncing visibility state to Planscape Server.
## ArchiCAD ↔ Planscape ↔ Revit ↔ ArchiCAD round-trip — cross-tool gaps (deep review)

Four-leg audit of the full loop (StingBridge/Python · Planscape.Server/C# · StingTools/Revit-C# ·
shared `stingtools_core`). **Verdict: today it is two disjoint half-loops (ArchiCAD↔Planscape and
Revit↔Planscape) that share a database but not an element identity, a merge policy, or a return path.**
Both *push* directions work; both *return* legs are missing/stubbed; and the working push legs do not
share a stable identity, so a change made in one tool cannot be re-found in another. The design intent —
key everything on the **IFC GlobalId** and unify via `ExternalElementMapping` — is correct and
documented in the code, just not wired end-to-end.

Hop status: **①ArchiCAD→Planscape** ✅ push works · **②Planscape→Revit** ❌ pull transport exists
(`PlanscapeServerClient.GetElementsDeltaAsync`) with zero callers, no native write-back ·
**③Revit→Planscape** ⚠️ push works but keyed on a Revit-minted GUID (or none) not the ArchiCAD
GlobalId · **④→ArchiCAD** ❌ `StingTools.ArchiCAD` is a scaffold; StingBridge live "Planscape-wins"
writes back stale local data; the IFC path writes a `_sting.ifc` side-file, never the authored model.

| ID | Gap | Severity | Where | Status |
|---|---|---|---|---|
| **R1** | **Identity doesn't survive the loop.** Same physical element = two un-mergeable `TaggedElement` rows (Revit keyed on `RevitElementId`/`UniqueId`; IFC/ArchiCAD keyed on `IfcGlobalId`), and the Revit row never even stores the GlobalId (`TagSyncController.MapDtoToEntity` drops `dto.IfcGlobalId`). `ExternalElementMapping` is populated but never read to merge them. Revit re-mints a fresh IfcGUID on export instead of carrying the ArchiCAD GlobalId (`ARCHICAD_GUID` vs `IFC_GLOBAL_ID_TXT`). The `/changes` feed keys on `TaggedElement.UniqueId`, so a Revit edit reaches a Python/ArchiCAD host with an unmatchable key and is dropped as `absent`. | CRITICAL | server + Revit + core + bridge | OPEN |
| **R2** | **Both return legs unimplemented.** Planscape→Revit tag write-back has no caller and no native-stamp path; →ArchiCAD does not exist (`StingTools.ArchiCAD` C++ stub: no HTTP impl, placeholder dialogs, `CollectElements()` returns empty, stale base URL `api.planscape.app`). StingBridge live conflict "Planscape wins" writes back the *same local values* because it only pulls timestamps (`get_element_timestamps`), not remote values. | HIGH | Revit + StingTools.ArchiCAD + bridge | OPEN |
| **R3** | **`/ifc/data` never refreshed compliance/`LastSyncAt`**, so ArchiCAD/Bonsai-only projects read 0%/stale forever and the 6-hourly `ComplianceSnapshotJob` (filters on `LastSyncAt`) skipped them. | HIGH | server | **CLOSED** — `IfcIngestService.UpdateProjectComplianceAsync` (this branch) |
| **R4** | **Divergent implementations.** The shared `stingtools_core` (reconcile engine, cursor `/changes` feed, digest-tiebreak convergence, conflict sidecar) spans **only the Python hosts**. Revit reimplements it in C# on a *different* pull endpoint (watermark `GET /tagsync/elements`), with *no* `ReconcileEngine`, and a max-per-key SEQ merge whose own comment admits it "cannot stop Revit and StingBridge minting the same number concurrently". | HIGH | Revit + core | OPEN |
| **R5** | **Provenance not stamped.** `TaggedElement.Source` is written only by the .ifc file-upload path; both live doors (`/tagsync`, `/ifc/data`) leave it null, so the server can't tell a Revit-origin row from an ArchiCAD-origin one. | MEDIUM | server | OPEN |
| **R6** | **Deletions never propagate.** No tombstones in the feed or `ChangeDelta`; an absent GlobalId is dropped. The loop can create but never retract. | MEDIUM | core + server | OPEN |
| **R7** | **Units.** `level_for_storey_name` is metre-only while IFC is often mm (fixed for the Bonsai/core path in PR #639; still live elsewhere); Revit geometry GLB ships raw **feet** (3.28× off vs metric ArchiCAD, `GeometrySyncHandler`); `GeorefDescriptor` mixes mm/m. | MEDIUM | Revit + core + bridge | PARTIAL |
| **R8** | **Two conflict policies.** The `/tagsync` door has real LWW + `Version` + `SyncConflict` log; the `/ifc/data` door only "skips if strictly older" (equal timestamps overwrite, no version bump, no audit). Two overlapping identity tables (`ExternalElementMapping` + self-marked-SUPERSEDED `ElementGlobalIdRegistry`) also coexist. | MEDIUM/LOW | server | OPEN |
| **R3-fu** | **Follow-up to R3:** `UpdateProjectComplianceAsync` duplicates `TagSyncController.ComputeComplianceAsync`'s scalar formula — extract one shared compliance calculator so the two doors (and `ComplianceSnapshotJob`, a third copy) can never drift. Also: this recompute counts `TaggedElement` rows, which R1 double-counts for mixed Revit+IFC projects. | LOW | server | OPEN |

**Recommended order to actually close the loop:** (1) make the IFC GlobalId the one true key end-to-end
— Revit carries the ArchiCAD GlobalId through on import instead of re-minting, and the server dedups
`TaggedElement` + has pull/issues/compliance consume `ExternalElementMapping`; (2) wire the two return
legs (a caller for `GetElementsDeltaAsync` that stamps `ASS_*` params; a real →ArchiCAD writer — the
complete `ArchiCadHostAdapter.apply_remote_change` already exists but is wired only into tests); (3)
R3 (done); then tombstones (R6), unit normalization (R7), and unifying conflict policy on the core
`ReconcileEngine` (R4/R8). Items (1)–(2) are architectural (identity redesign + two new write paths)
and warrant a short spec + owner decisions (dedup strategy; whether Revit should consume the Python
core via a service or stay C#).

**Spec:** the R1/R2 implementation plan (work items per codebase, owner decisions D1–D6, phasing, and
end-to-end acceptance tests) is written up in [`ROUND_TRIP_R1_R2_SPEC.md`](ROUND_TRIP_R1_R2_SPEC.md).

**Status (2026-08-16): the round-trip *identity* axis is CLOSED** — R1.1–R1.4 + 2b (identity), R3
(compliance), and SB-1b all shipped and merged (PRs #654/#655/#657/#658/#659/#660). What remains open is
a *different* axis — spatial placement (georeferencing/units/true-north) and federation reliability —
captured next.

## Federation coordinates, placement & hidden issues (deep review 2026-08-16)

Six-audit review of **spatial** federation (Revit + ArchiCAD + Tekla → Planscape viewer) — distinct from
the identity work above. Full findings + evidence in [`COORDINATION_AUDIT_FINDINGS.md`](COORDINATION_AUDIT_FINDINGS.md)
PART IV; the ordered fix brief for a terminal agent is
[`PERFECT_PLACEMENT_PROMPT.md`](PERFECT_PLACEMENT_PROMPT.md).

**Verdict:** the alignment machinery exists and is sound (`ModelTransformMath`, `IfcAlignmentValidator`,
`AutoAlignService`, a viewer that applies per-model T·R·S) but is **not wired end-to-end**. Today models
overlay correctly only if every tool pre-exported in identical shared coordinates + units.

### Coordinate placement gaps (fix brief: `PERFECT_PLACEMENT_PROMPT.md`, P1–P6)

| ID | Gap | Sev | Status |
|---|---|---|---|
| **B1** | Confirmation gate blocks auto-alignment — viewer no-ops unless `isConfirmed`; both auto paths store `IsConfirmed=false`, so a correctly computed transform never shows until manually confirmed. **Highest-leverage.** | CRIT | OPEN (P1) |
| **B2** | Primary Revit-GLB path (`ModelsController.Upload`) computes no transform; geometry exported about internal origin / project-north; the coord sidecar Revit computes is never uploaded. | CRIT | OPEN (P2) |
| **B3** | Unit reconciliation dead code (`IfcIngestController` `scaleFactor` returns 1.0 both branches); feet-vs-metric unreconciled; two Revit GLB writers disagree (m vs mm). | HIGH | OPEN (P3) |
| **B4** | Two inconsistent server translation conventions (each-to-origin vs relative-to-reference). | MED | OPEN (P5) |
| **B5** | StingBridge/core send no georef — `GeorefDescriptor` defined but zero consumers, under-populated, mixes mm/m. | HIGH | OPEN (P4) |
| **B6** | SceneNode world AABBs recomputed only on manual PUT, not after auto-transform → stale culling/clash bounds. | MED | OPEN (P5) |
| **Tekla** | No native producer; Tekla-authored IFC upload is the only route (verify + document). | — | out of scope (P6) |

### Hidden issues (separate tracks — NOT in the placement brief)

**Security — cross-project (within-tenant) broken access control (HIGHEST URGENCY; spun off as a task).**
`ModelTransformController`, `CoordinateSystemController`, `AlignmentController`, `SceneNodesController` GET
endpoints, `ModelDiffController`, and `FederatedModelHub.JoinProject` carry only `[Authorize]` and filter
by tenant — **missing the project-membership check** `ModelsController`/`IfcIngestController` apply. A
member of one project can read/modify another project's transform/CRS/geometry within the same tenant.
Also: `AutoAlignService` doesn't guard `IsConfirmed`, so auto-align overwrites a coordinator's confirmed
transform. (Cross-*tenant* is sound.)

**Reliability / lifecycle.**
- Revit geometry deltas silently, permanently lost on any failure (drain-then-fire-and-forget, no requeue) — `GeometrySyncHandler.cs:106`.
- Auto-delta covers only 9 categories; `LIVE_CLASH_TRIGGERS_ENABLED=false` disables all geometry sync — `LiveClashUpdater.cs:101`.
- Deletes don't propagate (no ArchiCAD/IFC tombstones); server ghosts accumulate.
- No versioning/supersede; soft-delete doesn't cascade; the promised 30-day purge job doesn't exist; deleted models keep rendering; `Force` flag dead.
- Advertised upload caps (2 GB ingest / 256 MB delta) exceed the 200 MB multipart parser → silent 400s.
- Delta apply non-atomic + non-idempotent; cross-tool clash absent; `FederationLinkedWalker`/`AsBuiltReconciler` orphaned.

**Recommended sequencing:** (1) security authz fix (small, live bug — task chip filed); (2) placement
brief P1–P6; (3) reliability track (geometry silent-loss + deletes + versioning).
## Federation hardening — open after the A/B/C tracks (2026-08-16)

Follow-ups identified while closing the federation-hardening tracks. Closed items
are written up in [`COORDINATION_AUDIT_FINDINGS.md`](COORDINATION_AUDIT_FINDINGS.md) PART IV.

- **`IfcController` has no project-membership gate.** It carries `[Authorize]`
  and checks `Project.TenantId`, but not `[ProjectAccess]` / 
  `RequireProjectMemberAsync` — the same defect class Track A fixed on
  `ModelTransform` / `CoordinateSystem` / `Alignment` / `SceneNodes` /
  `ModelDiff`. Deliberately not fixed in the C3 PR: it was outside the stated
  scope, and adding the gate changes the plugin's push path for any service
  account that is not a project member, which needs verification rather than
  assumption. Same question applies to `TagSyncController` and
  `FederatedModelController`'s sibling endpoints — audit the whole ingest
  surface in one pass rather than piecemeal.
- **`NotificationHub.JoinProject` resolves its tenant off the ambient
  `ITenantContext`**, which reads `IHttpContextAccessor` — not reliably
  populated inside a SignalR hub method. It works today; `FederatedModelHub`
  (Track A2) was given an explicit JWT-derived tenant instead. Align the two.
- **`ViewStylePack.Checksum` is declared and never computed or verified**
  (pre-existing; see CLAUDE.md). Wire it or drop it.
- **`ModelPurgeJob` is unproven against object storage.** The unit tests use a
  recording stub; a run against real MinIO/S3 (including the deferral path when
  a delete fails) has not happened.
- **`RevitGeoref.Read` is unverified against a live Revit document.** It needs a
  Revit session; the numbers it produces are asserted only from the server side
  against hand-written inputs.
- **`docs/PERFECT_PLACEMENT_PROMPT.md` does not exist** despite being cited as
  the Track B reference. Either write it or stop citing it.
- **`align-audit.mjs` has 6 failing checks on `main`.** Verified pre-existing by
  running the harness at `origin/main` in a scratch worktree: same 6 failures,
  and `coordination-viewer.js` is untouched by the federation-hardening tracks.
  They cover navigation literals (`/app/#models?project=`, `/app/#overview`), the
  non-glTF format guard, and the `element` / `camera` query params — i.e. the
  viewer SPA drifted away from what PLANSCAPE_ALIGNMENT_AUDIT.md pinned. Nothing
  fails CI on it today, which is why it drifted. Either re-fix the six or retire
  the assertions; leaving a harness that always fails trains people to ignore it.
