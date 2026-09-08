// ══════════════════════════════════════════════════════════════════════════
//  MaterialClassPlanner.cs — fill Revit's Material CLASS from the material's
//  own name, for the materials that have none.
//
//  WHY. Three parts of StingTools ask "what is this material?". Embodied carbon
//  and BOQ cost read Material.MaterialClass — a controlled field. The material
//  schedule and the PROD suffix pattern-match the NAME, which is free text.
//  Until the last two read Class as well, a model with Class left blank has its
//  carbon and cost answered by nothing at all.
//
//  Setting it by hand across 43 materials is the work this closes.
//
//  TWO RULES KEEP IT HONEST:
//    1. An EXISTING Class is never overwritten. Somebody chose it, and a bulk
//       tool that silently replaces a human's classification is worse than one
//       that does nothing.
//    2. A name that says nothing gets NOTHING. "Default Roof", "Material 12"
//       and "Finish - As Specified" are real material names from delivered
//       models; assigning them a plausible Class would launder a guess into a
//       controlled field, which is precisely the confidence this codebase keeps
//       having to unpick.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Materials
{
    public sealed class MaterialClassProposal
    {
        public string MaterialName = "";
        public string ExistingClass = "";
        /// <summary>Null when the name says nothing, or a class is already set.</summary>
        public string ProposedClass;
        public string Reason = "";

        public bool WillWrite => !string.IsNullOrEmpty(ProposedClass);
    }

    public static class MaterialClassPlanner
    {
        /// <summary>
        /// Revit's own Class vocabulary. These are the strings Revit ships and that its
        /// material browser groups by — inventing new ones would fragment the very field
        /// this exists to make dependable.
        /// </summary>
        public static readonly string[] RevitClasses =
        {
            "Concrete", "Masonry", "Metal", "Wood", "Glass", "Plastic", "Gypsum",
            "Ceramic", "Stone", "Paint", "Insulation", "Membrane", "Liquid", "Gas",
            "Textile", "Earth", "Generic",
        };

        // Longest, most specific needle first: "stone coated" must not read as Stone.
        private static readonly (string Needle, string Class)[] Map =
        {
            ("stone coated", "Metal"), ("galvanised", "Metal"), ("galvanized", "Metal"),
            ("reinforcement", "Metal"), ("rebar", "Metal"), ("steel", "Metal"),
            ("aluminium", "Metal"), ("aluminum", "Metal"), ("copper", "Metal"),
            ("brass", "Metal"), ("zinc", "Metal"), ("lead", "Metal"),

            // Masonry BEFORE concrete, or "Hollow Concrete Block" reads as Concrete and a
            // block wall is classified as in-situ. A concrete block is masonry; the word
            // "concrete" in its name describes what the block is made of, not what the
            // element is. Caught by a test, which is the only way an ordering bug of this
            // kind ever shows up.
            ("clay brick", "Masonry"), ("brick", "Masonry"),
            ("concrete block", "Masonry"), ("screen block", "Masonry"),
            ("block", "Masonry"), ("masonry", "Masonry"),

            ("concrete", "Concrete"), ("screed", "Concrete"), ("mortar", "Concrete"),
            ("grout", "Concrete"), ("terrazzo", "Concrete"),

            ("plaster", "Gypsum"), ("render", "Gypsum"), ("gypsum", "Gypsum"),
            ("plasterboard", "Gypsum"), ("drywall", "Gypsum"),

            ("porcelain", "Ceramic"), ("ceramic", "Ceramic"), ("tile", "Ceramic"),

            ("timber", "Wood"), ("hardwood", "Wood"), ("softwood", "Wood"),
            ("plywood", "Wood"), ("mvule", "Wood"), ("cypress", "Wood"),

            ("glass", "Glass"), ("glazing", "Glass"),

            ("upvc", "Plastic"), ("pvc", "Plastic"), ("hdpe", "Plastic"),
            ("polyethylene", "Plastic"), ("polypropylene", "Plastic"), ("vinyl", "Plastic"),

            ("mineral wool", "Insulation"), ("rockwool", "Insulation"),
            ("insulation", "Insulation"), ("polystyrene", "Insulation"),
            ("polyurethane", "Insulation"),

            ("bitumen", "Membrane"), ("dpm", "Membrane"), ("felt", "Membrane"),
            ("membrane", "Membrane"),

            ("granite", "Stone"), ("marble", "Stone"), ("limestone", "Stone"),
            ("sandstone", "Stone"), ("hardcore", "Stone"), ("aggregate", "Stone"),
            ("stone", "Stone"),

            ("paint", "Paint"), ("emulsion", "Paint"), ("enamel", "Paint"),
            ("coating", "Paint"), ("weatherguard", "Paint"),

            ("carpet", "Textile"), ("fabric", "Textile"),

            ("sand", "Earth"), ("murram", "Earth"), ("soil", "Earth"),
        };

        /// <param name="existingClass">What Revit already holds. Non-empty means leave it.</param>
        public static MaterialClassProposal Plan(string materialName, string existingClass)
        {
            var p = new MaterialClassProposal
            {
                MaterialName = materialName ?? "",
                ExistingClass = existingClass ?? "",
            };

            if (!string.IsNullOrWhiteSpace(existingClass)
                && !string.Equals(existingClass.Trim(), "Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                p.Reason = "already classified — not overwritten";
                return p;
            }

            if (string.IsNullOrWhiteSpace(materialName))
            { p.Reason = "unnamed"; return p; }

            foreach (var (needle, cls) in Map)
                if (materialName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    p.ProposedClass = cls;
                    p.Reason = $"name contains '{needle}'";
                    return p;
                }

            p.Reason = "the name names no substance — left blank rather than guessed";
            return p;
        }

        public static List<MaterialClassProposal> PlanAll(
            IEnumerable<(string Name, string ExistingClass)> materials)
            => (materials ?? Enumerable.Empty<(string, string)>())
               .Select(m => Plan(m.Name, m.ExistingClass)).ToList();

        public static string Summary(IReadOnlyCollection<MaterialClassProposal> ps)
        {
            if (ps == null || ps.Count == 0) return "No materials found.";
            int write = ps.Count(x => x.WillWrite);
            int had = ps.Count(x => !string.IsNullOrWhiteSpace(x.ExistingClass)
                                 && !string.Equals(x.ExistingClass.Trim(), "Unassigned", StringComparison.OrdinalIgnoreCase));
            int blank = ps.Count - write - had;
            return $"{ps.Count} material(s): {had} already classified and untouched, {write} will be set, "
                 + $"{blank} left blank because the name names no substance. Those {blank} are the ones to "
                 + "rename — a blank Class is visible, and a guessed one is not.";
        }
    }
}
