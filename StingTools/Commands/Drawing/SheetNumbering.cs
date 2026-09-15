// StingTools — Drawing Template Manager · the one place sheet numbers get written
//
// Auto-Number and Reorder both renumber, and both need the same two things: the
// configured pattern, and a rename that Revit will accept. Two copies of that
// would be two copies of the parking logic, and the day they disagree is the day
// a sheet is left sitting on "__STING_TEMP_1234".

using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using StingTools.Core;
using StingTools.Core.Drawing;
using StingTools.Docs;

namespace StingTools.Commands.Drawing
{
    internal static class SheetNumbering
    {
        internal sealed class Change
        {
            public ViewSheet Sheet;
            public string Old;
            public string New;
        }

        internal sealed class Outcome
        {
            public int Done;
            public int Failed;
            public List<string> Failures = new List<string>();
        }

        /// <summary>The number pattern, from TITLE_BLOCK.csv's DefaultValue column.
        ///
        /// Read through the same loader Populate uses, so a project-local CSV wins
        /// exactly as it does everywhere else. A second way of finding the same file
        /// is how two commands come to disagree about it.</summary>
        internal static string ReadPattern(Document doc)
        {
            try
            {
                string path = TitleBlockPopulateCommand.ResolveCsvPath(doc, "TITLE_BLOCK.csv");
                var csv = TitleBlockCsv.Load(path);
                string p = csv.ValueFor(ParamRegistry.TB_SHEET_NUMBER_PATTERN, "");
                if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
            }
            catch (Exception ex) { StingLog.Warn($"SheetNumbering pattern read: {ex.Message}"); }
            return "{disc}-{seq:D3}";
        }

        /// <summary>Apply a renumber plan in two passes, inside one transaction.
        ///
        /// Revit refuses a duplicate sheet number even for an instant, so every sheet
        /// parks on a unique temporary number before any final number is set. A sheet
        /// whose final number is refused goes back to the one it started with —
        /// "__STING_TEMP_1234" is a worse number than the old one, and leaving it
        /// there turns a failed rename into a corrupted set.</summary>
        internal static Outcome Apply(Document doc, List<Change> plan, string transactionName)
        {
            var outcome = new Outcome();
            if (plan == null || plan.Count == 0) return outcome;

            using (var tx = new Transaction(doc, transactionName))
            {
                tx.Start();

                var parked = new List<Change>();
                foreach (var c in plan)
                {
                    try
                    {
                        c.Sheet.SheetNumber = $"__STING_TEMP_{c.Sheet.Id.Value}";
                        parked.Add(c);
                    }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        outcome.Failures.Add($"  {c.Old}: could not park — {ex.Message}");
                        StingLog.Warn($"SheetNumbering park '{c.Old}': {ex.Message}");
                    }
                }

                foreach (var c in parked)
                {
                    try { c.Sheet.SheetNumber = c.New; outcome.Done++; }
                    catch (Exception ex)
                    {
                        outcome.Failed++;
                        outcome.Failures.Add($"  {c.Old} -> {c.New}: {ex.Message}");
                        StingLog.Warn($"SheetNumbering set '{c.New}': {ex.Message}");
                        try { c.Sheet.SheetNumber = c.Old; }
                        catch (Exception ex2)
                        {
                            StingLog.Warn($"SheetNumbering restore '{c.Old}': {ex2.Message}");
                            outcome.Failures.Add($"  {c.Old}: ALSO could not be put back — "
                                + "it is sitting on a temporary number and needs renaming by hand");
                        }
                    }
                }

                tx.Commit();
            }

            return outcome;
        }
    }
}
