// MasonryClassifier.cs — G-15. Brick vs block, decided from DATA, Revit-free.
//
// Split out of CompoundTakeoffBuilder so the decision is testable without a
// Document, matching how CompoundTakeoff already separates the arithmetic from the
// Revit-facing gatherer.

using System;

namespace StingTools.BOQ.Takeoff
{
    public static class MasonryClassifier
    {
        /// <summary>
        /// Brick or block, from dimensional evidence rather than a substring of the
        /// material name.
        /// </summary>
        /// <param name="blockSizeCanon">
        /// Canonicalised block size ("440x215"), from <c>BLE_BLOCK_SIZE_TXT</c> or
        /// inferred from the type name. Null/empty when there is none.
        /// </param>
        /// <param name="bondCanon">
        /// Canonicalised brick bond ("STRETCHER"), from <c>BLE_BRICK_BOND_TYPE_TXT</c>
        /// or inferred from the type name. Null/empty when there is none.
        /// </param>
        /// <param name="materialName">Primary material name; used only as a last resort.</param>
        /// <remarks>
        /// <para>
        /// The old test was <c>material.Contains("brick")</c>, so "Brick-faced blockwork"
        /// was measured with brick bond ratios — on 200mm work that is 0.025 m³/m² against
        /// the correct 0.011, a 2.27x over-measure with nothing flagged either side.
        /// </para>
        /// <para>
        /// The obvious fix — "a bond type present means brick" — does NOT hold, and this
        /// was checked before implementing. CompoundTakeoffBuilder.InferBrickBond exists
        /// precisely because a real brick wall may carry no bond PARAMETER and resolve it
        /// from the type name instead; a bond-presence test would call every such wall
        /// block. That is why bond evidence here means "parameter OR inferred".
        /// </para>
        /// <para>
        /// Block evidence is tested FIRST because a block size is unambiguous and a brick
        /// wall never carries one, which makes it the reliable discriminator. The material
        /// name survives only where there is no dimensional evidence either way, and even
        /// then "block" anywhere in the name vetoes "brick" — which is what catches the
        /// composite descriptions this defect came from.
        /// </para>
        /// </remarks>
        public static bool IsBrick(string blockSizeCanon, string bondCanon, string materialName)
        {
            if (!string.IsNullOrWhiteSpace(blockSizeCanon)) return false;   // 1. block wins
            if (!string.IsNullOrWhiteSpace(bondCanon)) return true;         // 2. then brick

            string m = (materialName ?? "").ToLowerInvariant();             // 3. last resort
            if (m.Contains("block")) return false;
            return m.Contains("brick");
        }
    }
}
