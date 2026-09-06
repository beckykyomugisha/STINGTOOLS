// LookupAudit.cs — G-27. Collect lookup() resolution states across a formula run.
//
// REPORT-ONLY. This changes no quantity and blocks no export. It exists to answer
// one question before any blocking decision is taken: on real data, how many of the
// 26 lookup() calls actually fire DEFAULTED, and how many UNRESOLVED?
//
// Blocking on DEFAULTED without that number could stop every BOQ in the product
// overnight, because a DEFAULT row is a legitimate estimating assumption and may
// well be the normal case rather than the exception.

using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.BOQ.Takeoff;
using StingTools.Core;

namespace StingTools.Temp
{
    /// <summary>
    /// Ambient collector for the formula evaluator's lookup() resolutions.
    /// <para>
    /// Static because the expression parser is constructed per formula, deep inside
    /// the evaluation loop, and threading a resolution object through every parse
    /// method would be a large change to a hot path for a report-only feature.
    /// Scoped by <see cref="BeginRun"/> / <see cref="EndRun"/> so it cannot leak
    /// between commands.
    /// </para>
    /// </summary>
    internal static class LookupAudit
    {
        [ThreadStatic] private static QuantityResolution _current;
        [ThreadStatic] private static Dictionary<string, int> _byState;

        public static bool Active => _current != null;

        public static void BeginRun()
        {
            _current = new QuantityResolution();
            _byState = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Measured"] = 0, ["Defaulted"] = 0, ["Unresolved"] = 0
            };
        }

        public static void Record(string table, string keyParam, string keyValue,
                                  string column, LookupState state)
        {
            if (_current == null) return;   // not in an audited run — no-op
            _current.Record(table, keyParam, keyValue, column, state);
            string k = state.ToString();
            _byState[k] = _byState.TryGetValue(k, out int n) ? n + 1 : 1;
        }

        /// <summary>Close the run and return a human-readable summary, or null if inactive.</summary>
        public static string EndRun()
        {
            if (_current == null) return null;
            var res = _current;
            var counts = _byState;
            _current = null;
            _byState = null;

            int measured = counts["Measured"], defaulted = counts["Defaulted"], unresolved = counts["Unresolved"];
            int total = measured + defaulted + unresolved;
            if (total == 0) return null;

            // Distinct (table.column) pairs, so the number reads as "how many of the
            // 26 call sites" rather than "how many elements".
            var sites = res.Traces
                .GroupBy(t => $"{t.Table}.{t.Column}")
                .ToDictionary(g => g.Key, g => g.Select(x => x.State).Max());
            int siteDefaulted = sites.Count(kv => kv.Value == LookupState.Defaulted);
            int siteUnresolved = sites.Count(kv => kv.Value == LookupState.Unresolved);

            string summary =
                $"lookup() resolution — {total} call(s) across {sites.Count} distinct site(s): "
              + $"measured {measured}, DEFAULTED {defaulted}, UNRESOLVED {unresolved}. "
              + $"Sites hitting a default at least once: {siteDefaulted}; never resolving: {siteUnresolved}.";

            StingLog.Info("G-27 " + summary);
            if (siteDefaulted > 0)
                StingLog.Warn($"G-27: {siteDefaulted} lookup site(s) used a DEFAULT row. Those quantities are "
                            + "ASSUMED, not measured. " + res.Note());
            return summary;
        }

        /// <summary>The live resolution, for a caller that wants the per-row flag.</summary>
        public static QuantityResolution Current => _current;
    }
}
