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
//    Midp_Import        — MIDP CSV → bulk-import into deliverables.json (MidpImportCommand.cs)
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

                string csv = OutputLocationHelper.GetRoutedTimestampedPath(doc, "REGISTER", "STING_Risks", ".csv");
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

                string midpPath;
                if (PresetDialog.Quiet)
                {
                    // Inside a preset the CSV is the step's params.midpCsv — never guessed.
                    midpPath = PresetDialog.InputFile(doc, "Midp_DriftReport", "midpCsv", "the MIDP/TIDP CSV", ref message);
                    if (midpPath == null) return Result.Failed;
                }
                else
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Pick a MIDP/TIDP CSV (Code,Title,Discipline,Milestone,PlannedDate,RequiredSuitability)",
                        Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                    };
                    if (dlg.ShowDialog() != true) return Result.Cancelled;
                    midpPath = dlg.FileName;
                }

                var plan = ParseMidpInteractive(midpPath, out int skipped, out int relativeLeftOut);
                if (relativeLeftOut > 0) StingLog.Warn($"Midp_DriftReport: {relativeLeftOut} row(s) left out — relative month only");
                if (plan.Count == 0)
                {
                    PresetDialog.Show(StingResultPanel.Create("MIDP drift")
                        .AddSection("NO ROWS").Text("No deliverable rows parsed. Expected header columns: "
                            + "Code,Title,Discipline,Milestone,PlannedDate,RequiredSuitability."), ref message);
                    return Result.Cancelled;
                }

                // Best-effort join to the live deliverables.json lifecycle (issued + suitability).
                JoinLifecycle(doc, plan);

                var s = Core.Delivery.MidpEngine.Detect(plan, DateTime.Now, 14);

                string csv = OutputLocationHelper.GetRoutedTimestampedPath(doc, "Compliance", "STING_MIDP_Drift", ".csv");
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
                if (relativeLeftOut > 0) panel.Text($"{relativeLeftOut} row(s) left out: only a relative month (M0, M1 …), no Planned Date.");
                PresetDialog.Show(panel.Text($"CSV: {Path.GetFileName(csv)}"), ref message);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("Midp_DriftReport", ex);
                message = ex.Message; return Result.Failed;
            }
        }

        /// <summary>
        /// Parse a MIDP/TIDP CSV into plan rows, tolerating the column-name
        /// variants real spreadsheets arrive with.
        ///
        /// Internal, not private: <see cref="MidpImportCommand"/> bulk-writes the
        /// same rows into deliverables.json and must agree with this reader about
        /// which column is which. A second copy of the header matching is exactly
        /// how the two would drift — the same reasoning that made
        /// <see cref="ResolveDeliverablesPath"/> internal.
        /// </summary>
        internal static List<DeliverablePlanItem> ParseMidpCsv(string path, out int skipped)
            => ParseMidpCsv(path, out skipped, out _, null);

        /// <summary>
        /// Parse, and when rows carry only a relative month ("M3", as the KUT MIDP template
        /// ships), ask once which date is M0 — or leave those rows out. The count left out is
        /// returned so the caller REPORTS it: before, those rows vanished without a word.
        /// </summary>
        internal static List<DeliverablePlanItem> ParseMidpInteractive(string path, out int skipped, out int relativeLeftOut)
        {
            var plan = ParseMidpCsv(path, out skipped, out int rel, null);
            relativeLeftOut = rel;
            if (rel == 0) return plan;

            if (PresetDialog.Quiet)
            {
                // Nobody to ask which date is M0. The step's params.m0 (yyyy-MM-dd) dates those
                // rows; without it they stay left out and the caller reports the count — the
                // same outcome as "Leave those rows out", never a date chosen on someone's behalf.
                string raw = (WorkflowEngine.StepParam("m0") ?? "").Trim();
                if (raw.Length == 0) return plan;
                if (!DateTime.TryParseExact(raw, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var stepM0))
                {
                    StingLog.Warn($"Midp: params.m0 '{raw}' is not yyyy-MM-dd; {rel} relative-month row(s) left out.");
                    return plan;
                }
                relativeLeftOut = 0;
                return ParseMidpCsv(path, out skipped, out _, stepM0);
            }

            var td = new Autodesk.Revit.UI.TaskDialog("MIDP — relative months")
            {
                MainInstruction = $"{rel} row(s) have no Planned Date, only a relative month (M0, M1 …).",
                MainContent = "Which date is month M0 (mobilisation)? Rows are dated M0 + n months. " +
                              "Or leave them out and fill Planned Date in the MIDP.",
                AllowCancellation = true,
            };
            td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink1, $"M0 = today ({DateTime.Today:dd MMM yyyy})");
            td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink2, $"M0 = tomorrow ({DateTime.Today.AddDays(1):dd MMM yyyy})");
            td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink3, "Leave those rows out");
            var r = td.Show();
            DateTime? m0 = r == Autodesk.Revit.UI.TaskDialogResult.CommandLink1 ? DateTime.Today
                         : r == Autodesk.Revit.UI.TaskDialogResult.CommandLink2 ? DateTime.Today.AddDays(1)
                         : (DateTime?)null;
            if (m0 == null) return plan;
            relativeLeftOut = 0;
            return ParseMidpCsv(path, out skipped, out _, m0);
        }

        /// <summary>
        /// Headers are compared NORMALISED (case, spaces, underscores and punctuation
        /// ignored) and aliases are tried in PRIORITY order. Before, "Planned Date" (the KUT
        /// template's header, with a space) matched nothing, so every row was skipped, and
        /// "Deliverable" was taken as the code so the title was always blank.
        ///
        /// A row whose Planned Date is empty but whose "Planned Rel Month" says Mn is dated
        /// <paramref name="mobilisation"/> + n months when a mobilisation date is supplied;
        /// otherwise it is counted in <paramref name="relativeOnly"/> — reported, never
        /// dated by guess and never silently dropped.
        /// </summary>
        internal static List<DeliverablePlanItem> ParseMidpCsv(string path, out int skipped, out int relativeOnly, DateTime? mobilisation)
            => MidpCsv.Parse(File.ReadAllLines(path), out skipped, out relativeOnly, mobilisation);

        /// <summary>
        /// Resolve deliverables.json from the consolidated metadata root
        /// (&lt;root&gt;/_data/_BIM_COORD/), falling back to the legacy sibling-of-RVT
        /// location for projects not yet migrated. This is the same path
        /// <see cref="Planscape.Docs.Templates.DeliverableLifecycle"/> persists to.
        /// Internal (not private): WarningsManager.BuildCoordData reads the same file to
        /// populate the BCC Deliverables tab — reusing this resolver rather than growing a
        /// fourth copy of the path logic is exactly what DocumentIdentity.cs's own header
        /// comment warns against.
        /// </summary>
        internal static string ResolveDeliverablesPath(Document doc)
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
                string dir = global::StingTools.Core.StingPaths.ModelDir(doc);
                if (!string.IsNullOrEmpty(dir))
                {
                    string legacy = StingPaths.MetaFile(doc, "_BIM_COORD", "deliverables.json");
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
                    // A suitability with no issue record is NOT an issue: this used to mark
                    // the row issued TODAY, so a freshly imported plan read as delivered.
                    else if (d.PlanActualDate.HasValue) { d.Issued = true; d.ActualDate = d.PlanActualDate; }
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

        private static bool TryParseDate(string s, out DateTime dt) => MidpCsv.TryParseDate(s, out dt);
        private static List<string> SplitCsv(string line) => MidpCsv.SplitCsv(line);

        private static string Get(List<string> c, int i) => (i >= 0 && i < c.Count) ? c[i].Trim() : "";
        private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
    }
}
