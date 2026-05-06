// StingTools — Drawing Template Manager · Phase 175
//
// DrainageInvertDimensioner places SpotElevation annotations at pipe
// invert levels (bottom-of-pipe centreline minus radius) — required by
// BS EN 12056 / Approved Document H for above-ground drainage and the
// equivalent of an "invert level" callout no commercial Revit tool
// auto-places today.
//
// Triggered by AutoAnnotationRule.RuleType == "AutoSpotInvert" with
// rule.Category narrowing to a pipe class (default: every Pipe Curve
// with a flow type tagged DRAINAGE / WASTE / RAIN / FOUL). When a
// rule.SymbolFamily is supplied we use that SpotDimensionType,
// otherwise the first project-loaded spot-elevation type wins.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace StingTools.Core.Drawing.Dimensioning
{
    internal static class DrainageInvertDimensioner
    {
        // System type names + classifications that should receive an
        // invert-level callout when a pack rule says "AutoSpotInvert"
        // without narrowing to a specific category.
        private static readonly HashSet<string> DrainageHints =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Sanitary", "SAN", "Soil", "Waste", "WST",
                "Vent", "Foul", "Storm", "Rainwater", "RWP", "RWD",
                "Drainage", "Drain"
            };

        public static void Run(Document doc, View view, AnnotationRulePack pack,
            AutoAnnotationRule rule, AnnotationResult result)
        {
            if (!GridDimensioner.IsDimensionable(view))
            {
                result.Warnings.Add($"AutoSpotInvert: view '{view?.Name}' is not 2D — skipped.");
                return;
            }

            var pipes = CollectDrainagePipes(doc, view, rule);
            if (pipes.Count == 0) return;

            var symId = ResolveSpotSymbolId(doc, rule?.TagFamily);

            foreach (var p in pipes)
            {
                try { EmitInvertSpot(doc, view, p, symId, result); }
                catch (Exception ex) { result.Warnings.Add($"AutoSpotInvert {p.Id}: {ex.Message}"); }
            }
        }

        private static List<Pipe> CollectDrainagePipes(Document doc, View view, AutoAnnotationRule rule)
        {
            var col = new FilteredElementCollector(doc, view.Id)
                .OfCategory(BuiltInCategory.OST_PipeCurves)
                .WhereElementIsNotElementType()
                .OfClass(typeof(Pipe))
                .Cast<Pipe>()
                .Where(p => p != null);

            // If the caller pinned a category other than Pipes, honour it
            // by returning an empty list — the rule was misconfigured and
            // the runner already emits a sensible warning elsewhere.
            if (!string.IsNullOrEmpty(rule?.Category) && !rule.Category.Equals("*", StringComparison.Ordinal))
            {
                if (Enum.TryParse<BuiltInCategory>(rule.Category, true, out var bic)
                    && bic != BuiltInCategory.OST_PipeCurves)
                    return new List<Pipe>();
            }

            // Filter to drainage by MEP system type / system classification.
            return col.Where(IsDrainagePipe).ToList();
        }

        private static bool IsDrainagePipe(Pipe p)
        {
            try
            {
                var sys = p.MEPSystem;
                if (sys != null)
                {
                    // Match by system name + type name — these survive every
                    // Revit version, unlike the Classification enum which has
                    // shifted between releases. Authors get the same out-of
                    // -box behaviour by naming systems "Sanitary", "Storm",
                    // "Vent", etc.
                    if (DrainageHints.Contains(sys.Name ?? "")) return true;
                    var typeEl = p.Document?.GetElement(sys.GetTypeId());
                    if (typeEl != null && DrainageHints.Contains(typeEl.Name ?? ""))
                        return true;
                }
                // Fallback: read the STING SYS token when authors haven't
                // configured a Revit system but have tagged the pipe.
                var sysCode = p.LookupParameter("ASS_SYSTEM_TYPE_TXT")?.AsString();
                if (!string.IsNullOrEmpty(sysCode) && DrainageHints.Contains(sysCode)) return true;
            }
            catch { }
            return false;
        }

        private static ElementId ResolveSpotSymbolId(Document doc, string preferredName)
        {
            try
            {
                var all = new FilteredElementCollector(doc)
                    .OfClass(typeof(SpotDimensionType))
                    .Cast<SpotDimensionType>()
                    .ToList();
                if (!string.IsNullOrEmpty(preferredName))
                {
                    var named = all.FirstOrDefault(t =>
                        string.Equals(t.Name, preferredName, StringComparison.OrdinalIgnoreCase));
                    if (named != null) return named.Id;
                }
                // Prefer types whose name suggests "Elevation" / "Invert".
                var elev = all.FirstOrDefault(t =>
                    (t.Name ?? "").IndexOf("invert", StringComparison.OrdinalIgnoreCase) >= 0);
                if (elev != null) return elev.Id;
                elev = all.FirstOrDefault(t =>
                    (t.Name ?? "").IndexOf("elev", StringComparison.OrdinalIgnoreCase) >= 0);
                if (elev != null) return elev.Id;
                return all.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
            }
            catch { return ElementId.InvalidElementId; }
        }

        private static void EmitInvertSpot(Document doc, View view, Pipe pipe,
            ElementId spotTypeId, AnnotationResult result)
        {
            if (!(pipe.Location is LocationCurve lc) || lc.Curve == null) return;

            var midPt = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) * 0.5;
            // Invert = centreline Z - radius. SpotElevation reads Z off
            // the picked face/reference, so dropping origin to invert
            // before NewSpotElevation gets the right number even when the
            // reference is the centreline.
            double radius = 0;
            try { radius = pipe.Diameter * 0.5; } catch { }
            var invert = new XYZ(midPt.X, midPt.Y, midPt.Z - radius);

            // Bend / end / refPt offsets — small in-plane offsets so the
            // leader sits clear of the centreline.
            var bend = invert + new XYZ(1.0, 1.0, 0);
            var end  = invert + new XYZ(2.0, 1.0, 0);
            var refPt = invert;

            Reference cref = lc.Curve.Reference ?? new Reference(pipe);
            try
            {
                var sd = doc.Create.NewSpotElevation(view, cref, invert, bend, end, refPt, hasLeader: true);
                if (sd != null && spotTypeId != ElementId.InvalidElementId)
                {
                    try { sd.ChangeTypeId(spotTypeId); } catch { }
                }
                if (sd != null) result.SpotsPlaced++;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"NewSpotElevation pipe {pipe.Id}: {ex.Message}");
            }
        }
    }
}
