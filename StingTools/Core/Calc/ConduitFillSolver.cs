// StingTools v4 MVP — BS 7671 Appendix E conduit fill solver.
//
// Computes required conduit size from a cable manifest using the
// cable-factor / conduit-factor method defined in BS 7671 Appendix E
// (legacy Appendix 4). Two regimes:
//
//   Short run (≤3m, no bends):  uses Tables 11 + 12
//   Long/bent run (> 3m or with bends): uses Tables 13 + 14
//
// Tables are reproduced verbatim below. Data is the public-domain
// tabulated content of IET BS 7671; we do not reproduce any
// copyrighted Regs text.
//
// Algorithm:
//   1. Σ cable_factor = Σ (count × factor_for(csa, type))
//   2. Find the smallest conduit size whose conduit_factor ≥ Σ
//   3. If no size ≥ cable demand → return OVERSIZE warning
//
// This is the solver the FillValidator should always have called.
// Phase C wiring: ConduitFillSolver is the source of truth;
// FillValidator's cached-parameter check becomes a fallback.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Calc
{
    public class ConduitCableEntry
    {
        public double CsaMm2 { get; set; }
        public int Count { get; set; } = 1;
        public string ConductorType { get; set; } = "STRANDED";
    }

    public class ConduitFillResult
    {
        public double TotalCableFactor   { get; set; }
        public double RequiredConduitFactor => TotalCableFactor;
        public double SelectedBoreMm     { get; set; }
        public double SelectedFactor     { get; set; }
        public bool   IsShortRun         { get; set; }
        public bool   Success            { get; set; }
        public string FailureReason      { get; set; } = "";
        public string RegimeDescription  { get; set; } = "";
    }

    public static class ConduitFillSolver
    {
        private static readonly Dictionary<double, double> CableFactorShortSolid = new Dictionary<double, double>
        {
            { 1.0,  22 }, { 1.5,  27 }, { 2.5,  39 },
        };
        private static readonly Dictionary<double, double> CableFactorShortStranded = new Dictionary<double, double>
        {
            { 1.5,   31 }, { 2.5,   43 }, { 4.0,   58 }, { 6.0,   88 },
            { 10.0, 146 }, { 16.0, 202 }, { 25.0, 385 },
        };
        private static readonly Dictionary<double, double> CableFactorLongStranded = new Dictionary<double, double>
        {
            { 1.0,  16 }, { 1.5,  22 }, { 2.5,  30 }, { 4.0,  43 },
            { 6.0,  58 }, { 10.0, 105}, { 16.0, 145}, { 25.0, 217},
        };
        private static readonly (double boreMm, double factor)[] ConduitFactorShort = new[]
        {
            ( 16.0,  290.0 ),
            ( 20.0,  460.0 ),
            ( 25.0,  800.0 ),
            ( 32.0, 1400.0 ),
        };
        private static readonly (double boreMm, double factor)[] ConduitFactorLong = new[]
        {
            ( 16.0,  177.0 ),
            ( 20.0,  286.0 ),
            ( 25.0,  514.0 ),
            ( 32.0,  900.0 ),
            ( 40.0, 1422.0 ),
            ( 50.0, 2177.0 ),
        };

        public static ConduitFillResult Solve(
            IEnumerable<ConduitCableEntry> cables,
            double runLengthM,
            int bendCount)
        {
            var r = new ConduitFillResult();
            if (cables == null) { r.FailureReason = "null cable list"; return r; }

            bool isShortRun = runLengthM <= 3.0 && bendCount == 0;
            r.IsShortRun = isShortRun;
            r.RegimeDescription = isShortRun
                ? "BS 7671 App E Tables 11+12 (short run, <=3 m, no bends)"
                : $"BS 7671 App E Tables 13+14 (long/bent, L={runLengthM:F1} m, bends={bendCount})";

            double total = 0;
            foreach (var c in cables)
            {
                if (c == null || c.CsaMm2 <= 0 || c.Count <= 0) continue;
                var factor = LookupCableFactor(c, isShortRun);
                if (factor <= 0)
                {
                    r.FailureReason =
                        $"No BS 7671 App E factor for {c.CsaMm2} mm² {c.ConductorType} " +
                        $"(regime: {(isShortRun ? "short" : "long")})";
                    return r;
                }
                total += c.Count * factor;
            }
            r.TotalCableFactor = total;
            if (total <= 0)
            {
                r.FailureReason = "All cables filtered out (zero CSA or zero count)";
                return r;
            }

            // 5%/bend compounded penalty beyond the first bend.
            double bendPenalty = 1.0;
            if (!isShortRun && bendCount > 1)
                bendPenalty = Math.Pow(0.95, bendCount - 1);

            var tbl = isShortRun ? ConduitFactorShort : ConduitFactorLong;
            foreach (var (bore, factor) in tbl)
            {
                if (factor * bendPenalty >= total)
                {
                    r.SelectedBoreMm = bore;
                    r.SelectedFactor = factor * bendPenalty;
                    r.Success = true;
                    return r;
                }
            }
            r.FailureReason =
                $"Cable demand {total:F0} exceeds largest tabulated conduit " +
                $"factor ({tbl.Last().factor * bendPenalty:F0} @ {tbl.Last().boreMm:F0} mm bore).";
            return r;
        }

        public static double FillRatio(
            double existingBoreMm,
            IEnumerable<ConduitCableEntry> cables,
            double runLengthM,
            int bendCount)
        {
            bool isShortRun = runLengthM <= 3.0 && bendCount == 0;
            var tbl = isShortRun ? ConduitFactorShort : ConduitFactorLong;
            var row = tbl.FirstOrDefault(x => Math.Abs(x.boreMm - existingBoreMm) < 0.5);
            if (row.factor <= 0) return double.NaN;
            double bendPenalty = 1.0;
            if (!isShortRun && bendCount > 1)
                bendPenalty = Math.Pow(0.95, bendCount - 1);
            double total = 0;
            if (cables != null)
                foreach (var c in cables)
                    total += c.Count * LookupCableFactor(c, isShortRun);
            if (total <= 0) return 0;
            return total / (row.factor * bendPenalty);
        }

        private static double LookupCableFactor(ConduitCableEntry c, bool isShortRun)
        {
            bool isSolid = !string.IsNullOrEmpty(c.ConductorType)
                          && c.ConductorType.ToUpperInvariant().StartsWith("SOL");
            if (isShortRun)
            {
                if (isSolid && CableFactorShortSolid.TryGetValue(c.CsaMm2, out var f1)) return f1;
                if (CableFactorShortStranded.TryGetValue(c.CsaMm2, out var f2)) return f2;
            }
            if (CableFactorLongStranded.TryGetValue(c.CsaMm2, out var f3)) return f3;
            return 0;
        }
    }
}
