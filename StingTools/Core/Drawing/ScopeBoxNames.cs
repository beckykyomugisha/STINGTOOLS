// StingTools — Scope-box planner · the name grammar for area and seed boxes
//
// Four kinds of scope box carry meaning in STING, told apart by prefix:
//
//   STING::<drawing-type>[::<level>[::<tag>]]   one box, one drawing type   (ScopeBoxBinder)
//   STING-LOC::<loc>                            a building's footprint      (ScopeBoxLoc)
//   STING-AREA::<area>[::<level>]               an area every plan type shares
//   STING-SEED::<w>x<d>                         a hand-drawn size to copy from
//
// The prefixes are disjoint on purpose. "STING::AREA::A01" would parse under the
// binder's grammar as a drawing type called AREA, so an area box needs a prefix
// the binder never matches; "STING-AREA::" does not start with "STING::".
//
// Why area boxes exist at all: a box tied to one drawing type multiplies by the
// catalogue. Ten plan types over five levels is fifty identical rectangles. An
// area box is an extent; every plan type whose sheet it fits can use it, and a
// box without a level serves every level it spans.
//
// Why seed boxes exist: the Revit API cannot create a scope box or change its
// size. It can copy, move, rotate and rename one. So a person draws each SIZE
// once, and STING copies it everywhere else. The size is measured from the
// box, never read from the name — the name is only a label, so it cannot lie.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace StingTools.Core.Drawing
{
    public enum ScopeBoxKind
    {
        /// <summary>No STING prefix — an ordinary scope box.</summary>
        Plain,
        /// <summary>STING::&lt;drawing-type&gt; — see <see cref="ScopeBoxBinder"/>.</summary>
        DrawingType,
        /// <summary>STING-LOC::&lt;loc&gt; — a building footprint.</summary>
        Building,
        /// <summary>STING-AREA::&lt;area&gt;[::&lt;level&gt;].</summary>
        Area,
        /// <summary>STING-SEED::&lt;w&gt;x&lt;d&gt;.</summary>
        Seed,
    }

    public static class ScopeBoxNames
    {
        public const string AreaPrefix = "STING-AREA::";
        public const string SeedPrefix = "STING-SEED::";
        public const string LocPrefix  = "STING-LOC::";
        /// <summary>ScopeBoxBinder's prefix, restated because that file needs the Revit API.</summary>
        public const string DrawingTypePrefix = "STING::";

        private static readonly Regex _segment = new Regex(@"^[A-Za-z0-9_\-\.]+$", RegexOptions.Compiled);
        private static readonly Regex _area =
            new Regex(@"^STING-AREA::([A-Za-z0-9_\-\.]+)(?:::([A-Za-z0-9_\-\.]+))?$",
                      RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public const string AreaPatternReason =
            "name has the STING-AREA:: prefix but does not match STING-AREA::<area>[::<level>] "
          + "(allowed chars: A-Z 0-9 . _ -)";

        /// <summary>Which kind of box a name declares. A malformed name still reports its kind.</summary>
        public static ScopeBoxKind Classify(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ScopeBoxKind.Plain;
            if (name.StartsWith(AreaPrefix, StringComparison.OrdinalIgnoreCase)) return ScopeBoxKind.Area;
            if (name.StartsWith(SeedPrefix, StringComparison.OrdinalIgnoreCase)) return ScopeBoxKind.Seed;
            if (name.StartsWith(LocPrefix,  StringComparison.OrdinalIgnoreCase)) return ScopeBoxKind.Building;
            if (name.StartsWith(DrawingTypePrefix, StringComparison.OrdinalIgnoreCase)) return ScopeBoxKind.DrawingType;
            return ScopeBoxKind.Plain;
        }

        public static bool IsValidSegment(string s) => !string.IsNullOrEmpty(s) && _segment.IsMatch(s);

        /// <summary>
        /// Parse an area name. Returns false with <paramref name="reason"/> null when the
        /// name is not an area box at all, and non-null when it claims to be one and is
        /// malformed — the same contract as <see cref="ScopeBoxBinder.TryParseName"/>.
        /// </summary>
        public static bool TryParseArea(string name, out string area, out string level, out string reason)
        {
            area = level = reason = null;
            if (Classify(name) != ScopeBoxKind.Area) return false;
            var m = _area.Match(name);
            if (!m.Success) { reason = AreaPatternReason; return false; }
            area  = m.Groups[1].Value;
            level = m.Groups[2].Success ? m.Groups[2].Value : null;
            return true;
        }

        /// <summary>Compose an area name; empty when <paramref name="area"/> is not a legal segment.</summary>
        public static string ComposeArea(string area, string level = null)
        {
            if (!IsValidSegment(area)) return string.Empty;
            if (string.IsNullOrWhiteSpace(level)) return AreaPrefix + area;
            return IsValidSegment(level) ? AreaPrefix + area + "::" + level : string.Empty;
        }

        /// <summary>
        /// "STING-SEED::59.5x42". The size is a label for people; the planner measures
        /// the box. Whole metres print without decimals.
        /// </summary>
        public static string ComposeSeed(double widthM, double depthM)
            => SeedPrefix + Metres(widthM) + "x" + Metres(depthM);

        public static string Metres(double m)
        {
            double r = Math.Round(m, 1);
            return Math.Abs(r - Math.Round(r)) < 1e-9
                ? ((long)Math.Round(r)).ToString(CultureInfo.InvariantCulture)
                : r.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
