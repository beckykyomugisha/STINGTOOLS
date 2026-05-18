// ══════════════════════════════════════════════════════════════════════════
//  BOQBccBridge.cs — Phase 108k
//  Bridges the BOQ Cost Manager into the BIM Coordination Center.
//  10-point integration from the Phase 108j roadmap — Item 1 (Monthly Cost
//  Review preset) was implemented inside WorkflowEngine already; Items
//  2–10 live here, each as a small static helper the host systems hook
//  with a single line.
//
//    Item 2  ComputeIssueCostImpact   — cost per open issue from linked elements
//    Item 3  OnRevisionCreated        — auto BOQ snapshot on every revision
//    Item 4  ComputeClashCosts        — cost per clash-group from affected elements
//    Item 5  GetBOQRatesByCategory    — replace Scheduling4DEngine's internal rates
//    Item 6  BuildMeetingAgendaBullet — BOQ delta bullet for auto-agenda
//    Item 7  ComputeBOQHealthBand     — "BOQ Data Quality" row on Model Health
//    Item 8  EmitBOQGapWarnings       — synthetic warnings for missing rates / tokens
//    Item 9  GetBOQDeliverableItem    — "Bill of Quantities (NRM2)" tracker row
//    Item 10 RouteExportToCDE         — move exports to WIP/SHARED/PUBLISHED
//
//  Design: every method is failure-tolerant and cheap to call. When the BOQ
//  engine can't produce a document (no tagged elements, corrupt doc) the
//  bridge silently no-ops, returning empty / null. Callers must handle
//  null with a friendly fallback.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using System.Text.RegularExpressions;
using Autodesk.Revit.UI;

namespace StingTools.BOQ
{
    internal static class BOQBccBridge
    {
        // Shared lazy BOQ retrieval. Building the doc is ~2-5 s on large
        // models; cache per-call per-doc so callers can compose without
        // paying a full rebuild each time.
        [ThreadStatic] private static BOQDocument _boqCache;
        [ThreadStatic] private static string _boqCacheDocKey;

        private static BOQDocument GetBoq(Document doc)
        {
            if (doc == null) return null;
            string key = doc.PathName ?? doc.Title ?? "";
            if (_boqCache != null && _boqCacheDocKey == key) return _boqCache;
            try
            {
                _boqCache = BOQCostManager.BuildBOQDocument(doc);
                _boqCacheDocKey = key;
                return _boqCache;
            }
            catch (Exception ex) { StingLog.Warn($"BOQBccBridge.GetBoq: {ex.Message}"); }
            return null;
        }

        public static void InvalidateCache()
        {
            _boqCache = null;
            _boqCacheDocKey = null;
        }

