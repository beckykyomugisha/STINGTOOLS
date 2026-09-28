// StingTools — tag text sizes against ISO 3098, for the tag STYLE systems.
//
// The tag style matrix (TAG_{size}{style}_{colour}_BOOL label rows, type names
// "2.5BOLD_RED" and the catalogue's "2.5_BOLD_RED_Filled30_T2") is built at four
// sizes: 2, 2.5, 3 and 3.5 mm. Only 2.5 and 3.5 are ISO 3098 lettering heights.
// Every DEFAULT — discipline presets, colour schemes, rule-set fallbacks, the
// catalogue's pre-created variants, the scale tiers — used 2 mm (and 3 mm), so a
// project that never chose a size got non-ISO text, below the 2.5 mm minimum for
// notes on A0–A3.
//
// The rule, in one place: defaults are ISO (2.5 mm, 3.5 mm for emphasis). The 2 and
// 3 mm rows stay in the matrix, so a project that deliberately picks them can, and
// existing families keep working — but nothing picks them on its own. This matches
// DrawingType.EffectiveTagTextSizeMm, which sizes size-variant families the same way.
//
// Revit-free; unit-tested (StingTools.Tags.Tests).

using System;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class IsoTagText
    {
        /// <summary>The size every default uses: 2.5 mm.</summary>
        public const string DefaultSize = "2.5";

        /// <summary>The emphasis size (one ISO 3098 step up): 3.5 mm.</summary>
        public const string EmphasisSize = "3.5";

        /// <summary>Sizes the style matrix is built at that are ISO 3098 heights.</summary>
        public static readonly string[] IsoMatrixSizes = { "2.5", "3.5" };

        /// <summary>Sizes the matrix still carries for projects that chose them. Never a default.</summary>
        public static readonly string[] NonIsoMatrixSizes = { "2", "3" };

        /// <summary>True when <paramref name="size"/> ("2.5", "3.5mm", "5") is an ISO 3098 height.</summary>
        public static bool IsIso(string size)
        {
            var mm = Parse(size);
            return mm.HasValue && DrawingType.IsoLetteringHeightsMm.Any(h => Math.Abs(h - mm.Value) < 1e-9);
        }

        /// <summary>The ISO size a default should use in place of <paramref name="size"/>:
        /// an ISO size is kept; anything else becomes the smallest ISO size in the matrix at
        /// or above it (2 → 2.5, 3 → 3.5, 1.5 → 2.5), capped at 3.5; empty → 2.5.</summary>
        public static string ToIso(string size)
        {
            var mm = Parse(size);
            if (!mm.HasValue) return DefaultSize;
            if (IsIso(size) && IsoMatrixSizes.Contains(Format(mm.Value))) return Format(mm.Value);
            foreach (var s in IsoMatrixSizes)
                if (double.Parse(s, CultureInfo.InvariantCulture) >= mm.Value - 1e-9) return s;
            return IsoMatrixSizes[IsoMatrixSizes.Length - 1];
        }

        /// <summary>A style type name with its size made ISO. Both namings are handled:
        /// legacy "2BOLD_RED" → "2.5BOLD_RED", catalogue "2_NOM_BLACK_None_T1" →
        /// "2.5_NOM_BLACK_None_T1". A name with no leading size is returned unchanged.</summary>
        public static string ToIsoTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return typeName;
            string t = typeName.Trim();
            int i = 0;
            while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.')) i++;
            if (i == 0) return typeName;
            string size = t.Substring(0, i);
            if (!Parse(size).HasValue) return typeName;
            return ToIso(size) + t.Substring(i);
        }

        private static double? Parse(string size)
        {
            var s = (size ?? "").Trim();
            if (s.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 2);
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
                ? v : (double?)null;
        }

        private static string Format(double mm) => mm.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
