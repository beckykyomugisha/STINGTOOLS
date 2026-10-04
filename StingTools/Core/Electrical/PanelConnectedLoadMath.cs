// PanelConnectedLoadMath — Revit-free half of PanelConnectedLoad (ROADMAP ELEC-27).
// StingTools.Tags.Tests compiles this file.

using System.Collections.Generic;
using System.Globalization;

namespace StingTools.Core.Electrical
{
    public static class PanelConnectedLoadMath
    {
        /// <summary>
        /// kW per board from (board id, circuit true power W) rows. Negative or non-finite
        /// powers are ignored (not a load). Boards with no row are absent — callers write
        /// nothing for them rather than a 0 that reads as "measured, empty".
        /// </summary>
        public static Dictionary<long, double> SumKw(IEnumerable<(long BoardId, double TrueW)> rows)
        {
            var w = new Dictionary<long, double>();
            if (rows == null) return w;
            foreach (var (id, p) in rows)
            {
                double add = double.IsFinite(p) && p > 0 ? p : 0;
                w[id] = (w.TryGetValue(id, out double cur) ? cur : 0) + add;
            }
            var kw = new Dictionary<long, double>();
            foreach (var kv in w) kw[kv.Key] = kv.Value / 1000.0;
            return kw;
        }

        /// <summary>The text written to the NUMBER parameter: kW, one decimal, invariant.</summary>
        public static string KwText(double kw) => kw.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
