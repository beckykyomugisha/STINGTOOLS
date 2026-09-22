// NumericTextMirror — writes a numeric parameter's formatted value into its
// TEXT twin, so a tag label can show it.
//
// WHY
//
// Revit has no number-to-string conversion in family formulas. A label's
// calculated value is Text, so `if(TAG_PARA_STATE_2_BOOL, <NUMBER>, "")` has
// two differently-typed branches and Revit rejects it as "Inconsistent Units"
// the moment it is typed. Measured 2026-09-22 while hand-building the LPS
// master: twelve rows across both build sheets could not be entered at all.
//
// The library already answers this with an _NR / _TXT pairing —
// ELC_LPS_PROTECTION_ANGLE_DEG alongside ELC_LPS_PROTECTION_ANGLE_TXT. The
// twins were defined but nothing ever filled them, so pointing a label at one
// would have rendered blank on every element, forever, and the obvious
// explanation would have been that the data was missing. That is this
// codebase's signature failure and the reason this file exists.
//
// WHAT IT DOES NOT DO
//
// It never invents a value. A numeric with no value leaves its twin alone
// rather than writing "0" or "": a tag reading "R: 0 Ω" for an earth electrode
// nobody has tested is worse than a blank, because it can be believed.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core
{
    /// <summary>Mirrors numeric parameters into their TEXT twins.</summary>
    public static class NumericTextMirror
    {
        // ── runtime ──────────────────────────────────────────────────────────

        private static Dictionary<string, string> _pairs;
        private static readonly object _lock = new object();

        /// <summary>Drops the cache so an edited MR_PARAMETERS.txt is picked up.</summary>
        public static void Reload() { lock (_lock) { _pairs = null; } }

        /// <summary>The pair map, built once from the shipped shared-parameter file.</summary>
        public static Dictionary<string, string> PairMap()
        {
            lock (_lock)
            {
                if (_pairs != null) return _pairs;
                var defs = new List<KeyValuePair<string, string>>();
                try
                {
                    string path = StingToolsApp.FindDataFile("MR_PARAMETERS.txt");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    {
                        StingLog.Warn("NumericTextMirror: MR_PARAMETERS.txt not found — " +
                                      "numeric tag rows will stay blank");
                    }
                    else
                    {
                        foreach (string line in File.ReadLines(path))
                        {
                            var f = line.Split('\t');
                            // PARAM <guid> <name> <datatype> ...
                            if (f.Length > 3 && f[0] == "PARAM")
                                defs.Add(new KeyValuePair<string, string>(f[2], f[3]));
                        }
                    }
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"NumericTextMirror.PairMap: {ex.Message}");
                }

                _pairs = NumericTextMirrorRule.Pairs(defs);
                StingLog.Info($"NumericTextMirror: {_pairs.Count} numeric/TEXT pair(s) from " +
                              $"{defs.Count} definition(s)");
                return _pairs;
            }
        }

        /// <summary>
        /// Fills every TEXT twin on this element whose numeric has a value.
        /// Returns how many were written.
        ///
        /// <para>Only writes when the twin is EMPTY unless
        /// <paramref name="overwrite"/>. A twin someone has typed by hand is
        /// their value, and silently replacing it with a formatted number would
        /// discard an answer this code cannot reproduce — "1.5 m (assumed)" is
        /// not recoverable from 1.5.</para>
        /// </summary>
        public static int MirrorAll(Element el, bool overwrite = false)
        {
            if (el == null) return 0;
            int written = 0;

            foreach (var kv in PairMap())
            {
                try
                {
                    Parameter num = el.LookupParameter(kv.Key);
                    if (num == null || !num.HasValue) continue;

                    Parameter twin = el.LookupParameter(kv.Value);
                    if (twin == null || twin.IsReadOnly || twin.StorageType != StorageType.String) continue;
                    if (!overwrite && !string.IsNullOrWhiteSpace(twin.AsString())) continue;

                    // GetDisplayText formats through the parameter's own units,
                    // so a LENGTH renders in project units rather than Revit's
                    // internal feet — the trap that once put "0.882867" on a
                    // drawing for a 25.00 L/s flow.
                    string text = ParameterHelpers.GetDisplayText(el, kv.Key);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    if (string.Equals(text, twin.AsString(), StringComparison.Ordinal)) continue;

                    if (twin.Set(text)) written++;
                }
                catch (Exception ex)
                {
                    StingLog.Warn($"NumericTextMirror '{kv.Key}' -> '{kv.Value}' on {el.Id}: {ex.Message}");
                }
            }
            return written;
        }
    }
}
