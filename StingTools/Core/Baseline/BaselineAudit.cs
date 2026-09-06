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

        /// <summary>
        /// Exact identity for the minter, when Name is not unique on its own.
        /// Two categories can want the same type name, and matching on a
        /// display string assembled with " / " would mean the minter re-parses
        /// what the auditor formatted — a seam that breaks the first time the
        /// formatting changes.
        /// </summary>
        public string Key = "";

        public string MatchKey => string.IsNullOrEmpty(Key) ? Group + "|" + Name : Key;

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

        /// <summary>Category display name → the FAMILY names loaded in it.
        /// Distinct from FamilyTypesByCategory, which holds type names.</summary>
        public Dictionary<string, List<string>> FamilyNamesByCategory =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>"Category|TypeName" → type parameter values in mm. Only the
        /// parameters the baseline asks about are collected.</summary>
        public Dictionary<string, Dictionary<string, double>> FamilyTypeParamsMm =
            new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Family name → the parameter names it already carries.</summary>
        public Dictionary<string, HashSet<string>> FamilyParamNames =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

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

        /// <summary>B3 — which catalogue packs this run adopted, if any. Null
        /// when nothing was resolved; the report then says nothing about
        /// catalogues, which is correct for a project that adopts none.</summary>
        public CatalogueAdoption Adoption;

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

            AuditFamilyTypes(r, baseline.FamilyTypes, model);
            AuditFamilyParameters(r, baseline.FamilyParameters, model);

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

        /// <summary>
        /// Layer 2. A type is only MISSING when a loaded family can host it -
        /// otherwise Apply would promise work it cannot do. That is the same
        /// split FamilyExpectation reports, applied one level down.
        /// </summary>
        private static void AuditFamilyTypes(BaselineAuditResult r,
            List<BaselineFamilyType> wanted, ModelInventory model)
        {
            const string group = "Family types";
            foreach (var ft in wanted ?? new List<BaselineFamilyType>())
            {
                if (ft == null || string.IsNullOrWhiteSpace(ft.TypeName)
                    || string.IsNullOrWhiteSpace(ft.Category)) continue;

                string cat = ft.Category.Trim(), typeName = ft.TypeName.Trim();
                string key = group + "|" + cat + "|" + typeName;
                string display = cat + " / " + typeName;

                string host = HostFamilyFor(ft, model);
                if (host == null)
                {
                    r.Findings.Add(new BaselineFinding
                    {
                        Kind = BaselineFindingKind.Guidance, Group = group,
                        Name = display, Key = key,
                        Detail = "no loaded family in this category matches ["
                               + string.Join(" | ", (ft.FamilyNamePatterns ?? new List<string>())
                                   .Where(x => !string.IsNullOrWhiteSpace(x)))
                               + "]. Load one; a type cannot be minted without a family to hold it."
                    });
                    continue;
                }

                model.FamilyTypesByCategory.TryGetValue(cat, out var existing);
                bool present = existing != null && existing.Any(t =>
                    string.Equals(t, typeName, StringComparison.OrdinalIgnoreCase));

                if (!present)
                {
                    r.Findings.Add(new BaselineFinding
                    {
                        Kind = BaselineFindingKind.Missing, Group = group,
                        Name = display, Key = key,
                        Detail = "in family [" + host + "] - " + DescribeParams(ft) + ft.Purpose
                    });
                    continue;
                }

                // Present. Same name, different dimensions is a CONFLICT. The
                // model version may be deliberate, and overwriting a door type
                // somebody authored is not a fix.
                var differs = ParamsThatDiffer(ft, cat, typeName, model);
                if (differs.Count > 0)
                {
                    r.Findings.Add(new BaselineFinding
                    {
                        Kind = BaselineFindingKind.Conflict, Group = group,
                        Name = display, Key = key,
                        Detail = "exists with different " + string.Join(", ", differs)
                               + ". Not overwritten - rename the baseline type, or accept the "
                               + "model version as correct for this project."
                    });
                    continue;
                }

                r.Findings.Add(new BaselineFinding
                {
                    Kind = BaselineFindingKind.Present, Group = group,
                    Name = display, Key = key, Detail = ft.Purpose
                });
            }
        }

        /// <summary>First loaded family in the category matching any pattern, or null.</summary>
        public static string HostFamilyFor(BaselineFamilyType ft, ModelInventory model)
        {
            if (ft == null || model == null || string.IsNullOrWhiteSpace(ft.Category)) return null;
            if (!model.FamilyNamesByCategory.TryGetValue(ft.Category.Trim(), out var families)
                || families == null) return null;

            foreach (string pattern in ft.FamilyNamePatterns ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(pattern)) continue;
                foreach (string fam in families)
                    if (!string.IsNullOrWhiteSpace(fam)
                        && fam.IndexOf(pattern.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                        return fam;
            }
            return null;
        }

        private static List<string> ParamsThatDiffer(BaselineFamilyType ft, string cat,
                                                     string typeName, ModelInventory model)
        {
            var differs = new List<string>();
            if (!model.FamilyTypeParamsMm.TryGetValue(cat + "|" + typeName, out var actual)
                || actual == null)
                return differs;   // not collected - cannot claim a difference

            foreach (var p in ft.Parameters ?? new List<BaselineFamilyTypeParam>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                if (!actual.TryGetValue(p.Name.Trim(), out double have)) continue;
                if (Math.Abs(have - p.ValueMm) > 0.5)   // sub-mm is rounding, not a difference
                    differs.Add(p.Name + " (" + have.ToString("N0") + " mm, baseline says "
                              + p.ValueMm.ToString("N0") + " mm)");
            }
            return differs;
        }

        private static string DescribeParams(BaselineFamilyType ft)
        {
            var named = (ft.Parameters ?? new List<BaselineFamilyTypeParam>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).ToList();
            if (named.Count == 0) return "";
            return string.Join(", ", named.Select(p => p.Name + " " + p.ValueMm.ToString("N0"))) + " - ";
        }

        /// <summary>
        /// Layer 3. One finding per CATEGORY, not per family: a report listing
        /// forty families is not read, and the decision (augment doors?) is
        /// taken per category anyway.
        /// </summary>
        private static void AuditFamilyParameters(BaselineAuditResult r,
            List<BaselineFamilyParameterSet> wanted, ModelInventory model)
        {
            const string group = "Family parameters";
            foreach (var fp in wanted ?? new List<BaselineFamilyParameterSet>())
            {
                if (fp == null || string.IsNullOrWhiteSpace(fp.Category)) continue;
                string cat = fp.Category.Trim();
                string key = group + "|" + cat;

                var names = (fp.Parameters ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
                if (names.Count == 0) continue;

                model.FamilyNamesByCategory.TryGetValue(cat, out var families);
                families = families ?? new List<string>();
                if (families.Count == 0)
                {
                    r.Findings.Add(new BaselineFinding
                    {
                        Kind = BaselineFindingKind.Guidance, Group = group, Name = cat, Key = key,
                        Detail = "no families are loaded in this category, so there is nothing to augment."
                    });
                    continue;
                }

                int needing = families.Count(f =>
                {
                    model.FamilyParamNames.TryGetValue(f, out var have);
                    return names.Any(n => have == null || !have.Contains(n));
                });

                r.Findings.Add(new BaselineFinding
                {
                    Kind = needing > 0 ? BaselineFindingKind.Missing : BaselineFindingKind.Present,
                    Group = group, Name = cat, Key = key,
                    Detail = needing > 0
                        ? needing + " of " + families.Count + " loaded famil(ies) lack "
                          + names.Count + " shared type parameter(s): " + string.Join(", ", names)
                        : "all " + families.Count + " loaded famil(ies) already carry "
                          + names.Count + " parameter(s)"
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

            // Adoption is a STATED DECISION and is reported as one. A project
            // that believes it adopted a pack and did not is exactly the silent
            // absence this mechanism exists to avoid, so an unknown id is named.
            string adopted = r.Adoption?.Summary();
            if (!string.IsNullOrEmpty(adopted))
            {
                sb.AppendLine();
                sb.AppendLine(adopted);
            }

            sb.AppendLine();
            sb.AppendLine($"Already conforming: {r.PresentCount} item(s).");
            return sb.ToString();
        }
    }
}
