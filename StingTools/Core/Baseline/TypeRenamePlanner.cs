// ══════════════════════════════════════════════════════════════════════════
//  TypeRenamePlanner.cs — propose house-standard names for the types a model
//  already has, and refuse to guess.
//
//  The house standard tells you what to CALL things. It does nothing for the
//  model already delivered, whose types are called "Exterior_CreamWhite_230",
//  "Generic - 225mm" and "stepsr 7" — names that carry a colour, a thickness
//  and a typo, and that no rule can ever measure.
//
//  Renaming those by hand is the work this closes. But a rename is destructive
//  in a way a tag is not: it changes what a schedule, a filter and a view
//  template all key on. So this PLANS, and the caller applies.
//
//  THE RULE THAT MAKES IT SAFE: a proposal is only made when the model itself
//  supplies the words. The substance comes from the type's own thickest
//  STRUCTURAL LAYER MATERIAL — chosen by whoever built the model, not inferred
//  from a name — and the size from that layer's thickness. Where there is no
//  structural layer to read, this proposes NOTHING and says so. A renamer that
//  guessed would replace a bad name with a confident wrong one, which is worse:
//  a reader can see that "Generic - 225mm" says nothing, and cannot see that
//  "RC Slab 225" is a lie about a timber deck.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StingTools.Core.Baseline
{
    /// <summary>What the planner was able to read off one type.</summary>
    public sealed class TypeRenameInput
    {
        /// <summary>Revit category display name — Walls / Floors / Roofs / Ceilings.</summary>
        public string Category = "";
        public string CurrentName = "";
        /// <summary>Layers as the compound structure gives them, exterior to interior.</summary>
        public List<MaterialLayer> Layers = new List<MaterialLayer>();
        /// <summary>How many elements use this type. Renaming an unused type is noise.</summary>
        public int InstanceCount;
    }

    public sealed class TypeRenameProposal
    {
        public string Category = "";
        public string CurrentName = "";
        /// <summary>Null when the model does not say enough to name the type.</summary>
        public string ProposedName;
        /// <summary>Why — shown to the reader either way.</summary>
        public string Reason = "";
        public int InstanceCount;

        public bool IsProposal => !string.IsNullOrEmpty(ProposedName);
        /// <summary>True when the current name already matches what would be proposed.</summary>
        public bool AlreadyConforms =>
            IsProposal && string.Equals(CurrentName?.Trim(), ProposedName, StringComparison.OrdinalIgnoreCase);
    }

    public static class TypeRenamePlanner
    {
        private static readonly Dictionary<string, string> UseCode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Walls"] = "WL", ["Floors"] = "FL", ["Roofs"] = "RF", ["Ceilings"] = "CL",
            };

        /// <summary>
        /// Words that name a substance, longest first so "Hollow Concrete Block" beats
        /// "Concrete". Keyed on the MATERIAL name, which is the deliberate half of the
        /// model — nobody renames a material to make a schedule work.
        /// </summary>
        private static readonly (string Needle, string Word)[] Substance =
        {
            ("hollow concrete block", "Blockwork"), ("solid concrete block", "Blockwork"),
            ("concrete block", "Blockwork"), ("blockwork", "Blockwork"),
            ("clay brick", "Clay Brick"), ("brick", "Clay Brick"),
            ("screen block", "Screen Block"),
            ("concrete", "RC"),
            ("reinforcement", "RC"),
            ("timber", "Timber"), ("softwood", "Timber"), ("hardwood", "Timber"),
            ("plywood", "Plywood"),
            ("galvanised sheet", "Corrugated Sheet"), ("galvanized sheet", "Corrugated Sheet"),
            ("clay tile", "Clay Tile Roof"), ("stone coated", "Stone Coated Tile Roof"),
            ("steel", "Steel"),
            ("gypsum", "Plasterboard"), ("plasterboard", "Plasterboard"),
            ("stone", "Stone"),
        };

        /// <summary>Finish words, read off the FINISH layers the same way.</summary>
        private static readonly (string Needle, string Word)[] Finish =
        {
            ("porcelain", "Porcelain Tiled"), ("ceramic", "Ceramic Tiled"), ("tile", "Tiled"),
            ("terrazzo", "Terrazzo"),
            ("plaster", "Plastered"), ("render", "Plastered"),
            ("gypsum", "Boarded"), ("plasterboard", "Boarded"),
            ("felt", "Felt"), ("bitumen", "Felt"),
            ("paint", "Painted"), ("screed", "Screed Only"),
        };

        /// <summary>
        /// Propose one name, or explain why not.
        ///
        /// <para>Shape: <c>STING {use} - {substance} {size} - {finish}</c>, which is the
        /// order the rules read and the order a browser sorts usefully.</para>
        /// </summary>
        public static TypeRenameProposal Plan(TypeRenameInput input)
        {
            var p = new TypeRenameProposal
            {
                Category = input?.Category ?? "",
                CurrentName = input?.CurrentName ?? "",
                InstanceCount = input?.InstanceCount ?? 0,
            };

            if (input == null || !UseCode.TryGetValue(p.Category, out string use))
            { p.Reason = "not a layered host category"; return p; }

            var layers = (input.Layers ?? new List<MaterialLayer>())
                .Where(l => l != null && !string.IsNullOrWhiteSpace(l.MaterialName)).ToList();
            if (layers.Count == 0)
            { p.Reason = "no layer names a material — nothing to read"; return p; }

            // The core, by the same rule the PROD suffix uses: thickest structural layer,
            // else thickest of any function. One definition, not two.
            var core = layers.Where(l => l.IsStructure && l.ThicknessMm > 0)
                             .OrderByDescending(l => l.ThicknessMm).ThenBy(l => l.Index).FirstOrDefault()
                    ?? layers.OrderByDescending(l => l.ThicknessMm).ThenBy(l => l.Index).FirstOrDefault();

            string substance = Match(core.MaterialName, Substance);
            if (substance == null)
            {
                p.Reason = $"the core material '{core.MaterialName}' names no substance this "
                         + "recognises — rename the MATERIAL first (rule M1), then re-run";
                return p;
            }

            // Size from the core's own thickness, so it describes the thing rather than
            // repeating whatever number the old name happened to carry.
            string size = core.ThicknessMm >= 1
                ? " " + Math.Round(core.ThicknessMm).ToString("0", CultureInfo.InvariantCulture)
                : "";

            // Finish from the finish layers, not from the core.
            string finish = layers.Where(l => !ReferenceEquals(l, core))
                                  .Select(l => Match(l.MaterialName, Finish))
                                  .FirstOrDefault(f => f != null);

            p.ProposedName = $"STING {use} - {substance}{size}"
                           + (finish != null ? $" - {finish}" : "");
            p.Reason = finish != null
                ? $"core '{core.MaterialName}' + finish layer"
                : $"core '{core.MaterialName}'; no finish layer to read";
            return p;
        }

        public static List<TypeRenameProposal> PlanAll(IEnumerable<TypeRenameInput> inputs)
            => (inputs ?? Enumerable.Empty<TypeRenameInput>()).Select(Plan).ToList();

        /// <summary>
        /// One line per outcome, so a reader sees the shape of the run before opening
        /// the CSV — and so "nothing proposed" is never mistaken for "nothing wrong".
        /// </summary>
        public static string Summary(IReadOnlyCollection<TypeRenameProposal> ps)
        {
            if (ps == null || ps.Count == 0) return "No layered host types found.";
            int conform = ps.Count(x => x.AlreadyConforms);
            int propose = ps.Count(x => x.IsProposal && !x.AlreadyConforms);
            int cannot = ps.Count(x => !x.IsProposal);
            return $"{ps.Count} type(s): {conform} already conform, {propose} can be renamed, "
                 + $"{cannot} cannot be named from the model. The {cannot} are not failures of this "
                 + "tool — their materials do not say what they are, and a rename that guessed would "
                 + "replace a name a reader can see is empty with one they cannot.";
        }

        private static string Match(string text, (string Needle, string Word)[] table)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var (needle, word) in table)
                if (text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return word;
            return null;
        }
    }
}
