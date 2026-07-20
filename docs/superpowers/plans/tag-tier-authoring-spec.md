# Tag Tier — Route B Authoring Spec

> Generated 2026-07-03 from the real per-mode tag-config CSVs (`StingTools/Data/STING_TAG_CONFIG_v5_0_{ARCH,MEP,STR,GEN,HEALTH}[_DesignConstruction].csv`) and the family→template maps in `StingTools/Tags/TagFamilyCreatorCommand.cs` (`CategoryTemplateMap`, `CategoryDisplayName`, `CategoryCsvFamilyKey`, and the five variant tuple arrays). This is task **B1** of the Route B spine in `2026-07-03-tag-tier-automation.md`.

This document tells a human, mechanically, **which master tag-template families to author** and **which stacked, styled Label rows to place in each** so that regeneration (`CreateTagFamiliesCommand` → `AuthorLabelsMulti`) can bind params + wire the depth×mode gate formulas across all 206 families with zero per-family Family-Editor work.

---

## 1. Distinct template shapes + family counts

The authoring cost is bounded by the number of distinct Revit `.rft` annotation shapes families are built from, **not** by the 206 families. Each `.rft` is one host geometry; every family built from it can carry the same stacked-Label layout because empty (wrong-mode / below-depth) rows collapse to `""`.

- **Total families:** **206** (`TagFamilyConfig.TotalFamilyCount` = 121 base categories + 8 tie-in + 3 discipline-sheet + 4 structural-variant + 9 MEP-variant + 56 healthcare-variant, minus duplicate-name collapses → 206 distinct `.rfa`).
- **Distinct `.rft` template shapes:** **52.**
- **Families carrying T4–T10 tier rows in the CSVs:** **146** of 204 CSV-parsed families. The other **58** are declared (mostly HEALTH `TAG_FAMILY,` catalog rows) with no per-family tier customisation — at regeneration they resolve to their **base category's** plan via `PerFamilyTierMap.Resolve` name-strip / category fallback, so they reuse their parent shape's master template. No extra templates needed for them.

### Template shapes ranked by family count

| # | `.rft` template | Families | Fams w/ own tier rows | Union rows the master must carry |
|---|---|---:|---:|---:|
| 1 | Generic Tag.rft | 67 | 50 | 65 |
| 2 | Specialty Equipment Tag.rft | 11 | 1 | 56 |
| 3 | Plumbing Fixture Tag.rft | 9 | 2 | 63 |
| 4 | Generic Model Tag.rft | 9 | 3 | 56 |
| 5 | Mechanical Equipment Tag.rft | 8 | 3 | 69 |
| 6 | Electrical Equipment Tag.rft | 8 | 8 | 67 |
| 7 | Pipe Tag.rft | 7 | 3 | 65 |
| 8 | Room Tag.rft | 7 | 3 | 50 |
| 9 | Duct Tag.rft | 6 | 5 | 66 |
| 10 | Structural Framing Tag.rft | 6 | 6 | 60 |
| 11 | Wall Tag.rft | 5 | 2 | 56 |
| 12 | Roof Tag.rft | 5 | 5 | 56 |
| 13 | Lighting Fixture Tag.rft | 4 | 1 | 56 |
| 14 | Stair Tag.rft | 4 | 4 | 56 |
| 15 | Cable Tray Tag.rft | 3 | 2 | 56 |
| 16 | Door Tag.rft | 3 | 1 | 56 |
| … | (36 further single-/double-family shapes) | 1–2 each | | 52–63 |

The remaining 36 shapes each back 1–2 families (Duct Fitting, Air Terminal, Sprinkler, Conduit, Window, Floor, Structural Foundation, Casework, Furniture, Material, Wire, etc.). Their master is authored from the same union pattern; union sizes 52–63 rows.

---

## 2. Overlap verdict (THE key question) — **one template per shape is viable**

**Verdict: HYBRID leaning strongly to one-template-per-`.rft`-shape. Author 52 master templates, one per `.rft`, each carrying the group's UNION of styled rows. This is NOT per-family authoring.**

### Evidence

Within every `.rft` group, families share the overwhelming majority of their T4–T10 content. Measured on the real CSVs:

