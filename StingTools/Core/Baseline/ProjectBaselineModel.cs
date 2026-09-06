// ══════════════════════════════════════════════════════════════════════════
//  ProjectBaselineModel.cs — the STING model-authoring baseline, as data.
//
//  WHY THIS EXISTS. A material schedule can only measure what the model
//  describes. A real export ended with: 10 wall/floor types inspected, ONE
//  carrying a finish layer (Gypsum Wall Board), and 23 placed rooms with no
//  finish text at all. Nothing was broken — the model simply never said what
//  its finishes were, so nothing could be measured. Fixing that per project,
//  one model at a time, is not a system.
//
//  WHAT IT IS NOT. Most of a STING "template" already exists as commands —
//  view templates, drawing types, view style packs, 290 AEC filters, browser
//  organisation, material packs. None of that is repeated here. This covers
//  the one gap those leave: the HOST TYPES and MATERIALS a model is authored
//  with, which is where the schedule's inputs actually come from.
//
//  THE HONEST SPLIT. Materials, wall/floor/roof/ceiling types and levels can
//  be created through the API. Structural column, framing and foundation
//  types, and doors and windows, are FAMILY types: with no .rfa loaded there
//  is nothing to create, so the baseline reports them and stops. A baseline
//  that pretended to mint a door family would be inventing the deliverable.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Baseline
{
    /// <summary>A material the baseline expects to exist, by name.</summary>
    public sealed class BaselineMaterial
    {
        public string Name = "";
        /// <summary>Why it is in the baseline — shown in the audit so a reader
        /// can judge whether it belongs, rather than trusting the list.</summary>
        public string Purpose = "";
        /// <summary>Optional RGB, "R,G,B". Absent means Revit's default.</summary>
        public string Colour = "";
    }

    /// <summary>One layer of a host type's compound structure.</summary>
    public sealed class BaselineLayer
    {
        /// <summary>Structure / Substrate / Insulation / Finish1 / Finish2 / Membrane.</summary>
        public string Function = "Structure";
        public string Material = "";
        public double ThicknessMm;
    }

    /// <summary>A wall / floor / roof / ceiling type the baseline expects.</summary>
    public sealed class BaselineHostType
    {
        public string Name = "";
        public string Purpose = "";
        /// <summary>Exterior-to-interior, as Revit orders them.</summary>
        public List<BaselineLayer> Layers = new List<BaselineLayer>();

        public double TotalThicknessMm => Layers?.Sum(l => Math.Max(0, l.ThicknessMm)) ?? 0;

        /// <summary>True when a layer names a tiled finish — the thing whose
        /// absence stopped the schedule measuring any tiling at all.</summary>
        public bool HasTiledFinish => Layers != null && Layers.Any(l =>
            IsFinish(l.Function) && MaterialSchedule.FinishTextClassifier.IsTile(l.Material));

        public static bool IsFinish(string function) =>
            string.Equals(function, "Finish1", StringComparison.OrdinalIgnoreCase)
         || string.Equals(function, "Finish2", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A family-backed type the baseline can only CHECK. Structural columns,
    /// framing, foundations, doors and windows all live in .rfa files; with
    /// none loaded there is nothing to create.
    /// </summary>
    public sealed class BaselineFamilyExpectation
    {
        /// <summary>Revit category display name, e.g. "Structural Columns".</summary>
        public string Category = "";
        /// <summary>Substrings a conforming type name should contain, any of.</summary>
        public List<string> NamePatterns = new List<string>();
        /// <summary>Minimum distinct types expected. 0 disables the count check.</summary>
        public int MinimumTypes;
        public string Guidance = "";
    }

    /// <summary>One type parameter to set on a minted family type.</summary>
    public sealed class BaselineFamilyTypeParam
    {
        /// <summary>Set by NAME, not by built-in enum: door and window families
        /// vary, and the enum a generic family uses is not the one a vendor
        /// family uses.</summary>
        public string Name = "";
        public double ValueMm;
    }

    /// <summary>
    /// A TYPE to mint inside a family that is already loaded.
    ///
    /// It cannot conjure the family. If nothing loaded matches, this is
    /// reported as guidance — the same honest split that reports
    /// "Structural Columns: 0 of 1 expected type(s) match" today.
    /// </summary>
    public sealed class BaselineFamilyType
    {
        public string Category = "";
        /// <summary>Ordered preference list. The FIRST loaded family in the
        /// category whose name contains any of these hosts the type. Never a
        /// family from a different category.</summary>
        public List<string> FamilyNamePatterns = new List<string>();
        public string TypeName = "";
        public string Purpose = "";
        public List<BaselineFamilyTypeParam> Parameters = new List<BaselineFamilyTypeParam>();

        /// <summary>
        /// The "STING " prefix is the identification contract, exactly as
        /// "STING VIS - " is for visibility filters. A vendor reissue reloaded
        /// with overwrite wipes minted types; the prefix is what lets the next
        /// audit recognise them as ours and re-mint them.
        /// </summary>
        public const string MintedPrefix = "STING ";
        public bool CarriesMintedPrefix =>
            (TypeName ?? "").StartsWith(MintedPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Shared parameters to add to every loaded family in a category.
    ///
    /// SHARED, not local. FamilyManager.AddParameter(name, group, spec,
    /// isInstance) creates a family-local parameter with no GUID, so two
    /// families given "the same" parameter hold two unrelated ones and cannot
    /// be scheduled together. The ExternalDefinition overload is the only one
    /// that produces a parameter the schedule can read across a project.
    /// </summary>
    public sealed class BaselineFamilyParameterSet
    {
        public string Category = "";
        public List<string> Parameters = new List<string>();
        /// <summary>False — a door type's leaf material does not vary per instance.</summary>
        public bool IsInstance;
        public string Group = "IdentityData";
    }

    /// <summary>A level the baseline expects, by name and elevation.</summary>
    public sealed class BaselineLevel
    {
        public string Name = "";
        public double ElevationMm;
        public string Purpose = "";
    }

    public sealed class ProjectBaseline
    {
        public string SchemaVersion = "1.0";
        public string Note = "";
        public List<BaselineMaterial> Materials = new List<BaselineMaterial>();
        public List<BaselineHostType> WallTypes = new List<BaselineHostType>();
        public List<BaselineHostType> FloorTypes = new List<BaselineHostType>();
        public List<BaselineHostType> RoofTypes = new List<BaselineHostType>();
        public List<BaselineHostType> CeilingTypes = new List<BaselineHostType>();
        public List<BaselineLevel> Levels = new List<BaselineLevel>();
        public List<BaselineFamilyExpectation> FamilyExpectations = new List<BaselineFamilyExpectation>();

        /// <summary>Layer 2 — types minted inside already-loaded families.</summary>
        public List<BaselineFamilyType> FamilyTypes = new List<BaselineFamilyType>();

        /// <summary>Layer 3 — shared parameters added to loaded families.</summary>
        public List<BaselineFamilyParameterSet> FamilyParameters = new List<BaselineFamilyParameterSet>();

        /// <summary>Catalogue pack ids this project adopts. Empty by default:
        /// no pack applies unless a project asks for it by id.</summary>
        public List<string> AdoptCatalogues = new List<string>();

        public IEnumerable<BaselineHostType> AllHostTypes =>
            (WallTypes ?? new List<BaselineHostType>())
            .Concat(FloorTypes ?? new List<BaselineHostType>())
            .Concat(RoofTypes ?? new List<BaselineHostType>())
            .Concat(CeilingTypes ?? new List<BaselineHostType>());

        /// <summary>
        /// Problems INSIDE the baseline itself, before it is compared to any
        /// model. A layer naming a material the baseline never declares mints a
        /// type with an empty layer and no error — the same silent-zero class as
        /// a mistyped MATERIAL_LOOKUP key.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var known = new HashSet<string>(
                (Materials ?? new List<BaselineMaterial>())
                    .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Name))
                    .Select(m => m.Name.Trim()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var t in AllHostTypes)
            {
                if (t == null) continue;
                if (string.IsNullOrWhiteSpace(t.Name)) { problems.Add("a host type has no name"); continue; }
                if (t.Layers == null || t.Layers.Count == 0)
                {
                    problems.Add($"'{t.Name}' declares no layers");
                    continue;
                }
                foreach (var l in t.Layers)
                {
                    if (l == null) continue;
                    if (l.ThicknessMm <= 0)
                        problems.Add($"'{t.Name}' layer '{l.Material}' has thickness {l.ThicknessMm}mm");
                    if (string.IsNullOrWhiteSpace(l.Material))
                        problems.Add($"'{t.Name}' has a layer with no material");
                    else if (!known.Contains(l.Material.Trim()))
                        problems.Add($"'{t.Name}' layer names material '{l.Material}', "
                                   + "which the baseline does not declare");
                }
            }

            // Layer 2 — a type with no name mints nothing; a parameter with no
            // name sets nothing. Both fail silently in Revit, so they are caught
            // here, before any model is touched.
            foreach (var ft in FamilyTypes ?? new List<BaselineFamilyType>())
            {
                if (ft == null) continue;
                if (string.IsNullOrWhiteSpace(ft.TypeName))
                { problems.Add("a family type has no typeName"); continue; }
                if (string.IsNullOrWhiteSpace(ft.Category))
                    problems.Add($"family type '{ft.TypeName}' names no category");
                if (ft.FamilyNamePatterns == null || ft.FamilyNamePatterns.Count == 0
                    || ft.FamilyNamePatterns.All(string.IsNullOrWhiteSpace))
                    problems.Add($"family type '{ft.TypeName}' declares no familyNamePatterns, "
                               + "so no loaded family can host it");
                if (!ft.CarriesMintedPrefix)
                    problems.Add($"family type '{ft.TypeName}' does not start with "
                               + $"'{BaselineFamilyType.MintedPrefix}' — the prefix is what lets a "
                               + "later audit recognise a minted type after a vendor family reload");
                foreach (var prm in ft.Parameters ?? new List<BaselineFamilyTypeParam>())
                    if (prm != null && string.IsNullOrWhiteSpace(prm.Name))
                        problems.Add($"family type '{ft.TypeName}' has a parameter with no name");
            }

            var ftDupes = (FamilyTypes ?? new List<BaselineFamilyType>())
                .Where(t => t != null && !string.IsNullOrWhiteSpace(t.TypeName))
                .GroupBy(t => (t.Category ?? "") + "|" + t.TypeName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (string d in ftDupes) problems.Add($"family type '{d}' is declared more than once");

            // Layer 3 — an empty parameter list augments nothing, which would
            // report families as "would be augmented" and then change nothing.
            foreach (var fp in FamilyParameters ?? new List<BaselineFamilyParameterSet>())
            {
                if (fp == null) continue;
                if (string.IsNullOrWhiteSpace(fp.Category))
                { problems.Add("a familyParameters entry names no category"); continue; }
                var named = (fp.Parameters ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (named.Count == 0)
                    problems.Add($"familyParameters for '{fp.Category}' lists no parameters");
                if (fp.IsInstance)
                    problems.Add($"familyParameters for '{fp.Category}' asks for INSTANCE parameters; "
                               + "the material schedule reads type parameters, so a per-instance "
                               + "value would be invisible to it");
            }

            var dupes = AllHostTypes.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (string d in dupes) problems.Add($"host type '{d}' is declared more than once");

            return problems;
        }
    }
}
