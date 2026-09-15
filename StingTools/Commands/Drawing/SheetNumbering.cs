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

            /// <summary>Where the old -> new pairs were recorded, or null when they
            /// could not be. Null is reported, not hidden: an operator told a trail
            /// exists when it does not is worse off than one told nothing.</summary>
            public string HistoryPath;
        }

        /// <summary>The file Sheet_NumberFromIso, Tidy and Restore already append to.
        /// One file, because the question it answers -- "this PDF says A-001, what is
        /// that sheet called now?" -- does not care which command did the renaming,
        /// and three separate logs would each hold a third of the answer.</summary>
        internal const string HistoryFile = "sheet_number_history.json";

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

            outcome.HistoryPath = RecordHistory(doc, plan, outcome, transactionName);
            return outcome;
        }
        /// <summary>Append what changed to sheet_number_history.json.
        ///
        /// This is the answer to the one question a renumber creates: somebody is
        /// holding a PDF that says A-001 and needs to know what that sheet is called
        /// now. Revit keeps no record -- the old number is simply gone -- so if this
        /// is not written, the link between an issued drawing and the model is lost
        /// with nothing anywhere admitting it.
        ///
        /// Only sheets that actually took their new number are logged. Recording a
        /// rename that failed would send the reader to a sheet number that does not
        /// exist.</summary>
        private static string RecordHistory(Document doc, List<Change> plan,
                                            Outcome outcome, string source)
        {
            try
            {
                if (doc == null || doc.IsFamilyDocument || string.IsNullOrEmpty(doc.PathName))
                    return null;

                var applied = new List<Change>();
                foreach (var c in plan)
                {
                    bool failed = outcome.Failures.Exists(f =>
                        f.TrimStart().StartsWith(c.Old + " ", StringComparison.Ordinal)
                        || f.TrimStart().StartsWith(c.Old + ":", StringComparison.Ordinal));
                    if (!failed) applied.Add(c);
                }
                if (applied.Count == 0) return null;

                string path = StingPaths.MetaFile(doc, "_BIM_COORD", HistoryFile);
                if (string.IsNullOrEmpty(path)) return null;

                Newtonsoft.Json.Linq.JArray log;
                try
                {
                    log = System.IO.File.Exists(path)
                        ? Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(path))
                        : new Newtonsoft.Json.Linq.JArray();
                }
                catch (Exception ex)
                {
                    // A corrupt log must not cost us THIS run's record, and must not
                    // be silently replaced either.
                    StingLog.Warn($"SheetNumbering: {HistoryFile} unreadable, starting a new "
                        + $"one beside it: {ex.Message}");
                    log = new Newtonsoft.Json.Linq.JArray();
                }

                log.Add(Newtonsoft.Json.Linq.JObject.FromObject(new
                {
                    when = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                    source,
                    changes = applied.ConvertAll(c => new { from = c.Old, to = c.New }),
                }));

                System.IO.File.WriteAllText(path,
                    Newtonsoft.Json.JsonConvert.SerializeObject(log, Newtonsoft.Json.Formatting.Indented));
                return path;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"SheetNumbering: could not write {HistoryFile}: {ex.Message}");
                return null;
            }
        }
    }
}
