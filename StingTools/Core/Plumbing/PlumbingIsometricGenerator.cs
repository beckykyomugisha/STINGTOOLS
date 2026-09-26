// PlumbingIsometricGenerator — draws a pipework isometric into a drafting view.
//
// Revit half of IsometricProjection: collects Pipe elements, groups them by
// piping system, projects each group isometrically and draws one drafting
// view per system ("STING ISO - <system>") with a detail line per pipe and a
// DN (+ fall for sloped pipes) label on each pipe long enough to carry one.
//
// Re-running is safe: an existing "STING ISO - <system>" view is cleared of
// the detail lines and text notes it owns and redrawn, so the view keeps its
// id (and any sheet placement) while its content tracks the model.
//
// The drawing is NOT TO SCALE — it is fitted to a paper box — and says so in
// its title note. Fittings are not drawn separately; pipes meet at their
// connector ends, which is how the model already joins them.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using StingTools.Core;

namespace StingTools.Core.Plumbing
{
    public class PlumbingIsometricOptions
    {
        public const string ViewPrefix = "STING ISO - ";

        /// <summary>Drafting-view scale (1:N). Drives text size on paper.</summary>
        public int    ViewScale          { get; set; } = 50;
        /// <summary>Larger paper extent of the drawing, mm (fits inside an A1 drawable zone).</summary>
        public double FitToPaperMm       { get; set; } = 550;
        /// <summary>Pipes shorter than this on paper are drawn but not labelled, mm.</summary>
        public double MinLabelledPaperMm { get; set; } = 12;
        /// <summary>Label offset from the pipe, on paper, mm.</summary>
        public double LabelOffsetPaperMm { get; set; } = 2.5;
        /// <summary>Text height used to choose a text type, on paper, mm.</summary>
        public double TextHeightMm       { get; set; } = 2.5;
        /// <summary>Only draw systems whose name contains this (case-insensitive). Empty = all.</summary>
        public string SystemFilter       { get; set; } = "";
        /// <summary>Pipes with less fall than this are treated as level (no fall label), %.</summary>
        public double MinSlopePct        { get; set; } = 0.05;
    }

    public class PlumbingIsometricViewResult
    {
        public string    SystemName   { get; set; } = "";
        public ElementId ViewId       { get; set; } = ElementId.InvalidElementId;
        public string    ViewName     { get; set; } = "";
        public bool      Reused       { get; set; }
        public int       PipesDrawn   { get; set; }
        public int       Labels       { get; set; }
        public int       Risers       { get; set; }
    }

    public class PlumbingIsometricResult
    {
        public List<PlumbingIsometricViewResult> Views { get; } = new List<PlumbingIsometricViewResult>();
        public List<string> Warnings { get; } = new List<string>();
        public int PipesConsidered { get; set; }
        public int PipesSkipped    { get; set; }
    }

    public static class PlumbingIsometricGenerator
    {
        private const double MmToFt = 1.0 / 304.8;
        private const string NoSystem = "(no system)";

