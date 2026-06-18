using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.Core;
using StingTools.Core.Classification;

namespace StingTools.Commands.Classification
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (C2) — CSI MasterFormat / SpecLink commands.
    //
    // CSI_Assign         resolve + write CSI_SECTION_TXT / CSI_TITLE_TXT on
    //                    taggable elements from STING_CSI_MASTERFORMAT_MAP.csv.
    // SpecLink_Reconcile compare model CSI sections against the RIB SpecLink spec
    //                    TOC (spec gaps / over-spec / title mismatches → XLSX).
    // ─────────────────────────────────────────────────────────────────────────

    internal static class CsiMap
    {
        public static List<CsiRule> Load(Document doc, out int corp, out int overlay)
        {
            corp = 0; overlay = 0;
            var rules = new List<CsiRule>();
            // Project overlay first so it wins ties (Resolve takes the earliest on a tie).
            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (!string.IsNullOrEmpty(dir))
                {
                    string p = Path.Combine(dir, "_BIM_COORD", "csi_map.csv");
                    if (File.Exists(p))
                    {
                        var r = CsiMasterFormat.ParseCsvLines(File.ReadAllLines(p));
                        overlay = r.Count; rules.AddRange(r);
                    }
                }
            }
            catch (Exception ex) { StingLog.Warn($"CSI overlay load: {ex.Message}"); }

            try
            {
                string c = StingToolsApp.FindDataFile("STING_CSI_MASTERFORMAT_MAP.csv");
                if (!string.IsNullOrEmpty(c) && File.Exists(c))
                {
                    var r = CsiMasterFormat.ParseCsvLines(File.ReadAllLines(c));
                    corp = r.Count; rules.AddRange(r);
                }
            }
            catch (Exception ex) { StingLog.Warn($"CSI corporate load: {ex.Message}"); }

            return rules;
        }

        public static List<Element> Scope(UIDocument uidoc, Document doc, out string label)
            => StingTools.Tags.TagSchemeCommandHelper.CollectScope(uidoc, doc, out label);
    }

    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CsiAssignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var rules = CsiMap.Load(doc, out int corp, out int overlay);
            if (rules.Count == 0)
            {
                TaskDialog.Show("CSI Assign", "No CSI map found. Ship STING_CSI_MASTERFORMAT_MAP.csv in data/ " +
                    "or add _BIM_COORD/csi_map.csv.");
                return Result.Succeeded;
            }

            var picker = new TaskDialog("CSI Assign")
            {
                MainInstruction = "Write CSI section to elements",
                MainContent = $"{rules.Count} rules ({corp} corporate + {overlay} project). Choose write mode:",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                AllowCancellation = true
            };
            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Fill empty only", "Only write where CSI_SECTION_TXT is blank");
            picker.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Overwrite all", "Re-resolve and overwrite existing values");
            var choice = picker.Show();
            if (choice == TaskDialogResult.Cancel) return Result.Cancelled;
            bool overwrite = choice == TaskDialogResult.CommandLink2;

            var scope = CsiMap.Scope(ctx.UIDoc, doc, out string scopeLabel);
            int assigned = 0, skippedSet = 0, unresolved = 0;
            var unmappedCats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            using (var t = new Transaction(doc, "STING CSI Assign"))
            {
                t.Start();
                foreach (var el in scope)
                {
                    string cat = ParameterHelpers.GetCategoryName(el);
                    string fam = ParameterHelpers.GetFamilyName(el);
                    string type = ParameterHelpers.GetFamilySymbolName(el);
                    string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                    var rule = CsiMasterFormat.Resolve(rules, cat, fam, type, sys);
                    if (rule == null)
                    {
                        unresolved++;
                        unmappedCats.TryGetValue(cat, out int c); unmappedCats[cat] = c + 1;
                        continue;
                    }
                    if (!overwrite && !string.IsNullOrEmpty(ParameterHelpers.GetString(el, ParamRegistry.CSI_SECTION)))
                    { skippedSet++; continue; }
                    if (!TagPipelineHelper.IsEditableInWorksharing(doc, el)) continue;

                    bool w1 = ParameterHelpers.SetString(el, ParamRegistry.CSI_SECTION, rule.Section, overwrite: true);
                    ParameterHelpers.SetString(el, ParamRegistry.CSI_TITLE, rule.Title, overwrite: true);
                    if (w1) assigned++;
                }
                t.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Scope: {scopeLabel}   Mode: {(overwrite ? "overwrite" : "fill empty")}");
            sb.AppendLine($"Assigned:        {assigned}");
            sb.AppendLine($"Skipped (set):   {skippedSet}");
            sb.AppendLine($"Unresolved:      {unresolved}");
            if (unmappedCats.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Unmapped categories (add rows to _BIM_COORD/csi_map.csv):");
                foreach (var kv in unmappedCats.OrderByDescending(k => k.Value).Take(15))
                    sb.AppendLine($"   {kv.Value,5}  {kv.Key}");
            }
            new TaskDialog("CSI Assign")
            {
                MainInstruction = $"{assigned} element(s) assigned a CSI section",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"CSI_Assign: {assigned} assigned, {unresolved} unresolved ({scopeLabel})");
            return Result.Succeeded;
        }
    }

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class SpecLinkReconcileCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select the SpecLink spec TOC (CSV or XLSX with Section + Title columns)",
                Filter = "Spec TOC (*.csv;*.xlsx)|*.csv;*.xlsx",
                InitialDirectory = OutputLocationHelper.GetOutputDirectory(doc)
            };
            if (dlg.ShowDialog() != true) return Result.Cancelled;

            Dictionary<string, string> spec;
            try { spec = ReadToc(dlg.FileName); }
            catch (Exception ex)
            {
                TaskDialog.Show("SpecLink Reconcile", $"Could not read the TOC:\n{ex.Message}");
                return Result.Failed;
            }
            if (spec.Count == 0)
            {
                TaskDialog.Show("SpecLink Reconcile", "No spec sections read — check the TOC has Section/Title columns.");
                return Result.Succeeded;
            }

            var model = ReadModelSections(doc);
            var rec = CsiMasterFormat.Reconcile(model, spec);

            string xlsx = WriteReport(doc, rec);
            var sb = new StringBuilder();
            sb.AppendLine($"Model CSI sections: {model.Count}   Spec sections: {spec.Count}");
            sb.AppendLine();
            sb.AppendLine($"Spec gaps (model section, no spec):     {rec.SpecGaps.Count}");
            sb.AppendLine($"Over-specification (spec, no model):    {rec.OverSpec.Count}  (INFO)");
            sb.AppendLine($"Title mismatches:                       {rec.TitleMismatches.Count}");
            if (rec.SpecGaps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("First spec gaps:");
                foreach (var g in rec.SpecGaps.Take(10)) sb.AppendLine($"   {g.Section}  {g.Title}");
            }
            if (xlsx != null) { sb.AppendLine(); sb.AppendLine($"Report: {xlsx}"); }

            new TaskDialog("SpecLink Reconcile")
            {
                MainInstruction = $"{rec.SpecGaps.Count} spec gap(s), {rec.TitleMismatches.Count} title mismatch(es)",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"SpecLink_Reconcile: gaps={rec.SpecGaps.Count} over={rec.OverSpec.Count} mismatch={rec.TitleMismatches.Count}");
            return Result.Succeeded;
        }

        private static Dictionary<string, string> ReadModelSections(Document doc)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(e => e.Category != null);
            foreach (var el in collector)
            {
                string sec = ParameterHelpers.GetString(el, ParamRegistry.CSI_SECTION);
                if (string.IsNullOrEmpty(sec)) continue;
                string key = CsiMasterFormat.NormalizeSection(sec);
                if (!d.ContainsKey(key))
                    d[key] = ParameterHelpers.GetString(el, ParamRegistry.CSI_TITLE);
            }
            return d;
        }

        private static Dictionary<string, string> ReadToc(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            var sectionHeaders = new[] { "section", "csi", "number", "code" };
            var titleHeaders = new[] { "title", "description", "name", "section title" };

            if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                using var wb = new XLWorkbook(path);
                var ws = wb.Worksheets.First();
                var used = ws.RangeUsed();
                if (used == null) return d;
                int fr = used.FirstRow().RowNumber(), lr = used.LastRow().RowNumber();
                int fc = used.FirstColumn().ColumnNumber(), lc = used.LastColumn().ColumnNumber();
                var hdr = new List<string>();
                for (int c = fc; c <= lc; c++) hdr.Add(ws.Cell(fr, c).GetString().Trim().ToLowerInvariant());
                int cSec = FindCol(hdr, sectionHeaders), cTit = FindCol(hdr, titleHeaders);
                if (cSec < 0) return d;
                for (int r = fr + 1; r <= lr; r++)
                {
                    string sec = ws.Cell(r, fc + cSec).GetString().Trim();
                    if (sec.Length == 0) continue;
                    string tit = cTit >= 0 ? ws.Cell(r, fc + cTit).GetString().Trim() : "";
                    string key = CsiMasterFormat.NormalizeSection(sec);
                    if (!d.ContainsKey(key)) d[key] = tit;
                }
            }
            else
            {
                var lines = File.ReadAllLines(path);
                if (lines.Length < 2) return d;
                var hdr = StingToolsApp.ParseCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
                int cSec = FindCol(hdr, sectionHeaders), cTit = FindCol(hdr, titleHeaders);
                if (cSec < 0) return d;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var f = StingToolsApp.ParseCsvLine(lines[i]);
                    if (cSec >= f.Length) continue;
                    string sec = f[cSec].Trim();
                    if (sec.Length == 0) continue;
                    string tit = (cTit >= 0 && cTit < f.Length) ? f[cTit].Trim() : "";
                    string key = CsiMasterFormat.NormalizeSection(sec);
                    if (!d.ContainsKey(key)) d[key] = tit;
                }
            }
            return d;
        }

        private static int FindCol(List<string> hdr, string[] cands)
        {
            foreach (var cand in cands.OrderByDescending(c => c.Length))
                for (int i = 0; i < hdr.Count; i++)
                    if (!string.IsNullOrEmpty(hdr[i]) && hdr[i].Contains(cand)) return i;
            return -1;
        }

        private static string WriteReport(Document doc, CsiMasterFormat.CsiReconcileResult rec)
        {
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("SpecLink Reconcile");
                string[] hdr = { "Type", "Section", "Model Title", "Spec Title" };
                for (int c = 0; c < hdr.Length; c++) { ws.Cell(1, c + 1).Value = hdr[c]; ws.Cell(1, c + 1).Style.Font.Bold = true; }
                int row = 2;
                foreach (var g in rec.SpecGaps) { ws.Cell(row, 1).Value = "SPEC_GAP"; ws.Cell(row, 2).Value = g.Section; ws.Cell(row, 3).Value = g.Title; row++; }
                foreach (var m in rec.TitleMismatches) { ws.Cell(row, 1).Value = "TITLE_MISMATCH"; ws.Cell(row, 2).Value = m.Section; ws.Cell(row, 3).Value = m.ModelTitle; ws.Cell(row, 4).Value = m.SpecTitle; row++; }
                foreach (var o in rec.OverSpec) { ws.Cell(row, 1).Value = "OVER_SPEC"; ws.Cell(row, 2).Value = o.Section; ws.Cell(row, 4).Value = o.Title; row++; }
                ws.Columns().AdjustToContents();
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_SpecLink_Reconcile_{DateTime.Now:yyyyMMdd}.xlsx");
                wb.SaveAs(path);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"SpecLink report: {ex.Message}"); return null; }
        }
    }
}