- **Mean pairwise Jaccard** of content-param sets **within a shape group ranges 0.76 – 1.00** (most groups ≥ 0.90). Families in a shape are near-identical in what they display.
- **Group-common intersection** covers **57 %–100 %** of each group's union:
  - Generic Tag.rft (67 fams): intersection = 36 params = **57 %** of the 63-param union.
  - Pipe Tag.rft: intersection = 55 = **87 %** of union.
  - Duct Tag.rft: intersection = 51 = **80 %** of union.
  - Mechanical Equipment Tag.rft: intersection = 51 = **76 %** of union.
  - Door / Roof / Stair / Wall / Room / Floor groups: **100 %** (single content profile).
- The variability is **concentrated in three tiers**: T5 (cost/procurement, 23 distinct params), **T7 (fabrication/QC, 29 distinct params)**, and T9 (as-built, 10). T4/T6/T8/T10 are almost entirely common. The family-specific rows are things like `HVC_SEGMENT_ROLE_TXT` (duct), `STR_BAR_MARK_TXT` (rebar), `PLM_PRESSURE_TEST_REF_TXT` (pipework), `HVC_REFRIGERANT_CHARGE_KG` (mech equip).

### Why the union approach makes it "one template per shape"

Because a below-depth or wrong-mode row already resolves to `""` and collapses (no gap), **over-provisioning rows on a template is free**. So a shape's master simply carries the **union of every styled row any family in that shape needs** (group union = 50–69 rows). At regeneration, `AuthorLabelsMulti` binds only the params that family's plan actually references and gates the rest to empty. A duct family and a duct-insulation family share one "Duct Tag" master even though their T7 rows differ — the master carries both T7 sets; each family lights up only its own.

Global figures confirm a single universal template is also *technically* possible but not recommended:
- **Global union of all T4–T10 params across all 146 families = 92** distinct params.
- **Global common core = 13 params** (T4 COMM×3, T6 CBN×2, T7 QC/spool/fab×3, T8 clash×3, T9 health×1, T10 IFC×1). Only ~24 % of any one family's rows come from this global core, so a single 92-row universal template would carry ~40 % dead rows for most families and mix incompatible T7 styling. Per-`.rft` masters (union 50–69) are the right granularity — they align with the geometry Revit already forces (one `.rft` per family) and keep each master's dead-row count low.

**Bottom line for the plan:** Route B is **~52 templates**, not 206 and not 15. Practically it is even lighter: the 16 single-content shapes (Door/Wall/Roof/Room/Floor/Stair/etc., Jaccard 1.00) are trivial 50–56-row masters, and the 6 "hard" MEP/structural shapes (Duct, Pipe, Mech Equip, Elec Equip, Struct Framing, Plumbing Fixture) carry the 60–69-row unions with the T5/T7/T9 variability folded in.

---

## 3. Fully-worked representative template — **Generic Tag.rft** (highest family count: 67)

Author **one** master family from `Generic Tag.rft`. Stack the 65 Label elements below in tier order; within a tier, DC-mode rows first then Handover-mode rows. Bind each Label to the named parameter; set its text **Style / Colour / Size** exactly as tabled; apply the prefix/suffix as literal text on the label (STING affix convention). `AuthorLabelsMulti` writes the `if(or(and(TAG_PARA_STATE_N_BOOL, HANDOVER_MODE_*_BOOL), …), PARAM, "")` gate onto each — the author does **not** hand-write formulas, only places + styles + binds.

Style/colour convention that falls out of the data (use as a sanity check): **T4 = BOLD BLUE, T5 = NOM PURPLE (payment sub-rows GREEN, variation RED, stale ORANGE), T6 = ITALIC GREEN, T7 = BOLD ORANGE, T8 = BOLD RED, T9 = ITALIC GREY, T10 = NOM GREY.** All T4–T10 rows are size **2.0 mm**.

