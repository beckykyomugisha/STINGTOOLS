// ══════════════════════════════════════════════════════════════════════════
//  SafeWrite.cs — H-4. One place that knows how a write actually reports
//  failure, so a a silent catch around a mutation stops being the default.
//
//  THE LESSON THIS ENCODES (from StampTokens, H-4 round 1):
//
//    A log line in the catch does not fix a swallowed parameter write.
//    ParameterHelpers.SetString / SetInt / SetIfEmpty return FALSE on an
//    unbound parameter and throw NOTHING. The exception handler never fires.
//    The RETURN VALUE is the signal; the exception is the rare case.
//
//  So the two idioms are different and must not share one wrapper:
//
//    Set()  — parameter writes. Checks the return, and when it is false
//             distinguishes "not bound to this category" (a real defect the
//             user can fix by running Load Shared Parameters) from "already
//             had a value" (intended, when overwrite is false).
//
//    Try()  — Revit API calls that report by throwing: sheet.Name,
//             view.SetCategoryHidden, sd.ChangeTypeId, FamilyManager.Set.
//             Here the exception IS the signal.
//
//  REPORTING IS AGGREGATED. A 4,000-element run must not emit 4,000 identical
//  warnings — that is its own kind of silence, and it is why the first
//  StampTokens fix reported once per parameter rather than once per element.
//  Each (context, target) pair warns ONCE into the caller's warning list and
//  logs with a running count.
//
//  NOT for optional reads. `try { x = el.get_Parameter(...); } catch { }`
//  around a read that legitimately throws when a parameter is absent is a
//  defensible Revit idiom and is left alone.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    public static class SafeWrite
    {
        // key = "context|target" → occurrences this session
        private static readonly ConcurrentDictionary<string, int> _seen =
            new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Reset the once-per-pair suppression. Call at the start of a run
        /// so a second run reports again rather than inheriting the first run's silence.</summary>
        public static void ResetRun() => _seen.Clear();

        /// <summary>
        /// A parameter write whose failure is reported by RETURN VALUE, not by
        /// throwing. Pass the write itself as <paramref name="write"/> so the
        /// caller keeps its exact overwrite / SetIfEmpty semantics.
        /// </summary>
        /// <returns>true when the value was written.</returns>
        public static bool Set(Element el, string paramName, Func<bool> write,
                               string context, ICollection<string> warnings = null)
        {
            if (write == null) return false;
            try
            {
                if (write()) return true;

                // False means one of two very different things. Only one is a defect:
                //   - parameter absent  → unbound for this category; nothing will
                //                         ever land here until it is bound
                //   - parameter present → it already held a value and the caller
                //                         asked not to overwrite. Intended.
                bool unbound = false;
                try { unbound = el?.LookupParameter(paramName) == null; } catch { }
                if (unbound)
                    Report(context, paramName, warnings,
                        $"'{paramName}' is not bound to this element's category, so the write "
                      + "cannot land. Run Load Shared Parameters and re-run; until then anything "
                      + "downstream reading it sees blank.");
                return false;
            }
            catch (Exception ex)
            {
                Report(context, paramName, warnings, $"'{paramName}' write threw: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// A Revit API call whose failure is reported by THROWING — sheet.Name,
        /// SetCategoryHidden, ChangeTypeId, FamilyManager.Set and friends.
        /// </summary>
        /// <returns>true when the call completed.</returns>
        public static bool Try(Action act, string context, string what,
                               ICollection<string> warnings = null)
        {
            if (act == null) return false;
            try { act(); return true; }
            catch (Exception ex)
            {
                Report(context, what, warnings, $"{what} failed: {ex.Message}");
                return false;
            }
        }

        private static void Report(string context, string target,
                                   ICollection<string> warnings, string detail)
        {
            string key = (context ?? "") + "|" + (target ?? "");
            int n = _seen.AddOrUpdate(key, 1, (_, c) => c + 1);

            // First occurrence reaches the user's warning list; later ones only
            // move the counter, so the report names the problem once and the log
            // carries the scale.
            if (n == 1) warnings?.Add($"{context}: {detail}");
            if (n == 1 || n == 10 || n % 100 == 0)
                StingLog.Warn($"SafeWrite[{context}] {detail} (occurrence {n})");
        }

        /// <summary>How many times a (context, target) pair has failed this session —
        /// for a caller that wants to append a final count.</summary>
        public static int Count(string context, string target)
            => _seen.TryGetValue((context ?? "") + "|" + (target ?? ""), out int n) ? n : 0;
    }
}
