// ══════════════════════════════════════════════════════════════════════════
//  DeliveryCommands.cs — Revit hooks for the PM-8 delivery layer.
//
//  Surfaces the pure Core.Delivery engines (RiskRegister / MidpEngine) and the
//  thin "raise a risk against this element/zone" hook that reuses the existing
//  sidecar + audit machinery.
//
//  Command tags:
//    Risk_Raise        — raise a risk (anchored to the current selection/zone)
//    Risk_Report        — register roll-up (RAG, top risks) + CSV
//    Midp_DriftReport   — MIDP CSV → drift detection vs the live lifecycle + CSV
//
//  Risks persist to <BIM manager>/risks.json (additive, safe-defaulted); each
//  raise also appends to the tamper-evident audit log.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StingTools.BIMManager;
using StingTools.Core;
using StingTools.Core.Delivery;
using StingTools.Select;
using StingTools.UI;

namespace StingTools.Commands.Delivery
{
    internal static class RiskStore
    {
        public static string PathFor(Document doc)
            => Path.Combine(BIMManagerEngine.GetBIMManagerDir(doc), "risks.json");

        public static List<RiskItem> Load(Document doc)
        {
            try
            {
                string p = PathFor(doc);
                if (!File.Exists(p)) return new List<RiskItem>();
                return JsonConvert.DeserializeObject<List<RiskItem>>(File.ReadAllText(p)) ?? new List<RiskItem>();
            }
            catch (Exception ex) { StingLog.Warn($"RiskStore.Load: {ex.Message}"); return new List<RiskItem>(); }
        }

        public static void Save(Document doc, List<RiskItem> risks)
        {
            string p = PathFor(doc);
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            File.WriteAllText(p, JsonConvert.SerializeObject(risks, Formatting.Indented));
        }
    }

    // ── Risk_Raise ────────────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RiskRaiseCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }
                var uidoc = ParameterHelpers.GetUIDoc(commandData);

