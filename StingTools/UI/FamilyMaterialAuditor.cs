using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using StingTools.Core;

namespace StingTools.UI
{
    /// <summary>
    /// F14 — Family folder audit (CTC BIM Manager parity).
    ///
    /// Opens every <c>.rfa</c> in a picked folder read-only, inspects
    /// the materials it carries (via FilteredElementCollector inside
    /// the family document), and emits a CSV report so admins can spot
    /// off-baseline materials that would arrive when a family is loaded
    /// into a project.
    ///
    /// Workflow
    ///   1) User picks a folder of .rfa files (recursive optional).
    ///   2) Auditor opens each file via Application.OpenDocumentFile in
    ///      its read-only mode, sweeps Material elements, records:
    ///        - family file path
    ///        - material name + class + RGB
    ///        - whether the name matches the corporate baseline
    ///          (BLE_*, MEP_*, STING_*) or an off-baseline keeper.
    ///   3) Closes the family doc (no save).
    ///   4) Writes a CSV next to the input folder + opens it.
    ///
    /// Read-only opens are slow — about 1–2 s per family on average.
    /// A 500-family vendor drop takes ~15 minutes. The audit is a
    /// background activity, not interactive.
    /// </summary>
    public class FamilyMaterialAuditRow
    {
        public string FamilyPath { get; set; }
        public string FamilyName { get; set; }
        public string MaterialName { get; set; }
        public string MaterialClass { get; set; }
        public string Origin { get; set; } // STING / BLE / MEP / Other
        public string ColorRgb { get; set; }
    }

