# STING Title-Block Family Inventory

> 45-family catalogue confirming every `STING_TB_*.rfa` the title-block factory will mint, with a visual SVG preview for each. Open `docs/title_blocks/CATALOGUE.html` in a browser to browse all previews on one page; individual SVGs live under `docs/title_blocks/previews/`.

## Summary

| Group | Count | Notes |
|---|---:|---|
| **Abstract bases** (extends parents, no `.rfa` minted) | 11 | 1 master `A1_common_v2.0` + 10 size+orientation commons |
| **Working sheets — Landscape** (BIM + NONBIM) | 10 | A0 / A1 / A2 / A3 / A4 |
| **Working sheets — Portrait** (BIM + NONBIM) | 10 | A0 / A1 / A2 / A3 / A4 |
| **Specialty** | 14 | Fabrication × 4, presentation × 2, cover, divider, register, transmittal cover, submission × 3, clarification |
| **Total** | **45** | |

Architecture: every concrete family extends a size+orientation abstract common, which itself extends the master `A1_common_v2.0` carrying the full identity-data parameter universe (40 shared params from `MR_PARAMETERS.txt`). The factory walks `extends` deep-merge so each `.rfa` ships with the complete parameter set bound, while individual JSON entries stay focused on layout deltas.

## Audit results (last run)

```
✅ JSON valid — schema v2, 45 families
✅ Every shared param across all 45 families resolves to MR_PARAMETERS.txt
✅ 45 SVG previews rendered (no errors)
```

## A. Working sheets

Every working-sheet paper size ships in **both orientations × both BIM modes**. The portrait variants share the bottom-strip layout convention with their landscape siblings — only the paper proportions differ. Slots resize automatically since they're declared in mm relative to the actual paper bounds.

### Landscape

| ID | Paper (mm) | Mode | Status |
|---|---:|---|---|
| `STING_TB_A0_BIM_v2.0`     | 1189 × 841 | BIM    | Stub layout (extends `A0_LAND_common_v2.0`) |
| `STING_TB_A0_NONBIM_v2.0`  | 1189 × 841 | NONBIM | Stub layout |
| `STING_TB_A1_BIM_v2.0`     | 841 × 594  | BIM    | **Full hand-tuned layout** (Phase 170) |
| `STING_TB_A1_NONBIM_v2.0`  | 841 × 594  | NONBIM | **Full hand-tuned layout** (Phase 170) |
| `STING_TB_A2_BIM_v2.0`     | 594 × 420  | BIM    | Stub layout |
| `STING_TB_A2_NONBIM_v2.0`  | 594 × 420  | NONBIM | Stub layout |
| `STING_TB_A3_BIM_v2.0`     | 420 × 297  | BIM    | Stub layout |
| `STING_TB_A3_NONBIM_v2.0`  | 420 × 297  | NONBIM | Stub layout |
| `STING_TB_A4_BIM_v2.0`     | 297 × 210  | BIM    | Stub layout |
| `STING_TB_A4_NONBIM_v2.0`  | 297 × 210  | NONBIM | Stub layout |

### Portrait

| ID | Paper (mm) | Mode | Status |
|---|---:|---|---|
| `STING_TB_A0_PORT_BIM_v2.0`     | 841 × 1189 | BIM    | Stub layout (extends `A0_PORT_common_v2.0`) |
| `STING_TB_A0_PORT_NONBIM_v2.0`  | 841 × 1189 | NONBIM | Stub layout |
| `STING_TB_A1_PORT_BIM_v2.0`     | 594 × 841  | BIM    | Stub layout — **explicit user request** |
| `STING_TB_A1_PORT_NONBIM_v2.0`  | 594 × 841  | NONBIM | Stub layout |
| `STING_TB_A2_PORT_BIM_v2.0`     | 420 × 594  | BIM    | Stub layout |
| `STING_TB_A2_PORT_NONBIM_v2.0`  | 420 × 594  | NONBIM | Stub layout |
| `STING_TB_A3_PORT_BIM_v2.0`     | 297 × 420  | BIM    | Stub layout |
| `STING_TB_A3_PORT_NONBIM_v2.0`  | 297 × 420  | NONBIM | Stub layout |
| `STING_TB_A4_PORT_BIM_v2.0`     | 210 × 297  | BIM    | Stub layout |
| `STING_TB_A4_PORT_NONBIM_v2.0`  | 210 × 297  | NONBIM | Stub layout |

