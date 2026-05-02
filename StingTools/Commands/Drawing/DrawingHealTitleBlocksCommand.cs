// StingTools — Drawing Template Manager · Phase 168
//
// DrawingHealTitleBlocksCommand is the partial-sync sibling of
// DrawingSyncStylesCommand. Where SyncStyles re-applies the entire
// profile (scale, detail level, view template, view-style pack,
// annotation, etc.), this command targets only the title-block
// parameter binding layer. Use it when the operator wants to fix
// title-block cells without disturbing in-progress view-template
// edits or pending annotation work.
//
// Each healed sheet contributes a row to <project>/_BIM_COORD/
// titleblock_heal_audit.jsonl so the project history records what
// changed, when, and by whom.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DrawingHealTitleBlocksCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                // Collect every stamped sheet — even the ones drift detector
                // missed because their drift was String-only previously and
                // we now want to heal Integer/Yes-No/Double/ElementId too.
                var sheets = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewSheet))
                    .Cast<ViewSheet>()
                    .Where(s => !s.IsPlaceholder)
                    .Where(s => !DrawingTypeStamper.IsLocked(s))
                    .Select(s => new
                    {
                        Sheet = s,
                        DtId  = ParameterHelpers.GetString(s, DrawingTypeStamper.PARAM_DRAWING_TYPE_ID) ?? "",
                    })
                    .Where(x => !string.IsNullOrEmpty(x.DtId))
                    .ToList();

                if (sheets.Count == 0)
                {
                    TaskDialog.Show("STING — Heal Title Blocks",
                        "No stamped & unlocked sheets found. Stamp sheets via the Drawing Types pipeline first.");
                    return Result.Succeeded;
                }

                var confirm = new TaskDialog("STING — Heal Title Blocks")
                {
                    MainInstruction = $"Heal title blocks on {sheets.Count} sheet(s)?",
                    MainContent =
                        "This rewrites every title-block parameter declared by each sheet's profile " +
                        "(titleBlockParams + per-symbol overlays). Scale / detail level / view template / " +
                        "view-style pack / annotation are NOT touched. Locked sheets are skipped.",
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Ok,
                };
                if (confirm.Show() != TaskDialogResult.Ok) return Result.Cancelled;

                var auditRows = new List<HealRow>();
                int totalParams = 0;
                int healed = 0;
                using (TitleBlockParamApplier.Batch())
                using (var tx = new Transaction(doc, "STING — Heal Title Blocks"))
                {
                    tx.Start();
                    foreach (var x in sheets)
                    {
                        var dt = DrawingTypeRegistry.Get(doc, x.DtId);
                        if (dt == null) continue;
                        var tokens = DrawingTokenContext.Build(
                            doc:        doc,
                            dt:         dt,
                            discCode:   dt.Discipline,
                            discipline: dt.Discipline,
                            seq:        DrawingTokenContext.ExtractSeqFromSheetNumber(x.Sheet.SheetNumber));
                        var result = TitleBlockParamApplier.Apply(doc, x.Sheet, dt, tokens);
                        totalParams += result.ParamsWritten;
                        if (result.ParamsWritten > 0) healed++;
                        if (result.ParamsWritten > 0 || result.Warnings.Count > 0)
                        {
                            auditRows.Add(new HealRow
                            {
                                UtcTimestamp  = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                User          = Environment.UserName ?? "unknown",
                                SheetNumber   = x.Sheet.SheetNumber ?? "",
                                SheetName     = x.Sheet.Name ?? "",
                                DrawingTypeId = x.DtId,
                                ParamsWritten = result.ParamsWritten,
                                Warnings      = result.Warnings.Take(10).ToList(),
                            });
                        }
                    }
                    tx.Commit();
                }

                if (auditRows.Count > 0) AppendAuditLog(doc, auditRows);

                var sb = new StringBuilder();
                sb.AppendLine($"Healed title blocks on {healed} of {sheets.Count} sheet(s).");
                sb.AppendLine($"Total param writes: {totalParams}.");
                int withWarn = auditRows.Count(a => a.Warnings != null && a.Warnings.Count > 0);
                if (withWarn > 0) sb.AppendLine($"{withWarn} sheet(s) emitted warnings — see _BIM_COORD/titleblock_heal_audit.jsonl.");
                TaskDialog.Show("STING — Heal Title Blocks", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("DrawingHealTitleBlocks", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private sealed class HealRow
        {
            public string UtcTimestamp { get; set; }
            public string User { get; set; }
            public string SheetNumber { get; set; }
            public string SheetName { get; set; }
            public string DrawingTypeId { get; set; }
            public int    ParamsWritten { get; set; }
            public List<string> Warnings { get; set; }
        }

        private static void AppendAuditLog(Document doc, List<HealRow> rows)
        {
            try
            {
                string outDir = !string.IsNullOrEmpty(doc?.PathName)
                    ? Path.Combine(Path.GetDirectoryName(doc.PathName) ?? "", "_BIM_COORD")
                    : Path.Combine(Path.GetTempPath(), "STING");
                Directory.CreateDirectory(outDir);
                var path = Path.Combine(outDir, "titleblock_heal_audit.jsonl");
                using (var sw = File.AppendText(path))
                {
                    foreach (var r in rows)
                        sw.WriteLine(JsonConvert.SerializeObject(r, Formatting.None));
                }
            }
            catch (Exception ex) { StingLog.Warn($"AppendAuditLog: {ex.Message}"); }
        }
    }
}
