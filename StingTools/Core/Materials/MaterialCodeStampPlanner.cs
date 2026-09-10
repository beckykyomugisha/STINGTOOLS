// ══════════════════════════════════════════════════════════════════════════
//  MaterialCodeStampPlanner.cs — put the register's CODE on the material, so
//  that something other than a name can answer "what is this?".
//
//  WHY. MAT_CODE is a declared shared parameter (MR_PARAMETERS.txt:879,
//  758ba3d0-ea41-51fc-8dbf-3bb444174385), bound to `Materials` and to nothing
//  else, and the BOQ rate chain keys on it: RateProviders "Pass C", confidence
//  80, between the PROD match (85) and the COBie type map (75). The register
//  that supplies the codes is 100% populated — 815 BLE + 464 MEP rows, every
//  one carrying a unique MAT_CODE.
//
//  One writer exists: MaterialCommands.ApplySharedParamValues (:1121), reached
//  only from CreateBLEMaterials / CreateMEPMaterials. The OTHER path that mints
//  materials — CompoundTypeCreator, which is what builds a project's wall /
//  floor / ceiling / roof catalogue — called ApplyMaterialProperties and not the
//  shared-parameter writer, so every material it created was born without a
//  code. And a model whose materials predate any of this has none either.
//
//  So this plans the backfill, and the decision is deliberately Revit-free: the
//  hard part is not reading a parameter, it is deciding WHETHER to write, and
//  that decision is provable against the shipped register without a host.
//
//  ── THE RULE ──────────────────────────────────────────────────────────────
//  Match by exact MAT_NAME — trimmed and case-insensitive, which is what
//  MaterialRegistry.ByName already means by exact. NOT fuzzy: Revit appends
//  " 2" and " (1)" on a name clash, and a material called
//  `CEMENT SCREED 50MM 2` may be a duplicate of the register row or may be
//  somebody's variant. Guessing which would put a governed code on an
//  ungoverned material, which is the failure this whole area is about.
//
//  NEVER OVERWRITE. A code already present belongs to whoever set it. Where it
//  disagrees with the register the row says so and the plan still does not
//  write — the register is a challenger, not an oracle, and a disagreement is a
//  thing for a human to read, not for a tool to settle.
//
//  ── THE THIRD COUNT IS THE USEFUL ONE ─────────────────────────────────────
//  Stamped and already-coded are bookkeeping. NoRegisterRow is the project's
//  own vocabulary — the materials the governed register does not describe — and
//  it is what the next register revision should absorb. Measured on the
//  1,815-name corpus in Fixtures/material_names_20260908.csv: 1,279 of those
//  names are register rows and 536 are not.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Core.Materials
{
    /// <summary>What the planner decided to do about one material's MAT_CODE.</summary>
    public enum MaterialCodeVerdict
    {
        /// <summary>No code today, and the register names this material. This one writes.</summary>
        Stamp,
        /// <summary>A code is already there. Never overwritten, register row or not.</summary>
        AlreadyCoded,
        /// <summary>No code today, and the register does not describe this material.
        /// Not a failure — it is the project's own vocabulary, and the number worth
        /// reading.</summary>
        NoRegisterRow,
    }

    /// <summary>One row of a material_code_plan CSV.</summary>
    public sealed class MaterialCodeStampRow
    {
        public string MaterialName = "";
        /// <summary>MAT_CODE as it stands on the material now. Empty when unset.</summary>
        public string ExistingCode = "";
        /// <summary>What the register says, or "" when it says nothing.</summary>
        public string RegisterCode = "";
        public MaterialCodeVerdict Verdict;
        public string Reason = "";

        /// <summary>The one verdict that touches the model.</summary>
        public bool WillWrite => Verdict == MaterialCodeVerdict.Stamp;

        /// <summary>A code is present AND the register names a different one. Reported,
        /// never reconciled — see the file header.</summary>
        public bool CodeDisagrees =>
            Verdict == MaterialCodeVerdict.AlreadyCoded
            && RegisterCode.Length > 0
            && !string.Equals(ExistingCode.Trim(), RegisterCode.Trim(),
                              StringComparison.OrdinalIgnoreCase);

        public override string ToString() => Verdict + ": " + MaterialName + " — " + Reason;
    }

    /// <summary>The three headline counts, plus the two a reader asks for next.</summary>
    public sealed class MaterialCodeStampTally
    {
        public int Total;
        public int Stamped;
        public int AlreadyCoded;
        public int NoRegisterRow;

        /// <summary>Materials the register does not name AT ALL, whether or not they
        /// already carry a code. <see cref="NoRegisterRow"/> counts only the ones that
        /// also have no code, because the three headline verdicts are about the WRITE
        /// decision and must not double-count. This is the vocabulary number.</summary>
        public int RegisterSilent;

        /// <summary>Already coded, and the register names a different code.</summary>
        public int Disagreements;
    }

    public static class MaterialCodeStampPlanner
    {
        /// <summary>Decide one material. <paramref name="existingCode"/> is MAT_CODE as
        /// read off the material — null and whitespace both mean "unset".</summary>
        public static MaterialCodeStampRow Plan(
            string materialName, string existingCode, MaterialRegistry registry)
        {
            var row = new MaterialCodeStampRow
            {
                MaterialName = (materialName ?? "").Trim(),
                ExistingCode = (existingCode ?? "").Trim(),
            };

            var reg = registry?.ByName(row.MaterialName);
            row.RegisterCode = (reg?.Code ?? "").Trim();

            if (row.ExistingCode.Length > 0)
            {
                row.Verdict = MaterialCodeVerdict.AlreadyCoded;
                if (row.RegisterCode.Length == 0)
                    row.Reason = "already " + row.ExistingCode
                               + "; the register does not name this material";
                else if (string.Equals(row.ExistingCode, row.RegisterCode,
                                       StringComparison.OrdinalIgnoreCase))
                    row.Reason = "already " + row.ExistingCode + ", which is what the register says";
                else
                    row.Reason = "already " + row.ExistingCode + "; the register says "
                               + row.RegisterCode + ". Reported, not changed — the register is a "
                               + "challenger, not an oracle.";
                return row;
            }

            if (row.RegisterCode.Length == 0)
            {
                row.Verdict = MaterialCodeVerdict.NoRegisterRow;
                row.Reason = "no register row with this MAT_NAME — the project's own vocabulary";
                return row;
            }

            row.Verdict = MaterialCodeVerdict.Stamp;
            row.Reason = "register row " + row.RegisterCode + " (" + reg.Source
                       + ") names this material exactly";
            return row;
        }

        /// <summary>Plan a whole model's materials. Order is the caller's.</summary>
        public static List<MaterialCodeStampRow> PlanAll(
            IEnumerable<KeyValuePair<string, string>> nameAndExistingCode,
            MaterialRegistry registry)
        {
            var outRows = new List<MaterialCodeStampRow>();
            foreach (var kv in nameAndExistingCode
                     ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                if (string.IsNullOrWhiteSpace(kv.Key)) continue;
                outRows.Add(Plan(kv.Key, kv.Value, registry));
            }
            return outRows;
        }

        public static MaterialCodeStampTally Tally(IEnumerable<MaterialCodeStampRow> rows)
        {
            var list = (rows ?? Enumerable.Empty<MaterialCodeStampRow>()).ToList();
            return new MaterialCodeStampTally
            {
                Total          = list.Count,
                Stamped        = list.Count(r => r.Verdict == MaterialCodeVerdict.Stamp),
                AlreadyCoded   = list.Count(r => r.Verdict == MaterialCodeVerdict.AlreadyCoded),
                NoRegisterRow  = list.Count(r => r.Verdict == MaterialCodeVerdict.NoRegisterRow),
                RegisterSilent = list.Count(r => r.RegisterCode.Length == 0),
                Disagreements  = list.Count(r => r.CodeDisagrees),
            };
        }

        public static string Summary(IEnumerable<MaterialCodeStampRow> rows)
        {
            var list = (rows ?? Enumerable.Empty<MaterialCodeStampRow>()).ToList();
            var t = Tally(list);
            if (t.Total == 0) return "This model has no materials.";

            var sb = new StringBuilder();
            sb.AppendLine(t.Total + " material(s):");
            sb.AppendLine("  " + t.Stamped + " will be stamped from the register");
            sb.AppendLine("  " + t.AlreadyCoded + " already carry a MAT_CODE — never overwritten");
            sb.AppendLine("  " + t.NoRegisterRow + " have no register row with that name");
            if (t.Disagreements > 0)
                sb.AppendLine("  (" + t.Disagreements + " of the coded ones disagree with the "
                            + "register — listed in the CSV, changed by nothing)");
            sb.AppendLine();
            sb.AppendLine(t.RegisterSilent + " of this model's materials are not in the governed "
                        + "register at all. That is the project's own vocabulary, and the number "
                        + "the next register revision should absorb — not an error.");
            return sb.ToString();
        }

        /// <summary>The plan CSV, in the shape Materials_SetClass writes.</summary>
        public static List<string> ToCsv(IEnumerable<MaterialCodeStampRow> rows)
        {
            var outLines = new List<string> { "Material,ExistingCode,RegisterCode,Verdict,Reason" };
            foreach (var r in rows ?? Enumerable.Empty<MaterialCodeStampRow>())
                outLines.Add(string.Join(",", Csv(r.MaterialName), Csv(r.ExistingCode),
                    Csv(r.RegisterCode), Csv(r.Verdict.ToString()), Csv(r.Reason)));
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
