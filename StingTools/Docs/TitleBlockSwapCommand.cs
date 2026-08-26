// StingTools — Title Block Swap (Phase 195)
//
// Fills the gap between the two title-block swap paths that existed before:
//
//   • Sheet Manager → Swap Title Block  — any family, but ONE sheet at a time.
//   • Title Block   → Set Variant       — every sheet, but only the six v1.0
//                                         STING_TB_<SIZE>_<R|B>_v1.0 families,
//                                         and only to the variant it infers.
//
// Neither could answer "I already have views on sheets and I want them all on a
// different title block". This command does: pick a scope, pick a target type,
// swap. It is a FamilySymbol change on the existing instance, never a
// delete + recreate — viewports, their positions, and every filled-in
// PRJ_TB_* / instance parameter value survive untouched.
//
// The one thing a swap cannot preserve is layout across a paper-size change:
// viewports keep their sheet XY, so moving A1 → A3 can push them off the border.
// The result panel says so, and points at Auto Layout.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Docs
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockSwapCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            var uidoc = (commandData?.Application
                ?? StingTools.UI.StingCommandHandler.CurrentApp)?.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                TaskDialog.Show("STING Swap Title Block", "No document open.");
                return Result.Cancelled;
            }

            // ── Loaded title block types ────────────────────────────────────
            var tbTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .OrderBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .ToList();

            if (tbTypes.Count == 0)
            {
                TaskDialog.Show("STING Swap Title Block",
                    "No title block families are loaded in this project.\n\n" +
                    "Load one first (Insert → Load Family, or the 'Build…' " +
                    "command to mint a STING title block from " +
                    "STING_TITLE_BLOCKS.json), then re-run this.");
                return Result.Cancelled;
            }

            var allSheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder)
                .OrderBy(s => s.SheetNumber)
                .ToList();

            if (allSheets.Count == 0)
            {
                TaskDialog.Show("STING Swap Title Block", "This project has no sheets.");
                return Result.Cancelled;
            }

            // ── Scope ───────────────────────────────────────────────────────
            var activeSheet = doc.ActiveView as ViewSheet;
            var selectedSheets = GetSelectedSheets(uidoc, doc);

            // Group sheets by the family currently placed on them, so the user can
            // say "every sheet that is on the old family" without hand-picking.
            var byCurrentFamily = new Dictionary<string, List<ViewSheet>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sh in allSheets)
            {
                var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, sh);
                string fn = tb?.Symbol?.FamilyName ?? "(no title block)";
                if (!byCurrentFamily.TryGetValue(fn, out var list))
                    byCurrentFamily[fn] = list = new List<ViewSheet>();
                list.Add(sh);
            }

            const string SCOPE_ACTIVE = "Active sheet only";
            const string SCOPE_SELECTED = "Selected sheets";
            const string SCOPE_ALL = "All sheets";
            const string SCOPE_BY_FAMILY = "Only sheets currently using one family…";

            var scopeOptions = new List<string>();
            if (activeSheet != null)
                scopeOptions.Add($"{SCOPE_ACTIVE}  ({activeSheet.SheetNumber})");
            if (selectedSheets.Count > 0)
                scopeOptions.Add($"{SCOPE_SELECTED}  ({selectedSheets.Count})");
            scopeOptions.Add($"{SCOPE_ALL}  ({allSheets.Count})");
            if (byCurrentFamily.Count > 1)
                scopeOptions.Add(SCOPE_BY_FAMILY);

            string scopePick = StingListPicker.Show("Swap Title Block",
                "Which sheets should get the new title block?", scopeOptions);
            if (string.IsNullOrEmpty(scopePick)) return Result.Cancelled;

            List<ViewSheet> targets;
            string scopeLabel;
            if (scopePick.StartsWith(SCOPE_ACTIVE, StringComparison.Ordinal))
            {
                targets = new List<ViewSheet> { activeSheet };
                scopeLabel = $"active sheet {activeSheet.SheetNumber}";
            }
            else if (scopePick.StartsWith(SCOPE_SELECTED, StringComparison.Ordinal))
            {
                targets = selectedSheets;
                scopeLabel = $"{selectedSheets.Count} selected sheet(s)";
            }
            else if (scopePick.StartsWith(SCOPE_BY_FAMILY, StringComparison.Ordinal))
            {
                var famOptions = byCurrentFamily
                    .OrderByDescending(k => k.Value.Count)
                    .Select(k => $"{k.Key}  ({k.Value.Count} sheet(s))")
                    .ToList();
                string famPick = StingListPicker.Show("Swap Title Block",
                    "Swap every sheet currently using which family?", famOptions);
                if (string.IsNullOrEmpty(famPick)) return Result.Cancelled;

                int fi = famOptions.IndexOf(famPick);
                if (fi < 0) return Result.Cancelled;
                var chosen = byCurrentFamily.OrderByDescending(k => k.Value.Count).ElementAt(fi);
                targets = chosen.Value;
                scopeLabel = $"{targets.Count} sheet(s) on '{chosen.Key}'";
            }
            else
            {
                targets = allSheets;
                scopeLabel = $"all {allSheets.Count} sheet(s)";
            }

            if (targets == null || targets.Count == 0)
            {
                TaskDialog.Show("STING Swap Title Block", "No sheets in the chosen scope.");
                return Result.Cancelled;
            }

            // ── Target type ─────────────────────────────────────────────────
            var typeLabels = tbTypes.Select(t => $"{t.FamilyName} : {t.Name}").ToList();
            string typePick = StingListPicker.Show("Swap Title Block",
                $"Target title block for {scopeLabel}:", typeLabels);
            if (string.IsNullOrEmpty(typePick)) return Result.Cancelled;

            int ti = typeLabels.IndexOf(typePick);
            if (ti < 0 || ti >= tbTypes.Count) return Result.Cancelled;
            var newType = tbTypes[ti];

            // ── Swap ────────────────────────────────────────────────────────
            int swapped = 0, placed = 0, already = 0, failed = 0;
            var sizeChanged = new List<string>();
            var detail = new List<string>();
            var failures = new List<string>();

            using (var tx = new Transaction(doc, "STING Swap Title Block"))
            {
                tx.Start();
                if (!newType.IsActive) newType.Activate();
                doc.Regenerate();

                foreach (var sheet in targets)
                {
                    if (sheet == null) continue;
                    try
                    {
                        var tb = TitleBlockEngine.GetTitleBlockOnSheet(doc, sheet);

                        if (tb == null)
                        {
                            doc.Create.NewFamilyInstance(XYZ.Zero, newType, sheet as Element,
                                Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                            placed++;
                            detail.Add($"{sheet.SheetNumber}: (none) → {newType.FamilyName} : {newType.Name}  [placed]");
                            continue;
                        }

                        if (tb.Symbol != null && tb.Symbol.Id == newType.Id) { already++; continue; }

                        string fromLabel = tb.Symbol != null
                            ? $"{tb.Symbol.FamilyName} : {tb.Symbol.Name}" : "(unknown)";
                        bool differentSize = !SameSheetSize(doc, sheet, tb, newType);

                        tb.Symbol = newType;
                        swapped++;
                        detail.Add($"{sheet.SheetNumber}: {fromLabel} → {newType.FamilyName} : {newType.Name}");
                        if (differentSize && sheet.GetAllViewports().Count > 0)
                            sizeChanged.Add(sheet.SheetNumber);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        failures.Add($"{sheet.SheetNumber}: {ex.Message}");
                        StingLog.Warn($"TB: swap failed on {sheet.SheetNumber}: {ex.Message}");
                    }
                }
                tx.Commit();
            }

            StingLog.Info($"TB Swap: target='{newType.FamilyName}:{newType.Name}' scope={scopeLabel} " +
                $"swapped={swapped} placed={placed} already={already} failed={failed}");

            // ── Report ──────────────────────────────────────────────────────
            var b = StingResultPanel.Create("")
                .SetTitle("Swap Title Block")
                .SetSubtitle($"{swapped + placed} sheet(s) now on {newType.FamilyName} : {newType.Name}")
                .SetOverallPct(targets.Count == 0 ? 100.0
                    : 100.0 * (swapped + placed + already) / targets.Count)
                .AddSection("Summary")
                .Metric("Scope", scopeLabel)
                .Metric("Sheets in scope", targets.Count.ToString())
                .Metric("Swapped", swapped.ToString())
                .Metric("Title block placed (sheet had none)", placed.ToString())
                .Metric("Already on target type", already.ToString())
                .Metric("Failed", failed.ToString());

            b.AddSection("Viewports")
                .Text("Swapping the type never moves or deletes a viewport — every view " +
                      "stays on its sheet at its current position.");
            if (sizeChanged.Count > 0)
            {
                b.Text($"\n{sizeChanged.Count} sheet(s) changed paper size, so viewports may now " +
                       "sit outside the new border. Run Sheet Manager → Auto Layout (or " +
                       "Arrange on Sheet) on:\n  " +
                       string.Join(", ", sizeChanged.Take(40)) +
                       (sizeChanged.Count > 40 ? $"  … +{sizeChanged.Count - 40} more" : ""));
            }

            if (failures.Count > 0)
                b.AddSection("Failures").Text(string.Join("\n", failures.Take(50)));
            if (detail.Count > 0)
                b.AddSection("Detail").Text(string.Join("\n", detail.Take(200)));

            b.Show();
            return Result.Succeeded;
        }

        /// <summary>Sheets currently picked in the Revit selection (project browser
        /// multi-select, or title blocks selected on screen).</summary>
        private static List<ViewSheet> GetSelectedSheets(UIDocument uidoc, Document doc)
        {
            var result = new List<ViewSheet>();
            try
            {
                var ids = uidoc?.Selection?.GetElementIds();
                if (ids == null) return result;
                var seen = new HashSet<long>();
                foreach (var id in ids)
                {
                    var el = doc.GetElement(id);
                    ViewSheet sh = el as ViewSheet;
                    // A selected title block stands in for its sheet.
                    if (sh == null && el != null && el.OwnerViewId != ElementId.InvalidElementId)
                        sh = doc.GetElement(el.OwnerViewId) as ViewSheet;
                    if (sh != null && !sh.IsPlaceholder && seen.Add(sh.Id.Value))
                        result.Add(sh);
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TB: GetSelectedSheets failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>Compare the placed title block's footprint against the target
        /// type's sheet width/height, to warn when viewports will fall outside the
        /// new border. Returns true when they match (or cannot be determined).</summary>
        private static bool SameSheetSize(Document doc, ViewSheet sheet,
            FamilyInstance current, FamilySymbol target)
        {
            try
            {
                var bb = current.get_BoundingBox(sheet);
                if (bb == null) return true;
                double curW = Math.Abs(bb.Max.X - bb.Min.X);
                double curH = Math.Abs(bb.Max.Y - bb.Min.Y);

                double tgtW = GetLength(target, BuiltInParameter.SHEET_WIDTH);
                double tgtH = GetLength(target, BuiltInParameter.SHEET_HEIGHT);
                if (tgtW <= 0 || tgtH <= 0) return true;   // unknown — do not warn

                const double TOL_FT = 10.0 / 304.8;        // 10 mm
                return Math.Abs(curW - tgtW) <= TOL_FT && Math.Abs(curH - tgtH) <= TOL_FT;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TB: SameSheetSize({sheet?.SheetNumber}) failed: {ex.Message}");
                return true;
            }
        }

        private static double GetLength(FamilySymbol sym, BuiltInParameter bip)
        {
            try
            {
                var p = sym?.get_Parameter(bip);
                return (p != null && p.HasValue) ? p.AsDouble() : 0.0;
            }
            catch (Exception ex) { StingLog.Warn($"TB: GetLength failed: {ex.Message}"); return 0.0; }
        }
    }
}
