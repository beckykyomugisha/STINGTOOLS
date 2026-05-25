using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.TemplateManager
{
    /// <summary>
    /// Phase 19 — single facade exposing the AEC Filter Library (Phase 166,
    /// 289 filters), Drawing Type catalogue (Phase 113, 90 drawing types),
    /// and View Style Packs (22 packs) as first-class data in the Template
    /// Manager dashboard. Long-term path is to deprecate the hardcoded
    /// 28-filter CreateFiltersCommand in favour of the AEC registry, but
    /// this facade lets users see + select the registry's data today.
    /// </summary>
    public static class CrossEngineFacade
    {
        // ── AEC filter library ────────────────────────────────────────
        public static OperationPreview AecFiltersPreview(Document doc)
        {
            var p = new OperationPreview
            {
                Operation = "CreateFilters",
                OperationLabel = "Create Filters (AEC Library 289)",
                SupportsConflictResolution = true
            };
            if (doc == null) return p;
            try
            {
                var existing = new HashSet<string>(
                    new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement))
                        .Cast<ParameterFilterElement>().Where(f => f?.Name != null).Select(f => f.Name),
                    StringComparer.OrdinalIgnoreCase);
                var library = global::StingTools.Core.Drawing.AecFilterRegistry.ListAll(doc);
                foreach (var def in library)
                {
                    bool exists = existing.Contains(def.Name);
                    p.Rows.Add(new PreviewRow
                    {
                        Key = def.Id,
                        Name = def.Name,
                        Category = def.Tags != null && def.Tags.Count > 0 ? def.Tags[0] : "filter",
                        Discipline = ExtractDisciplineTag(def.Tags),
                        Exists = exists,
                        Action = exists ? "Skip" : "Create",
                        Source = def.Origin ?? "library",
                        Detail = def.Description ?? ""
                    });
                }
            }
            catch (Exception ex) { p.Notes = "AEC scan failed: " + ex.Message; }

            p.AvailableDisciplines = p.Rows.Select(r => r.Discipline).Distinct().OrderBy(s => s).ToList();
            p.Summary = $"AEC Filter Library: {p.Rows.Count} filters · {p.NewCount} new · {p.ExistingCount} existing";
            return p;
        }

        // ── Drawing Type catalogue ────────────────────────────────────
        public static OperationPreview DrawingTypesPreview(Document doc)
        {
            var p = new OperationPreview
            {
                Operation = "DrawingTypesInspect",
                OperationLabel = "Drawing Types catalogue (90 types)"
            };
            if (doc == null) return p;
            try
            {
                var dts = global::StingTools.Core.Drawing.DrawingTypeRegistry.ListAll(doc);
                foreach (var dt in dts)
                {
                    p.Rows.Add(new PreviewRow
                    {
                        Key = dt.Id,
                        Name = dt.Name ?? dt.Id,
                        Category = dt.Purpose ?? "",
                        Discipline = dt.Discipline ?? "*",
                        Exists = true,
                        Action = "Inspect",
                        Source = dt.Origin ?? "corporate",
                        Detail = $"{dt.PaperSize} · 1:{dt.Scale}"
                    });
                }
            }
            catch (Exception ex) { p.Notes = "DT scan failed: " + ex.Message; }
            p.AvailableDisciplines = p.Rows.Select(r => r.Discipline).Distinct().OrderBy(s => s).ToList();
            p.Summary = $"Drawing Types: {p.Rows.Count}";
            return p;
        }

        // ── View Style Packs ──────────────────────────────────────────
        public static OperationPreview ViewStylePacksPreview(Document doc)
        {
            var p = new OperationPreview
            {
                Operation = "ViewStylePacks",
                OperationLabel = "View Style Packs (22 packs)"
            };
            if (doc == null) return p;
            try
            {
                var packs = global::StingTools.Core.Drawing.ViewStylePackRegistry.ListAll(doc);
                foreach (var pack in packs)
                {
                    p.Rows.Add(new PreviewRow
                    {
                        Key = pack.Id,
                        Name = pack.Name ?? pack.Id,
                        Category = pack.TemplateMode ?? "external",
                        Discipline = "*",
                        Exists = true,
                        Action = "Inspect",
                        Source = pack.Origin ?? "corporate",
                        Detail = pack.Description ?? ""
                    });
                }
            }
            catch (Exception ex) { p.Notes = "Pack scan failed: " + ex.Message; }
            p.Summary = $"View Style Packs: {p.Rows.Count}";
            return p;
        }

        private static string ExtractDisciplineTag(IList<string> tags)
        {
            if (tags == null) return "*";
            // Tags often carry a discipline marker like "Arch" / "MEP" / "Struct"
            foreach (var t in tags)
            {
                if (string.IsNullOrEmpty(t)) continue;
                if (t.Equals("Arch", StringComparison.OrdinalIgnoreCase)) return "A";
                if (t.Equals("Struct", StringComparison.OrdinalIgnoreCase)) return "S";
                if (t.Equals("HVAC", StringComparison.OrdinalIgnoreCase)) return "M";
                if (t.Equals("Elec", StringComparison.OrdinalIgnoreCase)) return "E";
                if (t.Equals("Plumb", StringComparison.OrdinalIgnoreCase)) return "P";
                if (t.Equals("Fire", StringComparison.OrdinalIgnoreCase)) return "FP";
                if (t.Equals("FM", StringComparison.OrdinalIgnoreCase) || t.Equals("COBie", StringComparison.OrdinalIgnoreCase)) return "FM";
                if (t.Equals("ISO 19650", StringComparison.OrdinalIgnoreCase)) return "ISO";
            }
            return "*";
        }
    }
}
