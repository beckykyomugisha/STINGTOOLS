// StingTools — Drawing Template Manager · Week 5
//
// GenerateFromScopeBoxesCommand: the single "Generate" button that
// walks every scope box named with the STING::<drawing-type> magic
// pattern, creates a view of the matching ViewFamily per box,
// assigns the scope box as crop, applies the profile's scale /
// template / style pack / annotation, and reports what was created
// vs updated vs skipped.
//
// Idempotent by design — a re-run only touches views that don't
// already exist for that (DrawingType, scope box) pair.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.Commands.Drawing
{
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class GenerateFromScopeBoxesCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string msg, ElementSet els)
        {
            try
            {
                var doc = data?.Application?.ActiveUIDocument?.Document;
                if (doc == null) { msg = "No document open."; return Result.Failed; }

                var bindings = ScopeBoxBinder.ScanProject(doc);
                if (bindings.Count == 0)
                {
                    TaskDialog.Show("STING — Generate from Scope Boxes",
                        "No scope boxes matching the STING::<drawing-type> pattern.\n\n" +
                        "Rename a scope box to e.g.\n" +
                        "   STING::arch-plan-A1-1to100::L02\n" +
                        "and re-run.");
                    return Result.Succeeded;
                }

                int created = 0, updated = 0, skipped = 0;
                var warnings = new List<string>();

                using (var tx = new Transaction(doc, "STING — Generate from Scope Boxes"))
                {
                    tx.Start();

                    foreach (var b in bindings)
                    {
                        var dt = DrawingTypeRegistry.Get(doc, b.DrawingTypeId);
                        if (dt == null)
                        {
                            warnings.Add($"'{b.ScopeBox.Name}' → unknown DrawingType '{b.DrawingTypeId}'");
                            skipped++;
                            continue;
                        }

                        try
                        {
                            var existing = ScopeBoxBinder.FindExistingView(doc, b);
                            if (existing != null)
                            {
                                DrawingTypePresentation.Apply(doc, existing, dt, runAnnotation: false);
                                updated++;
                                continue;
                            }

                            var v = CreateView(doc, dt, b, warnings);
                            if (v == null) { skipped++; continue; }
                            DrawingTypePresentation.Apply(doc, v, dt);
                            created++;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"'{b.ScopeBox.Name}' → {ex.Message}");
                            skipped++;
                        }
                    }

                    tx.Commit();
                }

                var sb = new StringBuilder();
                sb.AppendLine($"Scope boxes scanned: {bindings.Count}");
                sb.AppendLine($"  Views created:  {created}");
                sb.AppendLine($"  Views updated:  {updated}");
                sb.AppendLine($"  Skipped:        {skipped}");
                if (warnings.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Warnings:");
                    foreach (var w in warnings.Take(15)) sb.AppendLine("  " + w);
                    if (warnings.Count > 15) sb.AppendLine($"  …({warnings.Count - 15} more)");
                }
                TaskDialog.Show("STING — Generate from Scope Boxes", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                StingLog.Error("GenerateFromScopeBoxes", ex);
                msg = ex.Message;
                return Result.Failed;
            }
        }

        private static View CreateView(Document doc, DrawingType dt, ScopeBoxBinding b, List<string> warnings)
        {
            var family = FamilyForPurpose(dt.Purpose);
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == family);
            if (vft == null)
            {
                warnings.Add($"No ViewFamilyType for purpose '{dt.Purpose}'");
                return null;
            }

            View v = null;
            try
            {
                switch (family)
                {
                    case ViewFamily.FloorPlan:
                    case ViewFamily.CeilingPlan:
                    case ViewFamily.StructuralPlan:
                    case ViewFamily.AreaPlan:
                        {
                            var level = ResolveLevel(doc, b.LevelCode);
                            if (level == null) { warnings.Add($"No Level matches '{b.LevelCode ?? "(unset)"}'"); return null; }
                            v = ViewPlan.Create(doc, vft.Id, level.Id);
                            break;
                        }
                    case ViewFamily.ThreeDimensional:
                        v = View3D.CreateIsometric(doc, vft.Id);
                        break;
                    default:
                        warnings.Add($"Purpose '{dt.Purpose}' does not auto-create from scope box; use BatchSections/Elevations instead.");
                        return null;
                }
                if (v == null) return null;

                // Name it something recognisable and bind the scope box
                var safeName = $"STING - {dt.Id} - {b.ScopeBox.Name}";
                foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
                    safeName = safeName.Replace(ch, '_');
                try { v.Name = UniqueViewName(doc, safeName); } catch { }

                var sbParam = v.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                if (sbParam != null && !sbParam.IsReadOnly)
                    sbParam.Set(b.ScopeBox.Id);

                return v;
            }
            catch (Exception ex)
            {
                warnings.Add($"CreateView('{dt.Id}'): {ex.Message}");
                return null;
            }
        }

        private static ViewFamily FamilyForPurpose(string purpose)
        {
            switch (purpose)
            {
                case DrawingPurpose.Rcp:          return ViewFamily.CeilingPlan;
                case DrawingPurpose.ThreeD:       return ViewFamily.ThreeDimensional;
                case DrawingPurpose.Section:      return ViewFamily.Section;
                case DrawingPurpose.Elevation:    return ViewFamily.Elevation;
                case DrawingPurpose.Detail:       return ViewFamily.Detail;
                case DrawingPurpose.Schedule:     return ViewFamily.Schedule;
                case DrawingPurpose.Legend:       return ViewFamily.Legend;
                default:                          return ViewFamily.FloorPlan;
            }
        }

        private static Level ResolveLevel(Document doc, string levelCode)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            if (levels.Count == 0) return null;
            if (string.IsNullOrWhiteSpace(levelCode)) return levels.OrderBy(l => l.Elevation).FirstOrDefault();

            // Exact name match first, then case-insensitive contains,
            // then lowest-elevation fallback.
            return levels.FirstOrDefault(l => string.Equals(l.Name, levelCode, StringComparison.OrdinalIgnoreCase))
                ?? levels.FirstOrDefault(l => l.Name?.IndexOf(levelCode, StringComparison.OrdinalIgnoreCase) >= 0)
                ?? levels.OrderBy(l => l.Elevation).FirstOrDefault();
        }

        private static string UniqueViewName(Document doc, string baseName)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var el in new FilteredElementCollector(doc).OfClass(typeof(View)))
                if (el is View vv) existing.Add(vv.Name);
            if (!existing.Contains(baseName)) return baseName;
            for (int i = 2; i < 1000; i++)
            {
                var c = baseName + " (" + i + ")";
                if (!existing.Contains(c)) return c;
            }
            return baseName + "-" + Guid.NewGuid().ToString("N").Substring(0, 4);
        }
    }
}
