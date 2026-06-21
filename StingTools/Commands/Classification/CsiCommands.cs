using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using StingTools.BOQ;
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

        // ── Phase A (KUT lifecycle) — CSI section → NRM2 bridge cache ──────────
        // BuildLineItemFromElement calls SectionToNrm2 once per element; the
        // per-document cache keeps the CSV load to once per BuildBOQDocument run
        // (mirrors RateProviderRegistry's per-PathName caching). Invalidate is
        // wired alongside RateProviderRegistry.Invalidate on Cost_ReloadRules.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, string>> _nrm2Cache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Normalised CSI section → NRM2 work-section code, from the
        /// corporate map + project overlay. Empty when no rule carries an Nrm2.</summary>
        public static Dictionary<string, string> SectionToNrm2(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _nrm2Cache.GetOrAdd(key, _unused =>
            {
                var rules = Load(doc, out int _, out int _);
                return CsiMasterFormat.BuildSectionToNrm2(rules);
            });
        }

        public static void Invalidate() { _nrm2Cache.Clear(); _unitCache.Clear(); _specCache.Clear(); }

        // ── Phase H1 (KUT lifecycle) — CSI section → preferred-unit + spec store ──
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, string>> _unitCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, StingTools.BOQ.SpecSection>> _specCache
            = new System.Collections.Concurrent.ConcurrentDictionary<string, Dictionary<string, StingTools.BOQ.SpecSection>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Normalised CSI section → preferred measurement unit (advisory).</summary>
        public static Dictionary<string, string> SectionToUnit(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _unitCache.GetOrAdd(key, _unused =>
                CsiMasterFormat.BuildSectionToUnit(Load(doc, out int _, out int _)));
        }

        /// <summary>Normalised CSI section → issued SpecLink section text, from
        /// <project>/_BIM_COORD/speclink/sections.json. Empty when the store is absent
        /// (the dominant case — projects that haven't run SpecLink_ImportFolder yet).</summary>
        public static Dictionary<string, StingTools.BOQ.SpecSection> SpecSections(Document doc)
        {
            string key = doc?.PathName ?? "default";
            return _specCache.GetOrAdd(key, _unused =>
            {
                try
                {
                    string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                    if (string.IsNullOrEmpty(dir)) return new Dictionary<string, StingTools.BOQ.SpecSection>(StringComparer.Ordinal);
                    string p = Path.Combine(dir, "_BIM_COORD", "speclink", "sections.json");
                    if (!File.Exists(p)) return new Dictionary<string, StingTools.BOQ.SpecSection>(StringComparer.Ordinal);
                    return StingTools.BOQ.SpecStore.Parse(File.ReadAllText(p));
                }
                catch (Exception ex) { StingLog.Warn($"Spec store load: {ex.Message}"); return new Dictionary<string, StingTools.BOQ.SpecSection>(StringComparer.Ordinal); }
            });
        }
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

            // Phase B (KUT lifecycle) — cost↔spec gap rows from the BOQ join.
            // PRICED_UNSPECIFIED: priced BOQ line with no CSI section / a section
            //   not in the SpecLink ToC → carries the UGX value at risk.
            // SPECIFIED_UNPRICED: a ToC section that has zero measured BOQ value.
            var gaps = ComputeCostGaps(doc, spec);

            string xlsx = WriteReport(doc, rec, gaps);
            var sb = new StringBuilder();
            sb.AppendLine($"Model CSI sections: {model.Count}   Spec sections: {spec.Count}");
            sb.AppendLine();
            sb.AppendLine($"Spec gaps (model section, no spec):     {rec.SpecGaps.Count}");
            sb.AppendLine($"Over-specification (spec, no model):    {rec.OverSpec.Count}  (INFO)");
            sb.AppendLine($"Title mismatches:                       {rec.TitleMismatches.Count}");
            sb.AppendLine();
            sb.AppendLine("Cost ↔ spec gaps (BOQ join):");
            sb.AppendLine($"   Priced but unspecified:   {gaps.PricedUnspecified.Count} group(s) — UGX {gaps.PricedUnspecifiedTotalUgx:N0} at risk");
            sb.AppendLine($"   Specified but unpriced:   {gaps.SpecifiedUnpriced.Count} section(s)");
            if (!gaps.BoqAvailable)
                sb.AppendLine("   (BOQ document unavailable — cost gaps skipped.)");
            if (gaps.PricedUnspecified.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Top priced-unspecified (value at risk):");
                foreach (var g in gaps.PricedUnspecified.OrderByDescending(x => x.Ugx).Take(8))
                    sb.AppendLine($"   {g.Section,-12} {g.Category,-22} UGX {g.Ugx:N0} ({g.Count})");
            }
            if (xlsx != null) { sb.AppendLine(); sb.AppendLine($"Report: {xlsx}"); }

            new TaskDialog("SpecLink Reconcile")
            {
                MainInstruction = $"{rec.SpecGaps.Count} spec gap(s), {gaps.PricedUnspecified.Count} priced-unspecified group(s)",
                MainContent = sb.ToString()
            }.Show();
            StingLog.Info($"SpecLink_Reconcile: gaps={rec.SpecGaps.Count} over={rec.OverSpec.Count} mismatch={rec.TitleMismatches.Count} " +
                $"pricedUnspec={gaps.PricedUnspecified.Count}(UGX {gaps.PricedUnspecifiedTotalUgx:N0}) specUnpriced={gaps.SpecifiedUnpriced.Count}");
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

        // ── Phase B (KUT lifecycle) — cost ↔ spec gap join ────────────────────
        private sealed class CostSpecGapResult
        {
            public bool BoqAvailable;
            // grouped by (CSI section or "(none)", category): count + UGX at risk
            public List<(string Section, string Category, int Count, double Ugx)> PricedUnspecified
                = new List<(string, string, int, double)>();
            public double PricedUnspecifiedTotalUgx;
            public List<CsiMasterFormat.CsiTocEntry> SpecifiedUnpriced
                = new List<CsiMasterFormat.CsiTocEntry>();
        }

        /// <summary>Joins the live BOQ document against the spec TOC (sections already
        /// whitespace-normalised). Priced lines whose CSI section is empty or absent from
        /// the TOC are "priced but unspecified" (value at risk); TOC sections with zero
        /// measured BOQ value are "specified but unpriced".</summary>
        private static CostSpecGapResult ComputeCostGaps(Document doc, Dictionary<string, string> spec)
        {
            var result = new CostSpecGapResult();
            BOQDocument boq;
            try { boq = BOQCostManager.BuildBOQDocument(doc); }
            catch (Exception ex) { StingLog.Warn($"SpecLink cost gaps — BOQ build: {ex.Message}"); return result; }
            if (boq == null) return result;
            result.BoqAvailable = true;

            // measured UGX per normalised CSI section, plus priced-unspecified groups
            var measuredBySection = new Dictionary<string, double>(StringComparer.Ordinal);
            var unspecGroups = new Dictionary<(string Sec, string Cat), (int Count, double Ugx)>();

            foreach (var it in boq.AllItems)
            {
                if (it == null || it.TotalUGX <= 0) continue;
                string sec = CsiMasterFormat.NormalizeSection(it.CsiSection ?? "");
                if (sec.Length > 0)
                {
                    measuredBySection.TryGetValue(sec, out double v);
                    measuredBySection[sec] = v + it.TotalUGX;
                }
                bool unspecified = sec.Length == 0 || !spec.ContainsKey(sec);
                if (unspecified)
                {
                    string secKey = sec.Length == 0 ? "(none)" : sec;
                    var gk = (secKey, it.Category ?? "");
                    unspecGroups.TryGetValue(gk, out var agg);
                    unspecGroups[gk] = (agg.Count + 1, agg.Ugx + it.TotalUGX);
                    result.PricedUnspecifiedTotalUgx += it.TotalUGX;
                }
            }

            foreach (var kv in unspecGroups)
                result.PricedUnspecified.Add((kv.Key.Sec, kv.Key.Cat, kv.Value.Count, kv.Value.Ugx));

            foreach (var kv in spec)
            {
                measuredBySection.TryGetValue(kv.Key, out double measured);
                if (measured <= 0)
                    result.SpecifiedUnpriced.Add(new CsiMasterFormat.CsiTocEntry { Section = kv.Key, Title = kv.Value });
            }
            return result;
        }

        private static string WriteReport(Document doc, CsiMasterFormat.CsiReconcileResult rec, CostSpecGapResult gaps)
        {
            try
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet("SpecLink Reconcile");
                string[] hdr = { "Type", "Section", "Model Title / Category", "Spec Title", "Value at risk (UGX)", "Count" };
                for (int c = 0; c < hdr.Length; c++) { ws.Cell(1, c + 1).Value = hdr[c]; ws.Cell(1, c + 1).Style.Font.Bold = true; }
                int row = 2;
                foreach (var g in rec.SpecGaps) { ws.Cell(row, 1).Value = "SPEC_GAP"; ws.Cell(row, 2).Value = g.Section; ws.Cell(row, 3).Value = g.Title; row++; }
                foreach (var m in rec.TitleMismatches) { ws.Cell(row, 1).Value = "TITLE_MISMATCH"; ws.Cell(row, 2).Value = m.Section; ws.Cell(row, 3).Value = m.ModelTitle; ws.Cell(row, 4).Value = m.SpecTitle; row++; }
                foreach (var o in rec.OverSpec) { ws.Cell(row, 1).Value = "OVER_SPEC"; ws.Cell(row, 2).Value = o.Section; ws.Cell(row, 4).Value = o.Title; row++; }
                // Phase B — cost↔spec gaps
                foreach (var p in (gaps?.PricedUnspecified ?? new List<(string, string, int, double)>()).OrderByDescending(x => x.Ugx))
                {
                    ws.Cell(row, 1).Value = "PRICED_UNSPECIFIED";
                    ws.Cell(row, 2).Value = p.Section;
                    ws.Cell(row, 3).Value = p.Category;
                    ws.Cell(row, 5).Value = p.Ugx; ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
                    ws.Cell(row, 6).Value = p.Count;
                    row++;
                }
                foreach (var s in gaps?.SpecifiedUnpriced ?? new List<CsiMasterFormat.CsiTocEntry>())
                {
                    ws.Cell(row, 1).Value = "SPECIFIED_UNPRICED";
                    ws.Cell(row, 2).Value = s.Section;
                    ws.Cell(row, 4).Value = s.Title;
                    ws.Cell(row, 5).Value = 0; ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0";
                    row++;
                }
                ws.Columns().AdjustToContents();
                string path = OutputLocationHelper.GetOutputPath(doc, $"STING_SpecLink_Reconcile_{DateTime.Now:yyyyMMdd}.xlsx");
                wb.SaveAs(path);
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"SpecLink report: {ex.Message}"); return null; }
        }
    }
}