        /// <summary>Caller owns the transaction.</summary>
        public static PlumbingIsometricResult Generate(Document doc, IEnumerable<Pipe> pipes,
            PlumbingIsometricOptions opts)
        {
            opts = opts ?? new PlumbingIsometricOptions();
            var result = new PlumbingIsometricResult();
            if (doc == null) { result.Warnings.Add("No document."); return result; }

            var vft = DrainageSchematicGenerator.FindDraftingViewType(doc);
            if (vft == null)
            {
                result.Warnings.Add("The project has no Drafting view type — cannot create an isometric view.");
                return result;
            }

            var inputs = new List<IsoSegmentInput>();
            foreach (var pipe in pipes ?? Enumerable.Empty<Pipe>())
            {
                result.PipesConsidered++;
                var seg = ToSegment(pipe, opts, result.Warnings);
                if (seg == null) { result.PipesSkipped++; continue; }
                if (!string.IsNullOrWhiteSpace(opts.SystemFilter) &&
                    seg.Group.IndexOf(opts.SystemFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    result.PipesSkipped++;
                    continue;
                }
                inputs.Add(seg);
            }
            if (inputs.Count == 0)
            {
                result.Warnings.Add("No pipes with a straight location curve in scope.");
                return result;
            }

            double k = opts.ViewScale > 0 ? opts.ViewScale : 50;
            var projOpts = new IsoProjectionOptions
            {
                FitTo             = opts.FitToPaperMm * k * MmToFt,
                MinLabelledLength = opts.MinLabelledPaperMm * k * MmToFt,
                LabelOffset       = opts.LabelOffsetPaperMm * k * MmToFt
            };
            var textTypeId = FindTextType(doc, opts.TextHeightMm);

            foreach (var grp in inputs.GroupBy(i => i.Group).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var vr = DrawSystem(doc, vft, grp.Key, grp.ToList(), projOpts, opts, textTypeId, result.Warnings);
                    if (vr != null) result.Views.Add(vr);
                }
                catch (Exception ex)
                {
                    result.Warnings.Add($"System '{grp.Key}': {ex.Message}");
                    StingLog.Error($"PlumbingIsometricGenerator system '{grp.Key}'", ex);
                }
            }
            return result;
        }

        private static IsoSegmentInput ToSegment(Pipe pipe, PlumbingIsometricOptions opts, List<string> warnings)
        {
            if (pipe == null) return null;
            try
            {
                if (!(pipe.Location is LocationCurve lc) || !(lc.Curve is Line line)) return null;
                var a = line.GetEndPoint(0);
                var b = line.GetEndPoint(1);
                string sys = pipe.MEPSystem?.Name;
                if (string.IsNullOrWhiteSpace(sys))
                    sys = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString();
                if (string.IsNullOrWhiteSpace(sys)) sys = NoSystem;

                return new IsoSegmentInput
                {
                    Id    = pipe.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Start = new IsoPoint3(a.X, a.Y, a.Z),
                    End   = new IsoPoint3(b.X, b.Y, b.Z),
                    Group = sys.Trim(),
                    Label = LabelFor(pipe, a, b, opts)
                };
            }
            catch (Exception ex)
            {
                warnings.Add($"Pipe {pipe.Id}: {ex.Message}");
                return null;
            }
        }

        /// <summary>"DN50" and, for a pipe with fall, "DN100 1:40".</summary>
        private static string LabelFor(Pipe pipe, XYZ a, XYZ b, PlumbingIsometricOptions opts)
        {
            string dn = "";
            try
            {
                double dMm = pipe.Diameter / MmToFt;
                if (dMm > 0) dn = $"DN{Math.Round(dMm):F0}";
            }
            catch (Exception ex) { StingLog.Warn($"Iso label diameter {pipe.Id}: {ex.Message}"); }

            double run = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            double rise = Math.Abs(b.Z - a.Z);
            if (run > 1e-6 && rise > 1e-9)
            {
                double pct = rise / run * 100.0;
                // A riser/drop (mostly vertical) is not a "fall"; only label
                // gently sloped runs, which are the ones a fitter must set out.
                if (pct >= opts.MinSlopePct && pct <= 25.0)
                    return (dn + $" 1:{Math.Round(run / rise):F0}").Trim();
            }
            return dn;
        }

