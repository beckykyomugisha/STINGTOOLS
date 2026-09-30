// StingTools — revision numbering and revision-leak commands.
//
//   Revision_SetPerSheetNumbering — switch the project to per-sheet revision numbering
//       (ISO 19650: a sheet's first issue is P01 whatever cycle it joins in). Issued
//       revisions that would print a different number are LISTED and need consent.
//   Revision_LeakCheck (ReadOnly) — list sheets whose printed revision disagrees with
//       the revision they are issued at: a STING stamp (SHT_REV_TXT, the title block's
//       PRJ_TB_REVISION_NR_TXT) carrying a draft or a stale value is a leak and fails the
//       check; a draft sitting above the issued revision (Revit's own "Current Revision")
//       is reported for information — clouding the next round is normal between issues.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.BIMManager
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionSetPerSheetNumberingCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = ParameterHelpers.GetContext(commandData)?.Doc;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                bool noOne = WorkflowEngine.IsUnattended;
                Func<IReadOnlyList<RenumberChange>, bool> consent = noOne ? null : changes =>
                {
                    var sb = new StringBuilder();
                    foreach (var c in changes.Take(30)) sb.AppendLine("  " + c);
                    if (changes.Count > 30) sb.AppendLine($"  … and {changes.Count - 30} more");
                    var td = new TaskDialog("STING — per-sheet revision numbering")
                    {
                        MainInstruction = $"{changes.Count} ISSUED revision(s) would print a different number",
                        MainContent = "Per-sheet numbering (ISO 19650) numbers each drawing's revisions from its own " +
                                      "first issue. These drawings have already been issued, so their revision box " +
                                      "would change:\n\n" + sb +
                                      "\nRenumber only if these issues are being re-issued, or the old numbers were wrong.",
                        CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                        DefaultButton = TaskDialogResult.No,
                    };
                    return td.Show() == TaskDialogResult.Yes;
                };

                var o = RevisionNumberingSetup.EnsurePerSheet(doc, consent);
                if (o.Switched)
                {
                    try { TitleBlockRevisionSyncer.SyncAll(doc); }
                    catch (Exception ex) { StingLog.Warn("Per-sheet numbering: title block sync — " + ex.Message); }
                }
                if (!noOne) TaskDialog.Show("STING Revision Numbering", o.Message);
                if (o.NeedsConsent || (!o.Switched && !o.AlreadyPerSheet))
                {
                    message = o.Message;
                    return noOne ? Result.Failed : Result.Cancelled;
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Revision_SetPerSheetNumbering", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RevisionLeakCheckCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = ParameterHelpers.GetContext(commandData)?.Doc;
                if (doc == null) { message = "No document open."; return Result.Failed; }

                var findings = Scan(doc, out int sheetCount);
                var leaks = findings.Where(f => f.IsStamp && !f.IsLocked).ToList();
                var locked = findings.Where(f => f.IsStamp && f.IsLocked).ToList();
                var drafts = findings.Where(f => !f.IsStamp).ToList();

                string csv = null;
                if (findings.Count > 0)
                {
                    csv = OutputLocationHelper.GetRoutedTimestampedPath(doc, "Compliance", "STING_Revision_Leak", ".csv");
                    var sb = new StringBuilder("Sheet,Source,Found,Issued,Kind\n");
                    foreach (var f in findings)
                        sb.AppendLine(string.Join(",", Csv(f.SheetNumber), Csv(f.Source), Csv(f.Found),
                            Csv(f.Issued), !f.IsStamp ? "DRAFT-AHEAD" : f.IsLocked ? "LOCKED" : "LEAK"));
                    File.WriteAllText(csv, sb.ToString());
                }

                string summary =
                    $"{sheetCount} sheet(s) checked.\n" +
                    $"Leaks (a STING stamp disagrees with the issued revision): {leaks.Count}\n" +
                    $"Drafts above the issued revision (Revit 'Current Revision' shows them): {drafts.Count}\n" +
                    (leaks.Count > 0 ? "\n" + string.Join("\n", leaks.Take(20).Select(l => "  " + l)) +
                                       (leaks.Count > 20 ? $"\n  … and {leaks.Count - 20} more" : "") +
                                       "\n\nFix: run Revision Sync (RevisionSync) — it writes the issued revision." : "") +
                    (locked.Count > 0 ? $"\n\nLocked title blocks printing another revision: {locked.Count} (not counted as leaks — " +
                                        "Revision Sync leaves a locked title block untouched by design):\n" +
                                        string.Join("\n", locked.Take(10).Select(l => "  " + l)) +
                                        (locked.Count > 10 ? $"\n  … and {locked.Count - 10} more" : "") +
                                        "\n" + Commands.Drawing.TitleBlockLock.HowToClear : "") +
                    (drafts.Count > 0 ? "\n\nA title block that labels Revit's built-in 'Current Revision' would print " +
                                        "the draft; STING's revision box (PRJ_TB_REVISION_NR_TXT) does not." : "") +
                    (csv == null ? "" : $"\n\nReport: {csv}");
                StingLog.Info("Revision_LeakCheck: " + summary.Replace("\n", " | "));

                if (!WorkflowEngine.IsUnattended) TaskDialog.Show("STING Revision Leak Check", summary);
                if (leaks.Count > 0 && WorkflowEngine.IsRunningPreset)
                {
                    message = $"{leaks.Count} sheet revision stamp(s) disagree with the issued revision — run RevisionSync.";
                    return Result.Failed;
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Revision_LeakCheck", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        private const string TbSource = "PRJ_TB_REVISION_NR_TXT (title block)";
        private const string LockedTbSource = "PRJ_TB_REVISION_NR_TXT (title block, locked)";
        private static readonly HashSet<string> LockedSources = new HashSet<string> { LockedTbSource };

        internal static List<RevisionLeakFinding> Scan(Document doc, out int sheetCount)
        {
            var all = new List<RevisionLeakFinding>();
            var sheets = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
                .Where(s => !s.IsPlaceholder).OrderBy(s => s.SheetNumber).ToList();
            sheetCount = sheets.Count;
            foreach (var sheet in sheets)
            {
                var state = SheetRevisionReader.Read(doc, sheet);
                var stamps = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>(ParamRegistry.SHT_REV, Text(sheet, ParamRegistry.SHT_REV)),
                };
                try
                {
                    foreach (var tb in new FilteredElementCollector(doc, sheet.Id)
                                 .OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilyInstance)))
                    {
                        bool isLocked = false;
                        try { isLocked = TitleBlockParamApplier.IsTitleBlockLocked(tb, sheet); }
                        catch (Exception lx) { StingLog.Warn($"Revision_LeakCheck: lock read on {sheet.SheetNumber}: {lx.Message}"); }
                        stamps.Add(new KeyValuePair<string, string>(isLocked ? LockedTbSource : TbSource,
                                                                     Text(tb, "PRJ_TB_REVISION_NR_TXT")));
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Revision_LeakCheck: title blocks on {sheet.SheetNumber}: {ex.Message}"); }
                all.AddRange(SheetRevisionResolver.Check(sheet.SheetNumber, state, stamps, LockedSources));
            }
            return all;
        }

        /// <summary>The parameter's text, or null when the element has no such string
        /// parameter (nothing is printed from it, so it cannot leak).</summary>
        private static string Text(Element el, string name)
        {
            try
            {
                var p = el?.LookupParameter(name);
                if (p == null || p.StorageType != StorageType.String) return null;
                return p.AsString() ?? "";
            }
            catch { return null; }
        }

        private static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
