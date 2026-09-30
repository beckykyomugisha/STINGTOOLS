// StingTools — Project Setup Wizard · "Rename scope boxes" in STING's own grammar
//
// The wizard's rename pattern defaulted to {BLD}-{ZONE}-{INDEX}, giving names like
// "BLD1-Z01-01" that nothing in STING reads: not the tagger, not the planner, not the
// producers. A box renamed that way looked organised and did nothing.
//
// STING reads five scope-box grammars (ScopeBoxNames). Two of them a person can
// give an existing box by renaming it and have it work at once:
//
//   STING-LOC::<loc>    the tagger sets LOC for every element inside the box
//   STING-ZONE::<zone>  the tagger sets ZONE for every element inside the box
//
// The other three are not offered here:
//   STING-AREA::…  is a drawing area the planner sizes from a seed. It is produced as
//                  the saved plan lists it, or — when the plan does not list it, or no
//                  plan is saved — from its own name with the routed per-level default
//                  types (AreaBoxResolution). A wizard rename sets where elements are,
//                  not what is drawn, so it does not make area boxes.
//   STING-SEED::…  is a size the planner copies; the planner registers seeds.
//   STING::…       binds one box to one drawing type; the Scope Box Manager
//                  validates that grammar against the catalogue.
//
// So a pattern must produce STING-LOC:: or STING-ZONE:: names, every name must parse,
// each must be unique, and the box must be unrotated (LOC and ZONE containment uses
// the box's plan extents). Anything else is refused with the reason.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public sealed class ScopeBoxRenameRow
    {
        public string CurrentName { get; set; }
        public bool Rotated { get; set; }
        /// <summary>The proposed name; null when the row was refused.</summary>
        public string NewName { get; set; }
        /// <summary>Why the row was refused; null when it was not.</summary>
        public string Problem { get; set; }
    }

    public static class ScopeBoxRenamePattern
    {
        /// <summary>The wizard's default: one zone box per zone code.</summary>
        public const string DefaultPattern = "STING-ZONE::{ZONE}";

        public const string Tokens = "{LOC} {BLD} {ZONE} {INDEX} {NAME}";

        /// <summary>
        /// True when a box turned <paramref name="degrees"/> in plan is not axis-aligned.
        /// A quarter turn keeps the box's plan extents equal to the box, so it counts as
        /// unrotated for LOC / ZONE containment.
        /// </summary>
        public static bool IsRotated(double degrees)
            => Math.Abs(Math.IEEERemainder(degrees, 90.0)) > 0.01;

        /// <summary>
        /// Why <paramref name="newName"/> cannot be given to a scope box by the wizard,
        /// or null when it can. <paramref name="rotated"/>: the box is turned in plan.
        /// </summary>
        public static string Check(string newName, bool rotated)
        {
            var name = (newName ?? "").Trim();
            if (name.Length == 0) return "the name is empty";
            switch (ScopeBoxNames.Classify(name))
            {
                case ScopeBoxKind.Zone:
                    if (!ScopeBoxNames.TryParseZone(name, out _, out var zr)) return zr;
                    break;
                case ScopeBoxKind.Building:
                    var loc = name.Substring(ScopeBoxNames.LocPrefix.Length).Trim();
                    if (!ScopeBoxNames.IsValidSegment(loc))
                        return "name has the STING-LOC:: prefix but does not match STING-LOC::<loc> "
                             + "(one code; allowed chars: A-Z 0-9 . _ -)";
                    break;
                case ScopeBoxKind.Area:
                    return "STING-AREA:: boxes are made by the Scope Box Planner (Create boxes), which records them in its plan; a renamed box is in no plan and would never be produced from";
                case ScopeBoxKind.Seed:
                    return "STING-SEED:: boxes are registered by the Scope Box Planner (Register Seeds)";
                case ScopeBoxKind.DrawingType:
                    return "STING::<drawing-type> names are set and validated by the Scope Box Manager";
                default:
                    return "not a name STING reads — use STING-LOC::<loc> or STING-ZONE::<zone>";
            }
            if (rotated)
                return "the box is rotated; STING-LOC / STING-ZONE boxes must be unrotated (containment uses the box's plan extents)";
            return null;
        }

        /// <summary>
        /// Fill each included row's NewName from <paramref name="pattern"/>, or its
        /// Problem. {LOC} and {ZONE} cycle through the codes; {BLD} is the first LOC
        /// code; {INDEX} counts the rows 01, 02 …; {NAME} is the current name. A name
        /// another row, or another existing box, already has is refused on the later
        /// row. <paramref name="otherNames"/> holds the boxes not being renamed.
        /// </summary>
        public static List<ScopeBoxRenameRow> Apply(string pattern, IList<ScopeBoxRenameRow> rows,
            IList<string> locCodes, IList<string> zoneCodes, IEnumerable<string> otherNames = null)
        {
            var result = new List<ScopeBoxRenameRow>();
            if (rows == null) return result;
            var locs = (locCodes ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            var zones = (zoneCodes ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
            string bld = locs.FirstOrDefault() ?? "BLD1";
            var taken = new HashSet<string>(otherNames ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            string pat = string.IsNullOrWhiteSpace(pattern) ? DefaultPattern : pattern.Trim();

            int idx = 1;
            foreach (var r in rows)
            {
                var row = new ScopeBoxRenameRow { CurrentName = r?.CurrentName, Rotated = r?.Rotated ?? false };
                result.Add(row);
                string loc = locs.Count > 0 ? locs[(idx - 1) % locs.Count] : bld;
                string zone = zones.Count > 0 ? zones[(idx - 1) % zones.Count] : "Z01";
                string name = pat.Replace("{BLD}", bld)
                                 .Replace("{LOC}", loc)
                                 .Replace("{ZONE}", zone)
                                 .Replace("{INDEX}", idx.ToString("D2"))
                                 .Replace("{NAME}", row.CurrentName ?? "")
                                 .Trim();
                idx++;
                var why = Check(name, row.Rotated);
                if (why == null && !taken.Add(name))
                    why = $"'{name}' is already another box's name — give the pattern a code per box (more {(pat.Contains("{LOC}") ? "LOC" : "ZONE")} codes), or rename by hand";
                if (why != null) { row.Problem = why; continue; }
                row.NewName = name;
            }
            return result;
        }
    }
}
