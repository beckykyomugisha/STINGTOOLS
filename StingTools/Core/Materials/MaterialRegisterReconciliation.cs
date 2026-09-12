// ══════════════════════════════════════════════════════════════════════════
//  MaterialRegisterReconciliation.cs — the register and the needle table, each
//  used as a challenger to the other, and NEITHER used as the answer.
//
//  Two vocabularies answer "what substance is this material". Both are wrong in
//  places, and — this is the finding — neither is wrong in a way the other can
//  be trusted to correct automatically:
//
//    the register is RIGHT   MASONRY PAINT WHITE → Paint            (needles: Masonry)
//                            26 × CEMENT PLASTER/RENDER → Concrete  (needles: Gypsum)
//                            FIBERGLASS ACOUSTIC TILE → Insulation  (needles: Ceramic)
//                            EXPOSED AGGREGATE 100MM → Concrete     (needles: Stone)
//    the needles are RIGHT   GRANITE SKIRTING 100MM → Stone         (register: Wood)
//                            PVC SKIRTING 80MM → Plastic            (register: Wood)
//                            LIGHTWEIGHT SCREED 40MM → Concrete     (register: Metal)
//                            EXTERIOR TIMBER CLADDING 35MM → Wood   (register: Metal)
//                            CORK TILE 6MM → Wood                   (register: Carpet)
//
//  The register's class column classifies by TRADE: a skirting is Wood whatever
//  it is made of, a cladding is Metal, sanitaryware is Glass, a floor topping is
//  Concrete. That is a coherent thing for it to be — it is a procurement
//  register — and it is not what MaterialClass means to the carbon and cost
//  engines.
//
//  AND AGREEMENT RATE DOES NOT IDENTIFY THE SAFE CLASSES, which is what kills
//  every mechanical rule tried here. Measured 2026-09-09 over all 1,279 rows:
//
//      register class   agree  conflict   who is right where they differ
//      Plastic             52         0
//      Plaster             21         0
//      Fabric               4         0
//      Insulation          17         2   register
//      Masonry             67         9   register (paving units are masonry)
//      Glass                6         1   register
//      Wood                48         9   NEEDLES — every conflict is a skirting or paving
//      Paint               35         8   register (masonry paint is paint)
//      Metal               66        20   NEEDLES mostly (screed, cladding, conduit, tank)
//      Concrete            29        35   REGISTER mostly (cement plaster is cementitious)
//
//  Concrete agrees least and is mostly right; Wood agrees well and is badly
//  wrong where it differs. A whitelist keyed on agreement would be exactly
//  backwards. So this class REPORTS, and a human decides — which is what
//  "Report disagreements; propose edits separately with evidence attached"
//  means in practice.
//
//  The gate over this is a COUNT REGRESSION, not a zero. Zero disagreements is
//  not reachable today and pretending otherwise would leave the whole thing
//  disabled.
// ══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace StingTools.Core.Materials
{
    public enum ReconcileVerdict
    {
        /// <summary>Both name the same Revit class.</summary>
        Agree,
        /// <summary>Both have an opinion and they differ. The rows a human must settle.</summary>
        Conflict,
        /// <summary>The register names a class the needle table has no word for.</summary>
        OnlyRegister,
        /// <summary>The needle table answers and the register's class names no substance.</summary>
        OnlyNeedles,
        /// <summary>Neither answers. Not a disagreement — a gap in both.</summary>
        NeitherAnswers,
    }

    public sealed class ReconcileRow
    {
        public string Code = "";
        public string Name = "";
        public string Category = "";
        /// <summary>The register's own word — Plaster / Ceiling / Generic …</summary>
        public string RegisterClassRaw = "";
        /// <summary>That word translated to Revit's vocabulary, or null when it names
        /// no substance.</summary>
        public string RegisterClass;
        /// <summary>What MaterialClassPlanner's needle table says, or null.</summary>
        public string NeedleClass;
        public ReconcileVerdict Verdict;

        public override string ToString()
            => $"{Verdict}: {Code} {Name} — register {Show(RegisterClassRaw)}"
             + $"{(RegisterClass != null && !string.Equals(RegisterClass, RegisterClassRaw, StringComparison.OrdinalIgnoreCase) ? " → " + RegisterClass : "")}"
             + $", needles {Show(NeedleClass)}";

        private static string Show(string s) => string.IsNullOrWhiteSpace(s) ? "(none)" : s;
    }

    public static class MaterialRegisterReconciliation
    {
        public static List<ReconcileRow> Reconcile(MaterialRegistry registry)
        {
            var outRows = new List<ReconcileRow>();
            foreach (var r in registry?.Rows ?? Enumerable.Empty<MaterialRow>())
            {
                string mapped = MaterialRegistry.ToRevitClass(r.IdentityClass);
                // Planned with a BLANK existing class deliberately: the question is what the
                // NAME says, not what a document happens to hold.
                string needle = MaterialClassPlanner.Plan(r.Name, "").ProposedClass;

                ReconcileVerdict v;
                if (mapped != null && needle != null)
                    v = string.Equals(mapped, needle, StringComparison.OrdinalIgnoreCase)
                        ? ReconcileVerdict.Agree : ReconcileVerdict.Conflict;
                else if (mapped != null) v = ReconcileVerdict.OnlyRegister;
                else if (needle != null) v = ReconcileVerdict.OnlyNeedles;
                else v = ReconcileVerdict.NeitherAnswers;

                outRows.Add(new ReconcileRow
                {
                    Code = r.Code, Name = r.Name, Category = r.Category,
                    RegisterClassRaw = r.IdentityClass, RegisterClass = mapped,
                    NeedleClass = needle, Verdict = v,
                });
            }
            return outRows;
        }

        /// <summary>
        /// The rows whose <c>MAT_NAME</c> states a substance the class column contradicts by
        /// naming nothing at all. Started from the brief's 31 GYPSUM-named <c>Generic</c>
        /// rows and generalised, because the same shape holds for every substance word the
        /// needle table knows: if the NAME says it and the class column shrugs, the class
        /// column is the one that is behind.
        ///
        /// <para>These are the cheapest data edits to propose, because the evidence is in
        /// the row itself. They are still PROPOSED — this returns them, it does not write
        /// them, and 1,279 rows of governed corporate data are not edited in bulk by a
        /// tool that found a pattern.</para>
        /// </summary>
        public static List<ReconcileRow> NameSaysMoreThanClassDoes(IEnumerable<ReconcileRow> rows)
            => (rows ?? Enumerable.Empty<ReconcileRow>())
               .Where(r => r.Verdict == ReconcileVerdict.OnlyNeedles
                        && MaterialRegistry.ClassesThatNameNoSubstance
                               .Any(c => string.Equals(c, r.RegisterClassRaw, StringComparison.OrdinalIgnoreCase)))
               .ToList();

        public static string Summary(IReadOnlyCollection<ReconcileRow> rows)
        {
            if (rows == null || rows.Count == 0) return "The register loaded no rows.";
            var by = rows.GroupBy(r => r.Verdict).ToDictionary(g => g.Key, g => g.Count());
            int C(ReconcileVerdict v) => by.TryGetValue(v, out int n) ? n : 0;
            var sb = new StringBuilder();
            sb.AppendLine($"{rows.Count} register row(s):");
            sb.AppendLine($"  {C(ReconcileVerdict.Agree)} agree");
            sb.AppendLine($"  {C(ReconcileVerdict.Conflict)} CONFLICT — both have an opinion and they differ");
            sb.AppendLine($"  {C(ReconcileVerdict.OnlyRegister)} the register answers, the needle table has no word");
            sb.AppendLine($"  {C(ReconcileVerdict.OnlyNeedles)} the name says a substance, the class column names none");
            sb.AppendLine($"  {C(ReconcileVerdict.NeitherAnswers)} neither answers");
            sb.AppendLine();
            sb.AppendLine("The conflicts are not one side being wrong: the register is right about "
                        + "cement plaster and masonry paint, the needle table is right about "
                        + "skirtings and screed. Each row is a decision, which is why this "
                        + "reports and does not write.");
            return sb.ToString();
        }

        /// <summary>The full report, for humans to act on. One row per register row so the
        /// agreements are visible too — a report that showed only disagreements would hide
        /// its own denominator.</summary>
        public static List<string> ToCsv(IEnumerable<ReconcileRow> rows)
        {
            var outLines = new List<string>
            { "Verdict,Code,Name,Category,RegisterClassRaw,RegisterClassMapped,NeedleClass" };
            foreach (var r in rows ?? Enumerable.Empty<ReconcileRow>())
                outLines.Add(string.Join(",", Csv(r.Verdict.ToString()), Csv(r.Code), Csv(r.Name),
                    Csv(r.Category), Csv(r.RegisterClassRaw), Csv(r.RegisterClass ?? ""),
                    Csv(r.NeedleClass ?? "")));
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
