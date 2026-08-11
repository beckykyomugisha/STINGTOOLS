// ============================================================================
// UniversalTagDiffCommand.cs — does this tag family match the build sheet?
//
// WHY THIS EXISTS
//
// The universal tag label is 65 rows, each a calculated value whose formula
// gates its tier: if(TAG_PARA_STATE_n_BOOL, <source>, ""). Revit omits a
// parameter that evaluates to empty, so a gate turned off collapses its rows and
// the tag gets shorter. That is how tier depth was designed to work, and
// UNIVERSAL_TAG_FINALIZE_RUNNER.md records it working.
//
// On the master family in front of us it does not work, and the reason is not
// the design: the Formula column in Family Types is empty on every row. The
// gates exist as parameters; nothing reads them; every row renders
// unconditionally. Toggling a gate does nothing because no formula consults it.
//
// This command reports that difference precisely, per row, rather than leaving
// it to be inferred from a screenshot. It is READ-ONLY. It is also the oracle
// for any code that authors these formulas: author into a copy, run this, and a
// clean report is the proof. FamilyLabelAuthor currently writes the formula onto
// the SOURCE parameter rather than the calculated value, which this command
// flags as SELF-REFERENTIAL — so the check earns its keep before anything is
// automated.
//
// WHAT IT DOES NOT DO
//
// It does not create, repair or reorder label rows. The Revit API cannot author
// label rows at all (build sheet, line 7), and even the parts that ARE possible
// — binding a parameter, setting a formula — stay out of a command whose whole
// value is being trustworthy about what it found.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Tags;

