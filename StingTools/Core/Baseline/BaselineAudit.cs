// ══════════════════════════════════════════════════════════════════════════
//  BaselineAudit.cs — comparing a model to the baseline, and saying so.
//
//  Revit-free by design. The audit's whole job is to be BELIEVABLE: it decides
//  what gets written into somebody's live model, so what it reports and what
//  it proposes have to be testable without Revit. The Revit half only gathers
//  names and hands them here.
//
//  Two rules the report obeys:
//    * Nothing is proposed that cannot be created. Family-backed types are
//      reported as guidance, never as work this command will do.
//    * An existing type is never silently overwritten. A type whose name
//      matches but whose layers differ is reported as a CONFLICT for a human
//      to settle, because the model's version may well be the correct one.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;

namespace StingTools.Core.Baseline
{
    public enum BaselineFindingKind
    {
        /// <summary>Absent and creatable — this is what Apply will mint.</summary>
        Missing,
        /// <summary>Present and conforming. Reported so a clean model reads as clean.</summary>
        Present,
        /// <summary>Present under the same name but built differently. Never overwritten.</summary>
        Conflict,
        /// <summary>Absent and NOT creatable — a family type. Guidance only.</summary>
        Guidance
    }

    public sealed class BaselineFinding
    {
        public BaselineFindingKind Kind;
        public string Group = "";      // "Materials" / "Wall types" / …
        public string Name = "";
        public string Detail = "";

        /// <summary>LAYER 2 — the loaded family a Missing family type will be
        /// minted INSIDE. Empty for every other finding. Carried on the finding
        /// so the minter does not re-run the pattern match and risk choosing a
        /// different family than the one the audit told the user about.</summary>
        public string HostFamily = "";

        public bool IsActionable => Kind == BaselineFindingKind.Missing;
    }

    /// <summary>Everything the Revit side found in the model, as plain names.</summary>
    public sealed class ModelInventory
    {
        public HashSet<string> Materials = New();
        public HashSet<string> WallTypes = New();
        public HashSet<string> FloorTypes = New();
        public HashSet<string> RoofTypes = New();
        public HashSet<string> CeilingTypes = New();
        public HashSet<string> Levels = New();

        /// <summary>Category display name → the type names present in it.</summary>
        public Dictionary<string, List<string>> FamilyTypesByCategory =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Host type name → whether it carries a tiled finish layer.
        /// Absent means the type was not inspected.</summary>
        public Dictionary<string, bool> HostTypeHasTiledFinish =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>LAYER 2 — category display name → the FAMILY names loaded in
        /// it. Distinct from FamilyTypesByCategory: a type can only be minted
        /// inside a family that is already loaded, so the family list is what
        /// decides Missing from Guidance.</summary>
        public Dictionary<string, List<string>> FamiliesByCategory =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>LAYER 2 — "Category|TypeName" → that type's parameter values
        /// in millimetres, for the names the baseline declares. Present only for
        /// types whose name the baseline also declares; the reader does not walk
        /// every parameter of every type in the model.
        ///
        /// This is what turns a name match into a CONFLICT rather than a pass:
        /// the model's 800x2100 may be the deliberate one.</summary>
        public Dictionary<string, Dictionary<string, double>> FamilyTypeParametersMm =
            new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        public static string TypeKey(string category, string typeName)
            => (category ?? "").Trim() + "|" + (typeName ?? "").Trim();

        private static HashSet<string> New() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class BaselineAuditResult
    {
        public List<BaselineFinding> Findings = new List<BaselineFinding>();
        public List<string> BaselineProblems = new List<string>();

        public int MissingCount => Findings.Count(f => f.Kind == BaselineFindingKind.Missing);
        public int ConflictCount => Findings.Count(f => f.Kind == BaselineFindingKind.Conflict);
        public int GuidanceCount => Findings.Count(f => f.Kind == BaselineFindingKind.Guidance);
        public int PresentCount => Findings.Count(f => f.Kind == BaselineFindingKind.Present);

        /// <summary>True when there is nothing for Apply to do.</summary>
        public bool NothingToMint => MissingCount == 0;

        public IEnumerable<BaselineFinding> Missing =>
            Findings.Where(f => f.Kind == BaselineFindingKind.Missing);
    }

