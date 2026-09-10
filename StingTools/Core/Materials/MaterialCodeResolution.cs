// ══════════════════════════════════════════════════════════════════════════
//  MaterialCodeResolution.cs — an element's MAT_CODE, resolved THROUGH its
//  material rather than asked of the element directly.
//
//  WHAT WAS WRONG. Four places built a rate request like this:
//
//      MatCode = ParameterHelpers.GetString(el, "MAT_CODE") ?? "",
//
//  where `el` is a wall, a floor, a pipe. MAT_CODE is bound to `Materials` and
//  to nothing else — CATEGORY_BINDINGS.csv:4693, agreeing with
//  PARAMETER_CATEGORIES.csv:510, FAMILY_PARAMETER_BINDINGS.csv:4022,
//  BINDING_COVERAGE_MATRIX.csv:611 and PARAMETER_REGISTRY.json:9971. Asking a
//  wall for it returns empty, every time, for every element ever costed. So
//  CsvRateProvider "Pass 3 — MATERIAL" (RateProviders.cs:323, confidence 85)
//  has never fired, and the rate chain has always fallen through to matching on
//  category and name — which is the root of every vocabulary divergence this
//  month has produced.
//
//  The binding is not wrong. The READ was in the wrong place. The code belongs
//  on the material, because that is what the register row is ABOUT; putting it
//  on thousands of instances would copy a fact they already have through their
//  type, and would drift the moment a type changed.
//
//  ── THE DECISION, WHICH IS THE PART WORTH TESTING ─────────────────────────
//  Layers in, code out. Deliberately Revit-free: reading a parameter is not
//  where this goes wrong, choosing WHICH material answers is.
//
//    1. The PRIMARY layer material, chosen by PrimaryMaterialSelector — the
//       thickest structural layer, falling back to the thickest of any
//       function. One definition, the one #873 settled; a second would fork it.
//    2. Only when the layers name no material at all: the element's single
//       material, where Revit exposes one.
//    3. Only then: the element's own MAT_CODE, for the day somebody binds it
//       there. This is a fallback so that day needs no second visit — it is not
//       an invitation to do it.
//    4. Empty. AND EMPTY STAYS EMPTY. A guessed code is worse than no code:
//       Pass 3 is the most specific lookup in the chain and it would carry the
//       guess at confidence 85.
//
//  ── THE ONE SUBTLETY ──────────────────────────────────────────────────────
//  When the layers DO name a primary material and that material has no code,
//  this stops. It does NOT then try the element's first material. That fall-
//  through would hand back the FINISH SKIN — a 230 mm rendered masonry wall
//  answering "gypsum" — which is precisely the defect #873 was opened for. A
//  layered element is answered by its core or by nothing.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;

namespace StingTools.Core.Materials
{
    /// <summary>Where a resolved MAT_CODE came from. Carried so a caller can say
    /// WHY a rate matched, rather than only that it did.</summary>
    public enum MatCodeSource
    {
        /// <summary>Nothing answered. Code is empty and must stay empty.</summary>
        None,
        /// <summary>The primary layer material of a compound element.</summary>
        PrimaryLayerMaterial,
        /// <summary>The element's single material — families, MEP, anything with no
        /// compound structure.</summary>
        SingleMaterial,
        /// <summary>MAT_CODE read off the element itself. Only reachable if a future
        /// change binds the parameter to a host category.</summary>
        ElementParameter,
    }

    public sealed class MatCodeResolution
    {
        public string Code = "";
        /// <summary>The material the code came from, or "" when it did not come from one.</summary>
        public string MaterialName = "";
        public MatCodeSource Source = MatCodeSource.None;

        public bool Resolved => Code.Length > 0;

        public static readonly MatCodeResolution Nothing = new MatCodeResolution();

        public override string ToString()
            => Resolved ? Code + " (" + Source + (MaterialName.Length > 0 ? " · " + MaterialName : "") + ")"
                        : "no MAT_CODE";
    }

    public static class MaterialCodeResolution
    {
        /// <summary>
        /// Resolve an element's MAT_CODE from what its materials say.
        /// </summary>
        /// <param name="layers">The element type's compound structure, flattened.
        /// Null or empty for anything that is not a layered host.</param>
        /// <param name="codeByMaterialName">MAT_CODE per material NAME, as read off the
        /// document's Material elements. Case-insensitive by construction of the caller;
        /// a name absent from it simply has no code.</param>
        /// <param name="singleMaterialName">The element's own material, used only when
        /// the layers name none. See the file header for why it is not a fall-through.</param>
        /// <param name="elementOwnCode">MAT_CODE read off the element. Empty today.</param>
        public static MatCodeResolution Resolve(
            IReadOnlyList<MaterialLayer> layers,
            IReadOnlyDictionary<string, string> codeByMaterialName,
            string singleMaterialName,
            string elementOwnCode)
        {
            string primary = PrimaryMaterialSelector.Select(layers);

            if (!string.IsNullOrWhiteSpace(primary))
            {
                // The layers answered. Whatever comes back is the answer — including
                // "no code", which does NOT reopen the question with a different
                // material. See the file header.
                string code = Lookup(codeByMaterialName, primary);
                return code.Length > 0
                    ? new MatCodeResolution
                      {
                          Code = code,
                          MaterialName = primary.Trim(),
                          Source = MatCodeSource.PrimaryLayerMaterial,
                      }
                    : FromElementParameter(elementOwnCode);
            }

            if (!string.IsNullOrWhiteSpace(singleMaterialName))
            {
                string code = Lookup(codeByMaterialName, singleMaterialName);
                if (code.Length > 0)
                    return new MatCodeResolution
                    {
                        Code = code,
                        MaterialName = singleMaterialName.Trim(),
                        Source = MatCodeSource.SingleMaterial,
                    };
            }

            return FromElementParameter(elementOwnCode);
        }

        private static MatCodeResolution FromElementParameter(string elementOwnCode)
        {
            string own = (elementOwnCode ?? "").Trim();
            return own.Length > 0
                ? new MatCodeResolution { Code = own, Source = MatCodeSource.ElementParameter }
                : MatCodeResolution.Nothing;
        }

        private static string Lookup(IReadOnlyDictionary<string, string> map, string name)
        {
            if (map == null || string.IsNullOrWhiteSpace(name)) return "";
            return map.TryGetValue(name.Trim(), out string code) ? (code ?? "").Trim() : "";
        }
    }
}
