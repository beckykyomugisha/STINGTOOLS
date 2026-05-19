// StingTools Phase 183 — manual stale-size scan.
//
// Closes gap D8 (the lightweight version): instead of an IUpdater that
// runs on every modification and adds overhead, this is a user-invoked
// command that walks all ducts in scope, computes what
// MepAutoSizeDuctCommand WOULD size them to given current flow, and
// flags any whose actual size diverges by more than a configurable
// tolerance.
//
// Output:
//   - HVC_SIZE_STALE_BOOL = 1 stamped on each stale duct.
//   - StingResultPanel report with stale count + breakdown by role.
//   - Rows appended to the HVAC panel IssueGrid + a WorkflowGrid entry.
//
// Tolerance default: 20% area difference. Configurable via
// project setting HVC_STALE_TOLERANCE_PCT in the future.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Commands.Mep;   // MepSizeTables (Phase 184)
using StingTools.Core;
using StingTools.Core.Mep;
using StingTools.UI;

namespace StingTools.Commands.Hvac
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class HvacDetectStaleSizesCommand : IExternalCommand
    {
        private const double MmToFt = 1.0 / 304.8;
        private const double DefaultTolerancePct = 20.0;

        public Result Execute(ExternalCommandData commandData,
            ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx == null) { message = "No active document."; return Result.Failed; }
                var doc = ctx.Doc;

                MepSizingRules rules = MepSizingRegistry.Get(doc);
                double[] sizeTable = MepSizeTables.DuctSizesFor(doc);
                double defaultAspect = rules.DuctDefaultAspect > 0 ? rules.DuctDefaultAspect : 1.5;

                // Honour the same scope radio as the sizing commands.
                string scope = "Project";
                try { scope = StingHvacCommandHandler.CurrentScope ?? "Project"; } catch { }

                List<Element> ducts = CollectDucts(ctx, scope);
                if (ducts.Count == 0)
                {
                    TaskDialog.Show("STING HVAC — Stale sizes",
                        $"No ducts in scope ({scope}).");
                    return Result.Cancelled;
                }

                int stale = 0;
                int fresh = 0;
                int skipped = 0;
                var byRole = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var details = new List<string>();

                using (var tx = new Transaction(doc, "STING Flag stale duct sizes"))
                {
                    tx.Start();
                    foreach (var d in ducts)
                    {
                        try
                        {
                            double flowLs = ReadDouble(d, "HVC_FLOW_LS");
                            if (flowLs <= 0)
                            {
                                double flowCfm = ReadBuiltInFlowCfm(d);
                                flowLs = flowCfm * 0.4719;
                            }
                            if (flowLs <= 0) { skipped++; continue; }

                            string roleId = HvacSegmentRoleDetector.DetectRole(doc, d);
                            var role = rules.GetDuctRole(roleId);
                            double maxVelMs = role?.MaxVelocityMs ?? 6.0;
                            double maxAspect = role?.AspectMax > 0 ? role.AspectMax : 3.0;

                            double flowM3s = flowLs * 1e-3;
                            double targetArea = flowM3s / maxVelMs;
                            double targetWidth = Math.Sqrt(targetArea * defaultAspect) * 1000.0;
                            double targetHeight = targetWidth / defaultAspect;
                            if (targetWidth / targetHeight > maxAspect) targetHeight = targetWidth / maxAspect;
                            targetWidth  = MepSizeTables.RoundUpTo(targetWidth,  sizeTable);
                            targetHeight = MepSizeTables.RoundUpTo(targetHeight, sizeTable);

                            // Actual size
                            double w = ReadDouble(d, "Width")  * 304.8;
                            double h = ReadDouble(d, "Height") * 304.8;
                            double dia = ReadDouble(d, "Diameter") * 304.8;

                            double actualArea, targetAreaMm2;
                            if (w > 0 && h > 0)
                            {
                                actualArea = w * h;
                                targetAreaMm2 = targetWidth * targetHeight;
                            }
                            else if (dia > 0)
                            {
                                actualArea = Math.PI * dia * dia * 0.25;
                                double targetDiaMm = MepSizeTables.RoundUpTo(
                                    Math.Sqrt(4.0 * targetArea / Math.PI) * 1000.0, sizeTable);
                                targetAreaMm2 = Math.PI * targetDiaMm * targetDiaMm * 0.25;
                            }
                            else { skipped++; continue; }

                            double pctDiff = actualArea > 0
                                ? Math.Abs(actualArea - targetAreaMm2) / actualArea * 100.0
                                : 100.0;

                            bool isStale = pctDiff > DefaultTolerancePct;
                            try
                            {
                                // Overwrite=true so the flag can flip both ways
                                // (stale↔fresh) across successive scans.
                                ParameterHelpers.SetInt(d, ParamRegistry.HVC_SIZE_STALE_BOOL, isStale ? 1 : 0, overwrite: true);
                            }
                            catch (Exception exP) { StingLog.Warn($"Stale stamp {d.Id}: {exP.Message}"); }

                            if (isStale)
                            {
                                stale++;
                                if (!byRole.ContainsKey(roleId)) byRole[roleId] = 0;
                                byRole[roleId]++;
                                if (details.Count < 40)
                                {
                                    string actualSize = (w > 0 && h > 0)
                                        ? $"{w:F0}×{h:F0}" : $"Ø{dia:F0}";
                                    string targetSize = (w > 0 && h > 0)
                                        ? $"{targetWidth:F0}×{targetHeight:F0}"
                                        : "(round)";
                                    details.Add($"#{d.Id.Value} {roleId}: {actualSize} → {targetSize} (Δ{pctDiff:F0}%)");
                                }
                            }
                            else fresh++;
                        }
                        catch (Exception ex) { skipped++; StingLog.Warn($"Stale check {d.Id}: {ex.Message}"); }
                    }
                    tx.Commit();
                }

                // Build report
                var panel = StingResultPanel.Create("HVAC — Stale Sizes Scan");
                panel.SetSubtitle($"scope={scope} · tolerance={DefaultTolerancePct:F0}% area · {ducts.Count} ducts");
                panel.AddSection("SUMMARY")
                     .Metric("Stale",   stale.ToString())
                     .Metric("Fresh",   fresh.ToString())
                     .Metric("Skipped", skipped.ToString());

                if (byRole.Count > 0)
                {
                    panel.AddSection("BY ROLE");
                    foreach (var kv in byRole.OrderByDescending(k => k.Value))
                        panel.Metric(kv.Key, kv.Value.ToString());
                }
                if (details.Count > 0)
                {
                    panel.AddSection("DETAIL (first 40)");
                    foreach (var s in details) panel.Text(s);
                }
                panel.Show();

                // Surface in the HVAC panel.
                try
                {
                    StingHvacPanel.Instance?.PushRunRow(
                        $"Stale-size scan ({stale} stale of {ducts.Count})",
                        stale > 0 ? "⬡" : "⬤");
                    if (stale > 0 && StingHvacPanel.Instance != null)
                    {
                        StingHvacPanel.Instance.IssueRows.Add(new HvacIssueRow
                        {
                            Severity   = "⚠",
                            Element    = "(see report)",
                            Issue      = $"{stale} ducts drifted ≥ {DefaultTolerancePct:F0}% from target",
                            Suggestion = "Run Auto-size to re-size, or set HVC_SIZE_STALE_BOOL=0 to dismiss"
                        });
                    }
                }
                catch (Exception ex) { StingLog.Warn($"Panel push: {ex.Message}"); }

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("HvacDetectStaleSizesCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static List<Element> CollectDucts(StingCommandContext ctx, string scope)
        {
            var doc = ctx.Doc;
            if (scope == "Selection")
            {
                var ids = ctx.UIDoc?.Selection?.GetElementIds();
                if (ids == null) return new List<Element>();
                return ids.Select(id => doc.GetElement(id))
                    .Where(e => e != null && e.Category != null
                             && (BuiltInCategory)e.Category.Id.Value == BuiltInCategory.OST_DuctCurves)
                    .ToList();
            }
            if (scope == "ActiveView" && doc.ActiveView != null)
            {
                return new FilteredElementCollector(doc, doc.ActiveView.Id)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList();
            }
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_DuctCurves)
                .WhereElementIsNotElementType().ToList();
        }

        private static double ReadDouble(Element el, string param)
        {
            try
            {
                var p = el?.LookupParameter(param);
                if (p == null) return 0;
                if (p.StorageType == StorageType.Double) return p.AsDouble();
                if (p.StorageType == StorageType.Integer) return p.AsInteger();
                if (p.StorageType == StorageType.String &&
                    double.TryParse(p.AsString(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double v)) return v;
            }
            catch (Exception ex) { StingLog.Warn($"ReadDouble: {ex.Message}"); }
            return 0;
        }

        private static double ReadBuiltInFlowCfm(Element d)
        {
            try
            {
                var p = d?.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM);
                if (p != null && p.StorageType == StorageType.Double) return p.AsDouble();
            }
            catch { }
            return 0;
        }
    }
}
