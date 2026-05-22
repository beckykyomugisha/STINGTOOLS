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
                case "Cost":   CommitDoubleParam(doc, mat, row, raw, BuiltInParameter.ALL_MODEL_COST,
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
