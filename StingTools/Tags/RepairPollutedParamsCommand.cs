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
        /// Every unit-declaring parameter NativeParamMapper wrote through the
        /// un-converted MapBuiltIn path — the complete set, from the call sites in
        /// ParameterHelpers.MapMEPParams / MapElectricalParams, not the three that
        /// happened to be noticed. Each stored a Revit INTERNAL value under a name
        /// declaring something else: PLM_PPE_SZ_MM held feet, HVC_AIRFLOW_LPS held
        /// ft³/s. Every one is re-derived from a Revit built-in on the next tagging
        /// pass, so clearing loses nothing an operator authored.
        /// </summary>
        private static readonly string[] UnitSuspectParams =
        {
            // cross-writes
            "ASS_FLOW_RATE_TXT", "ASS_POWER_RATING_TXT",
            // HVAC
            "HVC_AIRFLOW_LPS", "HVC_DCT_FLW_CFM", "HVC_VEL_MPS", "HVC_PRESSURE_DROP_PA",
            "HVC_DCT_WIDTH_MM", "HVC_DCT_HEIGHT_MM",
            // Plumbing
            "PLM_PPE_FLW_LPS", "PLM_FLOW_RATE_LPS", "PLM_PPE_SZ_MM", "PLM_VEL_MPS",
            // Electrical
            "ELC_CKT_PWR_KW", "ELC_CKT_VLT_V", "ELC_VLT_PRIMARY_RATING_V",
            "ELC_PNL_CONNECTED_LOAD_KW",
        };

        private enum Fix { Extracted, Cleared }

        private sealed class Hit
        {
            public ElementId Id;
            public string Param;
            public string Value;
            public string Recovered;          // non-null → extraction succeeded
            public Fix Action => Recovered != null ? Fix.Extracted : Fix.Cleared;
            public string Why;                // why extraction was not possible
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

                    for (int slot = 0; slot < tokenParams.Length; slot++)
                    {
                        string pn = tokenParams[slot];
                        string v = ParameterHelpers.GetString(el, pn);
                        if (string.IsNullOrEmpty(v)) continue;
                        if (!IsPollutedToken(v, sep)) continue;

                        var hit = new Hit { Id = el.Id, Param = pn, Value = v };
                        hit.Recovered = TryExtractToken(el, v, slot, sep, out string why);
                        hit.Why = why;
                        tokenHits.Add(hit);
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
                int extracted = tokenHits.Count(h => h.Action == Fix.Extracted);
                int clearedTok = tokenHits.Count - extracted;

                var report = new StringBuilder();
                report.AppendLine($"  Elements scanned:        {scanned:N0}");
                report.AppendLine($"  Elements that would change: {touchedElements.Count:N0}");
                report.AppendLine($"  Polluted token values:   {tokenHits.Count:N0}");
                report.AppendLine($"      RECOVERED in place:  {extracted:N0}  (original value preserved — no renumbering)");
                report.AppendLine($"      cleared:             {clearedTok:N0}  (value not recoverable from the stored string)");
                report.AppendLine($"  Derived numeric values:  {unitHits.Count:N0}  (all cleared — re-derived from Revit)");
                report.AppendLine();
                report.AppendLine("  Per parameter:");
                foreach (var kv in byParam)
                    report.AppendLine($"    {kv.Key,-28} {kv.Value,7:N0}");

                if (tokenHits.Count > 0)
                {
                    report.AppendLine();
                    report.AppendLine("  Polluted tokens (first 10) — action per value:");
                    foreach (var h in tokenHits.Take(10))
                        report.AppendLine(h.Action == Fix.Extracted
                            ? $"    {h.Id.Value}  {h.Param}: '{Trim(h.Value)}'  →  RECOVER '{h.Recovered}'"
                            : $"    {h.Id.Value}  {h.Param}: '{Trim(h.Value)}'  →  CLEAR ({h.Why})");
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
                report.AppendLine("  A RECOVERED token keeps its original value — the asset is not");
                report.AppendLine("  renumbered, so issued drawings still match. A CLEARED token is");
                report.AppendLine("  re-derived on the next pass and MAY take a new sequence number.");
                report.AppendLine("  This does NOT re-tag. Run Tag & Combine afterwards.");

                var confirm = new TaskDialog("Repair Polluted Parameters")
                {
                    MainInstruction = $"Repair {tokenHits.Count + unitHits.Count:N0} value(s) on {touchedElements.Count:N0} element(s)? "
                                    + $"({extracted:N0} recovered, {clearedTok + unitHits.Count:N0} cleared)",
                    MainContent = report.ToString(),
                };
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Repair them",
                    $"Recovers {extracted:N0} token value(s) in place; clears the rest so the next Tag & Combine re-derives them");
                confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Report only",
                    "Change nothing");
                confirm.CommonButtons = TaskDialogCommonButtons.Cancel;

                if (confirm.Show() != TaskDialogResult.CommandLink1)
                {
                    StingLog.Info($"RepairPollutedParams: report-only — {tokenHits.Count} token + "
                                + $"{unitHits.Count} numeric value(s) on {touchedElements.Count} element(s); nothing changed.");
                    return Result.Cancelled;
                }

                // ── Repair: recover where the value survives, clear where it does not ──
                int recovered = 0, cleared = 0, failed = 0;
                using (var tx = new Transaction(doc, "STING Repair Polluted Parameters"))
                {
                    tx.Start();
                    foreach (var h in tokenHits.Concat(unitHits))
                    {
                        Element el = doc.GetElement(h.Id);
                        if (el == null) { failed++; continue; }
                        string write = h.Recovered ?? "";
                        if (!ParameterHelpers.SetString(el, h.Param, write, overwrite: true)) { failed++; continue; }
                        if (h.Recovered != null) recovered++; else cleared++;
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

                // Name every recovery in the log. A repair that silently rewrote a
                // sequence number is indistinguishable, later, from one that renumbered.
                foreach (var h in tokenHits.Where(x => x.Action == Fix.Extracted))
                    StingLog.Info($"RepairPollutedParams: {h.Id.Value} {h.Param} recovered '{h.Recovered}' from '{h.Value}'");

                StingLog.Info($"RepairPollutedParams: recovered={recovered}, cleared={cleared}, failed={failed}, "
                            + $"elements={touchedElements.Count}, scanned={scanned}");

                TaskDialog.Show("Repair Polluted Parameters",
                    $"Recovered {recovered:N0} value(s) in place, cleared {cleared:N0}, "
                  + $"across {touchedElements.Count:N0} element(s)."
                  + (failed > 0 ? $"\n{failed:N0} write(s) failed — see the log." : "")
                  + "\n\nEvery recovery is named in StingTools.log."
                  + "\n\nRun Tag & Combine now to rebuild the tags.");
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
        /// Recover a token's own value from a polluted string, or return null.
        ///
        /// The SEQ pollution is a whole assembled tag —
        /// 'A-BLD1-Z01-L01-LPS-LPS-WL-0038-' — and segment 7 of that string IS the
        /// element's sequence number. Clearing it renumbers the asset on the next pass;
        /// on a test file that costs nothing, on a project with issued drawings it
        /// invalidates every drawing that carries the old number. So: extract first.
        ///
        /// Recovery requires three things, and refuses on any doubt.
        ///   1. The stored string must split into at least the 8 canonical segments.
        ///   2. The segments that are NOT this token must match the element's own clean
        ///      tokens — otherwise the string is some other element's tag (copied, or
        ///      inherited from a type) and its sequence number belongs to that element.
        ///      At least one position must be checkable; an unverifiable string is cleared.
        ///   3. The extracted segment must itself be clean, and for SEQ must contain a
        ///      digit — a recovered value that is itself malformed is not a recovery.
        /// </summary>
        private static string TryExtractToken(Element el, string polluted, int slot, string sep, out string why)
        {
            why = null;
            string[] canonical = ParamRegistry.AllTokenParams ?? Array.Empty<string>();
            if (slot >= canonical.Length) { why = "not a positional token"; return null; }

            string[] parts = polluted.Split(new[] { sep }, StringSplitOptions.None);

            // TagPrefix shifts every segment right by one; TagSuffix only appends.
            int offset = string.IsNullOrEmpty(TagConfig.TagPrefix) ? 0 : 1;
            if (parts.Length < canonical.Length + offset)
            {
                why = $"only {parts.Length} segment(s), not a tag";
                return null;
            }

            // Ownership: compare the OTHER positions against this element's clean tokens.
            int checkable = 0, matched = 0;
            for (int i = 0; i < canonical.Length; i++)
            {
                if (i == slot) continue;
                string own = ParameterHelpers.GetString(el, canonical[i]);
                if (string.IsNullOrEmpty(own) || IsPollutedToken(own, sep)) continue;  // can't verify against a dirty token
                checkable++;
                if (string.Equals(own.Trim(), parts[i + offset].Trim(), StringComparison.OrdinalIgnoreCase))
                    matched++;
            }
            if (checkable == 0) { why = "no clean token to verify ownership against"; return null; }
            if (matched != checkable)
            {
                why = $"tag belongs to another element ({matched}/{checkable} positions match)";
                return null;
            }

            string candidate = parts[slot + offset].Trim();
            if (candidate.Length == 0) { why = "segment is empty"; return null; }
            if (IsPollutedToken(candidate, sep)) { why = "extracted segment is itself malformed"; return null; }

            bool isSeq = string.Equals(canonical[slot], ParamRegistry.SEQ, StringComparison.OrdinalIgnoreCase);
            if (isSeq && !candidate.Any(char.IsDigit))
            {
                why = "segment in the SEQ position carries no digit";
                return null;
            }
            return candidate;
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
