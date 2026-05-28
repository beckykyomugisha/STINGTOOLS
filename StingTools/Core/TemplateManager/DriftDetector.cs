using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Detects drift between a STING view template's current VG state and the
    /// expected corporate baseline. Stamps STING_TEMPLATE_CHECKSUM_TXT so
    /// subsequent scans are cheap. Mirror of Phase 184's
    /// LiveProfileSync / DrawingDriftDetector pattern.
    /// </summary>
    public sealed class TemplateDriftEntry
    {
        public int TemplateId { get; set; }
        public string TemplateName { get; set; } = "";
        public string Kind { get; set; } = "";       // "FilterSet" | "FilterOverride" | "Category" | "Missing" | "Orphan"
        public string Detail { get; set; } = "";
        public string ExpectedChecksum { get; set; } = "";
        public string ActualChecksum { get; set; } = "";
    }

    public static class DriftDetector
    {
        /// <summary>
        /// Walk every STING template, build a checksum of (filter ids + per-category
        /// override snippets), compare with the stamped value. Emits TemplateDriftEntry
        /// for any divergence.
        /// </summary>
        public static List<TemplateDriftEntry> Scan(Document doc)
        {
            var list = new List<TemplateDriftEntry>();
            if (doc == null) return list;
            try
            {
                var stingFilters = new HashSet<ElementId>(
                    new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement))
                        .Cast<ParameterFilterElement>()
                        .Where(f => f.Name?.StartsWith("STING") == true)
                        .Select(f => f.Id));

                foreach (View tmpl in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
                {
                    if (!tmpl.IsTemplate) continue;
                    if (!(tmpl.Name?.StartsWith("STING", StringComparison.OrdinalIgnoreCase) ?? false)) continue;

                    string actual = ComputeChecksum(doc, tmpl);
                    string expected = ReadStampedChecksum(tmpl);

                    if (string.IsNullOrEmpty(expected))
                    {
                        // Never stamped — surface as Missing
                        list.Add(new TemplateDriftEntry
                        {
                            TemplateId = tmpl.Id.IntegerValue,
                            TemplateName = tmpl.Name,
                            Kind = "Missing",
                            Detail = "No checksum stamp — never synced.",
                            ActualChecksum = actual
                        });
                        continue;
                    }
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                    {
                        list.Add(new TemplateDriftEntry
                        {
                            TemplateId = tmpl.Id.IntegerValue,
                            TemplateName = tmpl.Name,
                            Kind = "FilterOverride",
                            Detail = $"VG checksum drift",
                            ExpectedChecksum = expected,
                            ActualChecksum = actual
                        });
                    }

                    // Check for orphaned filters
                    foreach (var fid in tmpl.GetFilters())
                    {
                        if (doc.GetElement(fid) == null)
                        {
                            list.Add(new TemplateDriftEntry
                            {
                                TemplateId = tmpl.Id.IntegerValue,
                                TemplateName = tmpl.Name,
                                Kind = "Orphan",
                                Detail = $"Orphaned filter id {fid.IntegerValue}"
                            });
                        }
                    }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DriftDetector.Scan: {ex.Message}"); }
            return list;
        }

        /// <summary>Stamp the checksum onto each STING template (call from within a transaction).</summary>
        public static int StampAll(Document doc)
        {
            int stamped = 0;
            if (doc == null) return 0;
            foreach (View tmpl in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
            {
                if (!tmpl.IsTemplate) continue;
                if (!(tmpl.Name?.StartsWith("STING", StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                string ck = ComputeChecksum(doc, tmpl);
                if (WriteStamp(tmpl, ck)) stamped++;
            }
            return stamped;
        }

        private static string ComputeChecksum(Document doc, View tmpl)
        {
            var sb = new StringBuilder();
            try
            {
                sb.Append("tmpl:").Append(tmpl.Name).Append('|');
                sb.Append("scale:").Append(tmpl.Scale).Append('|');
                sb.Append("dl:").Append(tmpl.DetailLevel).Append('|');
                var fids = tmpl.GetFilters().OrderBy(i => i.IntegerValue).ToList();
                sb.Append("filters:");
                foreach (var fid in fids)
                {
                    sb.Append(fid.IntegerValue).Append(',');
                    try
                    {
                        var ogs = tmpl.GetFilterOverrides(fid);
                        bool vis = tmpl.GetFilterVisibility(fid);
                        sb.Append("v").Append(vis ? 1 : 0);
                        sb.Append("h").Append(ogs.Halftone ? 1 : 0);
                        sb.Append("t").Append(ogs.Transparency);
                        sb.Append("plw").Append(ogs.ProjectionLineWeight);
                        sb.Append("clw").Append(ogs.CutLineWeight);
                        sb.Append(';');
                    }
                    catch { }
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DriftDetector.ComputeChecksum: {ex.Message}"); }

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            return Convert.ToBase64String(bytes).Substring(0, 24);
        }

        private static string ReadStampedChecksum(View tmpl)
        {
            try
            {
                var p = tmpl.LookupParameter("STING_TEMPLATE_CHECKSUM_TXT");
                if (p != null && p.HasValue && p.StorageType == StorageType.String)
                    return p.AsString();
            }
            catch { }
            return null;
        }

        private static bool WriteStamp(View tmpl, string checksum)
        {
            try
            {
                var p = tmpl.LookupParameter("STING_TEMPLATE_CHECKSUM_TXT");
                if (p != null && !p.IsReadOnly && p.StorageType == StorageType.String)
                {
                    p.Set(checksum);
                    return true;
                }
            }
            catch (Exception ex) { StingTools.Core.StingLog.Warn($"DriftDetector.WriteStamp: {ex.Message}"); }
            return false;
        }

        /// <summary>True when STING_TEMPLATE_LOCKED_BOOL is set to 1 on the template.</summary>
        public static bool IsLocked(View tmpl)
        {
            try
            {
                var p = tmpl?.LookupParameter("STING_TEMPLATE_LOCKED_BOOL");
                if (p != null && p.HasValue)
                {
                    if (p.StorageType == StorageType.Integer) return p.AsInteger() == 1;
                    if (p.StorageType == StorageType.String)
                    {
                        var s = p.AsString();
                        return !string.IsNullOrEmpty(s) && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
