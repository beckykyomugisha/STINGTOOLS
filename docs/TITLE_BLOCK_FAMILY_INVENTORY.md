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

1. **Stub layouts**: 16 of the 20 working sheets currently use a templated bottom-strip layout that's correct for paper proportions but lacks the per-size design polish of the hand-tuned A1 LAND BIM/NONBIM. Phase 172 refines the stubs based on visual review of the SVG previews.
2. **Specialty layouts** are placeholders — each ships a valid `.rfa` shell with the full common parameter universe bound and a single full-bleed slot, but the per-purpose layout (KCCA-specific cells, PIPE BOM strip, etc.) is Phase 172.
3. **SVG previews** are the closest thing to a PDF/JPEG mock that this Linux sandbox can produce — no Revit, no PIL, no reportlab, no cairosvg available. Open the SVGs in any browser to review; print-to-PDF from the browser produces a clean PDF of every family. The geometry is rendered at 1 SVG-px = 1 mm so a browser print at 100% gives an actual-size paper preview.
4. **Slot reference planes** still fail at family-author time on Revit 2025 (`NewReferencePlane` rejected for title-block families). Slot bounds are read from JSON at runtime instead — the previews show the slot bboxes regardless.