    public static class BaselineAuditor
    {
        public static BaselineAuditResult Audit(ProjectBaseline baseline, ModelInventory model)
        {
            var r = new BaselineAuditResult();
            if (baseline == null) return r;
            model = model ?? new ModelInventory();

            // The baseline is checked against ITSELF first. Auditing a model
            // with a broken baseline reports confident nonsense.
            r.BaselineProblems.AddRange(baseline.Validate());

            foreach (var m in baseline.Materials ?? new List<BaselineMaterial>())
            {
                if (m == null || string.IsNullOrWhiteSpace(m.Name)) continue;
                r.Findings.Add(new BaselineFinding
                {
                    Kind = model.Materials.Contains(m.Name.Trim())
                        ? BaselineFindingKind.Present : BaselineFindingKind.Missing,
                    Group = "Materials",
                    Name = m.Name.Trim(),
                    Detail = m.Purpose
                });
            }

            AuditHostTypes(r, "Wall types", baseline.WallTypes, model.WallTypes, model);
            AuditHostTypes(r, "Floor types", baseline.FloorTypes, model.FloorTypes, model);
            AuditHostTypes(r, "Roof types", baseline.RoofTypes, model.RoofTypes, model);
            AuditHostTypes(r, "Ceiling types", baseline.CeilingTypes, model.CeilingTypes, model);

            foreach (var l in baseline.Levels ?? new List<BaselineLevel>())
            {
                if (l == null || string.IsNullOrWhiteSpace(l.Name)) continue;
                r.Findings.Add(new BaselineFinding
                {
                    Kind = model.Levels.Contains(l.Name.Trim())
                        ? BaselineFindingKind.Present : BaselineFindingKind.Missing,
                    Group = "Levels",
                    Name = l.Name.Trim(),
                    Detail = $"{l.ElevationMm:N0} mm — {l.Purpose}"
                });
            }

            foreach (var f in baseline.FamilyExpectations ?? new List<BaselineFamilyExpectation>())
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Category)) continue;
                r.Findings.Add(AuditFamilyExpectation(f, model));
            }

            foreach (var t in baseline.FamilyTypes ?? new List<BaselineFamilyType>())
            {
                if (t == null || string.IsNullOrWhiteSpace(t.TypeName)
                              || string.IsNullOrWhiteSpace(t.Category)) continue;
                r.Findings.Add(AuditFamilyType(t, model));
            }

