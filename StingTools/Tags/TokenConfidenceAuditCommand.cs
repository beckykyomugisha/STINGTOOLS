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
    //   ASS_ZONE_SOURCE_TXT      TYPE_OVERRIDE / Room / Default
    //   ASS_SYS_DETECT_LAYER_INT 1–7  (1–5 genuine detection, 6 category
    //                            fallback, 7 discipline default)
    //
    // This command is purely the reporting layer on top: it classifies LOC /
    // ZONE / SYS into High / Medium / Low confidence bands and surfaces the
    // silent-default cases that a completeness % hides.
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class TokenConfidenceAuditCommand : IExternalCommand
    {
        private enum Band { High, Medium, Low }

        public Result Execute(ExternalCommandData cmd, ref string msg, ElementSet els)
        {
            var ctx = ParameterHelpers.GetContext(cmd);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var scope = TagSchemeCommandHelper.CollectScope(ctx.UIDoc, doc, out string scopeLabel);

            int tagged = 0;
            // Band totals across the three audited tokens
            int hi = 0, med = 0, low = 0;
            // The silent-wrong-building case: literal BLD1 default AND LOC_SOURCE=Default
            int silentBld1 = 0;
            var discFallback = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var catFallback = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var offenders = new List<long>();

            var rows = new List<string>
            {
                "ElementId,Category,Discipline,LOC,LOC_SOURCE,ZONE,ZONE_SOURCE,SYS,SYS_LAYER,Bands"
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

                Band locBand = ClassifyLoc(locSrc);
                Band zoneBand = ClassifyZone(zoneSrc);
                Band sysBand = ClassifySys(sysLayer);

                AddBand(locBand, ref hi, ref med, ref low);
                AddBand(zoneBand, ref hi, ref med, ref low);
                AddBand(sysBand, ref hi, ref med, ref low);

                bool anyLow = locBand == Band.Low || zoneBand == Band.Low || sysBand == Band.Low;

                // SYS discipline-default fallback (layer 7 / unset) attributed per discipline + category
                if (sysBand == Band.Low)
                {
                    Bump(discFallback, disc);
                    Bump(catFallback, cat);
                }

                // Silent-wrong-building: model says BLD1 but only because nothing detected it
                bool silent = string.Equals(loc, "BLD1", StringComparison.OrdinalIgnoreCase)
                              && (string.IsNullOrEmpty(locSrc) || string.Equals(locSrc, "Default", StringComparison.OrdinalIgnoreCase));
                if (silent) silentBld1++;

                if (anyLow)
                {
                    if (offenders.Count < 10) offenders.Add(el.Id.Value);
                    string bands = $"LOC={locBand};ZONE={zoneBand};SYS={sysBand}";
                    rows.Add(string.Join(",",
                        el.Id.Value,
                        Csv(cat),
                        Csv(disc),
                        Csv(loc), Csv(locSrc),
                        Csv(zone), Csv(zoneSrc),
                        Csv(sys), sysLayer,
                        Csv(bands)));
                }
            }

            string csvPath = null;
            if (rows.Count > 1)
            {
                try
                {
                    csvPath = OutputLocationHelper.GetOutputPath(doc, "STING_TokenConfidence_Audit.csv");
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
            report.AppendLine($"  Medium (ProjectInfo; SYS layer 6):                          {med}");
            report.AppendLine($"  Low / fallback (Default / unset; SYS layer 7):              {low}");
            if (totalBands > 0)
                report.AppendLine($"  Low-band share: {(100.0 * low / totalBands):F1}%");
            report.AppendLine();
            report.AppendLine($"⚠ Silent BLD1 defaults (LOC=BLD1 with no detection source): {silentBld1}");
            report.AppendLine("   These read as 'building 1' in completeness reports but were");
            report.AppendLine("   never confirmed by a room, workset, scope box or project info.");
            report.AppendLine();

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
                report.AppendLine($"CSV (one row per low-band element): {csvPath}");

            TaskDialog td = new TaskDialog("Token Confidence Audit")
            {
                MainInstruction = low == 0 && silentBld1 == 0
                    ? "All audited tokens are detection-backed"
                    : $"{low} low-confidence token-fills, {silentBld1} silent BLD1 default(s)",
                MainContent = report.ToString()
            };
            td.Show();
            StingLog.Info($"TokenConfidenceAudit: {tagged} tagged, hi={hi} med={med} low={low}, silentBLD1={silentBld1} ({scopeLabel})");
            return Result.Succeeded;
        }

        // High: Room / TYPE_OVERRIDE / Workset / ScopeBox.  Medium: ProjectInfo.  Low: Default / empty.
        private static Band ClassifyLoc(string src)
        {
            if (string.IsNullOrEmpty(src)) return Band.Low;
            switch (src.Trim().ToUpperInvariant())
            {
                case "ROOM":
                case "TYPE_OVERRIDE":
                case "WORKSET":
                case "SCOPEBOX":
                    return Band.High;
                case "PROJECTINFO":
                    return Band.Medium;
                default:
                    return Band.Low; // "Default" or unknown
            }
        }

        // ZONE provenance is High (Room / TYPE_OVERRIDE) or Low (Default / empty); no Medium tier.
        private static Band ClassifyZone(string src)
        {
            if (string.IsNullOrEmpty(src)) return Band.Low;
            switch (src.Trim().ToUpperInvariant())
            {
                case "ROOM":
                case "TYPE_OVERRIDE":
                    return Band.High;
                default:
                    return Band.Low;
            }
        }

        // SYS layers: 1–5 genuine detection (High), 6 category fallback (Medium), 7/unset discipline default (Low).
        private static Band ClassifySys(int layer)
        {
            if (layer >= 1 && layer <= 5) return Band.High;
            if (layer == 6) return Band.Medium;
            return Band.Low; // 7 or unset (0)
        }

        private static void AddBand(Band b, ref int hi, ref int med, ref int low)
        {
            if (b == Band.High) hi++;
            else if (b == Band.Medium) med++;
            else low++;
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
