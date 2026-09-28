// StingTools — Scope-box planner · how big may a box be for a drawing type?
//
// A scope box becomes the crop of a plan, and the plan has to fit the sheet.
// So the largest box a drawing type can use is set by three things it already
// declares — nothing new is authored:
//
//     drawable area of its paper       STING_TITLE_BLOCKS.json  <paper>_<LAND|PORT>_common  "drawable"
//   × its main slot                    the drawing type's required plan slot  (normW, normH)
//   × its scale                        1:100 turns 591 mm of sheet into 59.1 m of building
//   × a fit factor                     room for dimensions, tags and grid bubbles outside the crop
//
// An A1 landscape plan at 1:100 (drawable 821 × 474, main slot 0.72 × 0.90)
// allows 59.1 × 42.7 m; at fit 0.9 that is 53.2 × 38.4 m.
//
// Drawables come from the shipped title-block specs, so a change there reaches
// the planner without a second table to update. A paper the specs do not
// describe yields no extent and a reason — never a guessed A1.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace StingTools.Core.Drawing
{
    public static class ScopeBoxSizing
    {
        /// <summary>Default share of the slot the crop may fill; the rest holds annotation outside the crop.</summary>
        public const double DefaultFitFactor = 0.9;

        private static readonly Regex _commonId =
            new Regex(@"^(A\d)(?:_(LAND|PORT))?_common", RegexOptions.IgnoreCase);

        /// <summary>Key for <see cref="DrawablesFromTitleBlocks"/>: "A1|Landscape".</summary>
        public static string DrawableKey(string paper, string orientation)
            => (paper ?? "").Trim().ToUpperInvariant() + "|"
             + (string.Equals((orientation ?? "").Trim(), "Portrait", StringComparison.OrdinalIgnoreCase) ? "Portrait" : "Landscape");

        /// <summary>
        /// Drawable rectangle (mm) per paper and orientation, read from the
        /// <c>&lt;paper&gt;_&lt;LAND|PORT&gt;_common</c> families of STING_TITLE_BLOCKS.json.
        /// A family with no orientation in its id counts as landscape when wider than tall.
        /// An explicit LAND/PORT family wins over an unmarked one.
        /// </summary>
        public static Dictionary<string, (double W, double H)> DrawablesFromTitleBlocks(string titleBlocksJson)
        {
            var map = new Dictionary<string, (double W, double H)>(StringComparer.OrdinalIgnoreCase);
            var explicitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(titleBlocksJson)) return map;
            var root = JObject.Parse(titleBlocksJson);
            foreach (var fam in (root["families"] as JArray) ?? new JArray())
            {
                var id = (string)fam["id"];
                var m = _commonId.Match(id ?? "");
                var d = fam["drawable"];
                if (!m.Success || d == null) continue;
                double w = (double?)d["w"] ?? 0, h = (double?)d["h"] ?? 0;
                if (w <= 0 || h <= 0) continue;
                string orient = m.Groups[2].Success
                    ? (m.Groups[2].Value.Equals("PORT", StringComparison.OrdinalIgnoreCase) ? "Portrait" : "Landscape")
                    : (w >= h ? "Landscape" : "Portrait");
                var key = DrawableKey(m.Groups[1].Value, orient);
                if (m.Groups[2].Success) { map[key] = (w, h); explicitKeys.Add(key); }
                else if (!explicitKeys.Contains(key)) map[key] = (w, h);
            }
            return map;
        }

        /// <summary>
        /// True when the drawing type is produced as a plan cropped by a scope box —
        /// the only kind an area box can serve. Sections, schedules and 3D are not.
        /// </summary>
        public static bool IsAreaCandidate(DrawingType dt, out string why)
        {
            why = null;
            if (dt == null) { why = "no drawing type"; return false; }
            if (!DrawingPurposeViewKind.TryResolve(dt.Purpose, out var kind)
                || (kind != DrawingViewKind.FloorPlan && kind != DrawingViewKind.Rcp))
            { why = $"purpose '{dt.Purpose}' is not a plan"; return false; }
            var crop = dt.Crop?.Kind ?? "";
            if (crop.IndexOf("ScopeBox", StringComparison.OrdinalIgnoreCase) < 0)
            { why = $"crop '{crop}' does not use a scope box"; return false; }
            if (dt.Scale <= 0) { why = "no scale"; return false; }
            return true;
        }

        private static readonly HashSet<string> _planSlotTypes =
            new HashSet<string>(new[] { "Plan", "RCP", "Coordination" }, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The slot the cropped plan lands in: the required plan-like slot, else the
        /// largest plan-like slot. None means the type has nowhere to put a plan.
        /// </summary>
        public static DrawingSlot MainSlot(DrawingType dt)
        {
            var plans = (dt?.Slots ?? new List<DrawingSlot>())
                .Where(s => s != null && _planSlotTypes.Contains(s.ViewType ?? "") && s.NormW > 0 && s.NormH > 0)
                .ToList();
            return plans.FirstOrDefault(s => s.Required)
                ?? plans.OrderByDescending(s => s.NormW * s.NormH).FirstOrDefault();
        }

        /// <summary>
        /// Largest plan extent, in metres, that fits the type's main slot at its scale.
        /// False with a reason when any input is missing — never a default.
        /// </summary>
        public static bool TryMaxExtent(DrawingType dt, IReadOnlyDictionary<string, (double W, double H)> drawables,
            double fitFactor, out double widthM, out double depthM, out string why)
        {
            widthM = depthM = 0; why = null;
            if (!IsAreaCandidate(dt, out why)) return false;
            var slot = MainSlot(dt);
            if (slot == null) { why = "no plan slot"; return false; }
            if (drawables == null || !drawables.TryGetValue(DrawableKey(dt.PaperSize, dt.Orientation), out var dr))
            { why = $"no drawable area for {dt.PaperSize} {dt.Orientation} in STING_TITLE_BLOCKS.json"; return false; }
            if (fitFactor <= 0 || fitFactor > 1) { why = $"fit factor {fitFactor} is outside (0, 1]"; return false; }
            int scale = slot.Scale ?? dt.Scale;
            widthM = dr.W * slot.NormW * scale / 1000.0 * fitFactor;
            depthM = dr.H * slot.NormH * scale / 1000.0 * fitFactor;
            return true;
        }

        /// <summary>"A1-100": paper and scale, the grouping that lets many types share one box size.</summary>
        public static string SizeClassKey(DrawingType dt)
        {
            var slot = MainSlot(dt);
            int scale = slot?.Scale ?? dt?.Scale ?? 0;
            var paper = (dt?.PaperSize ?? "").Trim().ToUpperInvariant();
            bool portrait = string.Equals((dt?.Orientation ?? "").Trim(), "Portrait", StringComparison.OrdinalIgnoreCase);
            return paper + (portrait ? "P" : "") + "-" + scale;
        }
    }
}
