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

    /// <summary>A level the baseline expects, by name and elevation.</summary>
    public sealed class BaselineLevel
    {
        public string Name = "";
        public double ElevationMm;
        public string Purpose = "";
    }

    /// <summary>
    /// One type parameter a minted family type must carry, in millimetres.
    ///
    /// Set by NAME, never by built-in enum: door and window families vary, and
    /// a family that names its opening "Rough Width" is not wrong. A parameter
    /// the family does not have is a per-type FAILURE with the parameter named,
    /// not a silent skip — a type minted at the wrong size is worse than one
    /// that was never minted, because it looks finished.
    /// </summary>
    public sealed class BaselineTypeParameter
    {
        public string Name = "";
        public double ValueMm;
    }

    /// <summary>
    /// LAYER 2 — a TYPE inside a family that is already loaded.
    ///
    /// The honest split of layer 1 survives here unchanged: this can mint a
    /// type, it cannot conjure the family. With no matching family loaded the
    /// entry is guidance, exactly as `Structural Columns: 0 of 1 expected
    /// type(s) match` reports today.
    /// </summary>
    public sealed class BaselineFamilyType
    {
        /// <summary>Revit category display name, e.g. "Doors".</summary>
        public string Category = "";

        /// <summary>
        /// Ordered preference list. The host is the FIRST loaded family in that
        /// category whose name contains any pattern. Absent that: guidance —
        /// never a substitute family from another category.
        /// </summary>
        public List<string> FamilyNamePatterns = new List<string>();

        /// <summary>
        /// The minted type's name. Must carry the STING prefix: after a vendor
        /// reloads their family, the prefix is the only thing that says which
        /// types were ours. See <see cref="StingPrefix"/>.
        /// </summary>
        public string TypeName = "";
        public string Purpose = "";

        public List<BaselineTypeParameter> Parameters = new List<BaselineTypeParameter>();

        /// <summary>
        /// The contract that makes a minted type identifiable after a vendor
        /// reload (spec §8 D4). Vendor families ARE in scope; a type minted in
        /// one must be findable and removable without guessing.
        /// </summary>
        public const string StingPrefix = "STING ";

        public bool HasStingPrefix =>
            (TypeName ?? "").TrimStart().StartsWith(StingPrefix, StringComparison.Ordinal);

        /// <summary>Compact "900x2100" style summary of the declared parameters,
        /// for the audit line. Empty when the type declares none.</summary>
        public string ParameterSummary()
        {
            var ps = (Parameters ?? new List<BaselineTypeParameter>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).ToList();
            if (ps.Count == 0) return "";
            return string.Join(", ", ps.Select(p => $"{p.Name} {p.ValueMm:0.###}mm"));
        }
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

        /// <summary>LAYER 2 — types minted inside families already loaded.</summary>
        public List<BaselineFamilyType> FamilyTypes = new List<BaselineFamilyType>();

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

            problems.AddRange(ValidateFamilyTypes());

            var dupes = AllHostTypes.Where(t => t != null && !string.IsNullOrWhiteSpace(t.Name))
                .GroupBy(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (string d in dupes) problems.Add($"host type '{d}' is declared more than once");

            return problems;
        }

        /// <summary>
        /// LAYER 2 problems, found before any model is touched.
        ///
        /// Every one of these would otherwise surface as a per-type mint
        /// failure on somebody's live model, which is a far more expensive
        /// place to learn that a name was blank.
        /// </summary>
        private List<string> ValidateFamilyTypes()
        {
            var problems = new List<string>();
            var ft = FamilyTypes ?? new List<BaselineFamilyType>();

            foreach (var t in ft)
            {
                if (t == null) continue;
                string label = string.IsNullOrWhiteSpace(t.TypeName) ? "(unnamed)" : t.TypeName.Trim();

                if (string.IsNullOrWhiteSpace(t.TypeName))
                    problems.Add("a family type has no typeName");
                else if (!t.HasStingPrefix)
                    problems.Add($"family type '{label}' does not start with '{BaselineFamilyType.StingPrefix}' "
                               + "— the prefix is what identifies a minted type after a vendor reloads "
                               + "their family, so without it the type cannot be found again");

                if (string.IsNullOrWhiteSpace(t.Category))
                    problems.Add($"family type '{label}' names no category");

                if (t.FamilyNamePatterns == null
                 || !t.FamilyNamePatterns.Any(p => !string.IsNullOrWhiteSpace(p)))
                    problems.Add($"family type '{label}' declares no familyNamePatterns, so no host "
                               + "family could ever be chosen for it");

                foreach (var p in t.Parameters ?? new List<BaselineTypeParameter>())
                {
                    if (p == null) continue;
                    if (string.IsNullOrWhiteSpace(p.Name))
                        problems.Add($"family type '{label}' has a parameter with no name");
                    else if (p.ValueMm <= 0)
                        problems.Add($"family type '{label}' parameter '{p.Name}' has value "
                                   + $"{p.ValueMm}mm");
                }
            }

            // Duplicates are per CATEGORY: "STING 900x2100" may legitimately
            // exist for both a door and a window.
            var dupes = ft.Where(t => t != null
                             && !string.IsNullOrWhiteSpace(t.TypeName)
                             && !string.IsNullOrWhiteSpace(t.Category))
                .GroupBy(t => t.Category.Trim() + "|" + t.TypeName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => g.Key);
            foreach (string d in dupes)
                problems.Add($"family type '{d.Replace("|", " / ")}' is declared more than once");

            return problems;
        }
    }
}
