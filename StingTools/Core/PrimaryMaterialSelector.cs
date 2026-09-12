// ══════════════════════════════════════════════════════════════════════════
//  PrimaryMaterialSelector.cs — which layer of a compound element IS the
//  element, for the purpose of naming what it is made of.
//
//  Found on a real model. Every 230 mm exterior wall came out as WL-GYP:
//
//      48 x  CLAY BRICK VILLAGE LARGE (295x150x130MM)  ->  WL-MAS   correct
//      11 x  Exterior_CreamWhite_230 2                 ->  WL-GYP   wrong
//       5 x  Exterior_BrownWhite_230                   ->  WL-GYP   wrong
//
//  A 230 mm exterior wall is masonry with a plaster render, and the override
//  table cannot be at fault: `\bbrick|\bmasonry|\bblock` sits ABOVE
//  `\bplaster|\bgypsum` in the file, so first-match-wins would have said MAS
//  had it seen the brick. It never saw the brick. The reader took
//  GetMaterialIds(false) and returned the FIRST NON-ZERO id, which on a
//  compound wall is whichever layer happens to come first — the finish skin.
//
//  The same shape showed on the roofs: `Generic - 225mm` took no suffix while
//  `Generic - 225mm 2` took -BIT. Two roofs of the same nominal build, told
//  apart by which layer sorted first.
//
//  THIS IS PHASE 254's LESSON ONE LAYER ALONG. That phase found no layer
//  reader accepted STRUCTURE, so a shingle carried on a structure layer was
//  invisible to the take-off. The take-off learned it; the PROD suffix did
//  not. A wall's product is its core, not its paint.
//
//  Revit-free on purpose: the Revit half reads CompoundStructure and hands the
//  layers here, so the CHOICE is testable without a Document. Every defect
//  this area has produced has been a wrong choice, not a failed read.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core
{
    /// <summary>One layer of a compound element, flattened out of Revit's
    /// <c>CompoundStructureLayer</c> so the selection rule can be tested.</summary>
    public sealed class MaterialLayer
    {
        /// <summary>Position in the compound structure, exterior-to-interior. The
        /// tie-break, so the answer is stable rather than dependent on sort order.</summary>
        public int Index;

        /// <summary>Layer material name, or null/empty when the layer has none.</summary>
        public string MaterialName;

        /// <summary>Layer thickness in millimetres. Membranes are 0.</summary>
        public double ThicknessMm;

        /// <summary>True when Revit's layer function is Structure.</summary>
        public bool IsStructure;
    }

    public static class PrimaryMaterialSelector
    {
        /// <summary>
        /// The material that names the element.
        ///
        /// <list type="number">
        /// <item>The THICKEST STRUCTURE layer. A wall is what holds it up.</item>
        /// <item>Failing that, the thickest layer of any function — a build-up with
        /// no declared structure is still mostly one thing.</item>
        /// <item>Ties break on the lowest index, so the answer never depends on the
        /// order a collector happened to return.</item>
        /// </list>
        ///
        /// <para>Zero-thickness layers (membranes, vapour barriers) lose on thickness
        /// like anything else — being listed first is exactly how a bitumen membrane
        /// came to name a roof, and ranking by thickness is what stops it. They can
        /// still answer when they are all there is, which is correct: the caller's
        /// fallback would pick the same layer with an extra Revit read.</para>
        ///
        /// <para>Returns null when no layer carries a material — the caller then
        /// falls back, rather than this inventing a name.</para>
        /// </summary>
        public static string Select(IReadOnlyList<MaterialLayer> layers)
        {
            if (layers == null || layers.Count == 0) return null;

            // Pass 1: structural layers of real thickness. The `> 0` is load-bearing:
            // a zero-width layer flagged Structure (a barrier, a modelled-in reinforcement
            // plane) would otherwise beat the actual core on the index tie-break.
            MaterialLayer best = Best(layers, l => l.IsStructure && l.ThicknessMm > 0);

            // Pass 2: anything. Thickest wins, so membranes lose without needing a rule
            // of their own — a `ThicknessMm > 0` filter here would be DEAD CODE, since a
            // zero-thickness layer can only win when every layer is zero, which this same
            // pass already handles on the tie-break. (Found by mutating it: removing the
            // filter changed nothing, which is the definition of a line that is not there.)
            if (best == null) best = Best(layers, l => true);

            return best?.MaterialName;
        }

        private static MaterialLayer Best(IReadOnlyList<MaterialLayer> layers, Func<MaterialLayer, bool> ok)
        {
            MaterialLayer best = null;
            for (int i = 0; i < layers.Count; i++)
            {
                var l = layers[i];
                if (l == null || string.IsNullOrWhiteSpace(l.MaterialName)) continue;
                if (!ok(l)) continue;

                if (best == null
                    || l.ThicknessMm > best.ThicknessMm
                    || (l.ThicknessMm == best.ThicknessMm && l.Index < best.Index))
                    best = l;
            }
            return best;
        }
    }
}
