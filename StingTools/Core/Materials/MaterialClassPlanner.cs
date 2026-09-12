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
//  THREE RULES KEEP IT HONEST:
//    1. An EXISTING Class is never overwritten. Somebody chose it, and a bulk
//       tool that silently replaces a human's classification is worse than one
//       that does nothing.
//    2. A name that says nothing gets NOTHING. "Default Roof", "Material 12"
//       and "Finish - As Specified" are real material names from delivered
//       models; assigning them a plausible Class would launder a guess into a
//       controlled field, which is precisely the confidence this codebase keeps
//       having to unpick.
//    3. A needle matches a WORD, not a run of letters, and SUBSTANCE outranks
//       FORM. Both halves of rule 3 were bought with a bad write.
//
//  WHAT RULE 3 COST (2026-09-08, on a 1,815-material delivered model; the whole
//  run is checked in at
//  StingTools.Tags.Tests/Fixtures/material_names_20260908.csv):
//
//    * A plain IndexOf gave 32 materials the class Gypsum "because the name
//      contains 'render'". Six were AccuRender library appearances — chrome,
//      transparent plastics, solid colours — where "render" is a substring of
//      the LIBRARY's name. Twenty-six were DWG-import placeholders literally
//      called "Render Material 128-128-128", where "render" is a whole word and
//      means visualisation, not cement render. The first six are what whole-word
//      matching is for; the last twenty-six are what NamesNoSubstance is for.
//      Whole-word matching alone would have left all 26 wrong.
//    * Nine materials were classified Ceramic because the name contains "tile" —
//      CARPET TILE, CORK TILE, RUBBER TILE, VINYL TILE, GRANITE TILE, LIMESTONE
//      TILE, MARBLE TILE, SLATE TILE, TRAVERTINE TILE. The model's own data
//      proved it: GRANITE SLAB was called Stone and GRANITE TILE Ceramic; SHEET
//      VINYL was Plastic and VINYL TILE Ceramic. A tile is a SHAPE. It says
//      nothing about what the thing is made of, so it is evaluated LAST, after
//      every substance word has had its turn.
//
//  All 41 were written into the user's model before the defect was found, which
//  is why MaterialClassRevertPlanner exists.
//
//  TWO ORDERING RULES THE SAME CORPUS FORCED, both easy to undo by accident:
//    * PLASTIC is evaluated before WOOD. Thirty-one materials are named
//      "VINYL LVT WOOD OAK-DARK" and friends — luxury vinyl tile printed with a
//      wood grain. The finish technology names the substance; the pattern it
//      imitates does not. Same reason Ceramic already beat Stone on
//      "FLOOR PORCELAIN WOOD-OAK".
//    * INSULATION and MEMBRANE are evaluated before PLASTIC, or
//      "Insulation - PVC Jacketed Fiberglass" becomes a plastic and
//      "EPDM RUBBER MEMBRANE 1.5MM" stops being a membrane the moment "rubber"
//      is added below.
//
//  A KNOWN LIMIT, stated rather than hidden: a colour word that is also a
//  substance still wins. "ROOF CLAY-TILE SLATE-GREY" reads as Stone and
//  "Roca - TENET - 402 City Oak" reads as Wood. Rule 1 keeps both harmless here
//  — every material of that shape in the corpus already carries a human's Class
//  — but a new model could hit it, and the honest fix is a better material name,
//  not a longer list of exceptions.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using StingTools.Core.MaterialSchedule;

namespace StingTools.Core.Materials
{
    public sealed class MaterialClassProposal
    {
        public string MaterialName = "";
        public string ExistingClass = "";
        /// <summary>Null when the name says nothing, or a class is already set.</summary>
        public string ProposedClass;
        public string Reason = "";

        // ── The register, as a CHALLENGER ────────────────────────────────────
        // Populated when a registry is passed to Plan. It does NOT change
        // ProposedClass, on purpose: measured over all 1,279 register rows the
        // BLE_APP-IDENTITY-CLASS column classifies by TRADE, so a skirting is
        // "Wood" whatever it is made of and a floor topping is "Concrete" even
        // when it is epoxy resin. It is right about cement plaster and masonry
        // paint and wrong about skirtings and screed, and agreement rate does
        // not separate the two — see MaterialRegistry's header for the numbers.
        // So it is carried alongside the answer for a reviewer to see, and
        // MaterialRegisterReconciliation is where the two get settled.