> **Stub layout** = the abstract size+orientation common ships a generated bottom-strip layout that's correct for the paper proportions but uses a templated cell arrangement (CLIENT/PROJECT/CONSULTANTS/CONTRACTOR + DRAWING TITLE + DATE/DRAWN/CHECKED/APPROVED + sheet-number block). Bound to the full PRJ_TB_/PRJ_ORG_ parameter universe. Phase 172 refines to per-size design polish — the stubs already produce visually-clean .rfa output.

## B. Specialty title blocks

| ID | Paper (mm) | Purpose |
|---|---:|---|
| `STING_TB_ASSEMBLY_PIPE_v1.0`     | 841 × 594 | Pipe spool fabrication — BOM strip on the right |
| `STING_TB_ASSEMBLY_DUCT_v1.0`     | 841 × 594 | Duct spool fabrication — gauge / insulation strip |
| `STING_TB_ASSEMBLY_COND_v1.0`     | 841 × 594 | Conduit / cable assembly fabrication |
| `STING_TB_ASSEMBLY_HANGER_v1.0`   | 841 × 594 | Hanger / support fabrication |
| `STING_TB_PRESENT_A1_v1.0`        | 841 × 594 | Presentation — full-bleed render area, colour-rich |
| `STING_TB_PRESENT_A1_MONO_v1.0`   | 841 × 594 | Presentation — mono variant |
| `STING_TB_COVER_A1_v1.0`          | 841 × 594 | Project / package cover — large logo banner |
| `STING_TB_DIVIDER_A1_v1.0`        | 841 × 594 | Discipline section divider — single big label |
| `STING_TB_REGISTER_A1_v1.0`       | 841 × 594 | Drawing register — full-page schedule |
| `STING_TB_TRANSMITTAL_A4_v1.0`    | 297 × 210 | Transmittal cover sheet — recipient list + signatures |
| `STING_TB_SUBMISSION_KCCA_v1.0`   | 841 × 594 | Kampala Capital City Authority submission |
| `STING_TB_SUBMISSION_ERA_v1.0`    | 841 × 594 | Electricity Regulatory Authority submission |
| `STING_TB_SUBMISSION_NEMA_v1.0`   | 841 × 594 | National Environment Management Authority submission |
| `STING_TB_CLARIFICATION_A3_v1.0`  | 420 × 297 | RFI / clarification sketch sheet |

## C. Document templates (separate subsystem)

The DOCX/XLSX templates in `StingTools/Docs/_template_sources/` are owned by the **template engine v1.1** (Phase 112), not the title-block factory — they're for cover sheets, transmittal letters, RFIs, etc. that ship as Word/Excel documents alongside the drawing PDFs. **All 16 are configured.**

| ID | Template file | Workflow |
|---|---|---|
| A01 | `deliverable_standard.docx`    | `deliverable_issue_default` |
| A02 | `deliverable_cancelled.docx`   | (no workflow) |
| A03 | `deliverable_superseded.docx`  | (no workflow) |
| A04 | `deliverable_replacing.docx`   | (no workflow) |
| A05 | `deliverable_tabular.xlsx`     | `deliverable_issue_default` |
| B06 | `transmittal.docx`             | `transmittal_default` |
| B07 | `technical_query.docx`         | `tq_default` |
| B08 | `rfi.docx`                     | `rfi_default` |
| B09 | `technical_response.docx`      | (no workflow) |
| C10 | `material_requisition.docx`    | `mr_default` |
| C11 | `submittal_cover.docx`         | (no workflow) |
| C12 | `variation.docx`               | (no workflow) |
| C13 | `letter_transmittal.docx`      | (no workflow) |
| D14 | `meeting_minutes.docx`         | (no workflow) |
| D15 | `progress_report.docx`         | (no workflow) |
| D16 | `handover_certificate.docx`    | (no workflow) |

These 16 templates + 5 workflow JSONs are extracted at first project open via `EmbeddedTemplates.ExtractIfMissing(doc)` in `StingToolsApp.OnDocumentOpened`.

## D. Fabrication subsystem (separate)

The fabrication assembly title blocks (`STING_TB_ASSEMBLY_*`) are paired with the **fabrication engine** (Phase 168/169) in `StingTools/Core/Fabrication/`:

- `FabricationEngine.cs` — main coordinator
- `AssemblyBuilder.cs` — assembles per-spool element groups
- `AssemblyGrouper.cs` — groups MEP elements into spools
- `AssemblyViewBuilder.cs` — generates per-spool views
- `IsoSymbolPlacer.cs` — ISO 6412 symbol placement
- `ShopDrawingComposer.cs` — composes the shop drawing including the title block

**ShopDrawingComposer integrates with the Drawing-Type Manager** (`Core/Drawing/DrawingDispatcher.cs`) to pick which assembly title block to use. Once the assembly title-block .rfa families are minted from this catalogue, the composer will load + place them automatically per discipline (PIPE / DUCT / COND / HANGER).

## E. Workflow

```
1. Run TitleBlock_CreateAll              → mints all 34 concrete .rfa
                                           (skips 11 abstract bases)
2. Run Build All from project setup      → loads them into the project
3. Run TitleBlock_AuditLegacy            → flags any sheets pointing at
                                           pre-May-2026 single-family names
4. Run TitleBlock_MigrateLegacy          → swaps every legacy instance to
                                           the BIM variant of its size
5. Per-sheet — TitleBlock_ToggleBIMMode  → BIM↔NONBIM swap on individual
                                           sheets that should be NONBIM
6. Per-sheet — TitleBlock_AutoPlaceViewports → routes selected views to slots
                                                 by purposeTag
```

## F. Regenerating the catalogue

```bash
# 1. Edit STING_TITLE_BLOCKS.json directly, OR run the expander:
python3 tools/expand_title_block_catalogue.py

# 2. Regenerate SVG previews:
python3 tools/generate_title_block_previews.py

# 3. Open the catalogue:
xdg-open docs/title_blocks/CATALOGUE.html         # Linux
start    docs/title_blocks/CATALOGUE.html         # Windows
open     docs/title_blocks/CATALOGUE.html         # macOS
```

## G. Caveats

1. **Layout polish status**: All 42 previously-stub families have been polished by
   `tools/polish_title_block_catalogue.py`:
   - 8 size+orientation commons (A0/A2/A3/A4 × LAND/PORT) carry refined 5-column
     bottom strips with proportional cell widths and per-size text scaling.
   - 16 concrete BIM/NONBIM working sheets have proper ISO-19650 BIM identity
     strips (top-row status/suitability/rev/loin/fed + 7-segment ID row, with
     suitability chip + status band) on BIM variants and a large sheet-number
     block on NONBIM variants — both sized to the paper.
   - 14 specialty families now have bespoke layouts per purpose:
     - **ASSEMBLY_PIPE/DUCT/COND/HANGER** — 200 mm right BOM strip (6 cols × 18 rows)
       + 80 mm bottom fab metadata strip (spool # / discipline / weight / fab loc /
       status / BOM rev) + discipline accent stripe.
     - **PRESENT_A1** + **PRESENT_A1_MONO** — full-bleed render area + 280×60 mm
       bottom-right watermark with project / title / rev / date / drawn / client.
     - **COVER_A1** — top dark-blue banner + amber accent + huge centred project
       name + address + client + RIBA stage; bottom strip with status / rev /
       date / transmittal.
     - **DIVIDER_A1** — large grey panel with 60 mm centred discipline label;
       50 mm bottom identity strip.
     - **REGISTER_A1** — full-page 9-column × 24-row schedule frame; ExportSheetRegister
       populates rows at print time.
     - **TRANSMITTAL_A4** — A4 landscape, header (TX REF / DATE / REV) + recipient
       (TO / FROM with addresses) + accompanying-documents table + signature footer.
     - **SUBMISSION_KCCA/ERA/NEMA** — 80 mm regulator banner with full authority
       name + statutory disclaimer band + 4-cell identity strip; banner accents
       Uganda-green / utility-blue / nema-brown.
     - **CLARIFICATION_A3** — A3 two-pane layout: QUERY legend slot (left) +
       SKETCH plan slot (right); RFI REF in header.
2. **Slot reference planes** still rejected by Revit 2025 title-block families —
   slot bounds are read from JSON at runtime instead. Doesn't affect previews.
3. **SVG previews** are this sandbox's best PDF/JPEG mock. Print-to-PDF from a
   browser at 100% scale gives actual-size paper.
4. **Re-running `polish_title_block_catalogue.py`** is idempotent — it overwrites
   every concrete family except the master `A1_common_v2.0` and the hand-tuned
   `STING_TB_A1_BIM_v2.0` / `STING_TB_A1_NONBIM_v2.0`.
