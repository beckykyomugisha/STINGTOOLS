// ══════════════════════════════════════════════════════════════════════════
//  SubstanceVocabulary.cs — one list of the words that name a timber-family
//  material, for the three places that used to keep their own.
//
//  THE DEFECT. Four independent word-lists answered "is this timber", and they
//  disagreed BOTH WAYS on a carbon figure:
//
//    Core/Materials/MaterialClassPlanner   timber hardwood softwood plywood mvule
//                                          cypress wood oak maple bamboo parquet
//                                          mdf hdf cork
//    BOQ/BiogenicCarbon                    timber wood softwood hardwood plywood
//                                          "ply " mdf clt glulam
//    Core/Baseline/TypeRenamePlanner       timber softwood hardwood plywood
//    BOQ/UgCarbonFactors                   reads Material.MaterialClass — the
//                                          controlled field, and therefore right
//
//  Measured over every distinct name the register and the delivered-model corpus
//  hold between them -- SubstanceVocabularyTests.AllMaterialNames() pools them, and
//  that helper is the ONE place the size is computed -- the three disagreed on a
//  large minority. Two examples:
//
//    MDF CEILING PANEL 12MM   class Wood · biogenic credit YES · not timber to the renamer
//    SOLID BAMBOO 14MM        class Wood · biogenic credit NO  · not timber to the renamer
//
//  ── WHAT CHANGED ON A CARBON NUMBER, NAMED ─────────────────────────────────
//
//  `BiogenicCarbon` matched by SUBSTRING and carried `"ply "` with a trailing
//  space. Across the whole pooled corpus that needle matched exactly three, and every one
//  was wrong:
//
//      PVC SINGLE PLY 1.5MM              a PVC roofing membrane
//      SUPPLY REGISTER 150X150MM 1-WAY   an air diffuser   (sup-PLY )
//      SUPPLY REGISTER 200X100MM 2-WAY   an air diffuser
//
//  Two air diffusers and a plastic membrane were earning a biogenic carbon
//  credit. `ply` is dropped: it has never once been right here, and `plywood`
//  covers what it was reaching for.
//
//  Fifteen names GAIN a credit, all of them bio-based, and the list is short
//  enough to read: BAMBOO PLANK · CHEVRON PARQUET 15MM · CORK TILE ·
//  CORK TILE 6MM · Cork - Plastic · FLOOR LAMINATE OAK-DARK/-GREY/-LIGHT/-MEDIUM ·
//  HDF CORE · HERRINGBONE PARQUET 15MM · MAPLE FLOORING · Oak Flooring ·
//  Roca - TENET - 402 City Oak · SOLID BAMBOO 14MM.
//
//  TWO OF THOSE FIFTEEN ARE QUESTIONABLE AND ARE NOT HIDDEN. `Roca - TENET -
//  402 City Oak` is a sanitaryware colour, not a timber; `Cork - Plastic` is a
//  cork-look plastic. Both are the colour-word collision already documented in
//  MaterialClassPlanner's header, and both are single library appearances rather
//  than modelled quantities — but a reviewer should know they moved.
//
//  ── WHOLE WORDS, WITH STEMS WHERE THE DATA HAS THEM ────────────────────────
//  Matching is whole-word (PatternMatch), because "sup-PLY" and "bRIDGE" are the
//  defect this codebase keeps re-finding. Whole-word costs the suffix forms, so
//  the ones the data actually contains are listed: `wooden` (WOODEN SPORTS FLOOR
//  22MM) and `barnwood` (BARNWOOD SIDING 22MM) both lost their credit without
//  them, and both are real timber.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;

namespace StingTools.Core.Materials
{
    public static class SubstanceVocabulary
    {
        /// <summary>
        /// Every word that names a timber-family material — the union of what the four
        /// consumers separately believed, minus <c>ply</c>, which matched nothing true.
        ///
        /// <para>Order is irrelevant here: this answers a yes/no, not a precedence.
        /// The ORDERED tables that decide a Revit class or a PROD code stay where they
        /// are and draw their wood rows from this.</para>
        /// </summary>
        public static readonly string[] Wood =
        {
            // generic
            "timber", "wood", "wooden", "barnwood",
            // grades and engineered products
            "hardwood", "softwood", "plywood", "mdf", "hdf", "clt", "glulam",
            // species and named forms
            "mvule", "cypress", "oak", "maple", "bamboo", "cork", "parquet",
        };

        /// <summary>
        /// True when the name states a timber-family material. Whole-word, so
        /// "SUPPLY REGISTER" is not plywood and "DUCTILE" is not a tile.
        ///
        /// <para>This is a question about the NAME. What an ordered class table then does
        /// with a name carrying two substance words — "Cork - Plastic", "VINYL LVT WOOD
        /// OAK-DARK" — is a precedence decision and belongs there, not here.</para>
        /// </summary>
        public static bool IsWood(string materialName)
        {
            if (string.IsNullOrWhiteSpace(materialName)) return false;
            foreach (string w in Wood)
                if (PatternMatch.Contains(materialName, w)) return true;
            return false;
        }

        /// <summary>The wood words paired with a caller's own label, so an ordered table
        /// can splice them in without restating the list. <c>plywood</c> is emitted FIRST
        /// when <paramref name="plywoodLabel"/> differs, because TypeRenamePlanner routes
        /// it to a different PROD key than the rest.</summary>
        public static IEnumerable<(string Needle, string Word)> WoodRows(
            string label, string plywoodLabel = null)
        {
            if (!string.IsNullOrEmpty(plywoodLabel) && plywoodLabel != label)
                yield return ("plywood", plywoodLabel);
            foreach (string w in Wood)
            {
                if (!string.IsNullOrEmpty(plywoodLabel) && plywoodLabel != label
                    && string.Equals(w, "plywood", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return (w, label);
            }
        }
    }
}
