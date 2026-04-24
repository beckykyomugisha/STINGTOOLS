using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>
    /// Central registry for the project's title block routing policy.
    /// Populated by <see cref="StingTools.Temp.ProjectSetupCommand"/> from the
    /// Project Setup Wizard selections and consulted by every sheet-creation
    /// command (BatchCreateSheets, DocAutomation, ShopDrawingComposer, etc.)
    /// so a single user choice flows through the whole pipeline.
    /// </summary>
    public static class TitleBlockRouter
    {
        /// <summary>Discipline code → title block FamilySymbol ElementId.
        /// Keys use the ISO 19650 single-letter discipline codes: A, S, M, E, P, FP, LV, G.</summary>
        public static Dictionary<string, ElementId> ByDiscipline { get; set; }
            = new Dictionary<string, ElementId>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fallback title block FamilySymbol ElementId when no per-discipline match.</summary>
        public static ElementId DefaultId { get; set; } = ElementId.InvalidElementId;

        /// <summary>Resolve a title block symbol for the given discipline.
        /// Priority: ByDiscipline[disc] → DefaultId → first OST_TitleBlocks symbol in doc.
        /// Returns an activated <see cref="FamilySymbol"/>, or null when the project has no title blocks.</summary>
        public static FamilySymbol Resolve(Document doc, string disciplineCode)
        {
            if (doc == null) return null;

            // 1) Per-discipline override
            if (!string.IsNullOrEmpty(disciplineCode)
                && ByDiscipline != null
                && ByDiscipline.TryGetValue(disciplineCode, out ElementId id)
                && id != ElementId.InvalidElementId)
            {
                if (doc.GetElement(id) is FamilySymbol fs) return EnsureActive(doc, fs);
            }

            // 2) Wizard default
            if (DefaultId != ElementId.InvalidElementId
                && doc.GetElement(DefaultId) is FamilySymbol dfs)
            {
                return EnsureActive(doc, dfs);
            }

            // 3) Match TagConfig.PreferredTitleBlockFamily by family name
            string preferred = TagConfig.PreferredTitleBlockFamily;
            var all = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(BuiltInCategory.OST_TitleBlocks)
                .Cast<FamilySymbol>()
                .ToList();
            if (!string.IsNullOrEmpty(preferred))
            {
                var match = all.FirstOrDefault(fs =>
                    string.Equals(fs.FamilyName, preferred, StringComparison.OrdinalIgnoreCase));
                if (match != null) return EnsureActive(doc, match);
            }

            // 4) First available
            return all.Count > 0 ? EnsureActive(doc, all[0]) : null;
        }

        /// <summary>Activate a FamilySymbol inside its own transaction if not already active.</summary>
        private static FamilySymbol EnsureActive(Document doc, FamilySymbol fs)
        {
            if (fs == null || fs.IsActive) return fs;
            try
            {
                using (var tx = new Transaction(doc, "STING Activate Title Block"))
                {
                    tx.Start();
                    fs.Activate();
                    doc.Regenerate();
                    tx.Commit();
                }
            }
            catch (Exception ex)
            {
                StingLog.Warn($"TitleBlockRouter activate '{fs.FamilyName}': {ex.Message}");
            }
            return fs;
        }
    }
}