        private static PlumbingIsometricViewResult DrawSystem(Document doc, ViewFamilyType vft,
            string systemName, List<IsoSegmentInput> segs, IsoProjectionOptions projOpts,
            PlumbingIsometricOptions opts, ElementId textTypeId, List<string> warnings)
        {
            string viewName = PlumbingIsometricOptions.ViewPrefix + SanitiseViewName(systemName);
            var vr = new PlumbingIsometricViewResult { SystemName = systemName, ViewName = viewName };

            var view = new FilteredElementCollector(doc).OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>()
                .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, viewName, StringComparison.OrdinalIgnoreCase));
            if (view != null)
            {
                ClearOwnedAnnotation(doc, view, warnings);
                vr.Reused = true;
            }
            else
            {
                view = ViewDrafting.Create(doc, vft.Id);
                try { view.Name = viewName; }
                catch (Exception ex) { warnings.Add($"Could not name view '{viewName}': {ex.Message}"); }
            }
            try { view.Scale = opts.ViewScale > 0 ? opts.ViewScale : 50; }
            catch (Exception ex) { warnings.Add($"View scale: {ex.Message}"); }
            vr.ViewId = view.Id;
            vr.ViewName = view.Name;

            var proj = IsometricProjection.Project(segs, projOpts);
            foreach (var s in proj.Segments)
            {
                var p0 = new XYZ(s.Start.U, s.Start.V, 0);
                var p1 = new XYZ(s.End.U, s.End.V, 0);
                if (p0.DistanceTo(p1) < doc.Application.ShortCurveTolerance) continue;
                try
                {
                    doc.Create.NewDetailCurve(view, Line.CreateBound(p0, p1));
                    vr.PipesDrawn++;
                    if (s.IsVertical) vr.Risers++;
                }
                catch (Exception ex) { warnings.Add($"Pipe {s.Id}: detail line failed — {ex.Message}"); continue; }

                if (!s.ShowLabel || textTypeId == ElementId.InvalidElementId) continue;
                try
                {
                    TextNote.Create(doc, view.Id, new XYZ(s.LabelAnchor.U, s.LabelAnchor.V, 0), s.Label, textTypeId);
                    vr.Labels++;
                }
                catch (Exception ex) { warnings.Add($"Pipe {s.Id}: label failed — {ex.Message}"); }
            }

            if (textTypeId != ElementId.InvalidElementId)
            {
                double k = opts.ViewScale > 0 ? opts.ViewScale : 50;
                var titleAt = new XYZ(0, proj.MaxV + 12 * k * MmToFt, 0);
                string title = $"ISOMETRIC — {systemName}   (NOT TO SCALE · {vr.PipesDrawn} pipes · " +
                               $"generated {DateTime.Now:yyyy-MM-dd})";
                try { TextNote.Create(doc, view.Id, titleAt, title, textTypeId); }
                catch (Exception ex) { warnings.Add($"Title note: {ex.Message}"); }
            }
            else warnings.Add("No text note type in the project — pipes drawn without labels.");

            return vr;
        }

        /// <summary>Delete the detail curves and text notes a previous run left in the view.</summary>
        private static void ClearOwnedAnnotation(Document doc, View view, List<string> warnings)
        {
            try
            {
                var ids = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .Where(e => e is DetailCurve || e is TextNote)
                    .Select(e => e.Id)
                    .ToList();
                if (ids.Count > 0) doc.Delete(ids);
            }
            catch (Exception ex) { warnings.Add($"Clearing '{view.Name}': {ex.Message}"); }
        }

        private static ElementId FindTextType(Document doc, double heightMm)
        {
            try
            {
                double target = heightMm * MmToFt;
                var best = new FilteredElementCollector(doc).OfClass(typeof(TextNoteType)).Cast<TextNoteType>()
                    .OrderBy(t =>
                    {
                        double h = t.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0;
                        return Math.Abs(h - target);
                    })
                    .FirstOrDefault();
                if (best != null) return best.Id;
                return doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"PlumbingIsometricGenerator.FindTextType: {ex.Message}");
                return ElementId.InvalidElementId;
            }
        }

        /// <summary>Revit view names cannot contain {}[]|;:\&lt;&gt;?`~ .</summary>
        internal static string SanitiseViewName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return NoSystem;
            var bad = "{}[]|;:\\<>?`~";
            var chars = s.Select(c => bad.IndexOf(c) >= 0 ? '-' : c).ToArray();
            return new string(chars).Trim();
        }
    }
}
