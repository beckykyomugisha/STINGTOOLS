// StingTools — Drawing Template Manager
//
// DrawingTypeValidator runs the pre-flight checks that stand between a
// user pressing "Generate" and the batch actually running. Its job is
// to catch the silent-fallback failures that cause rework later:
// missing title block family, missing view template, missing tag /
// dimension / section marker family, unloaded annotation families.
//
// Every check returns a ValidationIssue with a severity and a clear
// message; callers (batch commands, preflight dialog) decide whether
// to block, warn-and-proceed, or offer to auto-load the missing
// asset. Blocking vs warning is advisory here — callers own the UX.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Drawing
{
    public enum ValidationSeverity { Info, Warning, Error }

    public sealed class ValidationIssue
    {
        public ValidationSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string DrawingTypeId { get; set; }
        public string SuggestedFix { get; set; }
    }

    public sealed class ValidationReport
    {
        public string DrawingTypeId { get; set; }
        public List<ValidationIssue> Issues { get; } = new List<ValidationIssue>();
        public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);
        public bool HasWarnings => Issues.Any(i => i.Severity == ValidationSeverity.Warning);

        public void Add(ValidationSeverity sev, string code, string msg, string fix = null)
            => Issues.Add(new ValidationIssue
            {
                Severity = sev, Code = code, Message = msg,
                DrawingTypeId = DrawingTypeId, SuggestedFix = fix,
            });
    }

    public static class DrawingTypeValidator
    {
        /// <summary>
        /// Validate a single DrawingType against the current project:
        /// title block loaded, view template present, section marker
        /// family present, tag families present for each category in
        /// the annotation pack, slots geometry sensible.
        /// </summary>
        public static ValidationReport Validate(Document doc, DrawingType dt)
        {
            var r = new ValidationReport { DrawingTypeId = dt?.Id };
            if (dt == null)
            {
                r.Add(ValidationSeverity.Error, "DT-000", "DrawingType is null.");
                return r;
            }

            if (string.IsNullOrWhiteSpace(dt.Id))
                r.Add(ValidationSeverity.Error, "DT-001", "DrawingType has no id.");

            // Title block -------------------------------------------------
            if (!string.IsNullOrWhiteSpace(dt.TitleBlockFamily))
            {
                if (!HasTitleBlockFamily(doc, dt.TitleBlockFamily))
                    r.Add(ValidationSeverity.Error, "DT-010",
                        $"Title block family '{dt.TitleBlockFamily}' not loaded.",
                        "Load the family from Families/AssemblyTitleBlocks/ or point the profile at a different family.");
            }

            // View template ----------------------------------------------
            if (!string.IsNullOrWhiteSpace(dt.ViewTemplateName))
            {
                if (!HasViewTemplate(doc, dt.ViewTemplateName))
                    r.Add(ValidationSeverity.Warning, "DT-020",
                        $"View template '{dt.ViewTemplateName}' not found in project.",
                        "Create the template via Template Mgr > Template Setup Wizard, or clear the profile's viewTemplateName.");
            }

            // Viewport type ----------------------------------------------
            if (!string.IsNullOrWhiteSpace(dt.ViewportTypeName))
            {
                if (!HasViewportType(doc, dt.ViewportTypeName))
                    r.Add(ValidationSeverity.Warning, "DT-021",
                        $"Viewport type '{dt.ViewportTypeName}' not found.",
                        "Duplicate an existing Viewport Type and name it to match, or clear the field.");
            }

            // Section marker family --------------------------------------
            if (IsSectionLikePurpose(dt.Purpose) && !string.IsNullOrWhiteSpace(dt.SectionMarker?.Family))
            {
                if (!HasAnnotationFamily(doc, dt.SectionMarker.Family))
                    r.Add(ValidationSeverity.Warning, "DT-030",
                        $"Section/elevation marker family '{dt.SectionMarker.Family}' not loaded.",
                        "Load the marker family or set sectionMarker.family to null to use project default.");
            }

            // Tag families ------------------------------------------------
            if (dt.Annotation?.TagFamilies != null)
            {
                foreach (var kv in dt.Annotation.TagFamilies)
                {
                    if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                    if (!HasAnnotationFamily(doc, kv.Value))
                        r.Add(ValidationSeverity.Warning, "DT-040",
                            $"Tag family '{kv.Value}' (for {kv.Key}) not loaded.",
                            "Load the tag family or remove the mapping to use the project default tag for that category.");
                }
            }

            // Slot sanity -------------------------------------------------
            if (dt.Slots == null || dt.Slots.Count == 0)
                r.Add(ValidationSeverity.Info, "DT-050",
                    "DrawingType has no slots defined — generation will place views at sheet origin.");
            else
                foreach (var s in dt.Slots) ValidateSlot(s, r);

            // Pattern sanity ---------------------------------------------
            if (string.IsNullOrWhiteSpace(dt.SheetNumberPattern))
                r.Add(ValidationSeverity.Warning, "DT-060",
                    "sheetNumberPattern is empty — generated sheets may collide in numbering.");
            if (string.IsNullOrWhiteSpace(dt.SheetNamePattern))
                r.Add(ValidationSeverity.Info, "DT-061",
                    "sheetNamePattern is empty — sheets will be named by Revit's default.");

            if (dt.Scale <= 0)
                r.Add(ValidationSeverity.Error, "DT-070",
                    "scale must be positive (1:N denominator).");

            // ── Phase 137 — annotation family + production rule + managed pack checks ──

            ValidatePhase137Annotation(doc, dt, r);
            ValidatePhase137ProductionRules(dt, r);
            ValidatePhase137ManagedPack(doc, dt, r);

            return r;
        }

        private static void ValidatePhase137Annotation(Document doc, DrawingType dt, ValidationReport r)
        {
            if (doc == null || dt?.Annotation == null) return;

            void CheckFamily(string family, string code, string label)
            {
                if (string.IsNullOrWhiteSpace(family)) return;
                if (FindAnnotationFamily(doc, family) == null)
                    r.Add(ValidationSeverity.Warning, code,
                        $"{label} family '{family}' not found in project.",
                        "Load the family or clear the field on the profile.");
            }

            CheckFamily(dt.Annotation.NorthArrowFamily, "DT-137-NA", "North arrow");
            CheckFamily(dt.Annotation.ScaleBarFamily,   "DT-137-SB", "Scale bar");
            CheckFamily(dt.Annotation.KeyPlanFamily,    "DT-137-KP", "Key plan");

            if (dt.Annotation.SpotElevationRules != null)
                foreach (var s in dt.Annotation.SpotElevationRules)
                    CheckFamily(s?.SymbolFamily, "DT-137-SE", $"Spot-elevation symbol ({s?.Category})");
            if (dt.Annotation.SpotCoordinateRules != null)
                foreach (var s in dt.Annotation.SpotCoordinateRules)
                    CheckFamily(s?.SymbolFamily, "DT-137-SC", $"Spot-coordinate symbol ({s?.Category})");
        }

        private static void ValidatePhase137ProductionRules(DrawingType dt, ValidationReport r)
        {
            if (dt?.ProductionRules == null) return;
            var rules = dt.ProductionRules;
            if (rules.Count > 0 && (dt.Slots?.Count ?? 0) > 0)
            {
                int maxSlot = rules.Max(p => p?.SlotIndex ?? -1);
                if (maxSlot >= dt.Slots.Count)
                    r.Add(ValidationSeverity.Warning, "DT-137-SLOT",
                        $"ProductionRule references slotIndex {maxSlot} but profile only has {dt.Slots.Count} slot(s).",
                        "Add slots or lower slotIndex.");
            }
            else if (rules.Count > 1 && (dt.Slots?.Count ?? 0) == 0)
            {
                r.Add(ValidationSeverity.Info, "DT-137-NOSLOTS",
                    $"{rules.Count} production rules declared but profile has no slots — produced views will fall back to sheet-centre placement.");
            }
        }

        private static void ValidatePhase137ManagedPack(Document doc, DrawingType dt, ValidationReport r)
        {
            if (doc == null || string.IsNullOrEmpty(dt?.ViewStylePackId)) return;
            ViewStylePack pack;
            try { pack = ViewStylePackRegistry.Get(doc, dt.ViewStylePackId); }
            catch { return; }
            if (pack == null || !pack.IsManaged) return;

            try
            {
                bool anyStingSeed = new FilteredElementCollector(doc)
                    .OfClass(typeof(View))
                    .Cast<View>()
                    .Any(v => v.IsTemplate && (v.Name ?? "").StartsWith("STING - ", StringComparison.Ordinal));
                if (!anyStingSeed)
                    r.Add(ValidationSeverity.Warning, "DT-137-MGD-SEED",
                        $"Pack '{pack.Id}' is managed but no 'STING - ' seed templates exist; the syncer may fall back to a non-STING seed view.",
                        "Create at least one STING- prefixed template to seed managed templates from.");
            }
            catch { }

            if (!string.IsNullOrEmpty(pack.PhaseFilter))
            {
                try
                {
                    bool exists = new FilteredElementCollector(doc)
                        .OfClass(typeof(PhaseFilter))
                        .Cast<PhaseFilter>()
                        .Any(p => string.Equals(p.Name, pack.PhaseFilter, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                        r.Add(ValidationSeverity.Warning, "DT-137-MGD-PHASE",
                            $"Pack '{pack.Id}' references PhaseFilter '{pack.PhaseFilter}' which does not exist.",
                            "Create the phase filter or update the pack.");
                }
                catch { }
            }
        }

        private static FamilySymbol FindAnnotationFamily(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                return new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s =>
                        string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(s.FamilyName, name, StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }

        /// <summary>
        /// Validate every DrawingType in the library + routing-table
        /// coverage. Useful for a one-click "does my project have the
        /// assets to honour every corporate drawing type" audit.
        /// </summary>
        public static List<ValidationReport> ValidateAll(Document doc)
        {
            var reports = DrawingTypeRegistry.ListAll(doc).Select(t => Validate(doc, t)).ToList();

            // Routing coverage — flag routing rules pointing at
            // non-existent drawing types.
            var ids = new HashSet<string>(
                DrawingTypeRegistry.ListAll(doc).Select(t => t.Id ?? ""),
                StringComparer.OrdinalIgnoreCase);
            foreach (var rule in DrawingTypeRegistry.ListRouting(doc))
            {
                if (!ids.Contains(rule.DrawingTypeId ?? ""))
                {
                    var r = new ValidationReport { DrawingTypeId = "(routing)" };
                    r.Add(ValidationSeverity.Error, "DT-100",
                        $"Routing rule ({rule.Discipline}/{rule.Phase}/{rule.DocType}) references unknown drawing type '{rule.DrawingTypeId}'.",
                        "Fix the rule's drawingTypeId or add the missing DrawingType.");
                    reports.Add(r);
                }
            }
            return reports;
        }

        // Revit lookups --------------------------------------------------

        private static bool HasTitleBlockFamily(Document doc, string familyName)
        {
            try
            {
                var col = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_TitleBlocks)
                    .OfClass(typeof(FamilySymbol));
                foreach (var el in col)
                    if (el is FamilySymbol fs
                        && string.Equals(fs.FamilyName, familyName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool HasViewTemplate(Document doc, string name)
        {
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(View));
                foreach (var el in col)
                    if (el is View v && v.IsTemplate
                        && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool HasViewportType(Document doc, string name)
        {
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(ElementType));
                foreach (var el in col)
                    if (el is ElementType t
                        && t.FamilyName != null
                        && t.FamilyName.IndexOf("Viewport", StringComparison.OrdinalIgnoreCase) >= 0
                        && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool HasAnnotationFamily(Document doc, string familyName)
        {
            try
            {
                var col = new FilteredElementCollector(doc).OfClass(typeof(Family));
                foreach (var el in col)
                    if (el is Family f
                        && string.Equals(f.Name, familyName, StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { /* ignore */ }
            return false;
        }

        private static bool IsSectionLikePurpose(string purpose)
        {
            return string.Equals(purpose, DrawingPurpose.Section,   StringComparison.OrdinalIgnoreCase)
                || string.Equals(purpose, DrawingPurpose.Elevation, StringComparison.OrdinalIgnoreCase)
                || string.Equals(purpose, DrawingPurpose.Detail,    StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateSlot(DrawingSlot s, ValidationReport r)
        {
            if (s.NormX < 0 || s.NormY < 0 || s.NormW <= 0 || s.NormH <= 0)
                r.Add(ValidationSeverity.Error, "DT-055",
                    $"Slot '{s.Label}' has invalid geometry (normX={s.NormX} normY={s.NormY} normW={s.NormW} normH={s.NormH}).");
            if (s.NormX + s.NormW > 1.0001 || s.NormY + s.NormH > 1.0001)
                r.Add(ValidationSeverity.Warning, "DT-056",
                    $"Slot '{s.Label}' extends beyond the drawable zone (normX+W={s.NormX + s.NormW:F2} normY+H={s.NormY + s.NormH:F2}).");
        }
    }
}
