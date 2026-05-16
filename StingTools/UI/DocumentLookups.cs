// StingTools — Drawing Template Manager · editor polish
//
// DocumentLookups is a one-shot snapshot of the Revit document read
// when the editor dialog opens. Every card that used to be a free
// TextBox — title block family, view template, viewport type, text
// style, dimension style, scope box, section-marker family, filter
// name, hatch palette — now gets a document-sourced array here so
// the editor offers real values the project already contains.
//
// Arrays include the empty string at position 0 so a field can be
// cleared by picking the blank entry, and ComboBox IsEditable is
// left true everywhere so shops can still enter a name that hasn't
// been loaded yet (corporate profiles ship before their assets).

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.UI
{
    public sealed class DocumentLookups
    {
        public string[] TitleBlockFamilies   { get; private set; } = new string[] { "" };
        public string[] ViewTemplates        { get; private set; } = new string[] { "" };
        public string[] ViewportTypes        { get; private set; } = new string[] { "" };
        public string[] TextStyles           { get; private set; } = new string[] { "" };
        public string[] DimensionStyles      { get; private set; } = new string[] { "" };
        public string[] ScopeBoxes           { get; private set; } = new string[] { "" };
        public string[] FilterNames          { get; private set; } = new string[] { "" };
        public string[] AnnotationFamilies   { get; private set; } = new string[] { "" };
        public string[] CategoryNames        { get; private set; } = new string[] { "" };

        /// <summary>Canned list of hatch palette labels used in the JSON.</summary>
        public string[] HatchPalettes { get; private set; } = new string[]
        {
            "",
            "ISO 13567 monochrome",
            "Rich materials",
            "Monochrome line",
            "Fabrication high-contrast",
        };

        /// <summary>ISO 19650 discipline codes + wildcard.</summary>
        public string[] DisciplineCodes { get; private set; } = new string[]
        {
            "*", "A", "S", "M", "E", "P", "FP", "LV", "G", "ZZ"
        };

        /// <summary>BS EN 19650 document type codes.</summary>
        public string[] DocumentTypeCodes { get; private set; } = new string[]
        {
            "DR", "SH", "M3", "M2", "VS", "CA", "SP", "TC", "AN", "RP", "PR",
        };

        /// <summary>BS EN 19650 suitability codes (delivery team + published).</summary>
        public string[] SuitabilityCodes { get; private set; } = new string[]
        {
            "S0", "S1", "S2", "S3", "S4", "S5", "S6", "S7",
            "A1", "A2", "A3", "A4", "A5",
            "B1", "B2", "B3", "B4", "B5",
            "C1", "C2", "C3", "D1", "D2",
        };

        public string[] RevisionPrefixes { get; private set; } = new string[]
        {
            "P01", "P02", "P03", "C01", "C02", "T01",
        };

        public static DocumentLookups Build(Document doc)
        {
            var l = new DocumentLookups();
            if (doc == null) return l;

            l.TitleBlockFamilies = Dedup("", new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Select(fs => fs.FamilyName));

            l.ViewTemplates = Dedup("", new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .Select(v => v.Name));

            l.ViewportTypes = Dedup("", new FilteredElementCollector(doc)
                .OfClass(typeof(ElementType))
                .Cast<ElementType>()
                .Where(t => t.Category != null
                    && t.Category.Id.Value == (long)BuiltInCategory.OST_Viewports)
                .Select(t => t.Name));

            l.TextStyles = Dedup("", new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .Cast<ElementType>()
                .Select(t => t.Name));

            l.DimensionStyles = Dedup("", new FilteredElementCollector(doc)
                .OfClass(typeof(DimensionType))
                .Cast<DimensionType>()
                .Select(t => t.Name));

            l.ScopeBoxes = Dedup("", new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .Select(e => e.Name));

            l.FilterNames = Dedup("", new FilteredElementCollector(doc)
                .OfClass(typeof(ParameterFilterElement))
                .Cast<ParameterFilterElement>()
                .Select(f => f.Name));

            // Annotation family names — for section-marker / callout /
            // tag family pickers. Filter to Annotation category type.
            l.AnnotationFamilies = Dedup("", new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f =>
                {
                    try
                    {
                        return f.FamilyCategory != null
                            && f.FamilyCategory.CategoryType == CategoryType.Annotation;
                    }
                    catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); return false; }
                })
                .Select(f => f.Name));

            // Category names — model + annotation. Used by the VG
            // override rows to offer a long picker. Subcategory names
            // prefixed "parent/" so they're resolvable unambiguously.
            var names = new List<string>();
            try
            {
                foreach (Category c in doc.Settings.Categories)
                {
                    names.Add(c.Name);
                    foreach (Category sub in c.SubCategories) names.Add(c.Name + "/" + sub.Name);
                }
            }
            catch { /* degrade silently */ }
            l.CategoryNames = Dedup("", names);

            return l;
        }

        private static string[] Dedup(string leader, IEnumerable<string> src)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<string> { leader };
            foreach (var raw in src ?? Enumerable.Empty<string>())
            {
                var s = (raw ?? "").Trim();
                if (s.Length == 0) continue;
                if (set.Add(s)) list.Add(s);
            }
            list.Sort(1, list.Count - 1, StringComparer.OrdinalIgnoreCase);
            return list.ToArray();
        }
    }
}