| row | tier | mode | parameter | style | colour | size | prefix | suffix |
|---:|:--|:--|:--|:--|:--|:--|:--|:--|
| 1 | T4 | DC | ASS_DESIGN_OPTION_TXT | BOLD | BLUE | 2.0 | `Option:` | |
| 2 | T4 | DC | ASS_MODEL_REF_TXT | BOLD | BLUE | 2.0 | `Ref:` | |
| 3 | T4 | DC | ASS_KEYNOTE_TXT | BOLD | BLUE | 2.0 | `Key:` | |
| 4 | T4 | HO | COMM_STATE_TXT | BOLD | BLUE | 2.0 | `Comm:` | |
| 5 | T4 | HO | COMM_DATE_TXT | BOLD | BLUE | 2.0 | `on` | |
| 6 | T4 | HO | COMM_OPERATIVE_TXT | BOLD | BLUE | 2.0 | `by` | |
| 7 | T5 | DC | ASS_CAPACITY_TXT | NOM | PURPLE | 2.0 | `Cap:` | |
| 8 | T5 | DC | ASS_POWER_RATING_TXT | NOM | PURPLE | 2.0 | `Power:` | |
| 9 | T5 | DC | ASS_FLOW_RATE_TXT | NOM | PURPLE | 2.0 | `Flow:` | |
| 10 | T5 | DC | ASS_ITEM_CODE_TXT | NOM | PURPLE | 2.0 | `Item:` | |
| 11 | T5 | HO | CST_UG_PRICE_UGX_DISP_TXT | NOM | PURPLE | 2.0 | | `UGX` |
| 12 | T5 | HO | CST_INTL_PRICE_USD_DISP_TXT | NOM | PURPLE | 2.0 | `/ ` | `USD` |
| 13 | T5 | HO | CST_QUOTE_REF_TXT | NOM | PURPLE | 2.0 | `Quote:` | |
| 14 | T5 | HO | ASS_CST_UNIT_RATE_NR_DISP_TXT | NOM | PURPLE | 2.0 | `Rate:` | |
| 15 | T5 | HO | ASS_CST_CURRENCY_TXT | NOM | PURPLE | 2.0 | ` ` | |
| 16 | T5 | HO | ASS_CST_FX_TO_BASE_NR_DISP_TXT | NOM | PURPLE | 2.0 | `FX:` | |
| 17 | T5 | HO | ASS_CST_FX_DATE_DT | NOM | PURPLE | 2.0 | ` FX date:` | |
| 18 | T5 | HO | ASS_CST_AS_OF_DT | NOM | PURPLE | 2.0 | ` As of:` | |
| 19 | T5 | HO | ASS_CST_STALE_BOOL | BOLD | ORANGE | 2.0 | `Stale:` | |
| 20 | T5 | HO | ASS_CST_STALE_REASON_TXT | NOM | ORANGE | 2.0 | ` - ` | |
| 21 | T5 | HO | ASS_PMT_PCT_COMPLETE_NR_DISP_TXT | BOLD | GREEN | 2.0 | `Cmpl:` | `%` |
| 22 | T5 | HO | ASS_PMT_CERT_NO_NR_DISP_TXT | NOM | GREEN | 2.0 | ` Cert#:` | |
| 23 | T5 | HO | ASS_PMT_CERT_DATE_DT | NOM | GREEN | 2.0 | ` Certified:` | |
| 24 | T5 | HO | ASS_PMT_LAST_VALUED_DT | NOM | GREEN | 2.0 | ` Valued:` | |
| 25 | T5 | HO | ASS_VAR_NO_TXT | BOLD | RED | 2.0 | `Var:` | |
| 26 | T5 | HO | ASS_VAR_INSTRUCTION_DT | NOM | RED | 2.0 | ` Instr:` | |
| 27 | T5 | HO | ASS_VAR_VALUATION_NR_DISP_TXT | NOM | RED | 2.0 | ` Val:` | |
| 28 | T6 | DC | ASS_MANUFACTURER_TXT | ITALIC | GREEN | 2.0 | `Mfr:` | |
| 29 | T6 | DC | ASS_MODEL_NR_TXT | ITALIC | GREEN | 2.0 | `Model:` | |
| 30 | T6 | DC | ASS_EXPECTED_LIFE_YEARS_YRS | ITALIC | GREEN | 2.0 | `Life:` | `yr` |
| 31 | T6 | HO | CBN_B6_KG_CO2E_YR_DISP_TXT | ITALIC | GREEN | 2.0 | `B6:` | `kgCO₂e/yr` |
| 32 | T6 | HO | CBN_A1_A3_KG_CO2E_DISP_TXT | ITALIC | GREEN | 2.0 | `\| A1-A3:` | `kgCO₂e` |
| 33 | T6 | HO | CBN_RUNTIME_HRS_YR_NR_DISP_TXT | ITALIC | GREEN | 2.0 | `\| Run:` | `hr/yr` |
| 34 | T6 | HO | CBN_A4_KG_CO2E_DISP_TXT | ITALIC | GREEN | 2.0 | `\| A4:` | `kgCO₂e` |
| 35 | T7 | DC | ASS_INSTALL_DATE_TXT | BOLD | ORANGE | 2.0 | `Installed:` | |
| 36 | T7 | DC | CST_INSTALL_HRS_DISP_TXT | BOLD | ORANGE | 2.0 | `Hrs:` | `h` |
| 37 | T7 | DC | ASS_CST_INSTALL_UGX_NR | BOLD | ORANGE | 2.0 | `@` | `UGX` |
| 38 | T7 | HO | ASS_SPOOL_NR_TXT | BOLD | ORANGE | 2.0 | `Spool:` | |
| 39 | T7 | HO | ASS_FAB_STATUS_TXT | BOLD | ORANGE | 2.0 | `Fab:` | |
| 40 | T7 | HO | ASS_QC_INSPECTOR_TXT | BOLD | ORANGE | 2.0 | `QC:` | |
| 41 | T7 | HO | STR_WELD_PROCEDURE_REF_TXT | BOLD | ORANGE | 2.0 | `WPS:` | |
| 42 | T7 | HO | STR_BOLT_TORQUE_NM_NR_DISP_TXT | BOLD | ORANGE | 2.0 | `Tq:` | `Nm` |
| 43 | T7 | HO | STR_BAR_MARK_TXT | BOLD | ORANGE | 2.0 | `Mark:` | |
| 44 | T7 | HO | STR_BEND_SCHEDULE_REF_TXT | BOLD | ORANGE | 2.0 | `BBS:` | |
| 45 | T7 | HO | STR_CUTTING_LIST_REF_TXT | BOLD | ORANGE | 2.0 | `CL:` | |
| 46 | T8 | DC | ASS_CRITICALITY_RATING_NR | BOLD | RED | 2.0 | `Crit:` | `/5` |
| 47 | T8 | DC | ASS_ZONE_TXT | BOLD | RED | 2.0 | `Zone:` | |
| 48 | T8 | DC | ASS_LVL_COD_TXT | BOLD | RED | 2.0 | `Lvl:` | |
| 49 | T8 | HO | CLASH_TRIAGE_SEVERITY_NR_DISP_TXT | BOLD | RED | 2.0 | `Sev:` | `/5` |
| 50 | T8 | HO | CLASH_TRIAGE_CATEGORY_TXT | BOLD | RED | 2.0 | | |
| 51 | T8 | HO | CLASH_RESOLUTION_STATUS_TXT | BOLD | RED | 2.0 | `Res:` | |
| 52 | T9 | DC | ASS_WARRANTY_PARTS_TXT | ITALIC | GREY | 2.0 | `Parts:` | |
| 53 | T9 | DC | ASS_WARRANTY_LABOR_TXT | ITALIC | GREY | 2.0 | `Labor:` | |
| 54 | T9 | DC | ASS_WARRANTY_TXT | ITALIC | GREY | 2.0 | `Terms:` | |
| 55 | T9 | HO | ASBUILT_DEVIATION_MM_DISP_TXT | ITALIC | GREY | 2.0 | `Δ:` | `mm` |
| 56 | T9 | HO | ASBUILT_CAPTURE_DATE_TXT | ITALIC | GREY | 2.0 | `on` | |
| 57 | T9 | HO | HEALTH_SCORE_LAST_NR_DISP_TXT | ITALIC | GREY | 2.0 | `Health:` | `/100` |
| 58 | T9 | HO | ASBUILT_COVER_DEVIATION_MM_DISP_TXT | ITALIC | GREY | 2.0 | `ΔCvr:` | `mm` |
| 59 | T10 | DC | ASS_UNICLASS_2015_TXT | NOM | GREY | 2.0 | `Uniclass:` | |
| 60 | T10 | DC | ASS_KEYNOTE_TXT | NOM | GREY | 2.0 | `Key:` | |
| 61 | T10 | DC | ASS_MODEL_REF_TXT | NOM | GREY | 2.0 | `Ref:` | |
| 62 | T10 | DC | ASS_TRACE_SEQ_NR_DISP_TXT | NOM | GREY | 2.0 | `Trc:` | |
| 63 | T10 | HO | IFC_PSET_OVERRIDE_TXT | NOM | GREY | 2.0 | `IFC:` | |
| 64 | T10 | HO | ACC_ISSUE_ID_TXT | NOM | GREY | 2.0 | `ACC:` | |
| 65 | T10 | HO | ACC_SYNC_STATUS_TXT | NOM | GREY | 2.0 | `Sync:` | |

