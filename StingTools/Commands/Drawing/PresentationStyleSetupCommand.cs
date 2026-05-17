// StingTools — Presentation Style Setup
//
// Idempotent project-side setup for the pres-* ViewStylePack family.
// The pres-* packs reference three custom model fill patterns and three
// subcategories that do not ship with stock Revit. Running this command
// creates them — re-running is safe (each creation step checks for an
// existing element by name first and skips with StingLog.Warn).
//
//   Fill patterns (model, ToView orientation):
//     STING-Pres-Herringbone        — 45° + 135° crossed line pair @ 100mm
//     STING-Pres-HorizontalCladding — single 0° line family @ 50mm
//     STING-Pres-RoofHatch          — 45° + 135° crosshatch @ 75mm
//
//   Entourage subcategories:
//     STING-LargeTree   — large solid-fill trees (tall conifers, feature trees)
//     STING-SmallShrub  — small outline-only shrubs / low planting
//
//   Generic Models subcategory:
//     STING-PresGround  — extruded ground base slab element
//
// All operations run inside one Transaction so partial failure does not
// leave the project half-set-up.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class PresentationStyleSetupCommand : IExternalCommand
    {
        private const double MmToFeet = 1.0 / 304.8;
        private const double DegToRad = Math.PI / 180.0;

        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    TaskDialog.Show("STING — Presentation Setup", "No active document.");
                    return Result.Failed;
                }

                int patternsCreated = 0, patternsSkipped = 0;
                int subCatsCreated = 0,  subCatsSkipped = 0;
                var warnings = new List<string>();

                using (var tx = new Transaction(doc, "STING Presentation Setup"))
using StingTools.Core.Drawing;
                {
                    tx.Start();

                    // ── Fill patterns ──
                    var existingPatterns = new HashSet<string>(
                        new FilteredElementCollector(doc)
                            .OfClass(typeof(FillPatternElement))
                            .Cast<FillPatternElement>()
                            .Select(p => p.Name),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var def in PatternDefs)
                    {
                        if (existingPatterns.Contains(def.Name))
                        {
                            patternsSkipped++;
                            StingLog.Warn($"PresentationSetup: fill pattern '{def.Name}' already exists — skipped.");
                            continue;
                        }
                        try
                        {
                            CreateFillPattern(doc, def);
                            patternsCreated++;
                            StingLog.Info($"PresentationSetup: created fill pattern '{def.Name}'.");
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Pattern '{def.Name}': {ex.Message}");
                            StingLog.Error($"PresentationSetup: pattern '{def.Name}'", ex);
                        }
                    }

                    // ── Entourage subcategories ──
                    var entourage = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Entourage);
                    foreach (var sub in EntourageSubCats)
                    {
                        var (created, skipped, err) = EnsureSubCategory(doc, entourage, sub.Name, sub.Color);
                        if (created) subCatsCreated++;
                        if (skipped) subCatsSkipped++;
                        if (err != null) warnings.Add(err);
                    }

                    // ── Generic Models subcategory ──
                    var generic = doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);
                    var grnd = EnsureSubCategory(doc, generic, "STING-PresGround", new Color(120, 120, 120));
                    if (grnd.created) subCatsCreated++;
                    if (grnd.skipped) subCatsSkipped++;
                    if (grnd.err != null) warnings.Add(grnd.err);

                    tx.Commit();
                }

                var msgBody =
                    $"Fill patterns: {patternsCreated} created, {patternsSkipped} skipped.\n" +
                    $"Subcategories: {subCatsCreated} created, {subCatsSkipped} skipped.";
                if (warnings.Count > 0)
                    msgBody += "\n\nWarnings:\n  " + string.Join("\n  ", warnings);
                TaskDialog.Show("STING — Presentation Setup", msgBody);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("PresentationStyleSetupCommand", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        // ── Fill pattern definitions ──
        // Each definition is two FillGrid pairs (angle/spacing). spacing2Mm == 0
        // means "single family" — the second grid is omitted.

        private struct PatternDef
        {
            public string Name;
            public FillPatternHostOrientation Orientation;
            public double Angle1Deg;
            public double Spacing1Mm;
            public double Angle2Deg;
            public double Spacing2Mm;
        }

        private static readonly PatternDef[] PatternDefs = new[]
        {
            // 45° + 135° crossed line pair, 100mm spacing — herringbone-feel weave
            new PatternDef {
                Name = "STING-Pres-Herringbone",
                Orientation = FillPatternHostOrientation.ToView,
                Angle1Deg = 45,  Spacing1Mm = 100,
                Angle2Deg = 135, Spacing2Mm = 100,
            },
            // Single horizontal line family, 50mm spacing — cladding boards
            new PatternDef {
                Name = "STING-Pres-HorizontalCladding",
                Orientation = FillPatternHostOrientation.ToView,
                Angle1Deg = 0,   Spacing1Mm = 50,
                Angle2Deg = 0,   Spacing2Mm = 0,
            },
            // 45° + 135° crosshatch, 75mm spacing — roof tile texture
            new PatternDef {
                Name = "STING-Pres-RoofHatch",
                Orientation = FillPatternHostOrientation.ToView,
                Angle1Deg = 45,  Spacing1Mm = 75,
                Angle2Deg = 135, Spacing2Mm = 75,
            },
        };

        private static void CreateFillPattern(Document doc, PatternDef def)
        {
            var grids = new List<FillGrid>();
            double s1 = def.Spacing1Mm * MmToFeet;
            double s2 = def.Spacing2Mm * MmToFeet;
            if (s1 > 0)
                grids.Add(new FillGrid { Angle = def.Angle1Deg * DegToRad, Offset = s1 });
            if (s2 > 0)
                grids.Add(new FillGrid { Angle = def.Angle2Deg * DegToRad, Offset = s2 });
            if (grids.Count == 0) return;

            var pattern = new FillPattern
            {
                Target = FillPatternTarget.Model,
                HostOrientation = def.Orientation,
                Name = def.Name,
            };
            pattern.SetFillGrids(grids);
            FillPatternElement.Create(doc, pattern);
        }

        // ── Subcategory definitions ──

        private struct SubCatDef
        {
            public string Name;
            public Color Color;
        }

        private static readonly SubCatDef[] EntourageSubCats = new[]
        {
            // Large feature trees / tall conifers — solid-fill silhouettes
            new SubCatDef { Name = "STING-LargeTree",  Color = new Color(120, 120, 120) },
            // Small shrubs / low planting — outline only
            new SubCatDef { Name = "STING-SmallShrub", Color = new Color(120, 120, 120) },
        };

        /// <summary>
        /// Idempotent subcategory creation. Returns flags describing which
        /// branch fired so the caller can keep accurate counters and
        /// surface per-row warnings.
        /// </summary>
        private static (bool created, bool skipped, string err) EnsureSubCategory(
            Document doc, Category parent, string name, Color color)
        {
            if (parent == null)
                return (false, false, $"Subcategory '{name}': parent category missing.");
            try
            {
                foreach (Category existing in parent.SubCategories)
                {
                    if (string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        StingLog.Warn($"PresentationSetup: subcategory '{name}' already exists — skipped.");
                        return (false, true, null);
                    }
                }
                Category sub = doc.Settings.Categories.NewSubcategory(parent, name);
                try { sub.LineColor = color; } catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
                StingLog.Info($"PresentationSetup: created subcategory '{name}'.");
                return (true, false, null);
            }
            catch (Exception ex)
            {
                StingLog.Error($"PresentationSetup: subcategory '{name}'", ex);
                return (false, false, $"Subcategory '{name}': {ex.Message}");
            }
        }
    }
}