    public class FamilyMaterialAuditResult
    {
        public List<FamilyMaterialAuditRow> Rows { get; } = new List<FamilyMaterialAuditRow>();
        public List<string> Failures { get; } = new List<string>();
        public int FamiliesScanned { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    public static class FamilyMaterialAuditor
    {
        public static FamilyMaterialAuditResult Run(Application app, string folder, bool recursive)
        {
            var result = new FamilyMaterialAuditResult();
            if (app == null || string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;
            var started = DateTime.UtcNow;

            var rfaPaths = Directory.EnumerateFiles(folder, "*.rfa",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(p => !p.Contains("_backup") && !Path.GetFileName(p).StartsWith("~"))
                .Take(2000) // hard cap — bigger vendor drops should be split
                .ToList();

            foreach (var rfa in rfaPaths)
            {
                Document famDoc = null;
                try
                {
                    famDoc = app.OpenDocumentFile(rfa);
                    if (famDoc == null || !famDoc.IsFamilyDocument) continue;
                    var name = Path.GetFileNameWithoutExtension(rfa);
                    var materials = new FilteredElementCollector(famDoc)
                        .OfClass(typeof(Material))
                        .Cast<Material>()
                        .ToList();
                    foreach (var m in materials)
                    {
                        string mn = m.Name ?? "(unnamed)";
                        string origin =
                            mn.StartsWith("STING", StringComparison.OrdinalIgnoreCase) ? "STING" :
                            mn.StartsWith("BLE_",  StringComparison.OrdinalIgnoreCase) ? "BLE" :
                            mn.StartsWith("MEP_",  StringComparison.OrdinalIgnoreCase) ? "MEP" : "Other";
                        string rgb = "";
                        try
                        {
                            var c = m.Color; if (c != null && c.IsValid)
                                rgb = $"RGB({c.Red},{c.Green},{c.Blue})";
                        }
                        catch (Exception ex) { StingLog.Warn($"FamilyMaterialAuditor color: {ex.Message}"); }
                        result.Rows.Add(new FamilyMaterialAuditRow
                        {
                            FamilyPath = rfa,
                            FamilyName = name,
                            MaterialName = mn,
                            MaterialClass = m.MaterialClass ?? "",
                            Origin = origin,
                            ColorRgb = rgb,
                        });
                    }
                    result.FamiliesScanned++;
                }
                catch (Exception ex)
                {
                    result.Failures.Add($"{rfa}: {ex.Message}");
                    StingLog.Warn($"FamilyMaterialAuditor open '{rfa}': {ex.Message}");
                }
                finally
                {
                    try { famDoc?.Close(false); }
                    catch (Exception ex) { StingLog.Warn($"FamilyMaterialAuditor close: {ex.Message}"); }
                }
            }

            result.Elapsed = DateTime.UtcNow - started;
            return result;
        }

        /// <summary>
        /// D5 — Batch-fix: walk every .rfa in the folder, rename any
        /// material whose name matches a key in <paramref name="renameMap"/>
        /// to the mapped value. Family files are opened editable, saved,
        /// and closed. Skips files whose material set has no matches.
        ///
        /// Renames are case-insensitive. Returns a per-family report.
        /// Caller decides which materials to remap (build the map from
        /// the audit result + a corporate target list).
        /// </summary>
        public static FamilyMaterialAuditResult BatchRename(Application app, string folder,
            bool recursive, IDictionary<string, string> renameMap)
        {
            var report = new FamilyMaterialAuditResult();
            if (app == null || string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ||
                renameMap == null || renameMap.Count == 0) return report;
            var started = DateTime.UtcNow;
            var ciMap = new Dictionary<string, string>(renameMap, StringComparer.OrdinalIgnoreCase);

            var rfaPaths = Directory.EnumerateFiles(folder, "*.rfa",
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(p => !p.Contains("_backup") && !Path.GetFileName(p).StartsWith("~"))
                .Take(500) // batch-fix cap is lower than read-only audit
                .ToList();

            foreach (var rfa in rfaPaths)
            {
                Document famDoc = null;
                try
                {
                    famDoc = app.OpenDocumentFile(rfa);
                    if (famDoc == null || !famDoc.IsFamilyDocument) continue;
                    var materials = new FilteredElementCollector(famDoc).OfClass(typeof(Material))
                        .Cast<Material>().ToList();
                    int renamed = 0;
                    using (var t = new Transaction(famDoc, "STING Material Batch Rename"))
                    {
                        t.Start();
                        foreach (var m in materials)
                        {
                            try
                            {
                                if (ciMap.TryGetValue(m.Name ?? "", out var newName) &&
                                    !string.Equals(m.Name, newName, StringComparison.Ordinal))
                                {
                                    m.Name = newName;
                                    renamed++;
                                }
                            }
                            catch (Exception ex) { StingLog.Warn($"BatchRename mat '{m?.Name}' in '{rfa}': {ex.Message}"); }
                        }
                        if (renamed > 0) t.Commit(); else t.RollBack();
                    }
                    if (renamed > 0)
                    {
                        try
                        {
                            famDoc.Save();
                            report.Rows.Add(new FamilyMaterialAuditRow
                            {
                                FamilyPath = rfa,
                                FamilyName = Path.GetFileNameWithoutExtension(rfa),
                                MaterialName = $"renamed {renamed} material(s)",
                                Origin = "BATCH-FIX",
                                MaterialClass = "",
                                ColorRgb = "",
                            });
                        }
                        catch (Exception ex)
                        {
                            report.Failures.Add($"{rfa} save: {ex.Message}");
                        }
                    }
                    report.FamiliesScanned++;
                }
                catch (Exception ex)
                {
                    report.Failures.Add($"{rfa}: {ex.Message}");
                    StingLog.Warn($"BatchRename open '{rfa}': {ex.Message}");
                }
                finally
                {
                    try { famDoc?.Close(false); }
                    catch (Exception ex) { StingLog.Warn($"BatchRename close: {ex.Message}"); }
                }
            }
            report.Elapsed = DateTime.UtcNow - started;
            return report;
        }

        /// <summary>
        /// Write a CSV report next to the source folder. Returns the path.
        /// </summary>
        public static string WriteReport(string folder, FamilyMaterialAuditResult result)
        {
            string path = Path.Combine(folder,
                $"STING_family_material_audit_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            var sb = new StringBuilder();
            sb.AppendLine("FamilyPath,FamilyName,MaterialName,MaterialClass,Origin,ColorRgb");
            foreach (var r in result.Rows)
                sb.AppendLine($"\"{r.FamilyPath}\",\"{r.FamilyName}\",\"{r.MaterialName}\",\"{r.MaterialClass}\",\"{r.Origin}\",\"{r.ColorRgb}\"");
            if (result.Failures.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("# Failures:");
                foreach (var f in result.Failures) sb.AppendLine($"# {f}");
            }
            File.WriteAllText(path, sb.ToString());
            StingLog.Info($"FamilyMaterialAuditor: {result.Rows.Count} rows from {result.FamiliesScanned} families → {path}");
            return path;
        }
    }
}