                // Anchor to the first selected element (optional).
                long elemId = -1; string zone = ""; string anchorLabel = "project-level";
                var sel = uidoc?.Selection?.GetElementIds();
                if (sel != null && sel.Count > 0)
                {
                    var el = doc.GetElement(sel.First());
                    if (el != null)
                    {
                        elemId = el.Id.Value;
                        zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE) ?? "";
                        anchorLabel = $"{ParameterHelpers.GetCategoryName(el)} {elemId}";
                    }
                }

                string category = PickOne("STING — Risk category",
                    "What kind of risk?", new[] { "Design", "Cost", "Programme", "Health & Safety", "Quality", "Procurement", "Information" });
                if (category == null) return Result.Cancelled;
                int likelihood = PickScore("Likelihood (1 rare … 5 almost certain)");
                if (likelihood == 0) return Result.Cancelled;
                int impact = PickScore("Impact (1 negligible … 5 severe)");
                if (impact == 0) return Result.Cancelled;

                var risk = new RiskItem
                {
                    Id = $"R-{DateTime.Now:yyMMddHHmmss}",
                    Title = $"{category} risk on {anchorLabel}",
                    Category = category,
                    Likelihood = likelihood,
                    Impact = impact,
                    Status = RiskStatus.Open,
                    ElementId = elemId,
                    Zone = zone,
                    Owner = doc.Application?.Username ?? "",
                    RaisedDate = DateTime.Now.ToString("yyyy-MM-dd"),
                };

                var risks = RiskStore.Load(doc);
                risks.Add(risk);
                RiskStore.Save(doc, risks);

                // Reuse the tamper-evident audit log (same machinery as issues/SLA).
                try
                {
                    Planscape.Docs.Workflow.AuditLog.Append(doc, "risk.raised", risk.Id, new JObject
                    {
                        ["title"] = risk.Title,
                        ["category"] = risk.Category,
                        ["score"] = risk.InherentScore,
                        ["band"] = risk.InherentBand,
                        ["elementId"] = elemId,
                        ["zone"] = zone,
                    });
                }
                catch (Exception ex) { StingLog.Warn($"Risk audit: {ex.Message}"); }

                StingResultPanel.Create("Risk raised")
                    .AddSection("RISK")
                    .Metric("Id", risk.Id)
                    .Metric("Score (L×I)", $"{risk.InherentScore}")
                    .Metric("Band", risk.InherentBand)
                    .Metric("Anchor", anchorLabel)
                    .Text($"Saved to risks.json. Edit detail/mitigation there; run Risk_Report for the register.")
                    .Show();
                StingLog.Info($"Risk {risk.Id} raised: {risk.InherentScore} ({risk.InherentBand}).");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Risk_Raise", ex);
                message = ex.Message; return Result.Failed;
            }
        }

        private static string PickOne(string title, string msg, string[] options)
        {
            var items = options.Select(o => new StingListPicker.ListItem { Label = o, Tag = o }).ToList();
            var picked = StingListPicker.Show(title, msg, items, allowMultiSelect: false);
            return (picked != null && picked.Count > 0) ? picked[0].Tag as string : null;
        }

        private static int PickScore(string msg)
        {
            var items = Enumerable.Range(1, 5)
                .Select(n => new StingListPicker.ListItem { Label = n.ToString(), Tag = n }).ToList();
            var picked = StingListPicker.Show("STING — Score 1-5", msg, items, allowMultiSelect: false);
            return (picked != null && picked.Count > 0 && picked[0].Tag is int n) ? n : 0;
        }
    }

    // ── Risk_Report ───────────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class RiskReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var risks = RiskStore.Load(doc);
                if (risks.Count == 0)
                {
                    StingResultPanel.Create("Risk register")
                        .AddSection("EMPTY").Text("No risks yet. Select an element and run Risk_Raise.").Show();
                    return Result.Succeeded;
                }

                var s = RiskRegister.Summarise(risks, 10);

                string csv = OutputLocationHelper.GetTimestampedPath(doc, "STING_Risks", ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("Id,Title,Category,Status,Likelihood,Impact,InherentScore,InherentBand,ResidualScore,ResidualBand,ElementId,Zone,Owner,Mitigation");
                foreach (var r in risks.OrderByDescending(x => x.ResidualScore))
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Q(r.Id), Q(r.Title), Q(r.Category), Q(r.Status.ToString()),
                        r.Likelihood.ToString(), r.Impact.ToString(),
                        r.InherentScore.ToString(), Q(r.InherentBand),
                        r.ResidualScore.ToString(), Q(r.ResidualBand),
                        r.ElementId.ToString(), Q(r.Zone), Q(r.Owner), Q(r.Mitigation)
                    }));
                File.WriteAllText(csv, sb.ToString());

                var panel = StingResultPanel.Create("Risk register")
                    .SetSubtitle($"{s.Total} risk(s), {s.OpenCount} open")
                    .AddSection("RAG (residual)")
                    .Metric("Red", s.RedCount.ToString())
                    .Metric("Amber", s.AmberCount.ToString())
                    .Metric("Green", s.GreenCount.ToString())
                    .Metric("Open red", s.RedResidualCount.ToString())
                    .Metric("Avg residual (open)", s.AverageResidualScore.ToString("F1"))
                    .AddSection("TOP RISKS");
                foreach (var r in s.TopRisks.Take(8))
                    panel.Text($"[{r.ResidualBand}] {r.ResidualScore}  {r.Title}");
                panel.Text($"CSV: {Path.GetFileName(csv)}").Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Risk_Report", ex);
                message = ex.Message; return Result.Failed;
            }
        }

        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }

    // ── Midp_DriftReport ──────────────────────────────────────────────────────
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class MidpDriftReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                Document doc = ParameterHelpers.GetDoc(commandData);
                if (doc == null) { message = "No active document."; return Result.Failed; }

                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Pick a MIDP/TIDP CSV (Code,Title,Discipline,Milestone,PlannedDate,RequiredSuitability)",
                    Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                };
                if (dlg.ShowDialog() != true) return Result.Cancelled;

                var plan = ParseMidpCsv(dlg.FileName, out int skipped);
                if (plan.Count == 0)
                {
                    StingResultPanel.Create("MIDP drift")
                        .AddSection("NO ROWS").Text("No deliverable rows parsed. Expected header columns: "
                            + "Code,Title,Discipline,Milestone,PlannedDate,RequiredSuitability.").Show();
                    return Result.Cancelled;
                }

                // Best-effort join to the live deliverables.json lifecycle (issued + suitability).
                JoinLifecycle(doc, plan);

                var s = Core.Delivery.MidpEngine.Detect(plan, DateTime.Now, 14);

                string csv = OutputLocationHelper.GetTimestampedPath(doc, "STING_MIDP_Drift", ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("Code,Title,PlannedDate,State,DaysLateOrToGo,RequiredSuitability,ActualSuitability");
                foreach (var d in s.Drifts)
                    sb.AppendLine(string.Join(",", new[]
                    {
                        Q(d.Code), Q(d.Title), d.PlannedDate.ToString("yyyy-MM-dd"),
                        d.State.ToString(), d.DaysLateOrToGo.ToString(),
                        Q(d.RequiredSuitability), Q(d.ActualSuitability)
                    }));
                File.WriteAllText(csv, sb.ToString());

                var panel = StingResultPanel.Create("MIDP / TIDP drift")
                    .SetSubtitle($"{s.Total} deliverable(s) · {s.OnProgrammePct:F0}% on programme")
                    .AddSection("STATUS")
                    .Metric("On track", s.OnTrack.ToString())
                    .Metric("Not due", s.NotDue.ToString())
                    .Metric("At risk", s.AtRisk.ToString())
                    .Metric("Overdue", s.Overdue.ToString())
                    .Metric("Suitability short", s.SuitShort.ToString())
                    .AddSection("OFF-PROGRAMME");
                foreach (var d in s.Drifts.Where(x => x.State == DeliveryDriftState.Overdue
                                                   || x.State == DeliveryDriftState.AtRisk
                                                   || x.State == DeliveryDriftState.SuitShort).Take(12))
                    panel.Text($"[{d.State}] {d.Code} {d.Title} (planned {d.PlannedDate:yyyy-MM-dd})");
                if (skipped > 0) panel.Text($"{skipped} row(s) skipped (unparseable date).");
                panel.Text($"CSV: {Path.GetFileName(csv)}").Show();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Midp_DriftReport", ex);
                message = ex.Message; return Result.Failed;
            }
        }

        private static List<DeliverablePlanItem> ParseMidpCsv(string path, out int skipped)
        {
            skipped = 0;
            var rows = new List<DeliverablePlanItem>();
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return rows;
            var header = SplitCsv(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
            int Ix(params string[] names) => header.FindIndex(h => names.Contains(h));
            int iCode = Ix("code", "deliverable", "ref");
            int iTitle = Ix("title", "name", "description");
            int iDisc = Ix("discipline", "disc");
            int iMile = Ix("milestone", "stage", "datadrop");
            int iDate = Ix("planneddate", "planned", "date", "duedate");
            int iSuit = Ix("requiredsuitability", "suitability", "status", "scode");

            for (int r = 1; r < lines.Length; r++)
            {
                if (string.IsNullOrWhiteSpace(lines[r])) continue;
                var c = SplitCsv(lines[r]);
                string code = Get(c, iCode);
                if (string.IsNullOrWhiteSpace(code)) { continue; }
                if (!TryParseDate(Get(c, iDate), out var pd)) { skipped++; continue; }
                rows.Add(new DeliverablePlanItem
                {
                    Code = code,
                    Title = Get(c, iTitle),
                    Discipline = Get(c, iDisc),
                    Milestone = Get(c, iMile),
                    PlannedDate = pd,
                    RequiredSuitability = string.IsNullOrWhiteSpace(Get(c, iSuit)) ? "S2" : Get(c, iSuit).Trim(),
                });
            }
            return rows;
        }

        /// <summary>
        /// Resolve deliverables.json from the consolidated metadata root
        /// (&lt;root&gt;/_data/_BIM_COORD/), falling back to the legacy sibling-of-RVT
        /// location for projects not yet migrated. This is the same path
        /// <see cref="Planscape.Docs.Templates.DeliverableLifecycle"/> persists to.
        /// </summary>
        private static string ResolveDeliverablesPath(Document doc)
        {
            try
            {
                string meta = ProjectFolderEngine.GetMetaPath(doc, "_BIM_COORD");
                if (!string.IsNullOrEmpty(meta))
                {
                    string p = Path.Combine(meta, "deliverables.json");
                    if (File.Exists(p)) return p;
                }
            }
            catch (Exception ex) { StingLog.Warn($"MIDP deliverables path: {ex.Message}"); }

            try
            {
                string dir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (!string.IsNullOrEmpty(dir))
                {
                    string legacy = Path.Combine(dir, "_BIM_COORD", "deliverables.json");
                    if (File.Exists(legacy)) return legacy;
                }
            }
            catch (Exception ex) { StingLog.Warn($"MIDP legacy deliverables path: {ex.Message}"); }

            return null;
        }

        // Join issued/actual-suitability from the deliverables store written by
        // DeliverableLifecycle.Persist. That store serialises DeliverableRow via
        // JObject.FromObject, so keys are PascalCase (DocNumber / Suitability /
        // Status); the lowercase spellings are kept as tolerant fallbacks for
        // hand-authored or legacy files. Absent file ⇒ all planned-only.
        private static void JoinLifecycle(Document doc, List<DeliverablePlanItem> plan)
        {
            try
            {
                string p = ResolveDeliverablesPath(doc);
                if (string.IsNullOrEmpty(p)) return;
                var arr = JArray.Parse(File.ReadAllText(p));
                var byCode = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
                foreach (var o in arr.OfType<JObject>())
                {
                    string code = (string)(o["DocNumber"] ?? o["Code"]
                                        ?? o["doc_number"] ?? o["code"] ?? o["number"]);
                    if (!string.IsNullOrWhiteSpace(code)) byCode[code] = o;
                }
                foreach (var d in plan)
                {
                    if (!byCode.TryGetValue(d.Code, out var o)) continue;
                    string suit = (string)(o["Suitability"] ?? o["suitability"] ?? o["status"]) ?? "";
                    string issuedDate = (string)(o["IssuedDate"] ?? o["issued_date"]
                                              ?? o["issuedDate"] ?? o["last_issued"]) ?? "";
                    d.ActualSuitability = suit;
                    if (TryParseDate(issuedDate, out var idt)) { d.Issued = true; d.ActualDate = idt; }
                    else if (TryParseDate(LatestRevisionTimestamp(o), out var rdt)) { d.Issued = true; d.ActualDate = rdt; }
                    else if (!string.IsNullOrWhiteSpace(suit)) { d.Issued = true; d.ActualDate = DateTime.Now; }
                }
            }
            catch (Exception ex) { StingLog.Warn($"MIDP lifecycle join: {ex.Message}"); }
        }

        /// <summary>
        /// Newest RevisionHistory timestamp on a deliverable row, used as the issued
        /// date when the row carries no explicit one (DeliverableLifecycle records the
        /// issue moment in RevisionHistory, not in a dedicated field).
        /// </summary>
        private static string LatestRevisionTimestamp(JObject row)
        {
            try
            {
                var hist = row["RevisionHistory"] as JArray ?? row["revision_history"] as JArray;
                if (hist == null) return null;
                string best = null;
                DateTime bestDt = DateTime.MinValue;
                foreach (var h in hist.OfType<JObject>())
                {
                    string ts = (string)(h["Timestamp"] ?? h["timestamp"]);
                    if (TryParseDate(ts, out var dt) && dt > bestDt) { bestDt = dt; best = ts; }
                }
                return best;
            }
            catch (Exception ex) { StingLog.Warn($"MIDP revision timestamp: {ex.Message}"); return null; }
        }

        private static bool TryParseDate(string s, out DateTime dt)
        {
            dt = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)
                || DateTime.TryParseExact(s.Trim(), new[] { "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "dd-MMM-yy" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
        }

        private static List<string> SplitCsv(string line)
        {
            var outp = new List<string>(); var sb = new StringBuilder(); bool q = false;
            foreach (char ch in line)
            {
                if (ch == '"') q = !q;
                else if (ch == ',' && !q) { outp.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
            outp.Add(sb.ToString());
            return outp;
        }

        private static string Get(List<string> c, int i) => (i >= 0 && i < c.Count) ? c[i].Trim() : "";
        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
