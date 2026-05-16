// StingTools — Project Asset Picker
//
// Live readers over the active Revit document for editor dropdowns.
// Every list returns a sorted, deduplicated string[] suitable for
// ComboBox.ItemsSource. Used by DrawingTypeEditorDialog so users
// pick from what is actually loaded — eliminating typo errors.

using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.UI
{
    internal static class ProjectAssetPicker
    {
        // ── Title-block family symbols (FamilySymbol, OST_TitleBlocks) ──
        public static IEnumerable<string> TitleBlockFamilyTypes(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .WhereElementIsElementType()
                .Cast<FamilySymbol>()
                .Select(fs => $"{fs.Family?.Name} : {fs.Name}")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .OrderBy(s => s)
                .ToList();
        }

        // ── View templates (View where IsTemplate = true) ──
        public static IEnumerable<string> ViewTemplateNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .Select(v => v.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        // ── Viewport types (ElementType, Category = OST_Viewports) ──
        public static IEnumerable<string> ViewportTypeNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .Where(et => et.Category != null
                          && et.Category.Id.Value == (long)BuiltInCategory.OST_Viewports)
                .Select(et => et.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        // ── Scope boxes (OST_VolumeOfInterest) ──
        public static IEnumerable<string> ScopeBoxNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            return new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .Select(e => e.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        // ── Section / elevation / callout marker families ──
        public static IEnumerable<string> SectionMarkerFamilies(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            var bics = new[]
            {
                BuiltInCategory.OST_CalloutHeads,
                BuiltInCategory.OST_ElevationMarks,
                BuiltInCategory.OST_SectionHeads,
            };
            var result = new List<string>();
            foreach (var bic in bics)
            {
                result.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsElementType()
                    .Cast<ElementType>()
                    .Select(et => $"{(et is FamilySymbol fs ? fs.Family?.Name : et.FamilyName)} : {et.Name}")
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            }
            return result.Distinct().OrderBy(s => s).ToList();
        }

        // ── Dimension types (DimensionType) ──
        public static IEnumerable<string> DimensionStyleNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Select(dt => dt.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        // ── Text note types (TextNoteType) ──
        public static IEnumerable<string> TextStyleNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<TextNoteType>()
                .Select(tt => tt.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        // ── Filter elements (ParameterFilterElement) ──
        public static IEnumerable<string> ParameterFilterNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .Select(pf => pf.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        // ── Tag families (every annotation tag family) ──
        public static IEnumerable<string> TagFamilyNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            var tagBics = new[]
            {
                BuiltInCategory.OST_DoorTags,
                BuiltInCategory.OST_WindowTags,
                BuiltInCategory.OST_RoomTags,
                BuiltInCategory.OST_WallTags,
                BuiltInCategory.OST_FloorTags,
                BuiltInCategory.OST_CeilingTags,
                BuiltInCategory.OST_MEPSpaceTags,
                BuiltInCategory.OST_DuctTags,
                BuiltInCategory.OST_PipeTags,
                BuiltInCategory.OST_LightingFixtureTags,
                BuiltInCategory.OST_MechanicalEquipmentTags,
                BuiltInCategory.OST_ElectricalEquipmentTags,
                BuiltInCategory.OST_ElectricalFixtureTags,
                BuiltInCategory.OST_PlumbingFixtureTags,
                BuiltInCategory.OST_StructuralColumnTags,
                BuiltInCategory.OST_StructuralFramingTags,
                BuiltInCategory.OST_GenericModelTags,
                BuiltInCategory.OST_DetailComponentTags,
            };
            var result = new HashSet<string>();
            foreach (var bic in tagBics)
            {
                foreach (var et in new FilteredElementCollector(doc)
                    .OfCategory(bic)
                    .WhereElementIsElementType()
                    .Cast<ElementType>())
                {
                    var fname = et is FamilySymbol fs ? fs.Family?.Name : et.FamilyName;
                    if (!string.IsNullOrWhiteSpace(fname)) result.Add(fname);
                }
            }
            return result.OrderBy(s => s).ToList();
        }

        // ── Categories with bindable parameters (every category that
        // can host shared parameters — used by VG override + tag-family
        // editors to populate the Category column dropdown). ──
        public static IEnumerable<string> CategoryNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            var result = new HashSet<string>();
            try
            {
                foreach (Category c in doc.Settings.Categories)
                {
                    if (c == null) continue;
                    if (!c.AllowsBoundParameters) continue;
                    if (string.IsNullOrWhiteSpace(c.Name)) continue;
                    result.Add(c.Name);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return result.OrderBy(s => s).ToList();
        }

        // ── Model categories that can host tags (every model category
        // with bindable parameters). Used by the Annotation tab tag-
        // families + rules grids. We can't reliably reverse-map "Door
        // Tags" → "Doors" for every locale, so we just expose every
        // bindable model category and let the merged static
        // KnownTaggableCategories list curate the typical picks. ──
        public static IEnumerable<string> TaggableCategoryNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            var result = new HashSet<string>();
            try
            {
                foreach (Category c in doc.Settings.Categories)
                {
                    if (c == null) continue;
                    if (!c.AllowsBoundParameters) continue;
                    if (c.CategoryType != CategoryType.Model) continue;
                    if (string.IsNullOrWhiteSpace(c.Name)) continue;
                    result.Add(c.Name);
                }
            }
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return result.OrderBy(s => s).ToList();
        }

        // ── Levels ──
        public static IEnumerable<string> LevelNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(l => l.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        // ── Phases ──
        public static IEnumerable<string> PhaseNames(Document doc)
        {
            if (doc == null) return System.Linq.Enumerable.Empty<string>();
            return doc.Phases.Cast<Phase>()
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }

        // ── Worksets (project worksets, user-only) ──
        public static IEnumerable<string> WorksetNames(Document doc)
        {
            if (doc == null || !doc.IsWorkshared) return System.Linq.Enumerable.Empty<string>();
            return new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .Select(w => w.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .OrderBy(n => n)
                .ToList();
        }
    }
}