        /// <summary>MAT_CODE of the register row for this name, or null when the register
        /// does not contain it. Provenance: a reviewer can go and read the row.</summary>
        public string RegisterCode;
        /// <summary>What the register's class column says, translated to Revit's
        /// vocabulary — or null when it names a role rather than a substance.</summary>
        public string RegisterClass;
        /// <summary>True when the register has a substance opinion and it differs from
        /// <see cref="ProposedClass"/>. Not an error; a row for a human to look at.</summary>
        public bool RegisterDisagrees =>
            RegisterClass != null && ProposedClass != null
            && !string.Equals(RegisterClass, ProposedClass, StringComparison.OrdinalIgnoreCase);

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

        /// <summary>
        /// Names that carry a substance word and are not a material at all. Checked
        /// BEFORE the needle loop, because whole-word matching cannot help here: in
        /// "Render Material 128-128-128" the word "render" really is a whole word — it
        /// just means the renderer. Twenty-six of those arrived from a DWG import and
        /// every one of them was classified Gypsum.
        ///
        /// This is the same kind of statement as "notAProductFamilies" in
        /// STING_PROD_EXCLUSIONS.json: somebody looked at it and said what it is. Add to
        /// it only with a name you have actually seen, and say where you saw it.
        /// </summary>
        public static readonly string[] NamesNoSubstance =
        {
            "render material",   // DWG-import RGB placeholder, e.g. "Render Material 0-41-165"
        };

        // Longest, most specific needle first: "stone coated" must not read as Stone.
        //
        // W3: the WOOD rows are spliced in from SubstanceVocabulary rather than listed
        // here, because three separate places used to keep their own copy and they
        // disagreed on a large minority of real material names (counted 90 here and 85
        // in the tests, which is the point: it was never one fact) — on a carbon figure, since
        // BiogenicCarbon was one of them. The ORDER is unchanged and still matters:
        // wood sits after Plastic, so a vinyl printed with a wood grain is vinyl.
        private static readonly (string Needle, string Class)[] Map = BuildMap();

        private static (string Needle, string Class)[] BuildMap()
        {
            var m = new List<(string Needle, string Class)>();
            m.AddRange(new (string, string)[]
            {
            ("stone coated", "Metal"), ("galvanised", "Metal"), ("galvanized", "Metal"),
            ("zincalume", "Metal"), ("reinforcement", "Metal"), ("rebar", "Metal"),
            ("steel", "Metal"), ("aluminium", "Metal"), ("aluminum", "Metal"),
            ("copper", "Metal"), ("brass", "Metal"), ("zinc", "Metal"), ("lead", "Metal"),
            ("chrome", "Metal"), ("iron", "Metal"),

            // Masonry BEFORE concrete, or "Hollow Concrete Block" reads as Concrete and a
            // block wall is classified as in-situ. A concrete block is masonry; the word
            // "concrete" in its name describes what the block is made of, not what the
            // element is. Caught by a test, which is the only way an ordering bug of this
            // kind ever shows up.
            ("clay brick", "Masonry"), ("brick", "Masonry"),
            ("concrete block", "Masonry"), ("screen block", "Masonry"),
            ("block", "Masonry"), ("blockwork", "Masonry"), ("masonry", "Masonry"),

            ("concrete", "Concrete"), ("screed", "Concrete"), ("mortar", "Concrete"),
            ("grout", "Concrete"), ("terrazzo", "Concrete"),

            ("plaster", "Gypsum"), ("render", "Gypsum"), ("gypsum", "Gypsum"),
            ("plasterboard", "Gypsum"), ("drywall", "Gypsum"),

            ("porcelain", "Ceramic"), ("ceramic", "Ceramic"),

            // Insulation and membrane before plastic — see the header. A PVC-jacketed
            // insulation is insulation and an EPDM roofing membrane is a membrane; the
            // polymer each is made from is the less useful of the two facts.
            ("mineral wool", "Insulation"), ("rockwool", "Insulation"),
            ("insulation", "Insulation"), ("polystyrene", "Insulation"),
            ("polyurethane", "Insulation"),

            ("bitumen", "Membrane"), ("dpm", "Membrane"), ("felt", "Membrane"),
            ("membrane", "Membrane"),

            // Plastic before wood — see the header. "rubber" and "linoleum" are not
            // chemically plastics, and cork is a plant tissue rather than a wood; Revit's
            // vocabulary has no better bucket for a resilient floor finish, and splitting
            // one trade across three classes would help nobody. Stated so the next reader
            // knows it was a decision and not an oversight.
            ("upvc", "Plastic"), ("cpvc", "Plastic"), ("pvc", "Plastic"),
            ("hdpe", "Plastic"), ("polyethylene", "Plastic"), ("polypropylene", "Plastic"),
            ("polyvinyl", "Plastic"), ("vinyl", "Plastic"), ("lvt", "Plastic"),
            ("rubber", "Plastic"), ("linoleum", "Plastic"), ("plastic", "Plastic"),

            });

            // ── The one wood list, shared with BiogenicCarbon and TypeRenamePlanner ──
            m.AddRange(SubstanceVocabulary.WoodRows("Wood"));

            m.AddRange(new (string, string)[]
            {
            ("glass", "Glass"), ("glazing", "Glass"),

            // Textile before stone, or "Textile - Slate Blue" is a rock.
            ("carpet", "Textile"), ("fabric", "Textile"), ("textile", "Textile"),

            ("granite", "Stone"), ("marble", "Stone"), ("limestone", "Stone"),
            ("sandstone", "Stone"), ("bluestone", "Stone"), ("cobblestone", "Stone"),
            ("flagstone", "Stone"), ("slate", "Stone"), ("travertine", "Stone"),
            ("quartzite", "Stone"), ("hardcore", "Stone"), ("aggregate", "Stone"),
            ("stone", "Stone"),

            ("paint", "Paint"), ("painted", "Paint"), ("emulsion", "Paint"),
            ("enamel", "Paint"), ("coating", "Paint"), ("weatherguard", "Paint"),

            ("sand", "Earth"), ("murram", "Earth"), ("soil", "Earth"),

            // ── FORM, and nothing but form. Evaluated after every substance word above,
            //    because a tile is a shape: CARPET TILE is textile, CORK TILE is wood,
            //    SLATE TILE is stone. A name whose only content word is "tile" is a
            //    ceramic tile by convention — the one default this table makes, made
            //    last and in the open rather than by an accident of ordering.
            ("tile", "Ceramic"), ("tiles", "Ceramic"),
            });
            return m.ToArray();
        }