namespace StingTools.Commands.TagStudio
{
    /// <summary>
    /// Compares a tag family against STING_UNIVERSAL_TAG_ROWS.csv (generated from
    /// the build sheet) and reports, per row, whether the calculated value exists
    /// and carries the correct gated formula.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class UniversalTagDiffCommand : IExternalCommand
    {
        /// <summary>Per-row outcome. Order matters: worst first in the summary.</summary>
        private enum RowVerdict
        {
            Ok,                 // calc value present, formula matches the spec
            FormulaMismatch,    // present with a formula, but not the spec's
            SelfReferential,    // formula sits on the source parameter — will not work
            NoFormula,          // parameter exists, Formula cell empty  <- the master's state
            MissingParam,       // no family parameter of that name at all
            SourceUnbound,      // calc value fine, but the parameter it reads is not in the family
        }

        private sealed class RowResult
        {
            public UniversalTagRow Spec;
            public RowVerdict Verdict;
            public string Found;      // what the family actually has, for the report
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData?.Application?.ActiveUIDocument;
            Document doc = uidoc?.Document;
            if (doc == null)
            {
                message = "No active document.";
                return Result.Failed;
            }

            try
            {
                UniversalTagRowSpec.Reload();   // pick up an edited CSV without restarting Revit
                var spec = UniversalTagRowSpec.Load();
                if (spec.Count == 0)
                {
                    // An empty spec must never read as "nothing wrong".
                    TaskDialog.Show("Universal Tag Diff",
                        $"The row spec is unavailable — {UniversalTagRowSpec.DataFileName} was not found\n" +
                        "in the plugin's data folder.\n\n" +
                        "Regenerate it from the build sheet:\n" +
                        "  python tools/extract_universal_tag_rows.py\n\n" +
                        "No comparison was made.");
                    return Result.Cancelled;
                }

                if (doc.IsFamilyDocument)
                    return ReportOn(doc.FamilyManager, doc.Title, spec, openedByUs: null);

                // In a project: pick one loaded tag family and inspect it via
                // EditFamily, which hands back a family Document without taking
                // over the UI. We close it again in the finally.
                Family fam = PickTagFamily(doc);
                if (fam == null) return Result.Cancelled;

                Document fdoc = null;
                try
                {
                    fdoc = doc.EditFamily(fam);
                    if (fdoc == null || !fdoc.IsFamilyDocument)
                    {
                        TaskDialog.Show("Universal Tag Diff",
                            $"Could not open '{fam.Name}' for inspection.");
                        return Result.Failed;
                    }
                    return ReportOn(fdoc.FamilyManager, fam.Name, spec, openedByUs: fdoc);
                }
                finally
                {
                    // Close what we opened. Closing is best-effort: a family the
                    // user already had open in an editor tab cannot be closed
                    // from here, and that is not an error worth surfacing.
                    if (fdoc != null)
                    {
                        try { fdoc.Close(false); }
                        catch (Exception ex) { StingLog.Info($"UniversalTagDiff: leaving '{fam.Name}' open — {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex)
            {
                StingLog.Error("UniversalTagDiffCommand", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        // ------------------------------------------------------------------

        private static Result ReportOn(FamilyManager fm, string familyName,
                                       List<UniversalTagRow> spec, Document openedByUs)
        {
            if (fm == null)
            {
                TaskDialog.Show("Universal Tag Diff", $"'{familyName}' has no FamilyManager.");
                return Result.Failed;
            }

            // Index the family's parameters once, by name.
            var byName = new Dictionary<string, FamilyParameter>(StringComparer.Ordinal);
            foreach (FamilyParameter fp in fm.Parameters)
            {
                string n = fp.Definition?.Name;
                if (!string.IsNullOrEmpty(n)) byName[n] = fp;
            }

            var results = new List<RowResult>();
            foreach (UniversalTagRow row in spec)
            {
                if (!row.IsCalculated) continue;   // T1 primary row carries no formula
                results.Add(Judge(row, byName));
            }

            // Gate parameters: the formulas cannot work without them, and the
            // master is missing several (STATE_1/2/3 and the warning gate).
            var gatesMissing = UniversalTagRowSpec.GateParameters()
                .Where(g => !byName.ContainsKey(g))
                .ToList();
            const string WarnGate = "TAG_WARN_VISIBLE_BOOL";
            bool warnGateMissing = !byName.ContainsKey(WarnGate);

            var sb = new StringBuilder();
            sb.AppendLine($"Family: {familyName}");
            sb.AppendLine($"Spec:   {UniversalTagRowSpec.DataFileName} — {spec.Count} rows, {results.Count} calculated");
            sb.AppendLine();

            foreach (RowVerdict v in new[]
                     {
                         RowVerdict.Ok, RowVerdict.NoFormula, RowVerdict.MissingParam,
                         RowVerdict.SelfReferential, RowVerdict.FormulaMismatch, RowVerdict.SourceUnbound
                     })
            {
                int n = results.Count(r => r.Verdict == v);
                if (n > 0) sb.AppendLine($"  {Describe(v),-46} {n,3}");
            }

            if (gatesMissing.Count > 0 || warnGateMissing)
            {
                sb.AppendLine();
                sb.AppendLine("  MISSING GATE PARAMETERS — rows gated on these can never render:");
                foreach (string g in gatesMissing) sb.AppendLine($"    {g}");
                if (warnGateMissing) sb.AppendLine($"    {WarnGate}  (status badges)");
            }

            int ok = results.Count(r => r.Verdict == RowVerdict.Ok);
            sb.AppendLine();
            if (ok == results.Count && gatesMissing.Count == 0)
            {
                sb.AppendLine("CONFORMS. Every calculated row carries its gated formula, and every");
                sb.AppendLine("gate the spec references is bound. Tier depth will work on this family.");
            }
            else if (ok == 0 && results.All(r => r.Verdict == RowVerdict.NoFormula || r.Verdict == RowVerdict.MissingParam))
            {
                sb.AppendLine("NO GATING AT ALL. Every row renders unconditionally, so the tier");
                sb.AppendLine("gates do nothing when toggled — this is the state the master is in.");
                sb.AppendLine("Fix: give each calculated value the formula from the build sheet.");
            }
            else
            {
                sb.AppendLine($"PARTIAL: {ok} of {results.Count} rows are correct. Detail is in the log");
                sb.AppendLine("and the CSV below.");
            }

            // Full per-row detail goes to the log + a CSV, not the dialog — 65
            // rows do not fit in a TaskDialog and truncating them would hide the
            // ones that matter.
            string csvPath = WriteDetail(familyName, results);
            if (!string.IsNullOrEmpty(csvPath))
            {
                sb.AppendLine();
                sb.AppendLine("Per-row detail:");
                sb.AppendLine("  " + csvPath);
            }

            foreach (RowResult r in results.Where(x => x.Verdict != RowVerdict.Ok))
                StingLog.Info($"UniversalTagDiff [{familyName}] row {r.Spec.Row} {r.Spec.Tier} " +
                              $"'{r.Spec.Name}': {r.Verdict} — {r.Found}");

            TaskDialog.Show("Universal Tag Diff", sb.ToString());
            return Result.Succeeded;
        }

        private static RowResult Judge(UniversalTagRow row, Dictionary<string, FamilyParameter> byName)
        {
            var res = new RowResult { Spec = row };

            // The self-referential case first: a formula written onto the SOURCE
            // parameter rather than onto the calculated value. It is the shape
            // FamilyLabelAuthor produces, and it is worth naming explicitly
            // because Revit's own error ("circular chain of references") does not
            // say which of the two parameters was the wrong target.
            FamilyParameter source;
            if (!string.IsNullOrEmpty(row.SourceParameter) &&
                byName.TryGetValue(row.SourceParameter, out source) &&
                !string.IsNullOrEmpty(source.Formula) &&
                source.Formula.IndexOf(row.SourceParameter, StringComparison.Ordinal) >= 0)
            {
                res.Verdict = RowVerdict.SelfReferential;
                res.Found = $"{row.SourceParameter}.Formula = {source.Formula}";
                return res;
            }

            FamilyParameter calc;
            if (!byName.TryGetValue(row.Name, out calc))
            {
                res.Verdict = RowVerdict.MissingParam;
                res.Found = "(no family parameter of this name)";
                return res;
            }

            string actual = calc.Formula;
            if (string.IsNullOrWhiteSpace(actual))
            {
                res.Verdict = RowVerdict.NoFormula;
                res.Found = "(Formula cell empty)";
                return res;
            }

            if (!UniversalTagRowSpec.FormulaEquals(actual, row.Formula))
            {
                res.Verdict = RowVerdict.FormulaMismatch;
                res.Found = actual;
                return res;
            }

            if (!string.IsNullOrEmpty(row.SourceParameter) && !byName.ContainsKey(row.SourceParameter))
            {
                // Formula is right but reads a parameter the family does not
                // carry: it will evaluate to nothing and the row stays blank
                // whatever the gate says.
                res.Verdict = RowVerdict.SourceUnbound;
                res.Found = $"formula reads {row.SourceParameter}, which is not bound in this family";
                return res;
            }

            res.Verdict = RowVerdict.Ok;
            res.Found = actual;
            return res;
        }

        private static string Describe(RowVerdict v)
        {
            switch (v)
            {
                case RowVerdict.Ok: return "correct — gated formula present";
                case RowVerdict.NoFormula: return "no formula (row always renders)";
                case RowVerdict.MissingParam: return "calculated value missing";
                case RowVerdict.SelfReferential: return "formula on the SOURCE param (broken)";
                case RowVerdict.FormulaMismatch: return "formula differs from the spec";
                case RowVerdict.SourceUnbound: return "reads an unbound parameter";
                default: return v.ToString();
            }
        }

        private static string WriteDetail(string familyName, List<RowResult> results)
        {
            try
            {
                string safe = string.Join("_", (familyName ?? "family").Split(System.IO.Path.GetInvalidFileNameChars()));
                string dir = OutputLocationHelper.GetOutputDirectory();
                if (string.IsNullOrEmpty(dir)) return null;
                string path = System.IO.Path.Combine(dir, $"universal_tag_diff_{safe}.csv");

                var sb = new StringBuilder();
                sb.AppendLine("Row,Tier,CalcValueName,Verdict,Expected,Found");
                foreach (RowResult r in results)
                    sb.AppendLine(string.Join(",",
                        r.Spec.Row,
                        Q(r.Spec.Tier),
                        Q(r.Spec.Name),
                        Q(r.Verdict.ToString()),
                        Q(r.Spec.Formula),
                        Q(r.Found)));

                System.IO.File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"UniversalTagDiff.WriteDetail: {ex.Message}");
                return null;
            }
        }

        private static string Q(string s)
        {
            return "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        }

        private static Family PickTagFamily(Document doc)
        {
            var fams = new FilteredElementCollector(doc)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Where(f => f.FamilyCategory != null &&
                            f.FamilyCategory.Name.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fams.Count == 0)
            {
                TaskDialog.Show("Universal Tag Diff",
                    "No tag families are loaded in this project.\n\n" +
                    "Open the universal master in the Family Editor and run this again, " +
                    "or load the tag families first.");
                return null;
            }

            string chosen = StingTools.Select.StingListPicker.Show(
                "Universal Tag Diff",
                "Which tag family should be compared against the build sheet?",
                fams.Select(f => f.Name).ToList());

            if (string.IsNullOrEmpty(chosen)) return null;
            return fams.FirstOrDefault(f => string.Equals(f.Name, chosen, StringComparison.Ordinal));
        }
    }
}
