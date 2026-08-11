// ============================================================================
// RepairPollutedParamsCommand.cs — clear values that a fixed writer will not fix.
//
// Two writers shipped wrong and have been corrected:
//
//   1. Two formulas in FORMULAS_WITH_DEPENDENCIES.csv overwrote ASS_FUNC_TXT and
//      ASS_SEQ_NUM_TXT with a category-description string and a whole assembled
//      tag. Both rows are deleted and the formula loader now refuses any formula
//      targeting a token.
//   2. NativeParamMapper.MapBuiltIn wrote Revit INTERNAL units into SI-named
//      parameters (ASS_FLOW_RATE_TXT = 8.29895 for 235 L/s). It now converts.
//
// Neither fix heals an element that was already written, and Tag & Combine will
// not either: outside Overwrite collision mode the token write is SetIfEmpty and
// the mapper's write is SetIfEmpty, so a polluted-but-NON-EMPTY value is
// preserved, not replaced. Fixing a writer does not fix its prior output.
//
// This clears those values so the next Tag & Combine can derive them again.
//
// IT IS DESTRUCTIVE, SO IT IS SEPARATE. Folding a clear into Tag & Combine would
// mean an operator running a routine re-tag silently loses hand-entered values.
// It scans and REPORTS first; it changes nothing until the operator confirms,
// and the report names what it would touch.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Tags
{
    /// <summary>Clears token values corrupted by the retired formulas and numeric values stored in internal units.</summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class RepairPollutedParamsCommand : IExternalCommand
    {
        /// <summary>
        /// Derived numeric-text parameters written by NativeParamMapper through the
        /// un-converted MapBuiltIn path. Every one is re-derived from a Revit built-in
        /// on the next tagging pass, so clearing loses nothing an operator authored.
        /// </summary>
        private static readonly string[] UnitSuspectParams =
        {
            "ASS_FLOW_RATE_TXT", "ASS_POWER_RATING_TXT",
            "HVC_AIRFLOW_LPS", "HVC_PRESSURE_DROP_PA",
            "PLM_FLOW_RATE_LPS", "PLM_PIPE_FLOW_LPS", "PLM_VEL_MPS", "PLM_PPE_SZ_MM",
        };

        private sealed class Hit
        {
            public ElementId Id;
            public string Param;
            public string Value;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var ctx = ParameterHelpers.GetContext(commandData);
                if (ctx?.Doc == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
                Document doc = ctx.Doc;

                string sep = !string.IsNullOrEmpty(ParamRegistry.Separator) ? ParamRegistry.Separator : "-";
                string[] tokenParams = TokenParams();

                var tokenHits = new List<Hit>();
                var unitHits = new List<Hit>();
                var byParam = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var touchedElements = new HashSet<long>();

                // ── Scan ──────────────────────────────────────────────────────
                var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
                var catEnums = SharedParamGuids.AllCategoryEnums;
                if (catEnums != null && catEnums.Length > 0)
                    collector.WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory>(catEnums)));

                int scanned = 0;
                foreach (Element el in collector)
                {
                    if (el == null) continue;
                    scanned++;

                    foreach (string pn in tokenParams)
                    {
                        string v = ParameterHelpers.GetString(el, pn);
                        if (string.IsNullOrEmpty(v)) continue;
                        if (!IsPollutedToken(v, sep)) continue;
                        tokenHits.Add(new Hit { Id = el.Id, Param = pn, Value = v });
                        Bump(byParam, pn);
                        touchedElements.Add(el.Id.Value);
                    }

                    foreach (string pn in UnitSuspectParams)
                    {
                        string v = ParameterHelpers.GetString(el, pn);
                        if (string.IsNullOrEmpty(v)) continue;
                        unitHits.Add(new Hit { Id = el.Id, Param = pn, Value = v });
                        Bump(byParam, pn);
                        touchedElements.Add(el.Id.Value);
                    }
                }

                if (tokenHits.Count == 0 && unitHits.Count == 0)
                {
                    TaskDialog.Show("Repair Polluted Parameters",
                        $"Scanned {scanned:N0} elements. Nothing to repair.\n\n"
                      + "No token carries a separator, inner whitespace, or an over-long value, "
                      + "and no derived numeric parameter holds a stored value.");
                    return Result.Succeeded;
                }

                // ── Report BEFORE changing anything ───────────────────────────
                var report = new StringBuilder();
                report.AppendLine($"  Elements scanned:        {scanned:N0}");
                report.AppendLine($"  Elements that would change: {touchedElements.Count:N0}");
                report.AppendLine($"  Polluted token values:   {tokenHits.Count:N0}");
                report.AppendLine($"  Derived numeric values:  {unitHits.Count:N0}");
                report.AppendLine();
                report.AppendLine("  Per parameter:");
                foreach (var kv in byParam)
                    report.AppendLine($"    {kv.Key,-28} {kv.Value,7:N0}");

                if (tokenHits.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("  Polluted tokens (first 10):");
                    foreach (var h in tokenHits.Take(10))
                        report.AppendLine($"    {h.Id.Value}  {h.Param} = '{Trim(h.Value)}'");
                    if (tokenHits.Count > 10) report.AppendLine($"    …(+{tokenHits.Count - 10:N0} more)");
                }
                if (unitHits.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("  Derived numerics (first 10) — cleared so the corrected");
                    report.AppendLine("  unit conversion can rewrite them:");
                    foreach (var h in unitHits.Take(10))
                        report.AppendLine($"    {h.Id.Value}  {h.Param} = '{Trim(h.Value)}'");
                    if (unitHits.Count > 10) report.AppendLine($"    …(+{unitHits.Count - 10:N0} more)");
                }
                report.AppendLine();
                report.AppendLine("  Clearing does NOT re-tag. Run Tag & Combine afterwards to");
                report.AppendLine("  re-derive every cleared value.");

                var confirm = new TaskDialog("Repair Polluted Parameters")
                {
                    MainInstruction = $"Clear {tokenHits.Count + unitHits.Count:N0} value(s) on {touchedElements.Count:N0} element(s)?",
                    MainContent = report.ToString(),
                };
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Clear them",
                    "Blanks the values listed above so the next Tag & Combine re-derives them");
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Report only",
                    "Change nothing");
                confirm.CommonButtons = TaskDialogCommonButtons.Cancel;

                if (confirm.Show() != TaskDialogResult.CommandLink1)
                {
                    StingLog.Info($"RepairPollutedParams: report-only — {tokenHits.Count} token + "
                                + $"{unitHits.Count} numeric value(s) on {touchedElements.Count} element(s); nothing changed.");
                    return Result.Cancelled;
                }

                // ── Clear ─────────────────────────────────────────────────────
                int cleared = 0, failed = 0;
                using (var tx = new Transaction(doc, "STING Repair Polluted Parameters"))
                {
                    tx.Start();
                    foreach (var h in tokenHits.Concat(unitHits))
                    {
                        Element el = doc.GetElement(h.Id);
                        if (el == null) { failed++; continue; }
                        if (ParameterHelpers.SetString(el, h.Param, "", overwrite: true)) cleared++;
                        else failed++;
                    }

                    // The container hash short-circuits the ~53 container writes when the
                    // token set is unchanged. A cleared token IS a change, but the hash
                    // still holds the pre-clear value, so drop it or the next pass would
                    // skip the very rewrite this repair exists to enable.
                    foreach (long idVal in touchedElements)
                        ParamRegistry.InvalidateTokenHash(doc.GetElement(new ElementId(idVal)));
                    tx.Commit();
                }

                ComplianceScan.InvalidateCache();
                StingAutoTagger.InvalidateContext();

                StingLog.Info($"RepairPollutedParams: cleared={cleared}, failed={failed}, "
                            + $"elements={touchedElements.Count}, scanned={scanned}");

                TaskDialog.Show("Repair Polluted Parameters",
                    $"Cleared {cleared:N0} value(s) on {touchedElements.Count:N0} element(s)."
                  + (failed > 0 ? $"\n{failed:N0} write(s) failed — see the log." : "")
                  + "\n\nRun Tag & Combine now to re-derive them.");
                return Result.Succeeded;
            }
            catch (OperationCanceledException) { return Result.Cancelled; }
            catch (Exception ex)
            {
                StingLog.Error("RepairPollutedParamsCommand crashed", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// The same three conditions ParamRegistry's sanitiser rejects on. Matching it
        /// deliberately: anything the sanitiser would blank at read time is a value the
        /// element should not be storing.
        /// </summary>
        private static bool IsPollutedToken(string v, string sep)
        {
            if (v.IndexOf(sep, StringComparison.Ordinal) >= 0) return true;
            if (v.Trim().Length > 40) return true;
            foreach (char c in v.Trim())
                if (char.IsWhiteSpace(c)) return true;
            return false;
        }

        private static string[] TokenParams()
        {
            var set = new List<string>(ParamRegistry.AllTokenParams ?? Array.Empty<string>());
            if (!string.IsNullOrEmpty(ParamRegistry.STATUS) && !set.Contains(ParamRegistry.STATUS)) set.Add(ParamRegistry.STATUS);
            if (!string.IsNullOrEmpty(ParamRegistry.REV) && !set.Contains(ParamRegistry.REV)) set.Add(ParamRegistry.REV);
            return set.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
        }

        private static void Bump(SortedDictionary<string, int> d, string k)
        {
            d.TryGetValue(k, out int n);
            d[k] = n + 1;
        }

        private static string Trim(string s)
            => s != null && s.Length > 46 ? s.Substring(0, 46) + "…" : s;
    }
}
