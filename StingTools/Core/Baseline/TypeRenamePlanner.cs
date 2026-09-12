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
using StingTools.Core.Materials;

namespace StingTools.Core.Baseline
{
    /// <summary>What the planner was able to read off one type.</summary>
    public sealed class TypeRenameInput
    {
        /// <summary>Revit category display name — Walls / Floors / Roofs / Ceilings.</summary>
        public string Category = "";
        /// <summary>Revit family name — "Basic Wall", "Floor", "Basic Roof". Half of what
        /// the PROD rules match against, so the planner cannot ask what a type resolves to
        /// today without it.</summary>
        public string FamilyName = "";
        public string CurrentName = "";
        /// <summary>ISO 22014 Source field, from PRJ_ORG_ORIGINATOR_CODE_TXT. Defaults PLNS.</summary>
        public string Originator = "PLNS";
        /// <summary>Layers as the compound structure gives them, exterior to interior.</summary>
        public List<MaterialLayer> Layers = new List<MaterialLayer>();
        /// <summary>How many elements use this type. Renaming an unused type is noise.</summary>
        public int InstanceCount;
    }

    /// <summary>
    /// What a type resolves to TODAY, handed in by the caller so the planner stays
    /// Revit-free. <paramref name="IsSpecific"/> is the important half: a code that came
    /// from a family-aware rule is an ANSWER, and a category default is the absence of
    /// one. Replacing the first is a downgrade; replacing the second is the point.
    /// </summary>
    public sealed class ExistingProdCode
    {
        /// <summary>Base PROD code, without the material suffix — "FSP", not "FSP-CON".</summary>
        public string Code = "";
        /// <summary>project | declared | corporate | lps | sleeve | category | gen.</summary>
        public string Source = "";
        /// <summary>True for a family-aware rule; false for the category default.</summary>
        public bool IsSpecific;
    }

    public sealed class TypeRenameProposal
    {
        public string Category = "";
        public string CurrentName = "";
        /// <summary>ISO 22014 Source field.</summary>
        public string Originator = "PLNS";
        /// <summary>Null when the model does not say enough to name the type.</summary>
        public string ProposedName;
        /// <summary>The PROD code the proposed name declares in its Type field.</summary>
        public string ProdCode;
        /// <summary>The code the proposed name WOULD declare — set even when the proposal
        /// is refused, so the CSV can show what was rejected and why.</summary>
        public string DeclaredCode;
        /// <summary>What the type resolves to today, or null when the caller did not say.</summary>
        public ExistingProdCode Existing;
        /// <summary>Why — shown to the reader either way.</summary>
        public string Reason = "";
        public int InstanceCount;

        /// <summary>True when a name COULD have been composed and was withheld because it
        /// would have declared a different product code from the one the type already
        /// resolves to. A different thing from "the model does not say enough".</summary>
        public bool RefusedToProtectCode;

        /// <summary>True when a name was composed and withheld because another type in the
        /// same category would end up holding it. Distinct from both other refusals: the
        /// model said enough, the code was right, and the answer still collided.</summary>
        public bool RefusedAsDuplicate;

        /// <summary>The name that WAS composed, kept even when a gate withheld it, so the
        /// CSV can show what was rejected instead of an empty cell. Equal to
        /// <see cref="ProposedName"/> whenever a proposal stands.</summary>
        public string WithheldName;

        /// <summary>True when the caller's existing-code resolver threw. The product-code
        /// gate cannot fire for this type, and a run where that happened everywhere must not
        /// read the same as a run where the gate examined every type and found nothing.</summary>
        public bool ExistingLookupFailed;

        public bool IsProposal => !string.IsNullOrEmpty(ProposedName);
        /// <summary>True when the current name already matches what would be proposed.</summary>
        public bool AlreadyConforms =>
            IsProposal && string.Equals(CurrentName?.Trim(), ProposedName, StringComparison.OrdinalIgnoreCase);
    }

