// IsometricProjection — Revit-free geometry for pipework isometrics.
//
// Projects 3D pipe segments onto a 2D sheet plane using a true isometric
// (30° axes, equal foreshortening on X, Y and Z), fits the result to a
// target paper box, and chooses where each segment's label goes.
//
// Kept free of the Revit API so the maths is unit-tested
// (StingTools.Mep.Tests). PlumbingIsometricGenerator is the Revit half: it
// collects pipes, calls Project(), and draws detail lines + text notes into a
// drafting view.
//
// Coordinates: whatever unit the caller supplies (the generator uses feet,
// Revit's internal unit); the projection is linear so units carry through.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Plumbing
{
    /// <summary>A 3D point with no Revit dependency.</summary>
    public readonly struct IsoPoint3
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
        public IsoPoint3(double x, double y, double z) { X = x; Y = y; Z = z; }
    }

    /// <summary>A 2D point on the isometric sheet plane.</summary>
    public readonly struct IsoPoint2
    {
        public double U { get; }
        public double V { get; }
        public IsoPoint2(double u, double v) { U = u; V = v; }
        public double DistanceTo(IsoPoint2 o) => Math.Sqrt((U - o.U) * (U - o.U) + (V - o.V) * (V - o.V));
    }

    /// <summary>One pipe (or fitting run) to project.</summary>
    public class IsoSegmentInput
    {
        public string     Id     { get; set; } = "";
        public IsoPoint3  Start  { get; set; }
        public IsoPoint3  End    { get; set; }
        /// <summary>Label text, e.g. "DN50" or "DN100 1:40". Empty = unlabelled.</summary>
        public string     Label  { get; set; } = "";
        /// <summary>Grouping key (system name). Segments are drawn per group.</summary>
        public string     Group  { get; set; } = "";
    }

    public class IsoSegmentOutput
    {
        public string    Id          { get; set; } = "";
        public string    Group       { get; set; } = "";
        public IsoPoint2 Start       { get; set; }
        public IsoPoint2 End         { get; set; }
        public string    Label       { get; set; } = "";
        /// <summary>Where the label anchors (segment midpoint, offset off the line).</summary>
        public IsoPoint2 LabelAnchor { get; set; }
        /// <summary>False when the segment is too short on paper to carry a readable label.</summary>
        public bool      ShowLabel   { get; set; }
        /// <summary>True for a segment that runs (nearly) vertically in the model — a riser or drop.</summary>
        public bool      IsVertical  { get; set; }
        public double    ModelLength { get; set; }
    }

    public class IsoProjectionOptions
    {
        /// <summary>Segments shorter than this on paper (after fitting) are drawn but not labelled.</summary>
        public double MinLabelledLength { get; set; } = 0;
        /// <summary>Perpendicular offset of a label from its segment, in output units.</summary>
        public double LabelOffset       { get; set; } = 0;
        /// <summary>
        /// When &gt; 0, the projected drawing is scaled uniformly so its larger
        /// extent equals this value. 0 = keep projected size (true scale).
        /// </summary>
        public double FitTo             { get; set; } = 0;
        /// <summary>Drop segments whose model length is below this (zero-length connectors).</summary>
        public double MinModelLength    { get; set; } = 1e-6;
        /// <summary>Tolerance for classifying a segment as vertical: |dz| / length ≥ this.</summary>
        public double VerticalRatio     { get; set; } = 0.98;
    }

    public class IsoProjectionResult
    {
        public List<IsoSegmentOutput> Segments { get; } = new List<IsoSegmentOutput>();
        public double MinU { get; set; }
        public double MinV { get; set; }
        public double MaxU { get; set; }
        public double MaxV { get; set; }
        public double Width  => MaxU - MinU;
        public double Height => MaxV - MinV;
        /// <summary>Uniform factor applied by FitTo (1 when not fitted).</summary>
        public double Scale { get; set; } = 1.0;
        public int    Dropped { get; set; }
    }

    public static class IsometricProjection
    {
        private static readonly double Cos30 = Math.Cos(Math.PI / 6.0);   // 0.8660
        private const double Sin30 = 0.5;

        /// <summary>
        /// Standard isometric: the model X axis runs down-right at 30°, Y runs
        /// up-right at 30°, and Z is vertical on the sheet.
        /// u = (x + y)·cos30 ; v = (y − x)·sin30 + z
        /// A plan-north (+Y) run therefore reads up-right, and an east (+X) run
        /// reads down-right, which is the convention on UK/EU isometric sheets.
        /// </summary>
        public static IsoPoint2 Project(IsoPoint3 p)
            => new IsoPoint2((p.X + p.Y) * Cos30, (p.Y - p.X) * Sin30 + p.Z);

        public static IsoProjectionResult Project(
            IEnumerable<IsoSegmentInput> segments, IsoProjectionOptions opts = null)
        {
            opts = opts ?? new IsoProjectionOptions();
            var result = new IsoProjectionResult();
            var list = (segments ?? Enumerable.Empty<IsoSegmentInput>()).Where(s => s != null).ToList();

            var projected = new List<IsoSegmentOutput>();
            foreach (var s in list)
            {
                double dx = s.End.X - s.Start.X, dy = s.End.Y - s.Start.Y, dz = s.End.Z - s.Start.Z;
                double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < opts.MinModelLength) { result.Dropped++; continue; }
                projected.Add(new IsoSegmentOutput
                {
                    Id          = s.Id ?? "",
                    Group       = s.Group ?? "",
                    Start       = Project(s.Start),
                    End         = Project(s.End),
                    Label       = s.Label ?? "",
                    ModelLength = len,
                    IsVertical  = Math.Abs(dz) / len >= opts.VerticalRatio
                });
            }
            if (projected.Count == 0) return result;

            double minU = projected.Min(o => Math.Min(o.Start.U, o.End.U));
            double minV = projected.Min(o => Math.Min(o.Start.V, o.End.V));
            double maxU = projected.Max(o => Math.Max(o.Start.U, o.End.U));
            double maxV = projected.Max(o => Math.Max(o.Start.V, o.End.V));

            double extent = Math.Max(maxU - minU, maxV - minV);
            double k = opts.FitTo > 0 && extent > 0 ? opts.FitTo / extent : 1.0;
            result.Scale = k;

            // Translate so the drawing starts at (0,0), then scale.
            IsoPoint2 Norm(IsoPoint2 p) => new IsoPoint2((p.U - minU) * k, (p.V - minV) * k);

            foreach (var o in projected)
            {
                var a = Norm(o.Start);
                var b = Norm(o.End);
                o.Start = a;
                o.End   = b;
                double paperLen = a.DistanceTo(b);
                o.ShowLabel = !string.IsNullOrWhiteSpace(o.Label) && paperLen >= opts.MinLabelledLength;
                o.LabelAnchor = LabelAnchorFor(a, b, opts.LabelOffset);
                result.Segments.Add(o);
            }

            result.MinU = 0;
            result.MinV = 0;
            result.MaxU = (maxU - minU) * k;
            result.MaxV = (maxV - minV) * k;
            return result;
        }

        /// <summary>
        /// Midpoint of the segment, pushed <paramref name="offset"/> along the
        /// left-hand normal — always toward +V for a non-vertical line (label
        /// sits above the pipe), toward −U for a vertical one (left of a riser).
        /// </summary>
        public static IsoPoint2 LabelAnchorFor(IsoPoint2 a, IsoPoint2 b, double offset)
        {
            double mu = (a.U + b.U) / 2.0, mv = (a.V + b.V) / 2.0;
            double du = b.U - a.U, dv = b.V - a.V;
            double len = Math.Sqrt(du * du + dv * dv);
            if (len < 1e-12 || offset == 0) return new IsoPoint2(mu, mv);
            double nu = -dv / len, nv = du / len;          // left normal
            if (nv < 0 || (Math.Abs(nv) < 1e-9 && nu > 0)) { nu = -nu; nv = -nv; }
            return new IsoPoint2(mu + nu * offset, mv + nv * offset);
        }
    }
}