            return r;
        }

        /// <summary>LAYER 2 group label. One constant so the auditor, the
        /// minter's Missing lookup and the report cannot drift apart — the
        /// lookup is keyed on Group|Name.</summary>
        public const string FamilyTypeGroup = "Family types";

        /// <summary>
        /// Millimetre tolerance for "the same size". Revit stores lengths in
        /// feet, so a 900 mm parameter round-trips as 899.9999999; comparing
        /// exactly would report a conflict on every single type.
        /// </summary>
        private const double MmTolerance = 0.5;

        private static BaselineFinding AuditFamilyType(BaselineFamilyType t, ModelInventory model)
        {
            string cat = t.Category.Trim();
            string typeName = t.TypeName.Trim();
            string summary = t.ParameterSummary();

            model.FamilyTypesByCategory.TryGetValue(cat, out var existingTypes);
            bool exists = (existingTypes ?? new List<string>())
                .Any(x => string.Equals((x ?? "").Trim(), typeName, StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                // A name match with DIFFERENT parameters is a conflict, never an
                // overwrite. The model's 800x2100 may be deliberate, and #798 is
                // the standing reminder that a wrongly-"conforming" type is the
                // most expensive kind of wrong.
                var differences = Differences(t, model);
                if (differences.Count > 0)
                    return new BaselineFinding
                    {
                        Kind = BaselineFindingKind.Conflict,
                        Group = FamilyTypeGroup,
                        Name = $"{cat} / {typeName}",
                        Detail = "exists at " + string.Join(", ", differences)
                               + ". Not overwritten — the model's version may be the correct one."
                    };

                return new BaselineFinding
                {
                    Kind = BaselineFindingKind.Present, Group = FamilyTypeGroup,
                    Name = $"{cat} / {typeName}", Detail = t.Purpose
                };
            }

            // Not present. It can only be minted inside a family already loaded
            // in that category — this layer mints a TYPE, it cannot conjure the
            // family.
            string host = ChooseHostFamily(t, model);
            if (string.IsNullOrEmpty(host))
                return new BaselineFinding
                {
                    Kind = BaselineFindingKind.Guidance,
                    Group = FamilyTypeGroup,
                    Name = $"{cat} / {typeName}",
                    Detail = $"no loaded family in {cat} matches ["
                           + string.Join(" | ", (t.FamilyNamePatterns ?? new List<string>())
                                 .Where(x => !string.IsNullOrWhiteSpace(x)))
                           + "]. Load one, then re-run."
                };

            return new BaselineFinding
            {
                Kind = BaselineFindingKind.Missing,
                Group = FamilyTypeGroup,
                Name = $"{cat} / {typeName}",
                HostFamily = host,
                Detail = (summary.Length > 0 ? summary + " — " : "")
                       + $"in family \"{host}\"" + (string.IsNullOrWhiteSpace(t.Purpose)
                            ? "" : " — " + t.Purpose)
            };
        }

        /// <summary>
        /// The FIRST loaded family in the category whose name contains any
        /// pattern, in the order the patterns are declared — that ordering is
        /// the whole point of the list. Never a family from another category.
        /// </summary>
        public static string ChooseHostFamily(BaselineFamilyType t, ModelInventory model)
        {
            if (t == null || model == null || string.IsNullOrWhiteSpace(t.Category)) return "";
            if (!model.FamiliesByCategory.TryGetValue(t.Category.Trim(), out var families)) return "";
            families = families ?? new List<string>();

            foreach (string pattern in t.FamilyNamePatterns ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                string p = pattern.Trim();
                foreach (string f in families)
                    if (!string.IsNullOrWhiteSpace(f)
                     && f.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) return f.Trim();
            }
            return "";
        }

        /// <summary>Declared parameters whose model value differs, described as
        /// the model has them — "Width 800mm" — so the report says what IS, not
        /// what was wanted.</summary>
        private static List<string> Differences(BaselineFamilyType t, ModelInventory model)
        {
            var outList = new List<string>();
            if (!model.FamilyTypeParametersMm.TryGetValue(
                    ModelInventory.TypeKey(t.Category, t.TypeName), out var actual)) return outList;

            foreach (var p in t.Parameters ?? new List<BaselineTypeParameter>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                if (!actual.TryGetValue(p.Name.Trim(), out double have)) continue;   // unreadable: not a conflict
                if (Math.Abs(have - p.ValueMm) > MmTolerance)
                    outList.Add($"{p.Name.Trim()} {have:0.###}mm");
            }
            return outList;
        }

        private static void AuditHostTypes(BaselineAuditResult r, string group,
            List<BaselineHostType> expected, HashSet<string> present, ModelInventory model)
        {
            foreach (var t in expected ?? new List<BaselineHostType>())
            {
                if (t == null || string.IsNullOrWhiteSpace(t.Name)) continue;
                string name = t.Name.Trim();

                if (!present.Contains(name))
                {
                    r.Findings.Add(new BaselineFinding
                    {
                        Kind = BaselineFindingKind.Missing,
                        Group = group,
                        Name = name,
                        Detail = $"{t.Layers.Count} layer(s), {t.TotalThicknessMm:N0} mm — {t.Purpose}"
                    });
                    continue;
                }

                // Present. If the baseline expects a tiled finish and the model's
                // type has none, that is a CONFLICT, not a pass — it is exactly
                // the case that left a real export with no tiling at all. It is
                // still never overwritten: the model's build-up may be correct
                // and the baseline wrong for this project.
                bool wantsTile = t.HasTiledFinish;
                bool hasTile = model.HostTypeHasTiledFinish.TryGetValue(name, out bool v) && v;
                if (wantsTile && !hasTile)
                {
                    r.Findings.Add(new BaselineFinding
                    {
                        Kind = BaselineFindingKind.Conflict,
                        Group = group,
                        Name = name,
                        Detail = "exists, but carries no tiled finish layer, so nothing tiled can be "
                               + "measured from it. Not overwritten — add the layer, or rename the "
                               + "baseline type if the model's build-up is the correct one."
                    });
                    continue;
                }

                r.Findings.Add(new BaselineFinding
                {
                    Kind = BaselineFindingKind.Present, Group = group, Name = name, Detail = t.Purpose
                });
            }
        }

        private static BaselineFinding AuditFamilyExpectation(BaselineFamilyExpectation f, ModelInventory model)
        {
            model.FamilyTypesByCategory.TryGetValue(f.Category.Trim(), out var types);
            types = types ?? new List<string>();

            var conforming = types.Where(t => f.NamePatterns == null || f.NamePatterns.Count == 0
                || f.NamePatterns.Any(p => !string.IsNullOrWhiteSpace(p)
                    && (t ?? "").IndexOf(p.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)).ToList();

            bool enough = f.MinimumTypes <= 0 || conforming.Count >= f.MinimumTypes;

            return new BaselineFinding
            {
                // Never Missing: nothing here can be created without an .rfa, and
                // listing it as actionable would promise work Apply cannot do.
                Kind = enough ? BaselineFindingKind.Present : BaselineFindingKind.Guidance,
                Group = "Families (load, not created)",
                Name = f.Category.Trim(),
                Detail = enough
                    ? $"{conforming.Count} conforming type(s) present"
                    : $"{conforming.Count} of {f.MinimumTypes} expected type(s) match "
                      + (f.NamePatterns != null && f.NamePatterns.Count > 0
                            ? $"[{string.Join(" | ", f.NamePatterns)}]. " : ". ")
                      + f.Guidance
            };
        }

        /// <summary>
        /// The report a human reads before deciding to write to their model.
        /// Ordered so the two things that matter — what will change, and what
        /// will not — come first.
        /// </summary>
        public static string Report(BaselineAuditResult r)
        {
            if (r == null) return "";
            var sb = new System.Text.StringBuilder();

            if (r.BaselineProblems.Count > 0)
            {
                sb.AppendLine("THE BASELINE ITSELF HAS PROBLEMS — fix these before applying:");
                foreach (string p in r.BaselineProblems) sb.AppendLine("  • " + p);
                sb.AppendLine();
            }

            sb.AppendLine(r.NothingToMint
                ? "Nothing to create — every creatable item in the baseline is already present."
                : $"WILL CREATE {r.MissingCount} item(s):");
            foreach (var g in r.Missing.GroupBy(f => f.Group))
            {
                sb.AppendLine($"  {g.Key}:");
                foreach (var f in g) sb.AppendLine($"    + {f.Name}   ({f.Detail})");
            }

            if (r.ConflictCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"WILL NOT TOUCH — {r.ConflictCount} existing item(s) differ from the baseline:");
                foreach (var f in r.Findings.Where(x => x.Kind == BaselineFindingKind.Conflict))
                    sb.AppendLine($"    ! {f.Group} / {f.Name}: {f.Detail}");
            }

            if (r.GuidanceCount > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"CANNOT CREATE — {r.GuidanceCount} item(s) need families loaded by hand:");
                foreach (var f in r.Findings.Where(x => x.Kind == BaselineFindingKind.Guidance))
                    sb.AppendLine($"    ? {f.Name}: {f.Detail}");
            }

            sb.AppendLine();
            sb.AppendLine($"Already conforming: {r.PresentCount} item(s).");
            return sb.ToString();
        }
    }
}