        /// <param name="existingClass">What Revit already holds. Non-empty means leave it.</param>
        /// <param name="registry">
        /// The corporate material register, or null. When given, the row for this material
        /// is recorded on the proposal as PROVENANCE and as a CHALLENGE — it never becomes
        /// the answer. See <see cref="MaterialClassProposal.RegisterClass"/>.
        /// </param>
        public static MaterialClassProposal Plan(
            string materialName, string existingClass, MaterialRegistry registry = null)
        {
            var p = new MaterialClassProposal
            {
                MaterialName = materialName ?? "",
                ExistingClass = existingClass ?? "",
            };

            var reg = registry?.ByName(materialName);
            if (reg != null)
            {
                p.RegisterCode = reg.Code;
                p.RegisterClass = MaterialRegistry.ToRevitClass(reg.IdentityClass);
            }

            if (!string.IsNullOrWhiteSpace(existingClass)
                && !string.Equals(existingClass.Trim(), "Unassigned", StringComparison.OrdinalIgnoreCase))
            {
                p.Reason = "already classified — not overwritten";
                return p;
            }

            if (string.IsNullOrWhiteSpace(materialName))
            { p.Reason = "unnamed"; return p; }

            foreach (string phrase in NamesNoSubstance)
                if (PatternMatch.Contains(materialName, phrase))
                {
                    p.Reason = $"'{phrase}' names no substance — left blank rather than guessed";
                    return p;
                }

            // PatternMatch.Contains, not IndexOf: "_ACCURENDER" is not a render and
            // "ducTILE iron" is not a tile. Both were live wrong answers in a model.
            foreach (var (needle, cls) in Map)
                if (PatternMatch.Contains(materialName, needle))
                {
                    p.ProposedClass = cls;
                    p.Reason = $"name contains '{needle}'";
                    return p;
                }

            p.Reason = "the name names no substance — left blank rather than guessed";
            return p;
        }

        public static List<MaterialClassProposal> PlanAll(
            IEnumerable<(string Name, string ExistingClass)> materials,
            MaterialRegistry registry = null)
            => (materials ?? Enumerable.Empty<(string, string)>())
               .Select(m => Plan(m.Name, m.ExistingClass, registry)).ToList();

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
