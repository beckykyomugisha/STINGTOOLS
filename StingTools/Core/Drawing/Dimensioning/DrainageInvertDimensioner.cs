// StingTools — Drawing Template Manager · Phase 175
//
// DrainageInvertDimensioner labels drainage pipes with their invert levels
// (IL at each end, bore invert = centreline minus INTERNAL radius) and the
// gradient between — required by BS EN 12056 / Approved Document H.
//
// Triggered by AutoAnnotationRule.RuleType == "AutoSpotInvert" with
// rule.Category narrowing to a pipe class (default: every Pipe Curve
// with a flow type tagged DRAINAGE / WASTE / RAIN / FOUL). When a
// Placement is text, not a spot elevation — see "Placement" below for why.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core.Drawing;
using StingTools.Core;
using StingTools.Core.Plumbing;

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
                result.Warnings.Add(
                    $"AutoSpotInvert: view '{view?.Name}' is not 2D — skipped. " +
                    "Spot elevations need a section / plan view; use the drainage section associated with this run.");
                return;
            }

            var pipes = CollectDrainagePipes(doc, view, rule);
            if (pipes.Count == 0) return;

            PlaceInvertNotes(doc, view, pipes, result);
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
            catch (Exception ex) { StingLog.Warn($"Suppressed: {ex.Message}"); }
            return false;
        }

        // ── Placement ──────────────────────────────────────────────────────
        //
        // WHY TEXT, NOT A SPOT ELEVATION. A spot elevation reports the elevation
        // of the reference it is hosted on; on a pipe that is the centreline (or,
        // with "Display Elevations = Bottom", the OUTSIDE bottom). No setting
        // makes it report the bore invert, and moving its anchor point down by a
        // radius — what this used to do — changes where it points, not what it
        // says. So the IL is computed (InvertMath, via PipeInvert) and written as
        // text: "IL 9.95" at each end and the gradient "1:80" between, which is
        // what a drainage drawing shows.
        //
        // Text is not parametric, so a re-run UPDATES the notes it finds at the
        // same anchors instead of adding new ones — the values follow the model
        // every time the drawing is produced. A pipe that has moved since leaves
        // its old notes behind; they are recognisable ("IL …" / "1:…") and the
        // run says how many it could not match.

        private static void PlaceInvertNotes(Document doc, View view, List<Pipe> pipes, AnnotationResult result)
        {
            var opts = IlReportingOptions.Default;
            var noteType = doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            if (noteType == ElementId.InvalidElementId)
            {
                result.Warnings.Add("AutoSpotInvert: project has no default text note type — no invert levels placed.");
                return;
            }

            double paperFt = Math.Max(1, view.Scale) / 304.8;       // 1 mm on paper, in model feet
            double offset = 3.0 * paperFt;                            // label clear of the pipe
            double tol = 1.5 * paperFt;                               // "same anchor" on a re-run
            var n = view.ViewDirection;
            var o = view.Origin;
            var up = view.UpDirection;

            var existing = new List<TextNote>();
            try
            {
                foreach (var tn in new FilteredElementCollector(doc, view.Id).OfClass(typeof(TextNote)).Cast<TextNote>())
                    if (InvertMath.IsOurNote(tn.Text)) existing.Add(tn);
            }
            catch (Exception ex) { result.Warnings.Add($"AutoSpotInvert: could not read existing notes ({ex.Message}); duplicates possible."); }
            var claimed = new HashSet<ElementId>();

            int placed = 0, updated = 0, nominal = 0, levelPipes = 0, crossChecks = 0;
            foreach (var p in pipes)
            {
                try
                {
                    var r = PipeInvert.Compute(doc, p, opts, out var why);
                    if (r == null) { result.Warnings.Add($"AutoSpotInvert {p.Id}: no invert — {why}."); continue; }
                    if (r.Source == InvertSource.NominalFallback) nominal++;
                    if (r.IsLevel) levelPipes++;
                    if (r.CrossCheckNote != null && crossChecks++ < 3)
                        result.Warnings.Add($"AutoSpotInvert {p.Id}: {r.CrossCheckNote} Check before issue.");

                    XYZ Anchor(XYZ pt) => Project(pt, o, n) + up * offset;

                    Upsert(Anchor(r.UpPoint), InvertMath.FormatIl(r.UpInvertM, opts.Decimals));
                    if (!r.IsLevel)
                        Upsert(Anchor(r.DownPoint), InvertMath.FormatIl(r.DownInvertM, opts.Decimals));
                    if (r.Gradient != null)
                        Upsert(Anchor((r.UpPoint + r.DownPoint) * 0.5), r.Gradient);
                }
                catch (Exception ex) { result.Warnings.Add($"AutoSpotInvert {p.Id}: {ex.Message}"); }
            }

            void Upsert(XYZ at, string text)
            {
                TextNote hit = null;
                double best = tol;
                foreach (var tn in existing)
                {
                    if (claimed.Contains(tn.Id)) continue;
                    double d = tn.Coord.DistanceTo(at);
                    if (d <= best) { best = d; hit = tn; }
                }
                if (hit != null)
                {
                    claimed.Add(hit.Id);
                    if (!string.Equals(hit.Text?.Trim(), text, StringComparison.Ordinal)) { hit.Text = text; updated++; }
                    return;
                }
                var created = TextNote.Create(doc, view.Id, at, text, noteType);
                if (created != null) { claimed.Add(created.Id); placed++; result.SpotsPlaced++; }
            }

            if (updated > 0)
                result.Warnings.Add($"AutoSpotInvert: {updated} existing invert/gradient note(s) updated to the current model.");
            int orphans = existing.Count(tn => !claimed.Contains(tn.Id));
            if (orphans > 0)
                result.Warnings.Add($"AutoSpotInvert: {orphans} older IL/gradient note(s) in '{view.Name}' no longer sit on a pipe end " +
                                    "(the pipe moved or was deleted). They were left in place — review and delete.");
            if (nominal > 0)
                result.Warnings.Add($"AutoSpotInvert: {nominal} pipe(s) carry no internal diameter; their ILs use the NOMINAL size " +
                                    "and may be a few mm out. Set the pipe type's segment sizes.");
            if (levelPipes > 0)
                result.Warnings.Add($"AutoSpotInvert: {levelPipes} drainage pipe(s) are LEVEL — no gradient, one IL each. A level drain will not self-cleanse.");
            if (placed + updated > 0)
                StingLog.Info($"AutoSpotInvert '{view.Name}': {placed} placed, {updated} updated; datum {opts.DatumLabel}, {opts.Decimals} dp.");
        }

        private static XYZ Project(XYZ p, XYZ origin, XYZ normal)
            => p - normal * (p - origin).DotProduct(normal);
    }
}
