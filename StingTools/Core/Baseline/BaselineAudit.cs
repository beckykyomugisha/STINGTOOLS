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

            return r;
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
