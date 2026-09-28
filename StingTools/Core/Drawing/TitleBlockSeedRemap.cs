// StingTools — Drawing Template Manager · T-7
//
// Revit-free geometry of master-seed propagation (TitleBlockFactory
// .PropagateFromMasterSeed): which A1 master a family derives from, the
// whole-sheet affine between the two papers, how arcs scale, and which ISO
// 3098 text tier a copied label lands on.
//
// Text tiers. Text must NOT scale linearly with paper — A1 -> A3 is 50 %
// and would print unreadably small. ISO 3098-0 / ISO 5457 set minimum
// character heights in two classes: A0 and A1 share one set, A2 / A3 / A4
// share a set one tier smaller (titles 7 -> 5 mm, notes 3.5 -> 2.5 mm). So a
// label moves by the difference in class, not by the paper ratio:
//   A1 -> A0   0 tiers   (was: 0, by accident — "A0 not handled")
//   A1 -> A2  -1 tier    (was: 0 — A2 kept A1-size text)
//   A1 -> A3  -1 tier    (was: -1, via a substring test for "_A3")
// Portrait vs landscape of the same size is the same class.

using System;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public sealed class TitleBlockSeedRemap
    {
        /// <summary>ISO 3098 drafting text-height series (mm).</summary>
        public static readonly double[] IsoTextTiers = { 1.8, 2.0, 2.5, 3.5, 5.0, 7.0, 10.0 };

        private static readonly Regex CoverRx = new Regex(
            @"^STING_TB_COVER_(A0|A1|A2|A3)_v[\d.]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SheetRx = new Regex(
            @"^STING_TB_(A0|A1|A2|A3)(_PORT)?_(BIM|NONBIM)_v[\d.]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public string SourceSize { get; private set; }
        public string TargetSize { get; private set; }
        public double Kx { get; private set; }
        public double Ky { get; private set; }

        /// <summary>Tier steps applied to text (negative = smaller).</summary>
        public int TextTierSteps => TextSizeClass(TargetSize) - TextSizeClass(SourceSize);

        /// <summary>|kx/ky - 1|. Same-orientation ISO papers keep the √2 ratio,
        /// so this is ~0.1 % in practice; a circle only stays a circle when
        /// it is small.</summary>
        public double Anisotropy => Ky == 0 ? double.PositiveInfinity : Math.Abs(Kx / Ky - 1.0);

        /// <summary>Anisotropy above which an arc cannot be rescaled as an arc.</summary>
        public const double MaxArcAnisotropy = 0.01;

        public static bool TryCreate(string masterId, string targetId, out TitleBlockSeedRemap remap)
        {
            remap = null;
            if (!TryGetIsoPaper(masterId, out double sw, out double sh, out string ss)
                || !TryGetIsoPaper(targetId, out double tw, out double th, out string ts))
                return false;
            remap = new TitleBlockSeedRemap
            {
                SourceSize = ss, TargetSize = ts,
                Kx = tw / sw, Ky = th / sh,
            };
            return true;
        }

        public (double X, double Y) MapPoint(double x, double y) => (x * Kx, y * Ky);

        /// <summary>Radius under the (near-uniform) affine: geometric mean.</summary>
        public double MapRadius(double r) => r * Math.Sqrt(Kx * Ky);

        public bool ArcSurvives => Anisotropy <= MaxArcAnisotropy;

        public double MapTextHeight(double heightMm) => StepTextTier(heightMm, TextTierSteps);

        /// <summary>0 for A0/A1, -1 for A2/A3/A4 (ISO 3098-0 minimum-height classes).</summary>
        public static int TextSizeClass(string size)
        {
            switch ((size ?? "").ToUpperInvariant())
            {
                case "A0": case "A1": return 0;
                case "A2": case "A3": case "A4": return -1;
                default: return 0;
            }
        }

        /// <summary>Snap to the largest tier &lt;= height, then move
        /// <paramref name="steps"/> tiers, clamped to the series.</summary>
        public static double StepTextTier(double heightMm, int steps)
        {
            int idx = 0;
            for (int i = 0; i < IsoTextTiers.Length; i++)
                if (heightMm >= IsoTextTiers[i] - 1e-6) idx = i;
            if (steps == 0) return heightMm;   // untouched: keep an off-series height as authored
            int n = Math.Max(0, Math.Min(IsoTextTiers.Length - 1, idx + steps));
            return IsoTextTiers[n];
        }

        /// <summary>ISO A-series paper (mm) + size code from a working-sheet or
        /// cover spec id. False for anything else (fab / specialty families).</summary>
        public static bool TryGetIsoPaper(string specId, out double wMm, out double hMm, out string size)
        {
            wMm = hMm = 0; size = null;
            var cov = CoverRx.Match(specId ?? "");
            var m = cov.Success ? null : SheetRx.Match(specId ?? "");
            if (!cov.Success && !m.Success) return false;
            size = (cov.Success ? cov.Groups[1].Value : m.Groups[1].Value).ToUpperInvariant();
            switch (size)
            {
                case "A0": wMm = 1189; hMm = 841; break;
                case "A1": wMm = 841;  hMm = 594; break;
                case "A2": wMm = 594;  hMm = 420; break;
                case "A3": wMm = 420;  hMm = 297; break;
                default: return false;
            }
            if (m != null && m.Groups[2].Success) { var t = wMm; wMm = hMm; hMm = t; }
            return true;
        }

        /// <summary>Working-sheet ids map to the A1 master of the same mode AND
        /// orientation; covers map to the A1 cover. Null for the masters
        /// themselves and for fab / specialty families.</summary>
        public static string ResolveMasterSeedId(string specId)
        {
            if (CoverRx.IsMatch(specId ?? ""))
            {
                const string coverMaster = "STING_TB_COVER_A1_v1.0";
                return string.Equals(coverMaster, specId, StringComparison.OrdinalIgnoreCase) ? null : coverMaster;
            }
            var m = SheetRx.Match(specId ?? "");
            if (!m.Success) return null;
            string port = m.Groups[2].Success ? "_PORT" : "";
            string master = $"STING_TB_A1{port}_{m.Groups[3].Value.ToUpperInvariant()}_v2.0";
            return string.Equals(master, specId, StringComparison.OrdinalIgnoreCase) ? null : master;
        }
    }
}
