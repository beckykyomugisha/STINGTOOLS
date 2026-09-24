// StingTools — picking the tag size variant for a drawing.
//
// A label's text size is a TYPE property that no parameter can drive, so a
// selectable tag size means one tag per size: DrawingType.EffectiveTagTextSizeMm
// already decides the size a drawing wants (explicit tagTextSizeMm, else from
// the scale — 2.5 mm at 1:50), and nothing used it. Tags were always placed at
// whatever size the base family was authored at.
//
// Two authoring shapes are accepted, because which one a project uses is an
// authoring choice, not a code decision:
//   * a FAMILY per size — "<base family> 2.5mm" (the ROADMAP's plan: build the
//     master once, Save As per size changing only the label text size);
//   * a TYPE per size inside the base family, named "2.5mm".
// A family variant wins over a type variant; the nearest AVAILABLE size wins
// over an exact size that was never built (DrawingType.NearestAvailableTagSizeMm),
// so a 1:200 plan with only 2.5 and 3.5 built gets 2.5, not nothing.
//
// When no variant of the base family is loaded at all, the base is used exactly
// as before — this is inert until variants are authored, so it can ship now.
//
// Revit-free: names in, choice out.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.Drawing
{
    public static class TagSizeVariant
    {
        /// <summary>"STING - Door Tag" + 2.5 → "STING - Door Tag 2.5mm".</summary>
        public static string FamilyName(string baseFamily, double mm)
            => (baseFamily ?? "").TrimEnd() + " " + DrawingType.TagSizeToken(mm);

        /// <summary>The size a family- or type-name token carries: "2.5mm" → 2.5.
        /// Null when the text is not a size token.</summary>
        public static double? ParseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            var t = token.Trim();
            if (!t.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) return null;
            return double.TryParse(t.Substring(0, t.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
                ? v : (double?)null;
        }

        /// <summary>
        /// Size a TYPE name carries, in either naming the library uses: "2.5mm", or the tag
        /// style catalogue's "{size}_{style}_{colour}_{arrow}_T{tier}" ("2.5_NOM_BLACK_Open30_T2",
        /// TagStyleCatalogue.CanonicalTypeName). Null when the name carries no size. The
        /// catalogue form was not recognised at first, so families built to the catalogue
        /// convention (the specialist tag build sheet) never had their size chosen.
        /// </summary>
        public static double? SizeOfTypeName(string typeName)
        {
            var direct = ParseToken(typeName);
            if (direct.HasValue) return direct;
            var t = (typeName ?? "").Trim();
            int us = t.IndexOf('_');
            if (us <= 0) return null;
            return double.TryParse(t.Substring(0, us), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
                ? v : (double?)null;
        }

        /// <summary>
        /// Everything in a type name except its size: "2.5_BOLD_RED_Open30_T2" → "BOLD_RED_Open30_T2";
        /// "2.5mm" and names with no size → "". Two types are size variants of each other only
        /// when this matches — switching a bold red tag to "the 2 mm type" must not make it
        /// normal black.
        /// </summary>
        public static string StyleOfTypeName(string typeName)
        {
            var t = (typeName ?? "").Trim();
            if (ParseToken(t).HasValue) return "";
            int us = t.IndexOf('_');
            if (us > 0 && SizeOfTypeName(t).HasValue) return t.Substring(us + 1);
            return "";
        }

        /// <summary>Size of a family named "<paramref name="baseFamily"/> &lt;n&gt;mm", or null.</summary>
        public static double? SizeOfFamilyVariant(string familyName, string baseFamily)
        {
            if (string.IsNullOrEmpty(familyName) || string.IsNullOrEmpty(baseFamily)) return null;
            var prefix = baseFamily.TrimEnd() + " ";
            if (!familyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            return ParseToken(familyName.Substring(prefix.Length));
        }

        public enum Kind { None, Family, Type }

        /// <summary>
        /// Choose the variant for <paramref name="dt"/>. <paramref name="familyVariantSizes"/>:
        /// sizes of loaded "&lt;base&gt; &lt;n&gt;mm" families; <paramref name="typeVariantSizes"/>:
        /// sizes of "&lt;n&gt;mm" types inside the base family. Returns Kind.None (use the
        /// base as-is) when neither list has anything.
        /// </summary>
        public static (Kind Kind, double SizeMm) Choose(DrawingType dt,
            IEnumerable<double> familyVariantSizes, IEnumerable<double> typeVariantSizes)
        {
            if (dt == null) return (Kind.None, 0);
            var fam = (familyVariantSizes ?? Enumerable.Empty<double>()).Where(s => s > 0).Distinct().ToList();
            if (fam.Count > 0) return (Kind.Family, dt.NearestAvailableTagSizeMm(fam));
            var typ = (typeVariantSizes ?? Enumerable.Empty<double>()).Where(s => s > 0).Distinct().ToList();
            if (typ.Count > 0) return (Kind.Type, dt.NearestAvailableTagSizeMm(typ));
            return (Kind.None, 0);
        }
    }
}
