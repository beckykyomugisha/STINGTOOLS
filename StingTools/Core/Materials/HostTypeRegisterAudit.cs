// ══════════════════════════════════════════════════════════════════════════
//  HostTypeRegisterAudit.cs — does the model's build-up match the register row
//  its type is named after?
//
//  WHY. On 2026-09-09 `Baseline_RenameTypes` proposed the identical name
//  `PLNS_SLB_RC100` for 87 floor types. Verified: 86 of those 87 are register
//  MAT_NAMEs verbatim, all FLR-* rows (the 87th, "Concrete 100mm", is a Revit
//  stock type). The register says
//
//      STANDARD CEMENT SCREED 50MM  =  1 × 50 mm CEMENT SCREED (SUBSTRATE)
//
//  and the model has it as
//
//      1 × 100 mm Concrete, Cast-in-Place gray
//
//  — and so do the other 85. Every quantity taken off any of them answers
//  "100 mm of concrete", and the rename collision is a SYMPTOM: the planner
//  reads the core material, and 86 types share one.
//
//  Of the 95 FLR-* rows, 28 declare two or three layers and all 95 declare a
//  layer-1 material. **The register was read for its NAME column and nothing
//  else.**
//
//  This is the read-only half, and deliberately the ONLY half. Rebuilding 87
//  types' compound structures from a CSV, unattended, on a delivered model, is a
//  larger and less reversible act than the rename that started this — and the
//  audit is what tells a human which of the 87 are wrong before anybody decides
//  how to fix them.
//
//  Revit-free: it compares a plain description of the modelled layers against
//  the register row, so the comparison rules are provable without a host.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Core.Materials
{
    /// <summary>One modelled layer, as read off a Revit compound structure.</summary>
    public sealed class ModelledLayer
    {
        public string MaterialName = "";
        public double ThicknessMm;
        /// <summary>Revit's function word, or "" when it could not be read.</summary>
        public string Function = "";

        public override string ToString() => $"{MaterialName} {ThicknessMm:0.#}mm";
    }

    /// <summary>One host type in the model, reduced to what the audit compares.</summary>
    public sealed class ModelledHostType
    {
        public string Category = "";
        public string TypeName = "";
        public int InstanceCount;
        public List<ModelledLayer> Layers = new List<ModelledLayer>();

        /// <summary>The register code this type RECORDS, from StingProvenanceSchema —
        /// written by CompoundTypeCreator when it built the type from a register row.
        /// Empty for every type created before that landed, which is why name matching
        /// stays as the fallback rather than being replaced.</summary>
        public string RecordedCode = "";

        public double TotalThicknessMm => Layers?.Sum(l => Math.Max(0, l.ThicknessMm)) ?? 0;
    }

    public enum RegisterAuditVerdict
    {
        /// <summary>The type's name is not a register MAT_NAME. Not a finding — most
        /// types are not named after a register row, and calling that a mismatch would
        /// bury the ones that are.</summary>
        NotInRegister,
        /// <summary>Layer count, materials and thicknesses all match.</summary>
        Matches,
        /// <summary>The register declares layers and the model has ONE. The 87-type shape.</summary>
        Flattened,
        /// <summary>Same layer count, different materials or thicknesses.</summary>
        Differs,
        /// <summary>The model type has no compound structure to read.</summary>
        NoStructure,
        /// <summary>The type RECORDS a register code and its layers do not build that
        /// row. The strongest finding here, because a recorded code is a CLAIM about
        /// identity rather than a coincidence of naming — and the geometry refutes it.
        /// Never re-matched by name: that would let a wrong code hide behind a right
        /// name, which is the whole failure this verdict exists to surface.</summary>
        CodeSaysOtherwise,
    }

    public sealed class RegisterAuditRow
    {
        public string Category = "";
        public string TypeName = "";
        public int InstanceCount;
        public string RegisterCode = "";
        public RegisterAuditVerdict Verdict;
        /// <summary>One line, in the words the brief asked for:
        /// "register says 1 × 50 mm CEMENT SCREED, model has 1 × 100 mm Concrete…".</summary>
        public string Detail = "";

        public bool IsFinding => Verdict == RegisterAuditVerdict.Flattened
                              || Verdict == RegisterAuditVerdict.Differs
                              || Verdict == RegisterAuditVerdict.NoStructure
                              || Verdict == RegisterAuditVerdict.CodeSaysOtherwise;

        /// <summary>True when the register row was found by the type's RECORDED code
        /// rather than by its name.</summary>
        public bool MatchedByRecordedCode;

        public override string ToString() => $"{Verdict}: {TypeName} — {Detail}";
    }

    public static class HostTypeRegisterAudit
    {
        /// <summary>Thickness agreement tolerance. 1 mm, because Revit stores feet and a
        /// round trip through 304.8 does not land on the register's integer.</summary>
        public const double ToleranceMm = 1.0;

        public static List<RegisterAuditRow> Audit(
            IEnumerable<ModelledHostType> types, MaterialRegistry registry)
        {
            var outRows = new List<RegisterAuditRow>();
            foreach (var t in types ?? Enumerable.Empty<ModelledHostType>())
            {
                var row = new RegisterAuditRow
                {
                    Category = t.Category, TypeName = t.TypeName, InstanceCount = t.InstanceCount,
                };
                // The RECORDED code wins over the name. A type that says what it was
                // built from is not guessing, and a name is a coincidence — 86 of the 87
                // floor types carried a register MAT_NAME verbatim while building
                // something else entirely.
                string recorded = (t.RecordedCode ?? "").Trim();
                MaterialRow reg;
                if (recorded.Length > 0)
                {
                    row.MatchedByRecordedCode = true;
                    row.RegisterCode = recorded;
                    reg = registry?.ByCode(recorded);
                    if (reg == null)
                    {
                        // A recorded code the register does not issue. NOT re-matched by
                        // name: falling back here is exactly how a wrong code would hide
                        // behind a right name.
                        row.Verdict = RegisterAuditVerdict.CodeSaysOtherwise;
                        row.Detail = $"type records {recorded}, which is not a code this "
                                   + "register issues";
                        outRows.Add(row);
                        continue;
                    }
                }
                else
                {
                    reg = registry?.ByName(t.TypeName);
                    if (reg == null)
                    {
                        row.Verdict = RegisterAuditVerdict.NotInRegister;
                        row.Detail = "type name is not a register MAT_NAME";
                        outRows.Add(row);
                        continue;
                    }
                    row.RegisterCode = reg.Code;
                }

                var modelled = (t.Layers ?? new List<ModelledLayer>())
                               .Where(l => l != null).ToList();

                if (modelled.Count == 0)
                {
                    row.Verdict = row.MatchedByRecordedCode
                        ? RegisterAuditVerdict.CodeSaysOtherwise
                        : RegisterAuditVerdict.NoStructure;
                    row.Detail = $"register says {Describe(reg)}, model has no compound structure";
                    outRows.Add(row);
                    continue;
                }

                bool countMatches = modelled.Count == reg.Layers.Count;
                bool everyLayerMatches = countMatches && reg.Layers
                    .Zip(modelled, (r, m) => NameAgrees(r.Material, m.MaterialName)
                                          && Math.Abs(r.ThicknessMm - m.ThicknessMm) <= ToleranceMm)
                    .All(x => x);

                if (everyLayerMatches)
                {
                    row.Verdict = RegisterAuditVerdict.Matches;
                    row.Detail = $"{modelled.Count} layer(s), as the register declares";
                }
                else if (row.MatchedByRecordedCode)
                {
                    // A recorded code contradicted by the geometry. The LAYERS WIN — this
                    // reports, it never reconciles, because Revit measures the layers and
                    // anything else would price a building that was not drawn.
                    row.Verdict = RegisterAuditVerdict.CodeSaysOtherwise;
                    var other = MatchByLayers(registry, modelled, reg.Code);
                    row.Detail = $"type records {reg.Code} ({Describe(reg)}), model has "
                               + $"{Describe(modelled)}"
                               + (other != null
                                  ? $" — which is {other.Code} {other.Name}"
                                  : " — which is no register row");
                }
                else if (reg.Layers.Count > 1 && modelled.Count == 1)
                {
                    // Called out separately because it is the 87-type signature and has a
                    // single cause — the layer columns were never read — where "Differs"
                    // can be any of a dozen things.
                    row.Verdict = RegisterAuditVerdict.Flattened;
                    row.Detail = $"register says {Describe(reg)}, model has {Describe(modelled)}";
                }
                else
                {
                    row.Verdict = RegisterAuditVerdict.Differs;
                    row.Detail = $"register says {Describe(reg)}, model has {Describe(modelled)}";
                }
                outRows.Add(row);
            }
            return outRows;
        }

        /// <summary>
        /// The register row this build-up actually IS, or null. Used only to say what a
        /// contradicted code should probably have been — never to re-label the type,
        /// because a build-up that happens to match a row is not evidence the type meant
        /// that row.
        /// </summary>
        internal static MaterialRow MatchByLayers(
            MaterialRegistry registry, List<ModelledLayer> modelled, string excludeCode)
        {
            if (registry == null || modelled == null || modelled.Count == 0) return null;
            foreach (var r in registry.Rows)
            {
                if (r.Layers == null || r.Layers.Count != modelled.Count) continue;
                if (!string.IsNullOrEmpty(excludeCode)
                    && string.Equals(r.Code, excludeCode, StringComparison.OrdinalIgnoreCase))
                    continue;
                bool all = r.Layers.Zip(modelled, (rl, m) => NameAgrees(rl.Material, m.MaterialName)
                                       && Math.Abs(rl.ThicknessMm - m.ThicknessMm) <= ToleranceMm)
                                   .All(x => x);
                if (all) return r;
            }
            return null;
        }

        /// <summary>
        /// Two material names agree when one contains the other as a whole word run, after
        /// trimming. Not equality: the model's material is created FROM the register's layer
        /// material, and Revit appends " (1)", " 2" and similar on a name clash — which it
        /// does often, because the same layer material appears on dozens of rows.
        /// </summary>
        internal static bool NameAgrees(string registerMaterial, string modelMaterial)
        {
            string a = (registerMaterial ?? "").Trim();
            string b = (modelMaterial ?? "").Trim();
            if (a.Length == 0 || b.Length == 0) return false;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
            return MaterialSchedule.PatternMatch.Contains(b, a)
                || MaterialSchedule.PatternMatch.Contains(a, b);
        }

        private static string Describe(MaterialRow reg)
            => string.Join(" + ", reg.Layers.Select(l => $"1 × {l.ThicknessMm:0.#} mm {l.Material}"))
               is string s && s.Length > 0 ? s : $"a single {reg.ThicknessMm:0.#} mm layer (no layers declared)";

        private static string Describe(List<ModelledLayer> layers)
            => string.Join(" + ", layers.Select(l => $"1 × {l.ThicknessMm:0.#} mm {l.MaterialName}"));

        public static string Summary(IReadOnlyCollection<RegisterAuditRow> rows)
        {
            if (rows == null || rows.Count == 0) return "No layered host types found in this model.";
            int inReg = rows.Count(r => r.Verdict != RegisterAuditVerdict.NotInRegister);
            int match = rows.Count(r => r.Verdict == RegisterAuditVerdict.Matches);
            int flat = rows.Count(r => r.Verdict == RegisterAuditVerdict.Flattened);
            int diff = rows.Count(r => r.Verdict == RegisterAuditVerdict.Differs);
            int none = rows.Count(r => r.Verdict == RegisterAuditVerdict.NoStructure);
            int said = rows.Count(r => r.Verdict == RegisterAuditVerdict.CodeSaysOtherwise);

            var sb = new StringBuilder();
            sb.AppendLine($"{rows.Count} host type(s); {inReg} are named after a register row.");
            if (inReg == 0)
            {
                sb.AppendLine();
                sb.AppendLine("None of this model's type names appears in BLE_MATERIALS.csv or "
                            + "MEP_MATERIALS.csv, so there is nothing here to compare. That is a "
                            + "normal answer, not a failure — this audit only speaks about types "
                            + "that claim to BE a register material.");
                return sb.ToString();
            }

            sb.AppendLine($"  {match} match the register's build-up");
            sb.AppendLine($"  {flat} are FLATTENED — the register declares layers and the model has one");
            sb.AppendLine($"  {diff} differ in material or thickness");
            sb.AppendLine($"  {none} have no compound structure at all");
            if (said > 0)
                sb.AppendLine($"  {said} RECORD a register code their layers do not build "
                            + "— the code is a claim, the geometry is the fact");
            sb.AppendLine();
            sb.AppendLine("This is READ-ONLY and stays that way. A flattened type's geometry is "
                        + "wrong, but rebuilding it from a CSV also moves every element hosted on "
                        + "it — so this names them and a human decides.");
            return sb.ToString();
        }

        public static List<string> ToCsv(IEnumerable<RegisterAuditRow> rows)
        {
            var outLines = new List<string>
            { "Verdict,Category,TypeName,Instances,RegisterCode,MatchedBy,Detail" };
            foreach (var r in rows ?? Enumerable.Empty<RegisterAuditRow>())
                outLines.Add(string.Join(",", Csv(r.Verdict.ToString()), Csv(r.Category),
                    Csv(r.TypeName), r.InstanceCount, Csv(r.RegisterCode),
                    Csv(r.MatchedByRecordedCode ? "recorded code" : "name"), Csv(r.Detail)));
            return outLines;
        }

        private static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0
                ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }
    }
}
