// StingTools — Drawing Types QA / finishing rules (Revit-free).
//
// The decisions the drawing QA tools make (Sync Styles, Heal Title Blocks,
// Renumber, managed templates) live here, free of the Revit API, so they can
// be unit-tested by StingTools.Tags.Tests through <Compile Include>. The Revit
// callers gather facts, ask these rules, and act on the answer.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    internal static class DrawingQaRules
    {
        /// <summary>
        /// DTW-2: may a cached managed-template id be returned as-is? Only when
        /// the template's stamped checksum equals the pack's current checksum.
        /// An empty stamp is never current — the template was never stamped by
        /// the syncer, or the stamp was cleared, so it must be re-applied.
        /// </summary>
        internal static bool IsCachedTemplateCurrent(string storedChecksum, string currentChecksum)
            => !string.IsNullOrEmpty(storedChecksum)
               && string.Equals(storedChecksum, currentChecksum, StringComparison.Ordinal);

        /// <summary>
        /// DTW-5: may a profile's titleBlockParams entry write this parameter?
        /// Not when it is the sheet's own number or name. A title block exposes
        /// both, so a "Sheet Number" key renumbered the sheet behind
        /// SheetNumbering.Apply — bypassing the ISO policy, the style lock and the
        /// renumber history. Decided by the parameter's BuiltInParameter when the
        /// caller has it (<paramref name="builtInParameterName"/>, e.g.
        /// "SHEET_NUMBER") so a localised label is still caught, and by the
        /// English key otherwise.
        /// </summary>
        internal static bool IsSheetIdentityParam(string key, string builtInParameterName)
        {
            if (string.Equals(builtInParameterName, "SHEET_NUMBER", StringComparison.Ordinal)
                || string.Equals(builtInParameterName, "SHEET_NAME", StringComparison.Ordinal))
                return true;
            var k = (key ?? string.Empty).Trim();
            return string.Equals(k, "Sheet Number", StringComparison.OrdinalIgnoreCase)
                || string.Equals(k, "Sheet Name", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// DTW-15: do two invariant number texts ("2.5", "2.500") name the same
        /// value? Culture-free, so a comma-decimal machine does not read "2.5" as
        /// 25. Empty text equals zero — the applier writes 0 for an empty cell.
        /// False when either side is not a number.
        /// </summary>
        internal static bool NumericTextEquals(string a, string b)
        {
            if (!TryParseInvariant(a, out var x) || !TryParseInvariant(b, out var y)) return false;
            return Math.Abs(x - y) <= 1e-6 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)));
        }

        // ── DTW-11: Sheet Number from ISO — what may move ──────────────────

        internal sealed class IsoRenumberCandidate
        {
            public string Id;
            public string Current;
            /// <summary>The ISO 19650 identifier (SHT_TAG_1_TXT) the sheet would take.</summary>
            public string Target;
            /// <summary>STING_STYLE_LOCKED_BOOL — a locked sheet keeps its number.</summary>
            public bool Locked;
        }

        internal sealed class IsoRenumberPlan
        {
            public List<IsoRenumberCandidate> Moves { get; } = new List<IsoRenumberCandidate>();
            /// <summary>Locked sheets that would otherwise have moved.</summary>
            public List<string> Locked { get; } = new List<string>();
            /// <summary>Movers whose target is held by a sheet that is not moving.</summary>
            public List<string> Held { get; } = new List<string>();
            /// <summary>Targets two or more movers share: target → their current numbers.</summary>
            public Dictionary<string, List<string>> Duplicates { get; }
                = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// DTW-11: plan Sheet Number from ISO against EVERY sheet number in the
        /// model, not only the ones being renamed. The old command checked
        /// duplicates among its own targets, then renamed in two passes — a target
        /// already held by a sheet outside the plan failed its second pass and left
        /// that sheet on "~STINGTMP~", committed. Locked sheets never move, and a
        /// mover whose target is held stays put (reported), repeated until stable
        /// because a mover that stays keeps its number taken.
        /// </summary>
        internal static IsoRenumberPlan PlanIsoRenumber(
            IEnumerable<IsoRenumberCandidate> candidates, IEnumerable<string> allNumbers)
        {
            var plan = new IsoRenumberPlan();
            var movers = new List<IsoRenumberCandidate>();
            foreach (var c in candidates ?? Enumerable.Empty<IsoRenumberCandidate>())
            {
                if (c == null || string.IsNullOrWhiteSpace(c.Target)
                    || string.Equals(c.Target, c.Current, StringComparison.Ordinal)) continue;
                if (c.Locked) { plan.Locked.Add(c.Current); continue; }
                movers.Add(c);
            }

            foreach (var g in movers.GroupBy(m => m.Target, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                plan.Duplicates[g.Key] = g.Select(m => m.Current).ToList();
            if (plan.Duplicates.Count > 0) return plan; // caller refuses the run

            var all = new HashSet<string>((allNumbers ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase);
            bool changed = true;
            while (changed)
            {
                changed = false;
                var moving = new HashSet<string>(movers.Select(m => m.Current), StringComparer.OrdinalIgnoreCase);
                foreach (var m in movers.ToList())
                {
                    if (all.Contains(m.Target) && !moving.Contains(m.Target))
                    {
                        plan.Held.Add($"{m.Current} -> {m.Target}: '{m.Target}' is already another sheet's number; left as {m.Current}.");
                        movers.Remove(m);
                        changed = true;
                    }
                }
            }
            plan.Moves.AddRange(movers);
            return plan;
        }

        internal static bool TryParseInvariant(string text, out double value)
        {
            if (string.IsNullOrWhiteSpace(text)) { value = 0; return true; }
            return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }
    }
}
