using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    // ─────────────────────────────────────────────────────────────────────────
    // Phase 192 (A1) — Token Confidence Audit.
    //
    // STING auto-population is guaranteed-fill: it never leaves a token blank,
    // falling back to defaults (LOC→BLD1, ZONE→Z01, SYS layer 7 = discipline
    // default). On a six-building campus a default is indistinguishable from a
    // correct value in a plain completeness report, and the error only surfaces
    // at the Deliverable D record-model verification.
    //
    // The tagging pipeline already writes provenance on every element:
    //   ASS_LOC_SOURCE_TXT       TYPE_OVERRIDE / Room / ProjectInfo / Workset /
    //                            ScopeBox / Default
    //   ASS_ZONE_SOURCE_TXT      TYPE_OVERRIDE / Room / ScopeBox / Proximity /
    //                            Default
    //   ASS_SYS_DETECT_LAYER_INT 1–7  (1–5 genuine detection, 6 category
    //                            fallback, 7 discipline default)
    //
    // This command is purely the reporting layer on top: it classifies LOC /
    // ZONE / SYS into High / Medium / Low confidence bands and surfaces the
    // silent-default cases that a completeness % hides.
    //
    // TAGACC-18: the bands and their reasons come from Core/TokenConfidenceBands
    // (tested against the writer's vocabulary). Every Medium or Low fill is listed
    // in the CSV with the reason for each token.
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TokenConfidenceAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var scope = TagSchemeCommandHelper.CollectScope(ctx.UIDoc, doc, out string scopeLabel);

            int tagged = 0;
            // Band totals across the three audited tokens
            int hi = 0, med = 0, low = 0;
            // The silent-wrong-building case: LOC_SOURCE=Default, whatever value the
            // token policy's fallback wrote (it is not always BLD1).
            int silentLoc = 0;
            var reasonCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var discFallback = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var catFallback = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var offenders = new List<long>();

            var rows = new List<string>
            {
                "ElementId,Category,Discipline,LOC,LOC_SOURCE,LOC_BAND,LOC_REASON,ZONE,ZONE_SOURCE,ZONE_BAND,ZONE_REASON,SYS,SYS_LAYER,SYS_BAND,SYS_REASON"
            };

            foreach (var el in scope)
            {
                string tag1 = ParameterHelpers.GetString(el, ParamRegistry.TAG1);
                if (string.IsNullOrEmpty(tag1)) continue;
                tagged++;

                string disc = ParameterHelpers.GetString(el, ParamRegistry.DISC);
                if (string.IsNullOrEmpty(disc)) disc = "(none)";
                string cat = ParameterHelpers.GetCategoryName(el);

                string loc = ParameterHelpers.GetString(el, ParamRegistry.LOC);
                string locSrc = ParameterHelpers.GetString(el, ParamRegistry.LOC_SOURCE);
                string zone = ParameterHelpers.GetString(el, ParamRegistry.ZONE);
                string zoneSrc = ParameterHelpers.GetString(el, ParamRegistry.ZONE_SOURCE);
                string sys = ParameterHelpers.GetString(el, ParamRegistry.SYS);
                int sysLayer = ParameterHelpers.GetInt(el, ParamRegistry.SYS_DETECT_LAYER, 0);

                var locC = TokenConfidenceBands.ClassifyLoc(locSrc);
                var zoneC = TokenConfidenceBands.ClassifyZone(zoneSrc);
                var sysC = TokenConfidenceBands.ClassifySys(sysLayer);

                AddBand(locC.Band, ref hi, ref med, ref low);
                AddBand(zoneC.Band, ref hi, ref med, ref low);
                AddBand(sysC.Band, ref hi, ref med, ref low);
                CountReason(reasonCounts, "LOC", locC);
                CountReason(reasonCounts, "ZONE", zoneC);
                CountReason(reasonCounts, "SYS", sysC);

                bool anyLow = locC.Band == ConfidenceBand.Low || zoneC.Band == ConfidenceBand.Low || sysC.Band == ConfidenceBand.Low;
                bool anyFallback = anyLow || locC.Band == ConfidenceBand.Medium
                                   || zoneC.Band == ConfidenceBand.Medium || sysC.Band == ConfidenceBand.Medium;

                // SYS discipline-default fallback (layer 7 / unset) attributed per discipline + category
                if (sysC.Band == ConfidenceBand.Low)
                {
                    Bump(discFallback, disc);
                    Bump(catFallback, cat);
                }

                // Silent-wrong-building: the model names a building only because nothing detected one
                if (!string.IsNullOrEmpty(loc) && TokenConfidenceBands.IsLocDefault(locSrc)) silentLoc++;

                if (anyLow && offenders.Count < 10) offenders.Add(el.Id.Value);
                if (anyFallback)
                {
                    rows.Add(string.Join(",",
                        el.Id.Value,
                        Csv(cat),
                        Csv(disc),
                        Csv(loc), Csv(locSrc), locC.Band, Csv(locC.Reason),
                        Csv(zone), Csv(zoneSrc), zoneC.Band, Csv(zoneC.Reason),
                        Csv(sys), sysLayer, sysC.Band, Csv(sysC.Reason)));
                }
            }

            string csvPath = null;
            if (rows.Count > 1)
            {
                try
                {
                    csvPath = OutputLocationHelper.GetRoutedPath(doc, "Compliance", "STING_TokenConfidence_Audit.csv");
                    File.WriteAllLines(csvPath, rows, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"TokenConfidenceAudit CSV write: {ex.Message}");
                    csvPath = null;
                }
            }

            int totalBands = hi + med + low;
            var report = new StringBuilder();
            report.AppendLine($"Scope: {scopeLabel} — {tagged} tagged element(s)");
            report.AppendLine("Confidence is per token (LOC + ZONE + SYS audited):");
            report.AppendLine($"  High (Room / TYPE_OVERRIDE / Workset / ScopeBox; SYS 1–5):  {hi}");
            report.AppendLine($"  Medium (ProjectInfo / Proximity; SYS layer 6):              {med}");
            report.AppendLine($"  Low / fallback (Default / unset; SYS layer 7):              {low}");
            if (totalBands > 0)
                report.AppendLine($"  Low-band share: {(100.0 * low / totalBands):F1}%");
            report.AppendLine();
            report.AppendLine($"⚠ Silent LOC defaults (the policy fallback, nothing detected): {silentLoc}");
            report.AppendLine("   These read as a real building in completeness reports but were");
            report.AppendLine("   never confirmed by a room, workset, scope box or project info.");
            report.AppendLine();

            if (reasonCounts.Count > 0)
            {
                report.AppendLine("Why tokens were not detected (Medium + Low):");
                foreach (var kv in reasonCounts.OrderByDescending(k => k.Value))
                    report.AppendLine($"   {kv.Value,5}  {kv.Key}");
                report.AppendLine();
            }

            if (discFallback.Count > 0)
            {
                report.AppendLine("SYS discipline-default fallback, by discipline:");
                foreach (var kv in discFallback.OrderByDescending(k => k.Value))
                    report.AppendLine($"   {kv.Key,-12} {kv.Value}");
                report.AppendLine();
            }
            if (catFallback.Count > 0)
            {
                report.AppendLine("Worst categories (SYS fallback count, top 10):");
                foreach (var kv in catFallback.OrderByDescending(k => k.Value).Take(10))
                    report.AppendLine($"   {kv.Value,5}  {kv.Key}");
                report.AppendLine();
            }
            if (offenders.Count > 0)
            {
                report.AppendLine("First low-band ElementIds:");
                report.AppendLine("   " + string.Join(", ", offenders));
                report.AppendLine();
            }
            if (csvPath != null)
                report.AppendLine($"CSV (one row per element with a fallback, reason per token): {csvPath}");

            TaskDialog td = new TaskDialog("Token Confidence Audit")
            {
                MainInstruction = low == 0 && silentLoc == 0
                    ? "All audited tokens are detection-backed"
                    : $"{low} low-confidence token-fills, {silentLoc} silent LOC default(s)",
                MainContent = report.ToString()
            };
            // Inside a workflow preset: the report to the log, the headline to the step message.
            if (PresetDialog.Quiet)
            {
                StingLog.Info("Token Confidence Audit:" + Environment.NewLine + report);
                msg = $"Token confidence: {td.MainInstruction} ({tagged} tagged elements, {scopeLabel})"
                    + (csvPath != null ? $"; CSV {csvPath}" : "") + ".";
            }
            else td.Show();
            StingLog.Info($"TokenConfidenceAudit: {tagged} tagged, hi={hi} med={med} low={low}, silentLOC={silentLoc} ({scopeLabel})");
            return Result.Succeeded;
        }

        private static void AddBand(ConfidenceBand b, ref int hi, ref int med, ref int low)
        {
            if (b == ConfidenceBand.High) hi++;
            else if (b == ConfidenceBand.Medium) med++;
            else low++;
        }

        private static void CountReason(Dictionary<string, int> d, string token, TokenConfidence c)
        {
            if (c.Band == ConfidenceBand.High) return;
            Bump(d, $"{token}: {c.Reason}");
        }

        private static void Bump(Dictionary<string, int> d, string key)
        {
            if (string.IsNullOrEmpty(key)) key = "(none)";
            d.TryGetValue(key, out int c);
            d[key] = c + 1;
        }

        private static string Csv(string s)
        {
            s = s ?? "";
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