        // Index model BOQLineItems by RevitElementId for O(1) lookup.
        private static Dictionary<long, BOQLineItem> IndexByElementId(BOQDocument boq)
        {
            var d = new Dictionary<long, BOQLineItem>();
            if (boq == null) return d;
            foreach (var it in boq.AllItems)
                if (it.RevitElementId > 0)
                    d[it.RevitElementId] = it;
            return d;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Item 2 — Issue → BOQ cost impact
        //  For every open issue in {projectDir}/STING_BIM_MANAGER/issues.json,
        //  sum the TotalUGX of the elements listed in its element_ids array
        //  and store back as the issue's cost_impact_ugx field. Coordinators
        //  sort by impact to remediate the most expensive issues first.
        // ══════════════════════════════════════════════════════════════════

        public static int ComputeIssueCostImpact(Document doc)
        {
            if (doc == null) return 0;
            string issuesPath = GetBimManagerFile(doc, "issues.json");
            if (issuesPath == null || !File.Exists(issuesPath)) return 0;

            var boq = GetBoq(doc);
            var idx = IndexByElementId(boq);
            if (idx.Count == 0) return 0;

            JArray issues;
            try { issues = JArray.Parse(File.ReadAllText(issuesPath)); }
            catch (Exception ex) { StingLog.Warn($"Issue cost impact read: {ex.Message}"); return 0; }

            int touched = 0;
            foreach (var issue in issues)
            {
                var status = issue["status"]?.ToString() ?? "";
                if (status.Equals("CLOSED", StringComparison.OrdinalIgnoreCase)) continue;

                var elementIds = issue["element_ids"] as JArray;
                if (elementIds == null || elementIds.Count == 0) continue;

                double costUgx = 0;
                int matched = 0;
                foreach (var eid in elementIds)
                {
                    if (long.TryParse(eid?.ToString(), out long id)
                        && idx.TryGetValue(id, out var li))
                    {
                        costUgx += li.TotalUGX;
                        matched++;
                    }
                }
                issue["cost_impact_ugx"] = costUgx;
                issue["cost_impact_element_matches"] = matched;
                issue["cost_impact_updated"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                touched++;
            }

            try { AtomicJsonWrite(issuesPath, issues); }
            catch (Exception ex) { StingLog.Warn($"Issue cost impact write: {ex.Message}"); return 0; }

            StingLog.Info($"Issue cost impact: updated {touched} open issues.");
            return touched;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Item 3 — Revision → BOQ snapshot auto-save
        //  Called from CreateRevisionCommand after RevisionEngine snapshots
        //  the tag state. Auto-saves a BOQ snapshot so every revision has
        //  a matching cost baseline the QS can diff against.
        // ══════════════════════════════════════════════════════════════════

        public static string OnRevisionCreated(Document doc, string revisionCode, string revisionDescription)
        {
            if (doc == null) return null;
            InvalidateCache();  // fresh BOQ for a fresh revision
            try
            {
                var boq = GetBoq(doc);
                if (boq == null) return null;
                string label = string.IsNullOrEmpty(revisionDescription)
                    ? revisionCode
                    : $"{revisionCode} — {revisionDescription}";
                string path = BOQCostManager.SaveSnapshot(doc, boq, label, "Revision");
                StingLog.Info($"BOQ snapshot auto-saved for revision {revisionCode}: {Path.GetFileName(path ?? "")}");
                return path;
            }
            catch (Exception ex) { StingLog.Warn($"OnRevisionCreated: {ex.Message}"); return null; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Item 4 — Clash → remediation cost
        //  Scans the most recent clash-detection result JSON in
        //  STING_BIM_MANAGER/clash_*.json, sums the BOQ TotalUGX for each
        //  clash's affected-element list, and writes back a
        //  remediation_cost_ugx field. Tops up the coordination-center
        //  Clashes tab with a cost priority column.
        // ══════════════════════════════════════════════════════════════════

        public static int ComputeClashCosts(Document doc)
        {
            if (doc == null) return 0;
            string dir = GetBimManagerDir(doc);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;

            var files = Directory.GetFiles(dir, "clash_*.json")
                .Concat(Directory.GetFiles(dir, "crossclash_*.json"))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .Take(3)  // three most recent reports
                .ToList();
            if (files.Count == 0) return 0;

            var boq = GetBoq(doc);
            var idx = IndexByElementId(boq);
            int total = 0;

            foreach (var f in files)
            {
                JArray clashes;
                try
                {
                    var raw = File.ReadAllText(f);
                    var parsed = JToken.Parse(raw);
                    clashes = parsed as JArray ?? parsed["results"] as JArray ?? parsed["clashes"] as JArray;
                    if (clashes == null) continue;
                }
                catch (Exception ex) { StingLog.Warn($"Clash cost read {Path.GetFileName(f)}: {ex.Message}"); continue; }

                foreach (var c in clashes)
                {
                    double cost = 0;
                    int matches = 0;
                    var a = c["element_a_id"];
                    var b = c["element_b_id"];
                    foreach (var tok in new[] { a, b })
                    {
                        if (tok == null) continue;
                        if (long.TryParse(tok.ToString(), out long id)
                            && idx.TryGetValue(id, out var li))
                        {
                            cost += li.TotalUGX;
                            matches++;
                        }
                    }
                    c["remediation_cost_ugx"] = cost;
                    c["remediation_cost_matches"] = matches;
                }

                try { AtomicJsonWrite(f, clashes); total++; }
                catch (Exception ex) { StingLog.Warn($"Clash cost write {Path.GetFileName(f)}: {ex.Message}"); }
            }

            StingLog.Info($"Clash remediation costs updated across {total} clash report(s).");
            return total;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Item 5 — 4D/5D cash flow from BOQ
        //  Scheduling4DEngine currently uses its own internal rate table
        //  (DefaultCostRates) as a fallback when cost_rates_5d.csv has no
        //  entry. This bridge returns a Dictionary<category, rate> derived
        //  from the LIVE BOQDocument so the cash-flow curve, 4D timeline
        //  and 5D exports stay consistent with the Cost Manager's rates
        //  (including the P0 Override any QS edit set inline).
        //
        //  Returns the AVERAGE rate across all model items of each
        //  category so a section of mixed-size walls still gets a single
        //  reasonable rate figure. Keyed by Category name (case-insensitive).
        // ══════════════════════════════════════════════════════════════════

        public static Dictionary<string, double> GetBOQRatesByCategory(Document doc)
        {
            var d = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var boq = GetBoq(doc);
            if (boq == null) return d;
            var grp = boq.AllItems
                .Where(i => !string.IsNullOrEmpty(i.Category) && i.RateUGX > 0 && i.Source == BOQRowSource.Model)
                .GroupBy(i => i.Category, StringComparer.OrdinalIgnoreCase);
            foreach (var g in grp)
                d[g.Key] = g.Average(i => i.RateUGX);
            return d;
        }

        /// <summary>
        /// Returns a category → (ratePerUnit, unit, description) triple
        /// matching Scheduling4DEngine.DefaultCostRates's shape, so the
        /// engine can do a direct lookup. Only populated when the BOQ has
        /// a rate for the category.
        /// </summary>
        public static Dictionary<string, (double rate, string unit, string description)>
            GetBOQCostRateTable(Document doc)
        {
            var d = new Dictionary<string, (double, string, string)>(StringComparer.OrdinalIgnoreCase);
            var boq = GetBoq(doc);
            if (boq == null) return d;
            var grp = boq.AllItems
                .Where(i => !string.IsNullOrEmpty(i.Category) && i.RateUGX > 0 && i.Source == BOQRowSource.Model)
                .GroupBy(i => i.Category, StringComparer.OrdinalIgnoreCase);
            foreach (var g in grp)
            {
                double rate = g.Average(i => i.RateUGX);
                string unit = g.First().Unit ?? "each";
                string desc = g.First().Category ?? "";
                d[g.Key] = (rate, unit, desc);
            }
            return d;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Item 6 — Meeting agenda auto-bullet
        //  Compares the most recent BOQ snapshot to the previous one and
        //  returns a one-paragraph summary suitable for dropping into the
        //  auto-generated meeting minutes / agenda.
        // ══════════════════════════════════════════════════════════════════

        public static string BuildMeetingAgendaBullet(Document doc)
        {
            if (doc == null) return null;
            try
            {
                var snaps = BOQCostManager.ListSnapshots(doc);
                if (snaps == null || snaps.Count < 1) return null;
                var boq = GetBoq(doc);
                if (boq == null) return null;

                var latest = snaps[0];
                double vsLatest = boq.GrandTotalUGX - latest.GrandTotalUGX;

                var sb = new StringBuilder();
                sb.AppendLine("## Cost status");
                sb.Append($"- Current BOQ grand total: UGX {boq.GrandTotalUGX:N0} "
                    + $"(Modeled UGX {boq.ModeledTotalUGX:N0}, Provisional UGX {boq.ProvTotalUGX:N0}).").AppendLine();
                string sign = vsLatest >= 0 ? "+" : "";
                sb.Append($"- Change vs snapshot \"{latest.Label}\" ({latest.Date:d MMM}): "
                    + $"{sign}UGX {vsLatest:N0} ({100.0 * vsLatest / Math.Max(1, latest.GrandTotalUGX):F1}%).").AppendLine();

                if (snaps.Count >= 2)
                {
                    // Diff first vs second — senior QS looks for category movers
                    var diff = BOQCostManager.CompareSnapshots(snaps[1].Path, snaps[0].Path);
                    if (diff != null && diff.SectionDiffs.Count > 0)
                    {
                        var top3 = diff.SectionDiffs
                            .OrderByDescending(s => Math.Abs(s.Delta))
                            .Take(3)
                            .ToList();
                        if (top3.Count > 0)
                        {
                            sb.AppendLine("- Top 3 section movers:");
                            foreach (var s in top3)
                            {
                                string d = s.Delta >= 0 ? "+" : "";
                                sb.Append($"  - §{s.NRM2Section} {s.Name} — {d}UGX {s.Delta:N0} ({s.DeltaPct:F1}%)").AppendLine();
                            }
                        }
                    }
                }

                sb.Append($"- Paragraph coverage: {boq.ParagraphCoveragePct:F0}% "
                    + $"· Avg rate confidence: {boq.AverageRateConfidence:F0}/100.").AppendLine();
                return sb.ToString();
            }
            catch (Exception ex) { StingLog.Warn($"BuildMeetingAgendaBullet: {ex.Message}"); return null; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Item 7 — Model Health "BOQ Data Quality" band
        //  Surface a compact health record for the Model Health Dashboard.
        //  Score is a weighted mix of paragraph coverage (50%), rate
        //  confidence (30%) and rate-fill completeness (20%) so a model
        //  with 100% paragraphs but zero rates scores around 50.
        // ══════════════════════════════════════════════════════════════════

        public class BOQHealthBand
        {
            public double Score;             // 0-100
            public string Grade;             // "Excellent" / "Good" / "Fair" / "Poor"
            public string Rag;               // "Green" / "Amber" / "Red"
            public double ParagraphCoveragePct;
            public double AvgRateConfidence;
            public double RateFillPct;
            public int TotalItems;
            public int ItemsMissingRate;
            public int ItemsMissingParagraph;
        }

        public static BOQHealthBand ComputeBOQHealthBand(Document doc)
        {
            var b = new BOQHealthBand();
            var boq = GetBoq(doc);
            if (boq == null || boq.AllItems.Count == 0)
            {
                b.Grade = "No BOQ";
                b.Rag = "Red";
                return b;
            }

            b.TotalItems = boq.AllItems.Count;
            b.ItemsMissingRate = boq.AllItems.Count(i => i.RateUGX <= 0);
            b.ItemsMissingParagraph = boq.AllItems.Count(i => string.IsNullOrEmpty(i.ResolvedNRM2Paragraph));
            b.ParagraphCoveragePct = boq.ParagraphCoveragePct;
            b.AvgRateConfidence = boq.AverageRateConfidence;
            b.RateFillPct = b.TotalItems > 0
                ? 100.0 * (b.TotalItems - b.ItemsMissingRate) / b.TotalItems : 0;

            double score = 0.5 * b.ParagraphCoveragePct
                         + 0.3 * b.AvgRateConfidence
                         + 0.2 * b.RateFillPct;
            b.Score = Math.Round(Math.Max(0, Math.Min(100, score)), 1);
            if      (b.Score >= 85) { b.Grade = "Excellent"; b.Rag = "Green"; }
            else if (b.Score >= 65) { b.Grade = "Good";      b.Rag = "Green"; }
            else if (b.Score >= 45) { b.Grade = "Fair";      b.Rag = "Amber"; }
            else                    { b.Grade = "Poor";      b.Rag = "Red";   }
            return b;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Item 8 — Synthetic warnings for BOQ data gaps
        //  Feeds into WarningsManager.ScanWarnings so items with missing
        //  rates or unresolved paragraph tokens surface as diagnostic
        //  warnings alongside the Revit-native ones. Callers get a list
        //  of (description, severity, category, elementIds) tuples.
        // ══════════════════════════════════════════════════════════════════

        public class BOQGapWarning
        {
            public string Description;
            public string Severity;   // "MEDIUM" / "LOW"
            public string Category;
            public long   ElementId;
        }

        public static List<BOQGapWarning> EmitBOQGapWarnings(Document doc)
        {
            var list = new List<BOQGapWarning>();
            var boq = GetBoq(doc);
            if (boq == null) return list;

            int missingRate = 0, missingPara = 0, tokenPara = 0;
            var tokenRx = new System.Text.RegularExpressions.Regex(@"\[[A-Za-z0-9_]+\]",
                System.Text.RegularExpressions.RegexOptions.Compiled);

            foreach (var item in boq.AllItems)
            {
                if (item.Source != BOQRowSource.Model) continue;
                if (item.RateUGX <= 0)
                {
                    missingRate++;
                    list.Add(new BOQGapWarning
                    {
                        Description = $"BOQ rate missing for {item.Category ?? "element"} — item will price as zero",
                        Severity = "MEDIUM",
                        Category = "Data Quality",
                        ElementId = item.RevitElementId
                    });
                }
                if (string.IsNullOrEmpty(item.ResolvedNRM2Paragraph))
                {
                    missingPara++;
                    list.Add(new BOQGapWarning
                    {
                        Description = $"BOQ description missing for {item.Category ?? "element"} — fallback will be generated on export",
                        Severity = "LOW",
                        Category = "Data Quality",
                        ElementId = item.RevitElementId
                    });
                }
                else if (tokenRx.IsMatch(item.ResolvedNRM2Paragraph))
                {
                    tokenPara++;
                    list.Add(new BOQGapWarning
                    {
                        Description = $"BOQ description contains unresolved [token] for {item.Category ?? "element"} — re-run BOQ refresh to resolve",
                        Severity = "MEDIUM",
                        Category = "Data Quality",
                        ElementId = item.RevitElementId
                    });
                }
            }

            if (list.Count > 0)
                StingLog.Info($"BOQ gap warnings: {missingRate} missing rates, {missingPara} missing paragraphs, {tokenPara} with unresolved tokens.");
            return list;
        }

        // ══════════════════════════════════════════════════════════════════
        //  Item 9 — Deliverable tracker row
        //  Returns a DeliverableItem the Phase74 DeliverableTracker can
        //  append to its matrix. Completion status computed from whether
        //  a BOQ snapshot exists AND whether the Professional export was
        //  run for the current revision.
        // ══════════════════════════════════════════════════════════════════

        public static (string Name, string Milestone, string CommandTag, bool Complete, string Details)
            GetBOQDeliverableRow(Document doc, string milestone = "DD2")
        {
            try
            {
                var snaps = BOQCostManager.ListSnapshots(doc);
                bool hasSnap = snaps != null && snaps.Count > 0;

                string bimDir = GetBimManagerDir(doc);
                string lastExport = null;
                if (!string.IsNullOrEmpty(bimDir) && Directory.Exists(Path.GetDirectoryName(bimDir) ?? ""))
                {
                    var parent = Path.GetDirectoryName(bimDir);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        var xlsx = Directory.GetFiles(parent, "*STING_BOQ_Professional*.xlsx", SearchOption.TopDirectoryOnly)
                            .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                            .FirstOrDefault();
                        if (xlsx != null) lastExport = Path.GetFileName(xlsx);
                    }
                }

                bool complete = hasSnap && !string.IsNullOrEmpty(lastExport);
                string details;
                if (complete)
                    details = $"Snapshot {snaps[0].Date:d MMM yyyy} · Export {lastExport}";
                else if (hasSnap)
                    details = $"Snapshot {snaps[0].Date:d MMM yyyy} · NO export yet";
                else
                    details = "No snapshot or export on record";

                return ("Bill of Quantities (NRM2)", milestone, "BOQExportProfessional", complete, details);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"GetBOQDeliverableRow: {ex.Message}");
                return ("Bill of Quantities (NRM2)", milestone, "BOQExportProfessional", false, "(status unavailable)");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Item 10 — Route export to CDE folder per pricing mode
        //  After BOQProfessionalExportCommand saves the xlsx, copy it into
        //  the right CDE folder so the document register stays accurate:
        //    TenderIssue / PricedCopy  → SHARED
        //    ContractCopy              → PUBLISHED
        //    AsBuilt                   → ARCHIVE
        //  Never MOVES — always COPIES so the original stays next to the
        //  source Revit file and the user can find it without opening the
        //  CDE folder tree.
        // ══════════════════════════════════════════════════════════════════

        public static string RouteExportToCDE(Document doc, string xlsxPath, BOQPricingMode mode)
        {
            if (doc == null || string.IsNullOrEmpty(xlsxPath) || !File.Exists(xlsxPath)) return null;
            string cdeFolder;
            switch (mode)
            {
                case BOQPricingMode.ContractCopy: cdeFolder = "PUBLISHED"; break;
                case BOQPricingMode.AsBuilt:      cdeFolder = "ARCHIVE";   break;
                default:                          cdeFolder = "SHARED";    break;
            }
            try
            {
                string bimDir = GetBimManagerDir(doc);
                if (string.IsNullOrEmpty(bimDir)) return null;
                string cdeBase = Path.Combine(Path.GetDirectoryName(bimDir) ?? "", "_CDE", cdeFolder, "BOQ");
                Directory.CreateDirectory(cdeBase);
                string target = Path.Combine(cdeBase, Path.GetFileName(xlsxPath));
                File.Copy(xlsxPath, target, overwrite: true);
                StingLog.Info($"BOQ export routed to CDE {cdeFolder}: {target}");
                return target;
            }
            catch (Exception ex) { StingLog.Warn($"RouteExportToCDE: {ex.Message}"); return null; }
        }

        // ══════════════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════════════

        private static string GetBimManagerDir(Document doc)
        {
            try
            {
                return StingTools.BIMManager.BIMManagerEngine.GetBIMManagerDir(doc);
            }
            catch (Exception ex) { StingLog.Warn($"GetBimManagerDir: {ex.Message}"); return null; }
        }

        private static string GetBimManagerFile(Document doc, string fileName)
        {
            string dir = GetBimManagerDir(doc);
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, fileName);
        }

        private static void AtomicJsonWrite(string path, JToken content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content.ToString(Formatting.Indented));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }
    }
    // ══════════════════════════════════════════════════════════════════════
    //  BOQBccRefreshCommand — Phase 108k on-demand trigger
    //
    //  Runs the Items 2, 4, 7, 8 pass in a single click:
    //    · Issue cost impact (writes cost_impact_ugx on every open issue)
    //    · Clash remediation cost (writes remediation_cost_ugx on recent clashes)
    //    · BOQ data-quality band (reported in result panel)
    //    · Synthetic BOQ-gap warnings (reported in result panel)
    //
    //  Items 3 (revision), 6 (meeting), 9 (deliverable), 10 (CDE routing)
    //  fire automatically from their host commands — no direct trigger
    //  needed.
    //  Item 5 (cash flow) is a passive lookup for Scheduling4DEngine and
    //  doesn't need a refresh trigger either.
    // ══════════════════════════════════════════════════════════════════════
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    [Autodesk.Revit.Attributes.Regeneration(Autodesk.Revit.Attributes.RegenerationOption.Manual)]
    public class BOQBccRefreshCommand : Autodesk.Revit.UI.IExternalCommand
    {
        public Autodesk.Revit.UI.Result Execute(
            Autodesk.Revit.UI.ExternalCommandData commandData,
            ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) return Autodesk.Revit.UI.Result.Failed;
                var doc = ctx.Doc;

                // Ensure the shared cache picks up the latest model state
                BOQBccBridge.InvalidateCache();

                int issuesTouched = BOQBccBridge.ComputeIssueCostImpact(doc);
                int clashReports  = BOQBccBridge.ComputeClashCosts(doc);
                var health        = BOQBccBridge.ComputeBOQHealthBand(doc);
                var gaps          = BOQBccBridge.EmitBOQGapWarnings(doc);

                int gapRate = gaps.Count(g => g.Description.StartsWith("BOQ rate missing"));
                int gapPara = gaps.Count(g => g.Description.StartsWith("BOQ description missing"));
                int gapTok  = gaps.Count(g => g.Description.StartsWith("BOQ description contains"));

                StingTools.UI.StingResultPanel.Create("BOQ × BCC refresh complete")
                    .SetSubtitle("Items 2, 4, 7, 8 refreshed on demand; 3, 6, 9, 10 fire from their host commands.")
                    .AddSection("ISSUE COST IMPACT (Item 2)")
                    .Metric("Open issues updated", issuesTouched.ToString())
                    .AddSection("CLASH REMEDIATION COST (Item 4)")
                    .Metric("Clash reports annotated", clashReports.ToString())
                    .AddSection("BOQ DATA QUALITY (Item 7)")
                    .Metric("Score",                 $"{health.Score:F1} / 100 ({health.Grade})")
                    .Metric("Paragraph coverage",    $"{health.ParagraphCoveragePct:F0}%")
                    .Metric("Avg rate confidence",   $"{health.AvgRateConfidence:F0}")
                    .Metric("Rate-fill completeness", $"{health.RateFillPct:F0}%")
                    .Metric("Items missing rate",    health.ItemsMissingRate.ToString())
                    .Metric("Items missing paragraph", health.ItemsMissingParagraph.ToString())
                    .AddSection("BOQ-GAP WARNINGS (Item 8)")
                    .Metric("Missing rates",                 gapRate.ToString())
                    .Metric("Missing descriptions",          gapPara.ToString())
                    .Metric("Unresolved [tokens] in paragraphs", gapTok.ToString())
                    .Metric("Total synthetic warnings",      gaps.Count.ToString())
                    .Show();

                StingLog.Info($"BOQ×BCC refresh: issues={issuesTouched} clashes={clashReports} health={health.Score:F0} gaps={gaps.Count}");
                return Autodesk.Revit.UI.Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("BOQBccRefreshCommand", ex);
                message = ex.Message;
                return Autodesk.Revit.UI.Result.Failed;
            }
        }
    }
}