> Rows **1–3** above tiers T4–T10 are **not** shown here: every family already ships the default T1–T3 primary rows (`ASS_TAG_1_TXT` + the tier-2/3 blocks). Author T4–T10 as rows 4-onwards physically, below the existing T1–T3 stack. Total physical Label elements on this master = existing T1–T3 rows + 65 T4–T10 rows.

The machine-readable version of this table is at `scratchpad/authtable_generic.json` (regenerate via the analysis scripts if the CSVs change).

---

## 4. Sizing summary — manual authoring cost

**Per-template union row count** (the number of styled Label elements the author stacks for T4–T10, over the 52 shapes):

| metric | value |
|---|---|
| distinct templates to author | **52** |
| union rows / template — **min** | **52** |
| union rows / template — **median** | **58** |
| union rows / template — **max** | **69** (Mechanical Equipment Tag.rft) |
| union rows / template — **mean** | **59** |

**Per-family row count** (for reference — how many rows a *single* family actually lights up, T4–T10 union across its modes):

| metric | value |
|---|---|
| families with tier rows | **146** of 204 |
| rows / family — **min** | **21** |
| rows / family — **median** | **56** |
| rows / family — **max** | **64** |

**Total manual placement effort:** ~52 templates × ~59 rows ≈ **~3,000 Label placements once**, versus 146 families × ~56 rows ≈ **~8,200** if authored per family. The template approach is ~2.7× cheaper and, more importantly, is a one-time bounded job the regenerator then fans out across all 206 families automatically. The 16 single-content shapes are near-mechanical duplicates of the 56-row common layout; only the ~6 MEP/structural shapes need their extra T5/T7/T9 rows folded in.

