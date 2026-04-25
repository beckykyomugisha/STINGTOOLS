// ============================================================================
// StyleAuditCommand.cs — Audit tag family parameter + variant coverage.
//
// For every loaded STING-prefixed tag family in the project, check:
//   1. All expected parameters present (TagFamilyConfig.TagParams +
//      VisibilityParams + StyleParams).
//   2. All expected type variants present (TagStyleCatalogue.EnumerateStandardVariants).
//   3. No duplicate variants.
//   4. Family's baseline type matches its disciplinary default, where the
//      family's discipline can be inferred from its name.
//
// Output:
//   • Excel report (Family, MissingParams, MissingVariants, UnexpectedVariants,
//     DisciplineDefault, ActualDefault, Status)
//   • Red / Amber / Green per row based on total deviations
//   • TaskDialog offer to run MigrateTagFamiliesCommand on Red rows
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Commands.TagStudio
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class StyleAuditCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var ctx = ParameterHelpers.GetContext(commandData);
            if (ctx == null) { TaskDialog.Show("STING", "No document open."); return Result.Failed; }
            Document doc = ctx.Doc;

            var stingFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.Name != null &&
                            f.Name.StartsWith(TagFamilyConfig.FamilyPrefix, StringComparison.OrdinalIgnoreCase) &&
                            f.FamilyCategory != null &&
                            f.FamilyCategory.CategoryType == CategoryType.Annotation)
                .OrderBy(f => f.Name)
                .ToList();

            if (stingFamilies.Count == 0)
            {
                TaskDialog.Show("Style Audit",
                    "No STING-prefixed tag families are loaded in this project.");
                return Result.Succeeded;
            }

            var expectedParams = TagFamilyConfig.TagParams
                .Concat(TagFamilyConfig.VisibilityParams)
                .Concat(TagFamilyConfig.StyleParams)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var expectedVariants = TagStyleCatalogue.EnumerateStandardVariants()
                .ToList();
            var expectedVariantNames = new HashSet<string>(
                expectedVariants.Select(v => v.CanonicalTypeName),
                StringComparer.OrdinalIgnoreCase);

            var rows = new List<List<string>>();
            int redRows = 0, amberRows = 0, greenRows = 0;
            var redFamilyNames = new List<string>();

            foreach (var fam in stingFamilies)
            {
                // 1. Param coverage — check the first FamilySymbol (all symbols share params).
                var firstSym = fam.GetFamilySymbolIds()
                    .Select(id => doc.GetElement(id) as FamilySymbol)
                    .FirstOrDefault(s => s != null);
                int missingParams = 0;
                if (firstSym != null)
                {
                    foreach (string pname in expectedParams)
                    {
                        if (firstSym.LookupParameter(pname) == null) missingParams++;
                    }
                }

                // 2. Variant coverage
                var actualTypeNames = fam.GetFamilySymbolIds()
                    .Select(id => (doc.GetElement(id) as FamilySymbol)?.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
                var actualSet = new HashSet<string>(actualTypeNames, StringComparer.OrdinalIgnoreCase);
                int missingVariants = expectedVariantNames.Count(ev => !actualSet.Contains(ev));
                int unexpectedVariants = actualTypeNames.Count(n =>
                    LooksLikeCanonicalVariant(n) && !expectedVariantNames.Contains(n));

                // 3. Duplicates (names must be unique in a Revit family but we still check)
                int duplicateCount = actualTypeNames
                    .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .Count(g => g.Count() > 1);

                // 4. Baseline discipline default
                string disc = InferDiscipline(fam);
                var disciplineDefault = TagStyleCatalogue.GetDisciplineDefault(disc);
                string expectedBaselineName = disciplineDefault.ToVariantSpec().CanonicalTypeName;

                // We cannot tell which symbol is "current" from the Family API alone,
                // so report the first canonical-looking type as the family's de facto baseline.
                string actualBaseline = actualTypeNames.FirstOrDefault(LooksLikeCanonicalVariant) ?? "(none)";

                int totalDeviation = missingParams + missingVariants + unexpectedVariants + duplicateCount +
                    (string.Equals(actualBaseline, expectedBaselineName, StringComparison.OrdinalIgnoreCase) ? 0 : 1);

                string status;
                if (totalDeviation == 0) { status = "GREEN"; greenRows++; }
                else if (totalDeviation <= 5) { status = "AMBER"; amberRows++; }
                else { status = "RED"; redRows++; redFamilyNames.Add(fam.Name); }

                rows.Add(new List<string>
                {
                    fam.Name,
                    missingParams.ToString(),
                    missingVariants.ToString(),
                    unexpectedVariants.ToString(),
                    expectedBaselineName,
                    actualBaseline,
                    status,
                });
            }

            // ── Excel report ──
            string xlsx = null;
            try
            {
                string outDir = OutputLocationHelper.GetOutputDirectory(doc);
                xlsx = Path.Combine(outDir, $"STING_StyleAudit_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                StingExcelExporter.ExportTable(
                    xlsx, "StyleAudit",
                    new List<string>
                    {
                        "Family", "MissingParams", "MissingVariants", "UnexpectedVariants",
                        "DisciplineDefault", "ActualDefault", "Status"
                    },
                    rows, openFolder: false);
            }
            catch (Exception ex) { StingLog.Warn($"StyleAudit Excel export: {ex.Message}"); }

            // ── Summary + offer to migrate ──
            var td = new TaskDialog("Style Audit");
            td.MainInstruction = $"{stingFamilies.Count} families audited";
            td.MainContent =
                $"GREEN: {greenRows}   AMBER: {amberRows}   RED: {redRows}\n\n" +
                (xlsx != null ? $"Report: {xlsx}\n\n" : "") +
                (redRows > 0
                    ? $"{redRows} families have deep deviations. Run Migrate Tag Families to fix them?"
                    : "All families are in reasonable shape.");
            if (redRows > 0)
            {
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                    "Run Migrate Tag Families now",
                    "Upgrades parameters and creates standard type variants.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                    "Just close",
                    "You can run Migrate Tag Families later from the TAGS panel.");
                td.CommonButtons = TaskDialogCommonButtons.Close;
            }
            else
            {
                td.CommonButtons = TaskDialogCommonButtons.Close;
            }

            var r = td.Show();
            if (r == TaskDialogResult.CommandLink1 && redRows > 0)
            {
                // Cannot invoke another IExternalCommand directly from inside this one —
                // hand the user a clear next step.
                TaskDialog.Show("Style Audit",
                    "Click 'Migrate Tag Families' on the TAGS → Automation panel to run it now.\n\n" +
                    $"Families flagged RED ({redFamilyNames.Count}):\n  • " +
                    string.Join("\n  • ", redFamilyNames.Take(20)) +
                    (redFamilyNames.Count > 20 ? $"\n  ... and {redFamilyNames.Count - 20} more" : ""));
            }

            StingLog.Info($"StyleAudit: green={greenRows}, amber={amberRows}, red={redRows}");
            return Result.Succeeded;
        }

        /// <summary>Infer discipline code from a STING tag family name ("STING - Walls Tag" → "A", etc.).</summary>
        private string InferDiscipline(Family fam)
        {
            string nm = fam.Name ?? "";
            string upper = nm.ToUpperInvariant();
            if (upper.Contains(" MECH") || upper.Contains(" DUCT") || upper.Contains(" HVAC") ||
                upper.Contains(" AIR TERMINAL") || upper.Contains(" FLEX DUCT")) return "M";
            if (upper.Contains(" ELEC") || upper.Contains(" CABLE TRAY") || upper.Contains(" CONDUIT") ||
                upper.Contains(" LIGHTING")) return "E";
            if (upper.Contains(" PIPE") || upper.Contains(" PLUMB") || upper.Contains(" SPRINK")) return "P";
            if (upper.Contains(" FIRE")) return "FP";
            if (upper.Contains(" COMMUN") || upper.Contains(" DATA") || upper.Contains(" SECURITY") ||
                upper.Contains(" NURSE CALL") || upper.Contains(" TELEPHONE") || upper.Contains(" AUDIO")) return "LV";
            if (upper.Contains(" STRUCT") || upper.Contains(" COLUMN") || upper.Contains(" BEAM") ||
                upper.Contains(" FOUND") || upper.Contains(" REBAR")) return "S";
            return "A";
        }

        /// <summary>True if the name matches the canonical "size_style_colour_arrow_tN" pattern.</summary>
        private static bool LooksLikeCanonicalVariant(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            int last = name.LastIndexOf('_');
            if (last < 0 || last == name.Length - 1) return false;
            string tail = name.Substring(last + 1);
            return tail.Length >= 2 && (tail[0] == 'T' || tail[0] == 't')
                && int.TryParse(tail.Substring(1), out _);
        }
    }
}
