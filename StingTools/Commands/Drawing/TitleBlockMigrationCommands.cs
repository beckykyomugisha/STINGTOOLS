// StingTools — Phase 171 · Title-block legacy migration commands
//
// Phase 170's hybrid single-family layout was renamed in Phase 170
// revision to a two-family-per-size convention:
//
//   STING_TB_A1_v2.0          →  STING_TB_A1_BIM_v2.0     (was hybrid)
//                                STING_TB_A1_NONBIM_v2.0  (new sibling)
//
// Projects authored before May 2026 may still have sheets pointing
// at the old single-family name. These two commands handle that
// migration:
//
//   TitleBlock_AuditLegacy    — read-only — list every sheet whose
//     title-block family matches the old `STING_TB_<SIZE>_v<MAJOR>.<MINOR>`
//     pattern (no BIM/NONBIM segment). Reports counts + sample sheet
//     numbers so the operator knows what's about to migrate.
//
//   TitleBlock_MigrateLegacy  — for each legacy sheet, swap the
//     title-block family to the BIM variant of the same paper size
//     and version. Auto-loads the target .rfa from
//     Families/TitleBlocks/ when not in the project. Updates
//     STING_SHEET_BIM_MODE_TXT to "BIM" on every migrated sheet.
//     Defaults to BIM (the safer choice — preserves visible
//     identity-data cells); operator can run TitleBlock_ToggleBIMMode
//     afterwards on individual sheets that should be NONBIM.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.UI;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockAuditLegacyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var uiApp = data?.Application ?? StingCommandHandler.CurrentApp;
            if (uiApp?.ActiveUIDocument == null)
            { TaskDialog.Show("STING — Title Block Legacy Audit", "No active document."); return Result.Failed; }
            var doc = uiApp.ActiveUIDocument.Document;

            var hits = ScanLegacyTitleBlocks(doc);
            var sb = new StringBuilder();
            sb.AppendLine("STING — Title Block Legacy Audit");
            sb.AppendLine();
            sb.AppendLine($"Sheets scanned : {hits.TotalSheetsChecked}");
            sb.AppendLine($"Legacy hits    : {hits.Hits.Count}");
            sb.AppendLine();
            if (hits.Hits.Count == 0)
            {
                sb.AppendLine("✓ No sheets reference legacy single-family title blocks.");
                sb.AppendLine();
                sb.AppendLine("All title-block instances follow the STING_TB_<SIZE>_<MODE>_v<N> convention.");
                TaskDialog.Show("STING — Title Block Legacy Audit", sb.ToString());
                return Result.Succeeded;
            }
            // Group by legacy family name.
            var byFamily = hits.Hits
                .GroupBy(h => h.LegacyFamilyName, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count());
            foreach (var grp in byFamily)
            {
                var first = grp.First();
                sb.AppendLine($"  {grp.Count(),3} × {grp.Key}");
                sb.AppendLine($"      → suggested target: {first.SuggestedTargetFamilyName}");
                sb.AppendLine($"      sheets: {string.Join(", ", grp.Take(6).Select(h => h.SheetNumber))}"
                    + (grp.Count() > 6 ? $", … +{grp.Count() - 6} more" : ""));
                sb.AppendLine();
            }
            sb.AppendLine();
            sb.AppendLine("Run TitleBlock_MigrateLegacy to swap each legacy instance to its BIM variant.");
            TaskDialog.Show("STING — Title Block Legacy Audit", sb.ToString());
            return Result.Succeeded;
        }

        internal static LegacyAuditResult ScanLegacyTitleBlocks(Document doc)
        {
            var result = new LegacyAuditResult();
            var sheets = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .OfType<ViewSheet>()
                .ToList();
            result.TotalSheetsChecked = sheets.Count;
            foreach (var sheet in sheets)
            {
                var tb = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet);
                if (tb == null) continue;
                var familyName = TitleBlockSlotUtils.GetFamilyName(doc, tb);
                if (string.IsNullOrEmpty(familyName)) continue;
                var target = LegacyFamilyMatcher.SuggestTargetName(familyName);
                if (target == null) continue;
                result.Hits.Add(new LegacyHit
                {
                    SheetId               = sheet.Id,
                    SheetNumber           = sheet.SheetNumber,
                    SheetName             = sheet.Name,
                    LegacyFamilyName      = familyName,
                    SuggestedTargetFamilyName = target,
                });
            }
            return result;
        }
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class TitleBlockMigrateLegacyCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            var uiApp = data?.Application ?? StingCommandHandler.CurrentApp;
            if (uiApp?.ActiveUIDocument == null)
            { TaskDialog.Show("STING — Migrate Legacy Title Blocks", "No active document."); return Result.Failed; }
            var doc = uiApp.ActiveUIDocument.Document;

            var hits = TitleBlockAuditLegacyCommand.ScanLegacyTitleBlocks(doc);
            if (hits.Hits.Count == 0)
            {
                TaskDialog.Show("STING — Migrate Legacy Title Blocks",
                    "No legacy title-block instances found. Project is already on the two-family convention.");
                return Result.Succeeded;
            }

            // Confirm.
            var dlg = new TaskDialog("STING — Migrate Legacy Title Blocks")
            {
                MainInstruction = $"Found {hits.Hits.Count} sheet(s) using legacy single-family title blocks.",
                MainContent = "Each legacy instance will be swapped to its BIM variant of the same paper size + version. "
                    + "Run TitleBlock_ToggleBIMMode on individual sheets afterwards if any should be NONBIM. "
                    + "STING_SHEET_BIM_MODE_TXT will be set to \"BIM\" on every migrated sheet.",
                CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                AllowCancellation = true,
                DefaultButton = TaskDialogResult.Cancel,
            };
            if (dlg.Show() != TaskDialogResult.Ok) return Result.Cancelled;

            // Group hits by suggested target so we load each target family once.
            var byTarget = hits.Hits
                .GroupBy(h => h.SuggestedTargetFamilyName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int migrated = 0;
            int skipped  = 0;
            var report = new StringBuilder();

            using (var tg = new TransactionGroup(doc, "STING Migrate Legacy Title Blocks"))
            {
                tg.Start();
                foreach (var grp in byTarget)
                {
                    var targetName = grp.Key;
                    Family targetFamily = FindLoadedFamily(doc, targetName)
                                       ?? LoadFamilyFromDisk(doc, targetName);
                    if (targetFamily == null)
                    {
                        report.AppendLine($"  ✗ {targetName} not loaded and no .rfa found at "
                            + "Families/TitleBlocks/ — skipped " + grp.Count() + " sheet(s).");
                        skipped += grp.Count();
                        continue;
                    }
                    FamilySymbol targetSym = null;
                    foreach (var symId in targetFamily.GetFamilySymbolIds())
                    {
                        targetSym = doc.GetElement(symId) as FamilySymbol;
                        if (targetSym != null) break;
                    }
                    if (targetSym == null)
                    {
                        report.AppendLine($"  ✗ {targetName} loaded but has no types — skipped " + grp.Count() + " sheet(s).");
                        skipped += grp.Count();
                        continue;
                    }
                    using (var tx = new Transaction(doc, $"STING Migrate to {targetName}"))
                    {
                        tx.Start();
                        if (!targetSym.IsActive) targetSym.Activate();
                        doc.Regenerate();
                        foreach (var hit in grp)
                        {
                            try
                            {
                                var sheet = doc.GetElement(hit.SheetId) as ViewSheet;
                                if (sheet == null) continue;
                                var tb = TitleBlockSlotUtils.FindTitleBlockOnSheet(doc, sheet) as FamilyInstance;
                                if (tb == null) { skipped++; continue; }
                                tb.Symbol = targetSym;
                                var modeParam = tb.LookupParameter("STING_SHEET_BIM_MODE_TXT");
                                if (modeParam != null && !modeParam.IsReadOnly)
                                    try { modeParam.Set("BIM"); } catch { }
                                migrated++;
                                report.AppendLine($"  ✓ {sheet.SheetNumber}  →  {targetName}");
                            }
                            catch (Exception ex)
                            {
                                skipped++;
                                report.AppendLine($"  ✗ {hit.SheetNumber}  {ex.Message}");
                            }
                        }
                        tx.Commit();
                    }
                }
                tg.Assimilate();
            }

            var sb = new StringBuilder();
            sb.AppendLine("STING — Migrate Legacy Title Blocks");
            sb.AppendLine();
            sb.AppendLine($"Migrated : {migrated}");
            sb.AppendLine($"Skipped  : {skipped}");
            sb.AppendLine();
            sb.Append(report.ToString());
            TaskDialog.Show("STING — Migrate Legacy Title Blocks", sb.ToString());
            return migrated > 0 ? Result.Succeeded : Result.Failed;
        }

        private static Family FindLoadedFamily(Document doc, string name) =>
            new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .OfType<Family>()
                .FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        private static Family LoadFamilyFromDisk(Document doc, string name)
        {
            var candidates = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(doc.PathName))
                {
                    var dir = Path.GetDirectoryName(doc.PathName);
                    if (!string.IsNullOrEmpty(dir))
                        candidates.Add(Path.Combine(dir, "Families", "TitleBlocks", name + ".rfa"));
                }
            }
            catch { }
            try
            {
                var asm = StingToolsApp.AssemblyPath;
                if (!string.IsNullOrEmpty(asm))
                {
                    var d = Path.GetDirectoryName(asm);
                    if (!string.IsNullOrEmpty(d))
                        candidates.Add(Path.Combine(d, "Families", "TitleBlocks", name + ".rfa"));
                }
            }
            catch { }
            foreach (var c in candidates)
            {
                if (!File.Exists(c)) continue;
                try
                {
                    using (var tx = new Transaction(doc, $"STING Load {name}"))
                    {
                        tx.Start();
                        var ok = doc.LoadFamily(c, new TitleBlockFamilyLoadOptions(), out Family fam);
                        tx.Commit();
                        if (fam != null) return fam;
                        if (ok) return FindLoadedFamily(doc, name);
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"LoadFamilyFromDisk '{name}': {ex.Message}");
                }
            }
            return null;
        }
    }

    /// <summary>Result of a project scan for legacy single-family title
    /// blocks.</summary>
    internal sealed class LegacyAuditResult
    {
        public int TotalSheetsChecked { get; set; }
        public List<LegacyHit> Hits { get; } = new List<LegacyHit>();
    }

    internal sealed class LegacyHit
    {
        public ElementId SheetId { get; set; }
        public string    SheetNumber { get; set; }
        public string    SheetName { get; set; }
        public string    LegacyFamilyName { get; set; }
        public string    SuggestedTargetFamilyName { get; set; }
    }

    /// <summary>Pattern matcher for the legacy single-family naming scheme
    /// (`STING_TB_<SIZE>_v<MAJOR>.<MINOR>`) → suggested two-family name
    /// (`STING_TB_<SIZE>_BIM_v<MAJOR>.<MINOR>`).</summary>
    internal static class LegacyFamilyMatcher
    {
        // STING_TB_<SIZE>_v<MAJOR>.<MINOR> — but NOT STING_TB_<SIZE>_BIM_v…
        // and NOT STING_TB_<SIZE>_NONBIM_v… Negative lookaheads guard the
        // already-migrated names.
        private static readonly Regex Pattern = new Regex(
            @"^STING_TB_(?<size>[A-Za-z0-9]+(?:_PORT)?)_v(?<ver>\d+\.\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string SuggestTargetName(string legacyName)
        {
            if (string.IsNullOrEmpty(legacyName)) return null;
            if (legacyName.IndexOf("_BIM_",    StringComparison.OrdinalIgnoreCase) >= 0) return null;
            if (legacyName.IndexOf("_NONBIM_", StringComparison.OrdinalIgnoreCase) >= 0) return null;
            var m = Pattern.Match(legacyName);
            if (!m.Success) return null;
            return $"STING_TB_{m.Groups["size"].Value}_BIM_v{m.Groups["ver"].Value}";
        }
    }
}