---

## Notes / caveats for the author

1. **Row-less families (58) need no template of their own.** They are HEALTH catalog declarations (`TAG_FAMILY,` rows) that resolve to their base category's plan at regeneration — e.g. "STING - Autoclave Tag" (Generic Tag.rft) reuses the Generic master; "STING - VIE Tag" (Mechanical Equipment Tag.rft) reuses the Mech Equip master.
2. **10 creator families have a name-suffix mismatch vs the CSV** (tie-in variants, Specialty Asset/General). They resolve through `VariantSuffixToCsvName` / `CsvFamilyNameCandidates` at plan-lookup time and map to a base shape (Pipe/Conduit/Cable Tray/Sprinkler/Generic-Model/Specialty). No extra template.
3. **DISP_TXT vs raw param.** The Handover CSVs use display-formatted variants (`*_DISP_TXT`) where the DC CSVs sometimes use the raw numeric param. Bind the Label to exactly the parameter named in the row — `AuthorLabelsMulti` expects the CSV param name.
4. **Affix literals.** Prefix/suffix are placed as literal label text per STING convention (the `|` separators and unit suffixes like `kgCO₂e`). Preserve leading spaces exactly (e.g. `' Cert#:'`) — they are the inter-row separators.
5. **Do not author formulas by hand.** Place + style + bind only. The depth×mode `if(or(and(TAG_PARA_STATE_N_BOOL, HANDOVER_MODE_*_BOOL), …), PARAM, "")` gates are written by `FamilyLabelAuthor.AuthorLabelsMulti` at regeneration (Route B step B3).