    public static class TypeRenamePlanner
    {
        /// <summary>
        /// Category → the PROD code each substance resolves to there.
        ///
        /// <para>The ISO 22014 <b>Type</b> field IS the PROD code, so a conforming type
        /// name and the element's ISO 19650 tag cannot fork. Before this, a name carried
        /// the word "Blockwork" while the resolver derived "WBL" from it — two
        /// vocabularies for one fact, free to disagree the moment either was edited.</para>
        ///
        /// <para><b>This table is narrower than STING_PROD_CODES.csv, and cannot stop
        /// being.</b> Reconciled 2026-09-08 against the rule file and the house
        /// catalogue: eight codes the rules use on these four categories are unreachable
        /// from here, and every one of them is keyed on what the element is FOR, not what
        /// it is made of —</para>
        ///
        /// <code>
        ///   WCP  *Coping*                             RWL  *Retaining*
        ///   WBD  *Boundary Wall*|*Compound Wall*      FGS  *Ground Slab*|*Slab on Grade*
        ///   FSP  *Steps*|*Stair Landing Slab*         FRB  *Hollow Pot*|*Waffle*|*Ribbed Slab*
        ///   RFL  *Flat Roof*|*Built-Up Felt*          CSU  *Suspended Ceiling*|*Grid Ceiling*
        /// </code>
        ///
        /// <para>A concrete step and a concrete slab have the SAME core material. So does
        /// a retaining wall and a shear wall, a ground-bearing slab and a suspended one.
        /// No substance word can separate them, because the distinction is not a
        /// substance — which is why the fix for the FSP → SLB and WCP → WRC downgrades
        /// found on a delivered model is the refusal in <see cref="Plan"/>, not more rows
        /// here. Adding a row that guessed would put a confident wrong code in the one
        /// field the resolver trusts above all others.</para>
        ///
        /// <para>Two of the eight are arguably reachable and are deliberately left out
        /// for want of evidence: <b>FRB</b> could be read from a core material named
        /// "Hollow Pot", and <b>RTL</b> from the TILE in a roof's finish layer rather than
        /// the timber in its core. Both need a corpus of real material names to add
        /// safely, and this had one only for materials, not for host build-ups.</para>
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> CodeFor =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Walls"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Blockwork"] = "WBL", ["Clay Brick"] = "WBK", ["Screen Block"] = "SBP",
                    ["RC"] = "WRC", ["Timber"] = "WPT", ["Plasterboard"] = "WPT",
                    ["Steel"] = "WPT", ["Stone"] = "WSN",
                },
                ["Floors"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["RC"] = "SLB", ["Timber"] = "FTM", ["Plywood"] = "FTM",
                },
                ["Roofs"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["RC"] = "RCS", ["Corrugated Sheet"] = "RSH", ["Clay Tile Roof"] = "RTL",
                    ["Stone Coated Tile Roof"] = "RTL", ["Timber"] = "RSH",
                },
                ["Ceilings"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Plasterboard"] = "CPB", ["Timber"] = "CPB",
                },
            };

        /// <summary>
        /// Words that name a substance, longest first so "Hollow Concrete Block" beats
        /// "Concrete". Keyed on the MATERIAL name, which is the deliberate half of the
        /// model — nobody renames a material to make a schedule work.
        /// </summary>
        private static readonly (string Needle, string Word)[] Substance = BuildSubstance();

        private static (string Needle, string Word)[] BuildSubstance()
        {
            var m = new List<(string Needle, string Word)>();
            m.AddRange(new (string, string)[]
            {
            ("hollow concrete block", "Blockwork"), ("solid concrete block", "Blockwork"),
            ("concrete block", "Blockwork"), ("blockwork", "Blockwork"),
            // "Concrete Masonry Units" is the Revit library's name for a block, and it
            // contains the word "concrete". Without these three above the RC row, four
            // block walls on a delivered model — Generic - 150/200/300mm Masonry and
            // M_Exterior - Brick on CMU — were proposed as PLNS_WRC_*: reinforced
            // concrete. The concrete in the name is what the BLOCK is made of, not what
            // the WALL is; same ordering rule as the class table next door.
            ("concrete masonry units", "Blockwork"), ("concrete masonry unit", "Blockwork"),
            ("masonry units", "Blockwork"), ("masonry unit", "Blockwork"),
            ("cmu", "Blockwork"),
            ("clay brick", "Clay Brick"), ("brick", "Clay Brick"),
            ("screen block", "Screen Block"),
            ("concrete", "RC"),
            ("reinforcement", "RC"),
            });

            // ── The one wood list, shared with MaterialClassPlanner and BiogenicCarbon.
            //    "plywood" keeps its own word because it routes to a different CodeFor key
            //    than "Timber" does; everything else is Timber. Sharing the list added seven
            //    proposals this table used to refuse — every one a "Structure, Wood
            //    Joist/Rafter Layer" floor or roof, which is exactly what it says it is.
            m.AddRange(SubstanceVocabulary.WoodRows("Timber", plywoodLabel: "Plywood"));

            m.AddRange(new (string, string)[]
            {
            ("galvanised sheet", "Corrugated Sheet"), ("galvanized sheet", "Corrugated Sheet"),
            ("clay tiles", "Clay Tile Roof"), ("clay tile", "Clay Tile Roof"),
            ("stone coated", "Stone Coated Tile Roof"),
            ("steel", "Steel"),
            ("gypsum", "Plasterboard"), ("plasterboard", "Plasterboard"),
            ("cobblestone", "Stone"), ("bluestone", "Stone"), ("flagstone", "Stone"),
            ("stone", "Stone"),
            });
            return m.ToArray();
        }

        /// <summary>Finish words, read off the FINISH layers the same way.</summary>
        private static readonly (string Needle, string Word)[] Finish =
        {
            ("porcelain", "Porcelain Tiled"), ("ceramic", "Ceramic Tiled"),
            ("tiles", "Tiled"), ("tile", "Tiled"),
            ("terrazzo", "Terrazzo"),
            ("plastered", "Plastered"), ("plaster", "Plastered"),
            ("rendered", "Plastered"), ("render", "Plastered"),
            ("gypsum", "Boarded"), ("plasterboard", "Boarded"),
            ("felt", "Felt"), ("bitumen", "Felt"),
            ("painted", "Painted"), ("paint", "Painted"), ("screed", "Screed Only"),
        };

        /// <summary>
        /// Propose one name, or explain why not.
        ///
        /// <para>Shape: <c>STING {use} - {substance} {size} - {finish}</c>, which is the
        /// order the rules read and the order a browser sorts usefully.</para>
        /// </summary>
        /// <param name="resolveExisting">
        /// What this type resolves to TODAY. Optional so every existing caller and test
        /// still compiles, but the command passes it, and without it the code-downgrade
        /// refusal below cannot fire — a planner that is not told what the answer is
        /// cannot notice it is about to replace one.
        /// </param>
        public static TypeRenameProposal Plan(
            TypeRenameInput input, Func<TypeRenameInput, ExistingProdCode> resolveExisting = null)
        {
            var p = new TypeRenameProposal
            {
                Category = input?.Category ?? "",
                CurrentName = input?.CurrentName ?? "",
                InstanceCount = input?.InstanceCount ?? 0,
                Originator = string.IsNullOrWhiteSpace(input?.Originator) ? "PLNS" : input.Originator,
            };

            if (input == null || !CodeFor.TryGetValue(p.Category, out var codes))
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
            if (substance == null || !codes.TryGetValue(substance, out string prod))
            {
                p.Reason = $"the core material '{core.MaterialName}' names no substance this "
                         + "recognises — rename the MATERIAL first (rule M1), then re-run";
                return p;
            }

            // Size from the core's own thickness, so it describes the thing rather than
            // repeating whatever number the old name happened to carry.
            string size = core.ThicknessMm >= 1
                ? Math.Round(core.ThicknessMm).ToString("0", CultureInfo.InvariantCulture)
                : "";

            // Finish from the finish layers, not from the core.
            string finish = layers.Where(l => !ReferenceEquals(l, core))
                                  .Select(l => Match(l.MaterialName, Finish))
                                  .FirstOrDefault(f => f != null);

            // BS EN ISO 22014: Source_Type_Subtype. Underscore between fields, hyphen
            // between components inside a field, no spaces anywhere.
            p.ProdCode = prod;
            p.DeclaredCode = prod;
            // An unavailable resolver must not abort the plan — but it must not vanish
            // either. A resolver that throws for every type disables the code gate below
            // completely, and "0 held back" then means "the gate never ran", which reads
            // identically to "the gate found nothing wrong".
            try { p.Existing = resolveExisting?.Invoke(input); }
            catch (Exception) { p.Existing = null; p.ExistingLookupFailed = resolveExisting != null; }

            string composed = ProdNameCode.Compose(p.Originator, prod, substance + size, finish);
            p.WithheldName = composed;

            // ── The code gate. ───────────────────────────────────────────────────
            // ProdResolver puts a code DECLARED in a type name above the corporate
            // pattern rules, so composing a name does not merely rename the type — it
            // OVERWRITES its classification, and nothing downstream will ever query the
            // rule again. Where the type already resolves to a code from a real rule,
            // and this proposal would declare a different one, the rename is refused.
            //
            // A CATEGORY DEFAULT is not such a code. WL / FL / RF / CLG mean nobody has
            // classified the thing, and replacing one is the entire purpose of this
            // command; gating on those would leave it able to rename only the types that
            // least need it.
            //
            // Measured on a delivered model (2026-09-08): "stepsr 7" resolves FSP from
            // *Steps*, and "coping" resolves WCP from *Coping*. Both would have become
            // SLB and WRC. Ten of the twenty-seven types in the shipped house catalogue
            // are in the same shape — see the CodeFor note above.
            bool nameUnchanged = string.Equals(composed, p.CurrentName?.Trim(),
                                               StringComparison.OrdinalIgnoreCase);
            if (!nameUnchanged
                && p.Existing != null && p.Existing.IsSpecific
                && !string.IsNullOrWhiteSpace(p.Existing.Code)
                && !string.Equals(p.Existing.Code, prod, StringComparison.OrdinalIgnoreCase))
            {
                p.RefusedToProtectCode = true;
                p.Reason = $"would change the product code {p.Existing.Code} → {prod}. "
                         + $"'{p.CurrentName}' already resolves to {p.Existing.Code} from the "
                         + $"{p.Existing.Source} rules, and a code declared in a type NAME outranks "
                         + $"every rule — so renaming it '{composed}' would replace a correct answer "
                         + "with a worse one. The core material cannot say what an element is FOR, "
                         + "which is what that code records.";
                return p;
            }

            p.ProposedName = composed;
            p.Reason = finish != null
                ? $"core '{core.MaterialName}' + finish layer → {prod}"
                : $"core '{core.MaterialName}' → {prod}; no finish layer to read";
            if (p.Existing != null && !p.Existing.IsSpecific
                && !string.Equals(p.Existing.Code, prod, StringComparison.OrdinalIgnoreCase))
                p.Reason += $"; upgrades the product code {p.Existing.Code} ({p.Existing.Source}) → {prod}";
            return p;
        }

        public static List<TypeRenameProposal> PlanAll(
            IEnumerable<TypeRenameInput> inputs,
            Func<TypeRenameInput, ExistingProdCode> resolveExisting = null)
        {
            var plans = (inputs ?? Enumerable.Empty<TypeRenameInput>())
                        .Select(i => Plan(i, resolveExisting)).ToList();
            GateDuplicateNames(plans);
            return plans;
        }

        /// <summary>
        /// ── The uniqueness gate. ─────────────────────────────────────────────
        ///
        /// <para><b>Revit does not stop you.</b> Its UI refuses a duplicate type name; its
        /// API does not. On 2026-09-09 this command renamed 168 types on a delivered model
        /// and logged <c>168/168 renamed, 0 failed</c> — while <b>137 of them landed on 19
        /// names</b>. Eighty-seven floor types, the whole finish catalogue, became
        /// <c>PLNS_SLB_RC100</c>. Every <c>type.Name = …</c> succeeded, so the per-type
        /// try/catch had nothing to report. This gate is the only thing between a rename and
        /// that outcome.</para>
        ///
        /// <para><b>Refuse; never disambiguate.</b> Appending <c>-2</c>, <c>-3</c> … was the
        /// obvious repair and is the wrong one. Those 87 types are one 100 mm layer of grey
        /// concrete under 87 names: a numeric suffix would invent a distinction the build-ups
        /// do not contain, and — unlike the collision — it would look deliberate forever
        /// after. The refusal is worth more than the rename, because it names the real
        /// finding: those types are named, not modelled, and every quantity taken off them
        /// (area, volume, embodied carbon, cost) has been answering "100 mm of concrete"
        /// whatever the label said.</para>
        ///
        /// <para><b>Names being KEPT count too.</b> A rename landing on the name of a type
        /// that is not being renamed is the same duplicate to Revit. Comparing proposals only
        /// against other proposals would let that through, so the claim set is
        /// <c>ProposedName ?? CurrentName</c> for every type in the run.</para>
        ///
        /// <para><b>Scope is the category.</b> Revit scopes type names per category, so a
        /// floor and a roof may both be <c>PLNS_SLB_RC150</c>; gating across categories would
        /// refuse correct renames for a clash that cannot happen.</para>
        /// </summary>
        private static void GateDuplicateNames(List<TypeRenameProposal> plans)
        {
            var claims = new Dictionary<string, List<TypeRenameProposal>>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in plans)
            {
                string held = p.IsProposal ? p.ProposedName : p.CurrentName;
                if (string.IsNullOrWhiteSpace(held)) continue;
                string key = (p.Category ?? "") + "\u0000" + held.Trim();
                if (!claims.TryGetValue(key, out var l)) claims[key] = l = new List<TypeRenameProposal>();
                l.Add(p);
            }

            foreach (var group in claims.Values)
            {
                if (group.Count < 2) continue;

                foreach (var p in group)
                {
                    // A type that already carries the name is not colliding with itself, and
                    // one that is keeping its name is not doing anything to collide WITH.
                    // Only a move can be refused.
                    if (!p.IsProposal || p.AlreadyConforms) continue;

                    var peers = group.Where(o => !ReferenceEquals(o, p))
                                     .Select(o => o.CurrentName)
                                     .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

                    string named = string.Join(", ", peers.Take(3));
                    if (peers.Count > 3) named += $", and {peers.Count - 3} more";

                    p.RefusedAsDuplicate = true;
                    p.ProposedName = null;
                    p.Reason = $"'{p.WithheldName}' would also be taken by {peers.Count} other "
                             + $"{p.Category} type(s) — {named}. Revit's API accepts a duplicate type "
                             + "name without complaint, so nothing downstream would report it. They "
                             + "share a name because they share a build-up: same core material, same "
                             + "thickness, same finish. As modelled they are one type, and the "
                             + "difference lives only in the names a rename would erase — so give them "
                             + "different layers, or leave them named.";
                }
            }
        }

        /// <summary>
        /// One line per outcome, so a reader sees the shape of the run before opening
        /// the CSV — and so "nothing proposed" is never mistaken for "nothing wrong".
        /// </summary>
        public static string Summary(IReadOnlyCollection<TypeRenameProposal> ps)
        {
            if (ps == null || ps.Count == 0) return "No layered host types found.";
            int conform = ps.Count(x => x.AlreadyConforms);
            int propose = ps.Count(x => x.IsProposal && !x.AlreadyConforms);
            int guarded = ps.Count(x => x.RefusedToProtectCode);
            int clashed = ps.Count(x => x.RefusedAsDuplicate);
            int unchecked_ = ps.Count(x => x.ExistingLookupFailed);
            int cannot = ps.Count(x => !x.IsProposal) - guarded - clashed;
            string s = $"{ps.Count} type(s): {conform} already conform, {propose} can be renamed, "
                     + $"{cannot} cannot be named from the model. The {cannot} are not failures of this "
                     + "tool — their materials do not say what they are, and a rename that guessed would "
                     + "replace a name a reader can see is empty with one they cannot.";
            if (guarded > 0)
                s += $" A further {guarded} were held back because the new name would have declared a "
                   + "DIFFERENT product code from the one the type already resolves to — those are "
                   + "listed in the CSV with both codes, and the answer is usually to fix the name by "
                   + "hand rather than to let a rename overwrite a correct classification.";
            if (clashed > 0)
                s += $" And {clashed} were held back because two or more types would have ended up "
                   + "with the same name — Revit's API permits that silently, so the rename would "
                   + "have looked like a clean run. Types collide here when the model gives them "
                   + "identical build-ups, which means the distinction their old names carry is not "
                   + "in the model at all: their areas, volumes, carbon and cost are already being "
                   + "answered as though they were one type.";
            if (unchecked_ > 0)
                s += $" NOTE: {unchecked_} type(s) could not be checked against the product-code "
                   + "rules because the resolver failed, so the code gate did not run for them. "
                   + "Treat any rename among those as unverified rather than approved.";
            return s;
        }

        /// <summary>
        /// A needle matches a WORD, not a run of letters — the #863 shape, which this file
        /// carried until 2026-09-09 and which reaches a PROD code from here.
        ///
        /// <para>Switching to whole-word matching alone BREAKS SEVEN ROWS on a delivered
        /// model, because the data is plural and the needles were singular: five walls
        /// cored with "Concrete Masonry Units" fall back from Blockwork to RC, and two
        /// roofs cored with "CLAY TILES PREMIUM 15MM" get no name at all. The stems above
        /// are what makes the swap safe; measured against
        /// Fixtures/type_rename_core_materials_20260909.csv, 0 rows break.</para>
        /// </summary>
        private static string Match(string text, (string Needle, string Word)[] table)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            foreach (var (needle, word) in table)
                if (MaterialSchedule.PatternMatch.Contains(text, needle)) return word;
            return null;
        }
    }
}
