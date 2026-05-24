using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// Inline cell-edit commit pipeline for the MAT > Browse grid.
    ///
    /// Why a separate class: the dock-panel code-behind shouldn't carry
    /// transactional logic. <see cref="Commit"/> handles parsing, the
    /// transaction, parameter writes (BuiltInParameter.ALL_MODEL_COST for
    /// Cost, the STING_EMB_CARBON_NR shared parameter for kgCO₂e,
    /// Material.MaterialClass for Class), and the audit-log entry so
    /// every inline edit is traceable.
    ///
    /// No-op when the raw text parses to the same value the material
    /// already has — keeps the audit log free of phantom edits when a
    /// user just tabs through cells.
    /// </summary>
    public static class MatCellCommitter
    {
        public static void Commit(Document doc, Material mat, MaterialRow row, string columnHeader, string raw)
        {
            if (doc == null || mat == null || row == null) return;
            string col = (columnHeader ?? "").Trim();

            switch (col)
            {
                case "Cost":
                    // R-2 — Currency-intent guard before committing.
                    var loc = MaterialRow.ActiveLocale;
                    double parsedNv;
                    if (TryParseLocale(raw, loc, out parsedNv) &&
                        !ConfirmCurrencyIntent(doc, loc, row.Cost, parsedNv))
                        return; // user cancelled
                    CommitDoubleParam(doc, mat, row, raw, BuiltInParameter.ALL_MODEL_COST,
                                       row.Cost, "MAT_EditCost", "cost"); break;
                case "kgCO₂e": CommitSharedParam(doc, mat, row, raw, "STING_EMB_CARBON_NR",
                                                  row.CarbonKgCo2e, "MAT_EditCarbon", "carbonKgCo2e"); break;
                case "Class":  CommitClass(doc, mat, row, raw); break;
                default: return; // every other column is read-only
            }

            // N+5 — Outbound COBie sync. Cheap no-op when nothing in the
            // CSV references this material; otherwise a per-row name-casing
            // refresh + an audit log entry so the change is traceable.
            try { CobieMaterialBridge.SyncFromMaterial(doc, mat); }
            catch (Exception ex) { StingLog.Warn($"MatCellCommitter cobie sync: {ex.Message}"); }
        }

        private static void CommitDoubleParam(Document doc, Material mat, MaterialRow row,
            string raw, BuiltInParameter bip, double oldValue, string auditAction, string fieldKey)
        {
            // D6 — Locale-aware parsing. The user is typing in their
            // project locale (en-GB / de-DE / en-US ...); we accept both
            // the locale's culture AND InvariantCulture so "1,234.56"
            // works in en-US, "1.234,56" works in de-DE, and "1234.56"
            // works everywhere.
            var loc = MaterialRow.ActiveLocale;
            double nv;
            if (!TryParseLocale(raw, loc, out nv)) return;
            if (Math.Abs(nv - oldValue) < 0.0001) return;
            using (var t = new Transaction(doc, $"STING MAT edit {fieldKey} '{mat.Name}'"))
            {
                t.Start();
                try
                {
                    var p = mat.get_Parameter(bip);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) { t.RollBack(); return; }
                    p.Set(nv);
                    t.Commit();
                }
                catch (Exception ex) { StingLog.Warn($"Commit {fieldKey}: {ex.Message}"); try { t.RollBack(); } catch { } return; }
            }
            MaterialAuditLogger.Log(doc, auditAction, mat.Name,
                new Dictionary<string, object>
                {
                    ["old"] = oldValue,
                    ["new"] = nv,
                    ["locale"] = loc?.Region.ToString() ?? "default",
                });

            // BOQ-14 — Edit MAT cost → bump RateConfidence on every BOQ row
            // using this material so the rate-source heat-map shows the
            // higher-confidence source on the next dashboard load. The flag
            // is on ASS_CST_STALE_BOOL — see StingStaleMarker N+3.
            if (bip == BuiltInParameter.ALL_MODEL_COST)
            {
                try { BumpRateConfidenceForMaterialUsers(doc, mat); }
                catch (Exception ex) { StingLog.Warn($"BumpRateConfidence: {ex.Message}"); }
            }
        }

        /// <summary>
        /// R-2 — When the user is editing in a locale whose currency
        /// differs from the project's base currency, surface a one-shot
        /// confirmation. Returns true when the edit should proceed; false
        /// to cancel. No-op when locales align or project base isn't set.
        /// </summary>
        internal static bool ConfirmCurrencyIntent(Document doc, MaterialLocale loc, double oldValue, double newValue)
        {
            if (loc == null) return true;
            string projCurrency = ReadProjectBaseCurrency(doc);
            string localeCurrency = NormaliseCurrency(loc.CurrencySymbol);
            if (string.IsNullOrEmpty(projCurrency) || string.IsNullOrEmpty(localeCurrency)) return true;
            if (string.Equals(projCurrency, localeCurrency, StringComparison.OrdinalIgnoreCase)) return true;
            // Mismatch — confirm.
            var td = new Autodesk.Revit.UI.TaskDialog("STING Material — Cost edit currency check")
            {
                MainInstruction = $"You're editing in {loc.Region} locale ({localeCurrency}) but the project's base currency is {projCurrency}.",
                MainContent = $"The cell will store '{newValue:F2}' as a {projCurrency} value. Did you mean {localeCurrency}? If yes, convert manually before saving — STING doesn't auto-convert at the cell-commit boundary.",
                CommonButtons = Autodesk.Revit.UI.TaskDialogCommonButtons.Cancel,
            };
            td.AddCommandLink(Autodesk.Revit.UI.TaskDialogCommandLinkId.CommandLink1,
                "Store as " + projCurrency, "I meant the project base currency.");
            return td.Show() == Autodesk.Revit.UI.TaskDialogResult.CommandLink1;
        }

        private static string ReadProjectBaseCurrency(Document doc)
        {
            try
            {
                var p = doc?.ProjectInformation?.LookupParameter("PRJ_ORG_CURRENCY_TXT")
                       ?? doc?.ProjectInformation?.LookupParameter("Currency");
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                    return (p.AsString() ?? "").Trim().ToUpperInvariant();
            }
            catch (Exception ex) { StingLog.Warn($"ReadProjectBaseCurrency: {ex.Message}"); }
            return "";
        }

        private static string NormaliseCurrency(string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return "";
            switch (symbol.Trim())
            {
                case "£": return "GBP";
                case "€": return "EUR";
                case "$": return "USD";
                case "A$": return "AUD";
                default: return symbol.ToUpperInvariant();
            }
        }

        /// <summary>
        /// BOQ-14 — Walk every modelled element using the edited material
        /// and bump CST_RATE_SOURCE / CST_RATE_CONFIDENCE to reflect that
        /// the rate now comes from the (live, fresh) material library.
        /// Best-effort; falls through silently when the params aren't
        /// bound on the project.
        /// </summary>
        private static void BumpRateConfidenceForMaterialUsers(Document doc, Material mat)
        {
            if (doc == null || mat == null || mat.Id == null) return;
            using (var t = new Transaction(doc, $"STING Bump rate confidence for '{mat.Name}'"))
            {
                t.Start();
                int touched = 0;
                try
                {
                    // P-3 — Reverse index lookup. Previously walked every
                    // non-type element in the project (multi-second freeze
                    // on busy projects). Now scoped to the elements that
                    // actually use this material.
                    var users = MaterialUsageIndex.ElementsUsing(doc, mat.Id);
                    foreach (var elId in users)
                    {
                        try
                        {
                            var el = doc.GetElement(elId);
                            if (el == null) continue;
                            var src = el.LookupParameter("CST_RATE_SOURCE");
                            if (src != null && !src.IsReadOnly && src.StorageType == StorageType.String)
                                src.Set("material-library");
                            var conf = el.LookupParameter("CST_RATE_CONFIDENCE");
                            if (conf != null && !conf.IsReadOnly && conf.StorageType == StorageType.Integer)
                                conf.Set(95);
                            var stale = el.LookupParameter("ASS_CST_STALE_BOOL");
                            if (stale != null && !stale.IsReadOnly && stale.StorageType == StorageType.String)
                                stale.Set("1");
                            touched++;
                        }
                        catch (Exception ex) { StingLog.WarnRateLimited("BumpRate.El", $"BumpRate {elId}: {ex.Message}"); }
                    }
                    t.Commit();
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"BumpRateConfidence outer: {ex.Message}");
                    try { t.RollBack(); } catch { }
                    return;
                }
                if (touched > 0)
                    StingLog.Info($"BumpRateConfidence: bumped {touched} element(s) using '{mat.Name}' to confidence 95.");
            }
        }

        private static void CommitSharedParam(Document doc, Material mat, MaterialRow row,
            string raw, string paramName, double oldValue, string auditAction, string fieldKey)
        {
            var loc = MaterialRow.ActiveLocale;
            double nv;
            if (!TryParseLocale(raw, loc, out nv)) return;
            if (Math.Abs(nv - oldValue) < 0.0001) return;
            using (var t = new Transaction(doc, $"STING MAT edit {fieldKey} '{mat.Name}'"))
            {
                t.Start();
                try
                {
                    var p = mat.LookupParameter(paramName);
                    if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double)
                    {
                        // Shared param not bound — surface it so the user knows why the edit didn't stick.
                        t.RollBack();
                        Autodesk.Revit.UI.TaskDialog.Show("Inline Edit",
                            $"Couldn't write {paramName} — load STING shared parameters first (Load Shared Params from the dock panel).");
                        return;
                    }
                    p.Set(nv);
                    t.Commit();
                }
                catch (Exception ex) { StingLog.Warn($"Commit {fieldKey}: {ex.Message}"); try { t.RollBack(); } catch { } return; }
            }
            MaterialAuditLogger.Log(doc, auditAction, mat.Name,
                new Dictionary<string, object> { ["old"] = oldValue, ["new"] = nv });
        }

        /// <summary>
        /// D6 — Locale-aware double parser. Tries the locale's culture
        /// first (so de-DE "1.234,56" parses correctly), then falls back
        /// to InvariantCulture so a user typing "1234.56" anywhere
        /// always works. Empty / un-parseable input → false (no commit).
        /// </summary>
        private static bool TryParseLocale(string raw, MaterialLocale loc, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            string s = raw.Trim();
            // Strip currency / unit suffixes the formatter may have left.
            foreach (var symbol in new[] { "£", "$", "€", "A$", "kgCO₂e", "kg/m³", "lb/ft³" })
                if (s.Contains(symbol)) s = s.Replace(symbol, "");
            s = s.Trim();
            if (loc?.Culture != null && double.TryParse(s, NumberStyles.Any, loc.Culture, out value))
                return true;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static void CommitClass(Document doc, Material mat, MaterialRow row, string raw)
        {
            string nv = (raw ?? "").Trim();
            if (string.Equals(nv, row.Class ?? "", StringComparison.Ordinal)) return;
            string oldVal = mat.MaterialClass ?? "";
            using (var t = new Transaction(doc, $"STING MAT edit class '{mat.Name}'"))
            {
                t.Start();
                try
                {
                    mat.MaterialClass = nv;
                    t.Commit();
                }
                catch (Exception ex) { StingLog.Warn($"Commit class: {ex.Message}"); try { t.RollBack(); } catch { } return; }
            }
            MaterialAuditLogger.Log(doc, "MAT_EditClass", mat.Name,
                new Dictionary<string, object> { ["old"] = oldVal, ["new"] = nv });
        }
    }
}
